// ============================================================
// MET_PointReturnSystem
// 概要: コネクションエラー等でワールドから落ちた後、"同じインスタンス" に
//       戻ってきた場合に、いた場所へ自動で復帰させるシステム。
//       VRChat の Persistence(PlayerData) にローカルプレイヤーの
//       位置・向き・インスタンス識別子・保存時刻を定期保存しておき、
//       再入室時に条件を満たせば前回位置へテレポートで復帰する。
//
//       空のオブジェクトにこのスクリプト(＋Program Asset)を付けて
//       ワールドに置くだけで機能する。設定は任意。
//
// バージョン: 0.1.0  ※骨組み段階（動作する最小構成）
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//   ※上の桁が上がった場合、下の桁は0にリセット
//
// 復帰する条件（すべて満たしたときだけ復帰）:
//   ① 前回の保存データが存在する（＝初回入室ではない）
//   ② 保存時のインスタンス識別子が、今いるインスタンスと一致する
//      → 別インスタンスに入った場合は正規スポーンのまま
//   ③ 保存時刻からの経過が「復帰有効時間(既定30分)」未満
//      → 時間が空きすぎた場合は正規スポーンのまま
//
// インスタンス識別子について:
//   VRChat にはインスタンスIDを直接取得するAPIが無いため、
//   マスターが入室時に一意な値を生成し、同期変数で全員に配布する
//   （定番手法）。同じインスタンスが生きている限りこの値は保持され、
//   別インスタンスでは別の値になるため「同じインスタンスか」を判定できる。
//
// 設計メモ / TODO（今後の拡張ポイント）:
//   - 保存は saveInterval 秒ごとの定期実行（Update不使用・イベント駆動）。
//   - 誤復帰を避けるため、復帰の実挙動は autoReturnOnJoin で制御可能。
//   - スポーン直後の座標を保存しないよう、入室から最初の保存までは
//     saveInterval 秒の猶予を取っている（原点誤保存の簡易対策）。
//   - マスター自身が落ちて即再接続する等のエッジケースは未対応（骨組み）。
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Persistence;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_PointReturnSystem : UdonSharpBehaviour
{
    [Header("復帰設定")]
    [SerializeField, Tooltip("この時間(分)以上あけて入り直した場合は復帰しない")]
    private float returnValidMinutes = 30f;

    [SerializeField, Tooltip("入室時に前回位置へ自動復帰する（OFFにすると保存だけ行う）")]
    private bool autoReturnOnJoin = true;

    [Header("保存設定")]
    [SerializeField, Tooltip("位置を保存する間隔（秒）")]
    private float saveInterval = 3f;

    [Header("デバッグ設定")]
    [SerializeField, Tooltip("動作ログを Console に出力する")]
    private bool enableDebugLog = false;

    // --- PlayerData 保存キー（ワールド内で一意になるよう接頭辞を付ける） ---
    private const string KEY_POS = "MET_PRS_Pos";       // Vector3: 前回位置
    private const string KEY_ROT = "MET_PRS_Rot";       // Quaternion: 前回の向き
    private const string KEY_INSTANCE = "MET_PRS_Inst"; // long: インスタンス識別子
    private const string KEY_TIME = "MET_PRS_Time";     // double: 保存時刻(UnixEpoch秒/UTC)

    // インスタンス識別子（マスターが生成し、全クライアントへ同期配布）
    [UdonSynced] private long _instanceId = 0L;

    private VRCPlayerApi _local;
    private bool _instanceIdReady = false; // 識別子を受信/生成済みか
    private bool _restorePending = false;  // OnPlayerRestored 済みか
    private bool _restoreDone = false;     // 復帰判定を実行済みか（多重実行防止）

    // ============================================================
    // 初期化
    // ============================================================

    void Start()
    {
        _local = Networking.LocalPlayer;

        // マスターだけがインスタンス識別子を生成し、全員へ配る。
        // 既存インスタンスに後から入った場合は OnDeserialization で受信する。
        if (Networking.IsMaster)
        {
            _instanceId = GenerateInstanceId();
            _instanceIdReady = true;
            RequestSerialization();

            if (enableDebugLog)
                Debug.Log($"[MET_PointReturnSystem] インスタンス識別子を生成: {_instanceId}", this);
        }

        // 定期保存ループを開始（スポーン直後を保存しないよう1周期待ってから）
        SendCustomEventDelayedSeconds(nameof(_SaveTick), saveInterval);
    }

    // 遅延入室者がマスターから識別子を受け取るタイミング
    public override void OnDeserialization()
    {
        if (_instanceId != 0L && !_instanceIdReady)
        {
            _instanceIdReady = true;

            if (enableDebugLog)
                Debug.Log($"[MET_PointReturnSystem] インスタンス識別子を受信: {_instanceId}", this);

            // 識別子とデータ復元の到着順は不定なので、揃った側で復帰を試みる
            TryRestore();
        }
    }

    // ============================================================
    // 復帰（入室時）
    // ============================================================

    // 自分の永続データ(PlayerData)がロードされた
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;
        if (!player.isLocal) return;

        _restorePending = true;
        TryRestore();
    }

    // 「識別子受信」と「データ復元」の両方が揃ってから1回だけ復帰判定する
    private void TryRestore()
    {
        if (_restoreDone) return;
        if (!_restorePending) return;
        if (!_instanceIdReady) return;

        _restoreDone = true;

        if (!autoReturnOnJoin)
        {
            if (enableDebugLog)
                Debug.Log("[MET_PointReturnSystem] 自動復帰は無効（保存のみ実行）", this);
            return;
        }

        EvaluateAndReturn();
    }

    // 保存データを読み、条件を満たせば前回位置へテレポート
    private void EvaluateAndReturn()
    {
        if (!Utilities.IsValid(_local)) return;

        // ① 保存データの存在確認（初回入室なら何もしない）
        if (!PlayerData.HasKey(_local, KEY_INSTANCE) ||
            !PlayerData.HasKey(_local, KEY_TIME) ||
            !PlayerData.HasKey(_local, KEY_POS))
        {
            if (enableDebugLog)
                Debug.Log("[MET_PointReturnSystem] 保存データなし（初回入室）", this);
            return;
        }

        // ② 同一インスタンス判定
        if (!PlayerData.TryGetLong(_local, KEY_INSTANCE, out long savedInstance) ||
            savedInstance != _instanceId)
        {
            if (enableDebugLog)
                Debug.Log("[MET_PointReturnSystem] 別インスタンスのため復帰しない", this);
            return;
        }

        // ③ 経過時間判定
        if (!PlayerData.TryGetDouble(_local, KEY_TIME, out double savedTime) ||
            (GetNowSeconds() - savedTime) > returnValidMinutes * 60.0)
        {
            if (enableDebugLog)
                Debug.Log("[MET_PointReturnSystem] 時間が空いたため復帰しない", this);
            return;
        }

        // --- 条件成立：前回位置へ復帰 ---
        if (!PlayerData.TryGetVector3(_local, KEY_POS, out Vector3 pos)) return;

        Quaternion rot;
        if (!PlayerData.TryGetQuaternion(_local, KEY_ROT, out rot))
            rot = Quaternion.identity;

        _local.TeleportTo(pos, rot);

        if (enableDebugLog)
            Debug.Log($"[MET_PointReturnSystem] 前回位置へ復帰: {pos}", this);
    }

    // ============================================================
    // 保存（定期実行）
    // ============================================================

    // SendCustomEventDelayedSeconds から自己再帰で呼ばれる保存ループ
    public void _SaveTick()
    {
        SavePosition();
        SendCustomEventDelayedSeconds(nameof(_SaveTick), saveInterval);
    }

    private void SavePosition()
    {
        // 識別子が確定するまでは保存しない（不正な識別子で保存しないため）
        if (!_instanceIdReady) return;
        if (!Utilities.IsValid(_local)) return;

        Vector3 pos = _local.GetPosition();
        Quaternion rot = _local.GetRotation();

        PlayerData.SetVector3(KEY_POS, pos);
        PlayerData.SetQuaternion(KEY_ROT, rot);
        PlayerData.SetLong(KEY_INSTANCE, _instanceId);
        PlayerData.SetDouble(KEY_TIME, GetNowSeconds());
    }

    // ============================================================
    // ヘルパー
    // ============================================================

    // 実時刻＋乱数から一意性の高いインスタンス識別子を生成する
    private long GenerateInstanceId()
    {
        long ticks = System.DateTime.UtcNow.Ticks;
        long rnd = (long)Random.Range(1, int.MaxValue);
        return ticks ^ (rnd << 20);
    }

    // 現在時刻を UnixEpoch(1970/1/1 UTC) からの経過秒で返す
    private double GetNowSeconds()
    {
        System.TimeSpan span = System.DateTime.UtcNow - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        return span.TotalSeconds;
    }
}
