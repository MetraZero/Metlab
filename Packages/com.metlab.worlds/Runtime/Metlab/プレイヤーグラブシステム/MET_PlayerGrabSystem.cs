// ============================================================
// MET_PlayerGrabSystem
// 概要: 手に持つ「グラブアイテム」で他プレイヤーを遠隔から掴み、
//       フォースのようにふよふよ持ち上げて運べるようにするギミック。
//
//       VRChat では他人のアバターを直接動かせないため、非着席設定の
//       VRCStation（見えない椅子）に対象を拘束し、その Station を
//       アイテムの前方へ移動させることで「浮かせて運ぶ」を実現する。
//
// バージョン: 0.6.0  ※骨組み段階（動作する最小構成）
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//   ※上の桁が上がった場合、下の桁は0にリセット
//
//   v0.6.0 変更点:
//     - 【不具合修正①】解除時に対象が実際より上へズレて降ろされる問題を修正。
//       VRCStation は「降車時に足元を Station 原点へ置く」ため、着席中の
//       見た目位置より腰〜足の高さ分だけ上に飛び出していた。降車直前の
//       実足元位置を控え、降車後にそこへ戻すことで見た目通りの位置に降ろす。
//     - 【機能追加②】移動させながら解除すると対象が慣性で飛ぶ（投擲）。
//       保持ターゲット(Station)の実移動速度を Owner が毎フレーム計測し、
//       解除時に同期変数で配布。対象本人のクライアントで SetVelocity を
//       与えることで、掴んだまま振って離す“グラビティガン投げ”を実現。
//
// 操作仕様:
//   ① グラブアイテムを Pickup で手に持つ
//   ② 人に向けてトリガー(Use) → 照準内(既定15m/角度20°)の最寄りを掴む
//   ③ 右スティック上下(視点入力)で保持距離を調整（近づけ／離す）
//   ④ もう一度トリガー(Use) → その場に落とす（トグル）。移動しながら離すと
//      その時の速度で対象が慣性で飛ぶ（投げる）。
//   ※ アイテムを手放しても保持は続く。解除はトリガーのみ（完全トグル）。
//     アイテムを拾い直せば別のプレイヤーでも解除できる。
//
// なぜこの方式か（VRChat 制約）:
//   TeleportTo はローカルにしか効かず、他人を直接動かせない。
//   Station 着席はローカル処理なので、対象「本人のクライアント」で
//   UseStation を呼ぶ必要がある。そこで「今誰を掴んでいるか」を
//   同期変数で配り、各自が自分の担当分だけ着席/退席を行う。
//   保持ターゲット(Station)の移動は Owner が行い、VRC Object Sync で
//   全員へ位置同期する。
//
// 【アニメ崩れ対策】（掴んだ相手の体が乱れないための配慮）:
//   - Station は Immobilize For Vehicle（動く椅子用の最適化）を推奨。
//   - 保持ターゲットは Lerp で滑らかに追従させ、急なワープを避ける。
//   - UseStation/ExitStation は対象本人のクライアントでのみ実行。
//   - 二重着席・二重退席を _isSeatedLocally でガードする。
//
// 【重要】オブジェクト構成（手動セットアップ）:
//   ▼ グラブアイテム（手に持つ本体・このスクリプトを付ける）
//       1. VRC Pickup        … Auto Hold 推奨 / Use を有効に（トリガー用）
//       2. Rigidbody + Collider … Pickup に必須
//       3. 本スクリプト(＋Program Asset)
//       4. 照準の起点(任意)  … アイテム先端に空オブジェクトを置き、その
//                             Z+(青軸/forward)を狙う方向へ向けて「銃口」欄に
//                             割り当てる。未設定ならアイテム原点・前方から判定。
//       5. VRC Object Sync   … アイテム自体の位置・落下地点を全員へ同期する。
//                             （掴んでいない時／落とした後もSYNCしたい場合に必須。
//                              Pickup の Rigidbody をそのまま同期対象にできる）
//                             ※ Object Sync は Manual 同期の Udon と同居不可のため、
//                               本スクリプトは Continuous 同期にしてある（下記参照）。
//   ▼ 保持ターゲット（別オブジェクト・対象を拘束する見えない椅子）
//       4. VRC Station
//            - Seated              = OFF
//            - Player Mobility     = Immobilize For Vehicle
//            - Disable Station Exit = ON
//       5. VRC Object Sync   … 保持位置を全員へ同期
//       ※ Station の Collider は起動時に自動で無効化する。Seated=OFF の
//         Station は Collider があると「手動でUSE(乗る)」判定が出て、掴まれた
//         本人や周囲に透明な「使う」プロンプトが見えてしまうため。掴みの着席は
//         プログラム(UseStation)で行うので Collider は不要。
//       ※ 未使用時は本スクリプトが自動で遠く(地下)へ退避させて片付ける。
//   ▼ 本スクリプトの「保持ターゲット」に上記 VRCStation を割り当てる
//
// 【スタック対策】（天井の低い場所などで降ろされて動けなくなる問題）:
//   Station を降りた地点でプレイヤーカプセルが床と天井に挟まると、拘束が
//   解けていても物理的に押し出せず移動不能になる。そこで解除直後に
//   「本人のクライアントで」立てるかどうかを検算し、ダメなら救出する。
//     ① 真下へ Raycast して床を探し、そこで立てるならその床へ降ろす
//     ② それも無理なら「掴まれた瞬間に本人が居た位置」へ戻す
//   ※ 位置の記録・判定・TeleportTo はすべてローカル完結（同期不要）。
//
// 設計メモ / TODO（今後の拡張ポイント）:
//   - 現状 1アイテム = 同時に1人まで。
//   - 対象が既に別アイテムに掴まれている場合の競合は未処理（骨組み）。
//   - 「掴まれたくない人」の除外（オプトイン）は未実装（誰でも掴める）。
//   - 右スティック上下は視点入力(InputLookVertical)を流用するため、
//     距離調整中はカメラも動く点に注意（今後 別入力へ変更可能）。
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

// ※ Continuous 同期：本体に VRC Object Sync を同居させるため。
//   VRC Object Sync は Manual 同期の UdonBehaviour と同じ GameObject に
//   置けない（"cannot share ... manually synchronized Udon Behaviour"）。
//   同期変数は _capturedPlayerId（int 1つ）のみで負荷は軽微。
[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class MET_PlayerGrabSystem : UdonSharpBehaviour
{
    [Header("保持ターゲット")]
    [SerializeField, Tooltip("対象を拘束する VRCStation（別オブジェクト。Seated=OFF / Immobilize For Vehicle / Disable Station Exit=ON、VRC Object Sync 付き）")]
    private VRC.SDK3.Components.VRCStation holdStation;

    [SerializeField, Tooltip("保持ターゲットの当たり判定。手動での「USE（乗る）」判定を常に消すため起動時に無効化する。掴む着席はプログラム(UseStation)で行うため当たり判定は不要。未設定なら保持ターゲットから自動取得")]
    private Collider holdStationCollider;

    [Header("照準設定")]
    [SerializeField, Tooltip("照準の起点・方向（アイテム先端に置いた空オブジェクト等）。そのZ+(青軸/forward)方向へ判定を飛ばす。未設定ならこのオブジェクトの原点・前方を使用")]
    private Transform muzzle;

    [SerializeField, Tooltip("トリガーで掴める最大距離（m）")]
    private float aimRange = 15f;

    [SerializeField, Tooltip("前方とみなす許容角度（度）。この角度内の最寄りを対象化する")]
    private float aimAngle = 20f;

    [SerializeField, Tooltip("狙う高さのオフセット（足元からの上方向m）。胸〜頭あたりを狙う")]
    private float aimHeightOffset = 1.0f;

    [Header("距離設定")]
    [SerializeField, Tooltip("掴んだ直後の保持距離（m）")]
    private float defaultDistance = 3f;

    [SerializeField, Tooltip("最小保持距離（m）")]
    private float minDistance = 1.5f;

    [SerializeField, Tooltip("最大保持距離（m）")]
    private float maxDistance = 12f;

    [SerializeField, Tooltip("右スティック上下での距離変化速度（m/秒）")]
    private float distanceSpeed = 5f;

    [Header("追従設定")]
    [SerializeField, Tooltip("保持ターゲットの追従の滑らかさ（大きいほど速く追従）。急移動によるIK乱れを緩和")]
    private float followSharpness = 12f;

    [Header("投擲設定")]
    [SerializeField, Tooltip("移動させながら離した時に対象へ与える慣性の強さ倍率。1で保持ターゲットの実速度そのまま、大きいほど強く飛ぶ。0で投擲を無効化（その場で落とす）")]
    private float throwStrength = 1.0f;

    [SerializeField, Tooltip("投擲速度の上限（m/秒）。壁抜けや事故を防ぐためのクランプ")]
    private float maxThrowSpeed = 15f;

    [Header("待機設定")]
    [SerializeField, Tooltip("誰も掴んでいない間、保持ターゲット(VRCStation)を退避させる位置。未設定なら地下(0,-1000,0)へ退避する")]
    private Transform parkPoint;

    [SerializeField, Tooltip("落とした後、保持ターゲットを退避させるまでの遅延（秒）。0だと本人が降りる前にターゲットが退避位置へ動き、降車位置が退避位置(原点/親)にズレて飛ばされてしまう。全員の降車が終わる猶予として少し待つ")]
    private float releaseParkDelay = 0.5f;

    [Header("スタック対策")]
    [SerializeField, Tooltip("解除時に「立てない場所」に降ろされていないか検算し、必要なら安全な位置へ救出する。天井の低い場所で動けなくなる事故を防ぐ")]
    private bool enableStuckRescue = true;

    [SerializeField, Tooltip("床・壁とみなすレイヤー。真下の床探しと「立てるか」の判定に使う。既定は Default と Environment。Player/PlayerLocal を含めると自分のコライダーを拾って誤判定するので必ず外すこと")]
    private LayerMask groundLayers = (1 << 0) | (1 << 11); // Default(0) + Environment(11)

    [SerializeField, Tooltip("真下の床を探す最大距離（m）。これより下に床が無ければ「掴まれた地点」へ戻す")]
    private float groundSearchDistance = 30f;

    [SerializeField, Tooltip("立てるかどうかの判定に使うプレイヤーの想定身長（m）")]
    private float playerHeight = 1.8f;

    [SerializeField, Tooltip("立てるかどうかの判定に使うプレイヤーの想定半径（m）")]
    private float playerRadius = 0.2f;

    [Header("デバッグ設定")]
    [SerializeField, Tooltip("動作ログを Console に出力する")]
    private bool enableDebugLog = false;

    // 掴んでいるプレイヤーの playerId（-1 = 誰も掴んでいない）
    [UdonSynced] private int _capturedPlayerId = -1;

    // 掴んだ側のプレイヤーの playerId（-1 = なし）。
    // 掴んだ本人が退室したまま対象が拘束され続ける事故を防ぐために保持する。
    [UdonSynced] private int _grabberPlayerId = -1;

    // 解除時に対象へ与える慣性（ワールド速度 m/秒）。掴んだ側(Owner)が保持ターゲットの
    // 実移動速度を書き込み、対象本人のクライアントが降車後に SetVelocity で反映する。
    [UdonSynced] private Vector3 _releaseVelocity = Vector3.zero;

    private VRCPlayerApi _local;
    private bool _isSeatedLocally = false; // 自分がこの Station に着席中か
    private float _currentDistance = 3f;   // 現在の保持距離（Owner が制御）
    private float _lookVerticalInput = 0f; // 右スティック上下の入力値（毎フレーム保持し滑らかに反映）
    private bool _wasOwner = false;        // 前フレームに自分が Owner だったか（所有権の移り変わり検出用）

    // スタック対策：掴まれた瞬間の自分の位置・向き（ローカル専用・同期しない）
    private Vector3 _rescuePosition = Vector3.zero;
    private Quaternion _rescueRotation = Quaternion.identity;
    private bool _hasRescuePoint = false;

    // 投擲用：保持ターゲット(Station)の移動速度を Owner が毎フレーム計測する（ローカル専用）
    private Vector3 _prevStationPos = Vector3.zero;
    private Vector3 _stationVelocity = Vector3.zero;

    // 降車ポップ補正：降車直前の自分の実足元位置・向き（ローカル専用・同期しない）
    private Vector3 _preExitPos = Vector3.zero;
    private Quaternion _preExitRot = Quaternion.identity;

    private const int NO_PLAYER = -1;
    private const float PARK_DEPTH = -1000f;    // 待機位置未設定時の退避 Y 座標（地下）
    private const float STAND_CLEARANCE = 0.05f; // 立てるか判定する際、床自体を拾わないよう浮かせる高さ（m）
    private const int RESCUE_DELAY_FRAMES = 2;   // 降車処理の完了を待ってから救出判定するフレーム数

    // ============================================================
    // 初期化
    // ============================================================

    void Start()
    {
        _local = Networking.LocalPlayer;
        _currentDistance = defaultDistance;

        // 銃口未設定なら自分自身の Transform を照準起点に使う
        if (muzzle == null) muzzle = transform;

        if (holdStation == null)
        {
            Debug.LogError("[MET_PlayerGrabSystem] 「保持ターゲット」(VRCStation) が未設定です。", this);
        }
        else
        {
            // 保持ターゲットの Collider を常時無効化する。
            // Seated=OFF の VRCStation は Collider があると「手動でUSE(乗る)」判定を
            // 生み、掴まれている本人や周囲に透明な「使う」プロンプトが出てしまう。
            // 掴みの着席はプログラム(UseStation)で行うため Collider は不要。
            if (holdStationCollider == null)
                holdStationCollider = holdStation.GetComponent<Collider>();
            if (holdStationCollider != null)
                holdStationCollider.enabled = false;
        }

        // 起動時は誰も掴んでいない → Station を退避させて片付ける
        // （Owner のみが移動し、VRC Object Sync で全員へ配布される）
        ParkHoldStation();
    }

    // ============================================================
    // トリガー：掴む / 落とす（トグル）
    // ============================================================

    public override void OnPickupUseDown()
    {
        if (!Utilities.IsValid(_local)) return;

        if (enableDebugLog)
            Debug.Log("[MET_PlayerGrabSystem] トリガー検知", this);

        TakeOwnership();

        if (_capturedPlayerId == NO_PLAYER)
        {
            // --- 掴む：照準内の最寄りプレイヤーを対象化 ---
            int target = FindAimedPlayer();
            if (target == NO_PLAYER)
            {
                if (enableDebugLog)
                    Debug.Log($"[MET_PlayerGrabSystem] 照準内に対象なし（現在の人数={VRCPlayerApi.GetPlayerCount()}）", this);
                return;
            }

            // 掴んだ瞬間は「今の実距離」を保持距離の初期値にする。
            // 固定値(defaultDistance)にすると対象が急に引き寄せ／押し出しされ、
            // テレポートしたように感じるため、その場の位置を維持する。
            VRCPlayerApi tp = VRCPlayerApi.GetPlayerById(target);
            if (Utilities.IsValid(tp))
            {
                Vector3 aimPoint = tp.GetPosition() + Vector3.up * aimHeightOffset;
                float grabDist = Vector3.Distance(muzzle.position, aimPoint);
                _currentDistance = Mathf.Clamp(grabDist, minDistance, maxDistance);
            }
            else
            {
                _currentDistance = defaultDistance;
            }

            // 掴む直前に保持ターゲットを対象付近へ寄せて、ワープによるIK乱れを軽減
            SnapHoldTargetToItem();

            _capturedPlayerId = target;
            _grabberPlayerId = _local.playerId;
            _releaseVelocity = Vector3.zero; // 前回の投擲速度が残らないよう掴み直しでリセット
            RequestSerialization();
            ApplyCaptureState();

            if (enableDebugLog)
                Debug.Log($"[MET_PlayerGrabSystem] 持ち上げ: playerId={target}", this);
        }
        else
        {
            // --- 落とす ---
            Release();
        }
    }

    // アイテムを手放しても保持は解除しない（完全トグル仕様）。
    // アイテムは重力を受けない設定なので手放した位置に留まり、保持ターゲットも
    // そこへ追従したまま静止する。解除したくなったら誰かが拾い直してトリガーを押す。
    public override void OnDrop()
    {
        _lookVerticalInput = 0f; // 手放した時点で距離調整入力は打ち切る

        if (enableDebugLog && _capturedPlayerId != NO_PLAYER)
            Debug.Log("[MET_PlayerGrabSystem] アイテムを手放したが保持は継続", this);
    }

    private void Release()
    {
        // 解除の瞬間の保持ターゲットの実速度を投擲速度として配布する。
        // 移動しながら離せば速度が乗り、止めて離せばほぼ0（＝その場で落ちる）。
        // ※ 退室補正など Station を動かしていない解除では _stationVelocity≈0 のため飛ばない。
        _releaseVelocity = Vector3.ClampMagnitude(_stationVelocity * throwStrength, maxThrowSpeed);

        _capturedPlayerId = NO_PLAYER;
        _grabberPlayerId = NO_PLAYER;
        _lookVerticalInput = 0f;
        RequestSerialization();
        ApplyCaptureState();

        // 【重要】Station はここでは動かさない。
        // 掴まれていた本人は各自のクライアントで ExitStation を呼んで降りるが、
        // 降車位置は「その時点の Station の現在地」になる。ここで即座に退避させると、
        // 同期が届いた本人が退避位置（原点/親の場所）で降ろされて飛ばされてしまう。
        // そこで Station は保持位置に残したまま、全員の降車が済む猶予を置いてから
        // 退避させる（遅延実行）。これで「今アイテムが指している場所」に降ろせる。
        SendCustomEventDelayedSeconds(nameof(ParkHoldStation), releaseParkDelay);

        if (enableDebugLog)
            Debug.Log("[MET_PlayerGrabSystem] 落とす", this);
    }

    // ============================================================
    // 距離調整：右スティック上下（視点入力を流用）
    // ============================================================

    public override void InputLookVertical(float value, UdonInputEventArgs args)
    {
        // ここでは入力値を保持するだけ。実際の距離更新は PostLateUpdate で
        // 毎フレーム行う。イベントは入力変化時中心にしか飛ばないため、
        // 直接ここで加算するとスティック倒しっぱなしでカクついてしまう。
        _lookVerticalInput = value;
    }

    // ============================================================
    // 保持ターゲットの位置更新（Owner のみ・保持中）
    // ============================================================

    public override void PostLateUpdate()
    {
        if (_capturedPlayerId == NO_PLAYER) return;
        if (holdStation == null) return;

        bool isOwner = Networking.IsOwner(gameObject);
        if (!isOwner)
        {
            _wasOwner = false;
            return;
        }

        // 保持中に所有権を引き継いだ直後（＝他人が掴んだ相手を横取り／解除しに来た）は、
        // 自分の _currentDistance が前任者の値と食い違う。そのまま追従させると対象が
        // 一瞬でワープするので、現在の実距離を引き継いで滑らかさを保つ。
        if (!_wasOwner)
        {
            _wasOwner = true;
            float handoverDist = Vector3.Distance(muzzle.position, holdStation.transform.position);
            _currentDistance = Mathf.Clamp(handoverDist, minDistance, maxDistance);

            // 所有権を引き継いだ直後は速度計測の基準を今の位置に置き直す
            // （前任者との位置差で誤った巨大速度が出るのを防ぐ）
            _prevStationPos = holdStation.transform.position;
            _stationVelocity = Vector3.zero;
        }

        // 右スティック上下による距離更新を毎フレーム反映（シームレスな変化）
        if (Mathf.Abs(_lookVerticalInput) > 0.01f)
        {
            _currentDistance += _lookVerticalInput * distanceSpeed * Time.deltaTime;
            _currentDistance = Mathf.Clamp(_currentDistance, minDistance, maxDistance);
        }

        Vector3 targetPos = muzzle.position + muzzle.forward * _currentDistance;
        Transform st = holdStation.transform;

        // 指数補間で滑らかに追従（急なワープを避けてアバターのIK乱れを軽減）
        float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        st.position = Vector3.Lerp(st.position, targetPos, t);

        // 投擲用：保持ターゲットの実移動速度を計測（対象が実際に体感した速度）。
        // ノイズを抑えるため軽く平滑化する。解除時にこの値を慣性として与える。
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Vector3 instVelocity = (st.position - _prevStationPos) / dt;
            _stationVelocity = Vector3.Lerp(_stationVelocity, instVelocity, 0.5f);
        }
        _prevStationPos = st.position;
    }

    // 掴む瞬間に保持ターゲットをアイテム前方へ即座に配置（追従の初期位置）
    private void SnapHoldTargetToItem()
    {
        if (holdStation == null) return;
        if (!Networking.IsOwner(holdStation.gameObject)) return;
        holdStation.transform.position = muzzle.position + muzzle.forward * _currentDistance;

        // 掴んだ直後は速度計測の基準を初期化（初回フレームで誤速度が出ないように）
        _prevStationPos = holdStation.transform.position;
        _stationVelocity = Vector3.zero;
    }

    // 未使用時に保持ターゲット(VRCStation)を遠くへ退避させて片付ける。
    // 地下(または指定位置)へ隠す。Owner のみが移動し、VRC Object Sync で全員へ同期。
    // ※ SendCustomEventDelayedSeconds から遅延実行されるため public。
    public void ParkHoldStation()
    {
        if (holdStation == null) return;
        if (!Networking.IsOwner(holdStation.gameObject)) return;
        // 遅延中に新たに誰かを掴んだ場合は退避しない（保持位置を尊重する）
        if (_capturedPlayerId != NO_PLAYER) return;

        Vector3 pos = (parkPoint != null)
            ? parkPoint.position
            : new Vector3(0f, PARK_DEPTH, 0f);
        holdStation.transform.position = pos;
    }

    // ============================================================
    // 同期反映（全クライアント）
    // ============================================================

    public override void OnDeserialization()
    {
        ApplyCaptureState();
    }

    // 「自分が掴まれているか」を判定し、Station の着席状態を合わせる
    private void ApplyCaptureState()
    {
        if (holdStation == null) return;
        if (!Utilities.IsValid(_local)) return;

        bool iAmTarget = (_capturedPlayerId == _local.playerId);

        // ※ OnStationEntered/Exited のコールバックに依存せず、着席操作の
        //   直後に自分でフラグを更新する（Station を別オブジェクトに置いても
        //   確実に動くように）。Disable Station Exit=ON 前提で状態がズレない。
        if (iAmTarget && !_isSeatedLocally)
        {
            // 掴まれる直前の自分の位置を控えておく（救出できなかった時の戻り先）
            _rescuePosition = _local.GetPosition();
            _rescueRotation = _local.GetRotation();
            _hasRescuePoint = true;

            holdStation.UseStation(_local);  // 自分が掴まれた → 拘束
            _isSeatedLocally = true;
        }
        else if (!iAmTarget && _isSeatedLocally)
        {
            // 【不具合修正①】VRCStation は「降車時に足元を Station 原点へ置く」ため、
            // 着席中の見た目位置（腰が原点／足元は下）より上へ飛び出してしまう。
            // 降車直前の実足元位置・向きを控えておき、降車後にそこへ戻す。
            _preExitPos = _local.GetPosition();
            _preExitRot = _local.GetRotation();

            holdStation.ExitStation(_local); // 解除された → 降りる
            _isSeatedLocally = false;

            // 降車処理が完了してから位置補正・投擲・検算を行う
            // （同フレームだと降車前の座標を読む／降車ワープに上書きされるため遅延）
            SendCustomEventDelayedFrames(nameof(AfterStationExit), RESCUE_DELAY_FRAMES);
        }
    }

    // ============================================================
    // スタック対策（降ろされた本人のクライアントでのみ実行）
    // ============================================================

    // 降車完了後の後処理（降ろされた本人のクライアントでのみ実行）。
    //   ① 降車ポップ補正：着席中の見た目位置へ戻す（不具合修正①）
    //   ② スタック救出：天井などで立てない場合は安全な位置へ
    //   ③ 投擲：移動しながら離された場合は慣性を与えて飛ばす（機能追加②）
    // ※ SendCustomEventDelayedFrames から遅延実行されるため public。
    //   （メソッド名は VRChat 予約イベント OnStationExited と衝突するため別名にしている）
    public void AfterStationExit()
    {
        if (!Utilities.IsValid(_local)) return;
        if (_isSeatedLocally) return; // 猶予中に掴み直されていたら何もしない

        // ① 降車ポップ補正：着席中の実足元位置へ戻し、上へのズレを打ち消す
        _local.TeleportTo(_preExitPos, _preExitRot);

        // ② スタック救出（天井と床に挟まって動けない事故を防ぐ）。
        //    救出でワープした場合は投擲すると変な方向へ飛ぶため投げない。
        bool rescued = TryRescue(_preExitPos);

        // ③ 投擲：移動しながら離された時のみ慣性を与える（止めて離せば速度≈0）
        if (!rescued && _releaseVelocity.sqrMagnitude > 0.0001f)
        {
            // TeleportTo は速度を打ち消すため、必ずその後に速度を与える
            _local.SetVelocity(_releaseVelocity);
            if (enableDebugLog)
                Debug.Log($"[MET_PlayerGrabSystem] 投擲: 速度={_releaseVelocity} (大きさ={_releaseVelocity.magnitude})", this);
        }
    }

    // 降ろされた位置で立てない（天井と床に挟まっている）場合に安全な位置へ救出する。
    // 戻り値: 救出のためワープしたら true（＝投擲を中止する合図）。
    private bool TryRescue(Vector3 here)
    {
        if (!enableStuckRescue) return false;
        if (CanStandAt(here)) return false; // 問題なく立てているなら触らない

        // ① 真下の床を探して、そこで立てるならその床へ降ろす
        Vector3 origin = here + Vector3.up * playerHeight;
        RaycastHit hit;
        if (Physics.Raycast(origin, Vector3.down, out hit,
                            groundSearchDistance + playerHeight,
                            groundLayers.value, QueryTriggerInteraction.Ignore))
        {
            if (CanStandAt(hit.point))
            {
                _local.TeleportTo(hit.point, _local.GetRotation());
                if (enableDebugLog)
                    Debug.Log($"[MET_PlayerGrabSystem] 救出: 真下の床へ降ろした {hit.point}", this);
                return true;
            }
        }

        // ② 床がない／床でも立てない → 掴まれた瞬間の位置へ戻す
        if (_hasRescuePoint)
        {
            _local.TeleportTo(_rescuePosition, _rescueRotation);
            if (enableDebugLog)
                Debug.Log($"[MET_PlayerGrabSystem] 救出: 掴まれた地点へ戻した {_rescuePosition}", this);
            return true;
        }

        if (enableDebugLog)
            Debug.LogWarning("[MET_PlayerGrabSystem] 救出先が見つからなかった", this);
        return false;
    }

    // 指定した足元座標にプレイヤーが立てる隙間があるか（頭上・周囲のクリアランス判定）
    private bool CanStandAt(Vector3 footPos)
    {
        float r = playerRadius;

        // 床自体を拾わないよう少し浮かせた位置から、身長ぶんのカプセルを検査する
        Vector3 bottom = footPos + Vector3.up * (r + STAND_CLEARANCE);
        Vector3 top = footPos + Vector3.up * (playerHeight - r);
        if (top.y <= bottom.y) return false; // 身長設定が半径より小さい等の異常値

        return !Physics.CheckCapsule(bottom, top, r, groundLayers.value, QueryTriggerInteraction.Ignore);
    }

    // ============================================================
    // プレイヤー退室補正（マスターのみ）
    // ============================================================

    // 手放しても解除されない仕様のため、関係者が退室すると対象が拘束されたまま
    // 取り残される恐れがある。そこで以下のどちらかで自動解除する。
    //   ・掴まれていた本人が退室した   → 保持を畳んで片付ける
    //   ・掴んだ側が退室した           → 残された対象を解放する
    // 判定はマスターに一本化する（全員が同時に解除を試みると競合するため）。
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (_capturedPlayerId == NO_PLAYER) return;
        if (!Utilities.IsValid(_local) || !_local.isMaster) return;

        // 退室済みの player は無効化されている場合があるため playerId だけを読む
        int leftId = (player != null) ? player.playerId : NO_PLAYER;
        if (leftId == NO_PLAYER) return;

        bool targetLeft = (leftId == _capturedPlayerId);
        bool grabberLeft = (leftId == _grabberPlayerId);
        if (!targetLeft && !grabberLeft) return;

        TakeOwnership();

        if (targetLeft)
        {
            // 降車する本人がもういないので、退避を待つ必要はない
            _capturedPlayerId = NO_PLAYER;
            _grabberPlayerId = NO_PLAYER;
            RequestSerialization();
            ApplyCaptureState();
            ParkHoldStation();
        }
        else
        {
            // 掴まれた本人は残っているので、通常の解除（降車の猶予つき）を行う
            Release();
        }

        if (enableDebugLog)
            Debug.Log($"[MET_PlayerGrabSystem] 退室により自動解除: leftId={leftId}", this);
    }

    // ============================================================
    // ヘルパー
    // ============================================================

    // アイテムと保持ターゲット両方の所有権を自分に揃える
    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(_local, gameObject);

        if (holdStation != null && !Networking.IsOwner(holdStation.gameObject))
            Networking.SetOwner(_local, holdStation.gameObject);
    }

    // アイテム前方の照準内（距離・角度）で最も正面に近い他プレイヤーを返す
    private int FindAimedPlayer()
    {
        int count = VRCPlayerApi.GetPlayerCount();
        if (count <= 0) return NO_PLAYER;

        VRCPlayerApi[] players = new VRCPlayerApi[count];
        VRCPlayerApi.GetPlayers(players);

        Vector3 origin = muzzle.position;
        Vector3 forward = muzzle.forward;
        int bestId = NO_PLAYER;
        float bestAngle = aimAngle; // これより角度が小さい候補だけ採用

        foreach (VRCPlayerApi p in players)
        {
            if (!Utilities.IsValid(p)) continue;
            if (p.isLocal) continue; // 自分自身は掴まない

            Vector3 aimPoint = p.GetPosition() + Vector3.up * aimHeightOffset;
            Vector3 to = aimPoint - origin;
            float dist = to.magnitude;
            float ang = Vector3.Angle(forward, to);

            // デバッグ：候補ごとの距離・角度を出す（許容範囲と見比べて原因特定）
            if (enableDebugLog)
                Debug.Log($"[MET_PlayerGrabSystem] 候補 id={p.playerId} 距離={dist} 角度={ang}（許容 {aimRange}m / {aimAngle}度）", this);

            if (dist > aimRange || dist < 0.01f) continue;

            if (ang <= bestAngle)
            {
                bestAngle = ang;
                bestId = p.playerId;
            }
        }

        return bestId;
    }
}
