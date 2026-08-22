// ============================================================
// MET_PickupScaler
// 概要: Pickup を片手で持ちながら、空いている方の手の Grab ボタンを
//       押して「両手持ち」にし、両手を近づけると縮小・離すと拡大する
//       スケール変更ギミック。全方向（縦横高さ）を一律に拡大縮小する。
//       最小・最大スケールは初期スケールに対する倍率で指定する。
//       「グローバル同期する」ON でスケールを全員へ同期、OFF で完全ローカル。
//       （VRC Object Sync は localScale を同期しないため本スクリプトが担う）
//
// バージョン: 1.2.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//   ※上の桁が上がった場合、下の桁は0にリセット
//
//   v1.2.0 変更点:
//     - スケールに応じて Rigidbody の質量(mass)を変える機能を追加（既定 OFF）。
//       変化則は指数指定（既定3＝体積比、大きいほど重く小さいほど軽い）。
//       質量は倍率から毎回算出するため追加の同期変数は不要（全員一致）。
//       ※ Collider の当たり判定サイズはスケールに自動追従するため対象外。
//   v1.1.0 変更点:
//     - スケール同期の ON/OFF を「グローバル同期する」チェックボックスで
//       選べるようにした（既定 ON）。OFF 時は送信・所有権取得を行わず
//       完全ローカルで動作する。
//     - 適用倍率を非同期のローカル変数(_currentMul)に分離し、OFF 時に
//       他クライアントの同期値で上書きされないようにした。
//
// 操作仕様（VR専用。Desktop は両手ジェスチャー不可）:
//   ① 対象オブジェクトを片手の Pickup でつかむ
//   ② もう片方（空いている手）の Grab ボタンを「押しっぱなし」にする
//      → その瞬間の両手間距離を基準に両手スケール操作を開始
//   ③ 両手を近づける = 縮小 / 両手を離す = 拡大
//   ④ 空いている手の Grab を離すと確定（そのスケールで固定）
//
// なぜこの方式か（VRChat 制約）:
//   VRC Pickup は同時に片手でしか保持できないため、空いている手の
//   「掴む」動作は Grab ボタンの入力イベント(InputGrab)を handType で
//   判別して検出する。手の位置はローカルプレイヤーの Tracking Data
//   （左右の手）から取得し、その距離比でスケールを決める。
//
// 【重要】オブジェクト構成（手動セットアップ）:
//   1. VRC Pickup            … 手で持てるように（Rigidbody + Collider 必須）
//   2. 本スクリプト(＋Program Asset)
//   3. VRC Object Sync（任意）… 位置・回転を全員へ同期したい場合に付与。
//                               スケールを同期するなら本スクリプトの
//                               「グローバル同期する」を ON にし、位置ズレを
//                               防ぐため VRC Object Sync も併用するのが推奨。
//   ※ VRC Object Sync は Manual 同期の Udon と同居できないため、本スクリプトは
//     Continuous 同期にしてある（同期変数はスケール倍率 float 1つのみで軽量）。
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.SDK3.Components;

// Continuous 同期：VRC Object Sync と同じ GameObject に同居できるようにするため。
// （VRC Object Sync は Manual 同期の UdonBehaviour と同居不可）
[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class MET_PickupScaler : UdonSharpBehaviour
{
    [Header("参照（未設定ならこのオブジェクトから自動取得）")]
    [SerializeField, Tooltip("拡大縮小の対象となる VRC Pickup。未設定なら自動取得")]
    private VRCPickup pickup;

    [SerializeField, Tooltip("VRC Object Sync（位置同期用）。付いている場合、位置・回転は Object Sync が同期する。スケールは本スクリプトが同期する。未設定なら自動取得")]
    private VRCObjectSync objectSync;

    [Header("同期設定")]
    [SerializeField, Tooltip("ON: スケールを全員へ同期する（位置ズレを防ぐため VRC Object Sync の併用を推奨）。OFF: 完全ローカル（自分だけスケールが変わり、他人には元のサイズのまま）")]
    private bool syncScaleGlobally = true;

    [Header("スケール範囲（初期スケール基準の倍率）")]
    [SerializeField, Tooltip("最小スケール倍率。初期スケールを1.0とした時の下限（例: 0.2 = 元の20%まで縮小可）")]
    private float minScaleMul = 0.2f;

    [SerializeField, Tooltip("最大スケール倍率。初期スケールを1.0とした時の上限（例: 5 = 元の5倍まで拡大可）")]
    private float maxScaleMul = 5f;

    [Header("操作設定")]
    [SerializeField, Tooltip("両手の距離変化に対する感度。1で手の動きどおり。大きいほど少ない手の動きで大きく変化する")]
    private float sensitivity = 1f;

    [Header("質量（Rigidbody）設定")]
    [SerializeField, Tooltip("ON: スケールに応じて Rigidbody の質量を変える。OFF: 質量は初期値のまま（従来の投げ心地を維持）")]
    private bool adjustMass = false;

    [SerializeField, Tooltip("質量の変化の強さ（指数）。初期質量 × 倍率^この値。3=体積比（自然）、1=倍率どおり（線形）、0=質量固定。大きいほど大小での重さの差が激しくなる")]
    private float massScalePower = 3f;

    [Header("デバッグ")]
    [SerializeField, Tooltip("動作ログを Console に出力する")]
    private bool enableDebugLog = false;

    // 全員へ同期するスケール倍率（グローバル同期 ON の時だけ使用）。
    // Continuous 同期のため Owner の書き込みが自動で配布される。
    [UdonSynced] private float _syncedMul = 1f;

    private VRCPlayerApi _local;
    private Vector3 _baseScale = Vector3.one; // 初期（設計）スケール。倍率1.0の基準
    private Rigidbody _rigidbody;             // 質量変更対象（任意）
    private float _baseMass = 1f;             // 初期質量。倍率1.0時の基準
    private bool _initialized = false;

    // 実際に適用している倍率（源泉）。同期 OFF 時に受信値で上書きされないよう
    // 同期変数(_syncedMul)とは分離して保持する。
    private float _currentMul = 1f;

    private bool _isHeldByLocal = false;      // 自分がこの Pickup を保持中か
    private bool _isScaling = false;          // 両手スケール操作中か
    private HandType _scalingFreeHand;        // 操作を開始した「空いている手」
    private float _grabStartDist = 0f;        // 操作開始時の両手間距離
    private float _grabStartMul = 1f;         // 操作開始時のスケール倍率

    // 両手間距離がこれ未満だと基準として不安定なため無視する（m）
    private const float MIN_START_DIST = 0.05f;
    // 倍率がこの差以上変化した時だけ反映・同期する（微小変化の無駄な同期を防ぐ）
    private const float MUL_EPSILON = 0.0005f;

    // ============================================================
    // 初期化
    // ============================================================

    void Start()
    {
        _local = Networking.LocalPlayer;
        EnsureInitialized();
        ApplyScale();
    }

    // 初期スケールの取得・参照解決・範囲補正を一度だけ行う。
    // 遅参加時に OnDeserialization が Start より先に来ても正しく動くよう、
    // 両方の入口から呼び出して確実に初期化する。
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        if (pickup == null) pickup = GetComponent<VRCPickup>();
        if (objectSync == null) objectSync = GetComponent<VRCObjectSync>();
        if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

        // 初期質量を「倍率1.0の基準」として控える
        if (_rigidbody != null) _baseMass = _rigidbody.mass;

        // 範囲設定の異常値を補正（最小 > 最大 などを防ぐ）
        if (minScaleMul <= 0f) minScaleMul = 0.01f;
        if (maxScaleMul < minScaleMul) maxScaleMul = minScaleMul;
        if (sensitivity <= 0f) sensitivity = 1f;

        // この時点の localScale を「倍率1.0の基準」とする。
        // （まだ誰もスケールを変えていない設計スケール）
        _baseScale = transform.localScale;
    }

    // ============================================================
    // Pickup 保持状態
    // ============================================================

    public override void OnPickup()
    {
        _isHeldByLocal = true;
        // 同期 ON の時だけ、同期変数の書き込み権のため所有権を揃える
        if (syncScaleGlobally) TakeOwnership();

        if (enableDebugLog)
            Debug.Log("[MET_PickupScaler] Pickup 保持開始", this);
    }

    public override void OnDrop()
    {
        _isHeldByLocal = false;
        if (_isScaling) EndScaling();

        if (enableDebugLog)
            Debug.Log("[MET_PickupScaler] Pickup 手放し", this);
    }

    // ============================================================
    // 空いている手の Grab 検出（両手スケール操作の開始／終了）
    // ============================================================

    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (pickup == null) return;
        if (!_isHeldByLocal) return;            // 自分が保持している時だけ反応
        if (!Utilities.IsValid(_local)) return;
        if (!_local.IsUserInVR()) return;       // 両手ジェスチャーは VR のみ

        // 保持している手と反対（空いている手）を求める
        VRC_Pickup.PickupHand held = pickup.currentHand;
        if (held == VRC_Pickup.PickupHand.None) return;
        HandType freeHand = (held == VRC_Pickup.PickupHand.Left) ? HandType.RIGHT : HandType.LEFT;

        if (value)
        {
            // 空いている手の Grab 押下で開始
            if (_isScaling) return;             // 既に操作中なら無視
            if (args.handType != freeHand) return;
            BeginScaling(freeHand);
        }
        else
        {
            // 操作を開始した手を離したら確定
            if (_isScaling && args.handType == _scalingFreeHand)
                EndScaling();
        }
    }

    private void BeginScaling(HandType freeHand)
    {
        if (syncScaleGlobally) TakeOwnership();

        _isScaling = true;
        _scalingFreeHand = freeHand;
        _grabStartDist = HandDistance();
        _grabStartMul = _currentMul;

        if (enableDebugLog)
            Debug.Log($"[MET_PickupScaler] 両手スケール開始 手間距離={_grabStartDist:F3} 現在倍率={_currentMul:F3}", this);
    }

    private void EndScaling()
    {
        _isScaling = false;

        if (enableDebugLog)
            Debug.Log($"[MET_PickupScaler] 両手スケール終了 倍率={_currentMul:F3}", this);
    }

    // ============================================================
    // スケール更新（操作中の保持者のみ）
    // ============================================================

    public override void PostLateUpdate()
    {
        if (!_isScaling) return;

        // 保持が外れていたら安全に終了
        if (pickup == null || !_isHeldByLocal)
        {
            EndScaling();
            return;
        }

        float dist = HandDistance();

        // 開始距離が不安定（極端に近い）だった場合は、有効になった時点で基準を取り直す
        if (_grabStartDist < MIN_START_DIST)
        {
            if (dist >= MIN_START_DIST)
            {
                _grabStartDist = dist;
                _grabStartMul = _currentMul;
            }
            return;
        }
        if (dist < MIN_START_DIST) return; // 一時的に手が重なった等は無視

        // 手間距離の比を感度で調整して倍率へ反映。
        // rawRatio > 1（手を離す）→ 拡大 / rawRatio < 1（手を近づける）→ 縮小
        float rawRatio = dist / _grabStartDist;
        float ratio = Mathf.Pow(rawRatio, sensitivity);
        float newMul = Mathf.Clamp(_grabStartMul * ratio, minScaleMul, maxScaleMul);

        if (Mathf.Abs(newMul - _currentMul) > MUL_EPSILON)
        {
            _currentMul = newMul;
            ApplyScale();

            // グローバル同期 ON の時だけ配布する（OFF は完全ローカル）
            if (syncScaleGlobally)
            {
                _syncedMul = newMul;
                RequestSerialization();
            }
        }
    }

    // ============================================================
    // 同期反映（全クライアント）
    // ============================================================

    public override void OnDeserialization()
    {
        // 完全ローカル運用の時は他クライアントの値を無視する
        if (!syncScaleGlobally) return;

        EnsureInitialized();
        _currentMul = Mathf.Clamp(_syncedMul, minScaleMul, maxScaleMul);
        ApplyScale();
    }

    // ============================================================
    // ヘルパー
    // ============================================================

    // 現在の倍率を localScale（＋任意で Rigidbody 質量）へ反映（全方向一律）
    private void ApplyScale()
    {
        transform.localScale = new Vector3(
            _baseScale.x * _currentMul,
            _baseScale.y * _currentMul,
            _baseScale.z * _currentMul);

        // スケールに応じた質量変更（ON かつ Rigidbody がある時のみ）。
        // 倍率から毎回算出するので全クライアントで同じ質量になる。
        if (adjustMass && _rigidbody != null)
        {
            float m = _baseMass * Mathf.Pow(_currentMul, massScalePower);
            // Rigidbody.mass は 0 以下不可のため微小下限でクランプ
            _rigidbody.mass = Mathf.Max(m, 0.0001f);
        }
    }

    // ローカルプレイヤーの左右の手（Tracking Data）間の距離
    private float HandDistance()
    {
        if (!Utilities.IsValid(_local)) return 0f;
        Vector3 l = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
        Vector3 r = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
        return Vector3.Distance(l, r);
    }

    // このオブジェクトの所有権を自分に揃える（同期変数の書き込み権のため）
    private void TakeOwnership()
    {
        if (!Utilities.IsValid(_local)) return;
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(_local, gameObject);
    }
}
