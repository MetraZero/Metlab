// ============================================================
// MET_DesktopCameraSystem
// 概要: あらかじめワールドに設置しておいたカメラへ、デスクトップ画面の
//       視点を切り替える「監視カメラ」システム。TABでON/OFFし、
//       数字キーでカメラを選ぶ。右ドラッグで向き、ホイールでズーム。
//
// バージョン: 2.1.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//   ※上の桁が上がった場合、下の桁は0にリセット
//
// [変更履歴]
//   2.1.0 - HUDにジェスチャー操作の案内を追加（カメラモードのON/OFF問わず常時表示）。
//           ジェスチャーシステムを割り当てた場合のみ表示され、未設定なら出ない。
//   2.0.1 - HUDの選択マークを ▶ から 【】 へ変更（日本語フォントで豆腐になるため）。
//           カメラ一覧の表記を「1：カメラ1」形式に統一。
//   2.0.0 - 表示方式を全面変更。カメラのDepthで画面を上書きする方式は
//           VRChat上で機能しないことが判明したため、RenderTexture へ描画し
//           Screen Space - Overlay の Canvas に全画面 RawImage で映す方式にした。
//           HUD専用カメラと専用レイヤーは不要になった。
//   1.0.0 - 初版（Depth上書き方式・不成立）。
//
// 操作仕様:
//   TAB          … システムのON/OFF（OFFで元の視点へ戻る）
//   1 / 2 / 3 …  … カメラ切り替え（ON中のみ・最大9台）
//   右ドラッグ    … カメラの向き調整（押している間だけ）
//   ホイール      … ズーム（画角）
//   ※ 向き・ズームはカメラごとに保持される。
//
// 【重要】この仕組みが成立する原理:
//   VRChat では、ワールドに置いた Camera の Depth を上げても画面描画を
//   上書きできない（実機で検証済み・機能しない）。
//   一方 Screen Space - Overlay の Canvas は、カメラを介さず画面へ直接描画され、
//   かつ VR時は HMD に出ず「PC側のウィンドウにのみ」現れる。
//   そこでカメラ映像を RenderTexture に焼き、その Canvas に全画面の RawImage
//   として貼ることで「PC画面だけ監視カメラに切り替わる」を実現する。
//   ※ 非VR起動でもVR起動でも、PC上に出るウィンドウが対象になる。
//
// 【重要】オブジェクト構成（手動セットアップ）:
//   ▼ RenderTexture（カメラの台数ぶん作る）
//       Project で右クリック → Create → Render Texture
//       サイズは 1280x720 程度で十分（大きいほど綺麗だが重い）
//   ▼ 視点カメラ（1〜3台程度・それぞれ別オブジェクト）
//       1. Camera
//            - Target Texture = 対応する RenderTexture を割り当てる
//            - Depth は既定(0)のままでよい（もう使わない）
//            - 初期の位置・向き・Field of View がそのまま初期値になる
//       2. Audio Listener は必ず削除する（音が二重になるため）
//       3. VRC Object Sync は付けないこと（各自が独立して操作するため）
//   ▼ Canvas（1つ・HUDと映像を兼ねる）
//       - Render Mode = Screen Space - Overlay（カメラ指定は不要）
//       - 子に RawImage … 画面全面に広げる。これがカメラ映像の表示先
//       - 子に TextMeshProUGUI … 案内表示。RawImage より手前に来るよう
//         ヒエラルキー上で RawImage の «下» に置くこと
//   ▼ 本スクリプト（管理用の空オブジェクトに付ける）
//       「視点カメラ」「カメラ映像」を同じ並び順で割り当て、
//       「映像表示先」に RawImage、「案内テキスト」に TextMeshProUGUI を刺す
//       （視点カメラの並び順が、そのまま 1・2・3 キーに対応する）
//
// 設計メモ / TODO（今後の拡張ポイント）:
//   - すべてローカル完結。同期しないため各プレイヤーが独立して使える。
//   - カメラ切替キーは 1〜9 固定。別キーにしたい場合は _CameraKeyFor を変更。
//   - ズームは Field of View で行う。もし Udon から書き込めない場合は
//     カメラを前後に動かすドリー方式へ差し替えること（_ApplyZoom を変更）。
// ============================================================

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_DesktopCameraSystem : UdonSharpBehaviour
{
    [Header("■ 視点カメラ")]
    [SerializeField, Tooltip("切り替えて表示するカメラ。上から順に 1・2・3… キーへ対応（最大9台）。\n各カメラの Target Texture に、下の「カメラ映像」と同じ RenderTexture を割り当てておくこと")]
    private Camera[] viewCameras;

    [SerializeField, Tooltip("各カメラが描画する RenderTexture。上の「視点カメラ」と同じ並び順にすること")]
    private RenderTexture[] cameraTextures;

    [SerializeField, Tooltip("カメラ映像を映す全画面の RawImage（Screen Space - Overlay の Canvas 配下）")]
    private RawImage screenView;

    [SerializeField, Tooltip("案内文に出すカメラ名（上の並びと対応）。空欄や数が足りない分は「カメラ1」等を自動生成")]
    private string[] cameraLabels;

    [Header("■ HUD（画面隅の案内）")]
    [SerializeField, Tooltip("案内を表示する TextMeshProUGUI。HUDカメラ配下の Canvas に置くこと")]
    private TextMeshProUGUI hudText;

    [SerializeField, Tooltip("システムOFF中に出す案内文")]
    private string hudMessageOff = "[TAB] カメラを見る";

    [SerializeField, Tooltip("システムON中、操作説明として末尾に付ける案内文")]
    private string hudMessageOn = "右ドラッグ: 向き  ホイール: ズーム";

    [Header("■ ジェスチャーシステム連携（任意）")]
    [SerializeField, Tooltip("シーン内のジェスチャーシステム。割り当てるとHUDにジェスチャーの案内を追加する。\n未設定なら案内は出ない（ジェスチャーシステムを置いていないワールドでも使えるように）")]
    private MET_GestureSystemManager gestureManager;

    [SerializeField, Tooltip("ジェスチャーの案内文。カメラモードのON/OFFに関わらず常に表示する。\n※ジェスチャー側の受付キーを変更した場合は、ここの文言も合わせて直すこと")]
    private string gestureMessage = "[G] ＋ WASD でジェスチャー入力";

    [Header("■ キー割り当て")]
    [SerializeField, Tooltip("システムのON/OFFを切り替えるキー")]
    private KeyCode toggleKey = KeyCode.Tab;

    [Header("■ 向き調整")]
    [SerializeField, Tooltip("ON=マウス右ボタンを押している間だけ向きを変えられる（誤操作防止・推奨）\nOFF=ON中は常にマウスで向きが変わる")]
    private bool requireRightClickToAim = true;

    [SerializeField, Tooltip("マウスでの回転速度（度/秒）")]
    private float lookSpeed = 120f;

    [SerializeField, Tooltip("上下を向ける限界角度（度）。真上・真下で反転するのを防ぐ")]
    private float pitchLimit = 80f;

    [Header("■ ズーム")]
    [SerializeField, Tooltip("ホイールでのズーム速度（度/ノッチ）")]
    private float zoomSpeed = 40f;

    [SerializeField, Tooltip("最小画角（度）。小さいほど望遠（拡大）")]
    private float minFov = 15f;

    [SerializeField, Tooltip("最大画角（度）。大きいほど広角（縮小）")]
    private float maxFov = 90f;

    [Header("■ その他")]
    [SerializeField, Tooltip("OFFにした時、ONにした瞬間の体の向きへ戻す。\n※VRChatの仕様上マウスを動かすと裏で自分の頭も回ってしまうため、その打ち消し")]
    private bool restoreRotationOnExit = true;

    [SerializeField, Tooltip("動作ログを Console に出力する")]
    private bool enableDebugLog = false;

    // ---- 内部状態（すべてローカル・同期しない）----
    private bool _active = false;
    private int _currentIndex = -1;

    // カメラごとの向き・画角を保持する（切り替えて戻ってきても維持される）
    private float[] _yaw;
    private float[] _pitch;
    private float[] _fov;

    // 右スティック／マウス移動の入力値（Udonの入力イベントで受け取る）
    private float _lookH = 0f;
    private float _lookV = 0f;

    // ON時の体の向き（OFF時に戻すため）
    private Quaternion _savedRotation = Quaternion.identity;
    private bool _hasSavedRotation = false;

    private const int MAX_CAMERAS = 9; // 1〜9キーに対応するため

    // ============================================================
    // 初期化
    // ============================================================

    void Start()
    {
        if (viewCameras == null || viewCameras.Length == 0)
        {
            Debug.LogError("[MET_DesktopCameraSystem] 「視点カメラ」が1台も設定されていません。", this);
            return;
        }

        int n = viewCameras.Length;
        _yaw = new float[n];
        _pitch = new float[n];
        _fov = new float[n];

        // 各カメラの「置いたときの向き・画角」を初期値として控える
        for (int i = 0; i < n; i++)
        {
            Camera c = viewCameras[i];
            if (c == null) continue;

            Vector3 e = c.transform.eulerAngles;
            _yaw[i] = e.y;
            _pitch[i] = _NormalizeAngle(e.x); // 0〜360 を -180〜180 に直す
            _fov[i] = c.fieldOfView;
        }

        if (cameraTextures == null || cameraTextures.Length < n)
        {
            Debug.LogError("[MET_DesktopCameraSystem] 「カメラ映像」(RenderTexture) の数が「視点カメラ」より少ないです。同じ並び順で同じ数だけ設定してください。", this);
        }

        if (screenView == null)
        {
            Debug.LogError("[MET_DesktopCameraSystem] 「映像表示先」(RawImage) が未設定です。", this);
        }

        _DisableAllCameras();
        _ShowScreen(false);
        _active = false;
        _UpdateHud();

        if (viewCameras.Length > MAX_CAMERAS)
        {
            Debug.LogWarning($"[MET_DesktopCameraSystem] カメラが{MAX_CAMERAS}台を超えています。{MAX_CAMERAS + 1}台目以降はキーで選べません。", this);
        }
    }

    // ============================================================
    // 毎フレームのキー・マウス処理
    // ============================================================

    void Update()
    {
        // TAB は ON/OFF 問わず常に受け付ける
        if (Input.GetKeyDown(toggleKey)) _Toggle();

        if (!_active) return;

        _HandleCameraSwitch();
        _HandleLook();
        _HandleZoom();
    }

    // 数字キーでのカメラ切り替え
    private void _HandleCameraSwitch()
    {
        int n = viewCameras.Length;
        if (n > MAX_CAMERAS) n = MAX_CAMERAS;

        for (int i = 0; i < n; i++)
        {
            KeyCode k = _CameraKeyFor(i);
            if (k == KeyCode.None) continue;
            if (Input.GetKeyDown(k))
            {
                _SelectCamera(i);
                return; // 同フレームに複数切り替えない
            }
        }
    }

    // マウスでの向き調整
    private void _HandleLook()
    {
        if (_currentIndex < 0) return;
        if (requireRightClickToAim && !Input.GetMouseButton(1)) return;
        if (Mathf.Abs(_lookH) < 0.001f && Mathf.Abs(_lookV) < 0.001f) return;

        float dt = Time.deltaTime;
        _yaw[_currentIndex] += _lookH * lookSpeed * dt;
        _pitch[_currentIndex] -= _lookV * lookSpeed * dt; // 上入力で上を向く
        _pitch[_currentIndex] = Mathf.Clamp(_pitch[_currentIndex], -pitchLimit, pitchLimit);

        _ApplyRotation();
    }

    // ホイールでのズーム
    private void _HandleZoom()
    {
        if (_currentIndex < 0) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // 手前に回す（負）と広角、奥に回す（正）と望遠になるよう符号を反転
        _fov[_currentIndex] = Mathf.Clamp(_fov[_currentIndex] - scroll * zoomSpeed, minFov, maxFov);
        _ApplyZoom();
    }

    // ============================================================
    // ON / OFF
    // ============================================================

    private void _Toggle()
    {
        if (_active) _Deactivate();
        else _Activate();
    }

    private void _Activate()
    {
        if (viewCameras == null || viewCameras.Length == 0) return;

        // OFF時に戻すため、今の体の向きを控えておく
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (Utilities.IsValid(lp))
        {
            _savedRotation = lp.GetRotation();
            _hasSavedRotation = true;
        }

        _active = true;
        _SelectCamera(0); // 既定はカメラ1

        if (enableDebugLog) Debug.Log("[MET_DesktopCameraSystem] システムON", this);
    }

    private void _Deactivate()
    {
        _active = false;
        _currentIndex = -1;
        _DisableAllCameras();
        _ShowScreen(false); // 映像を隠して元の視界へ戻す
        _UpdateHud();

        // マウス操作で裏で回ってしまった体の向きを、ONにした時点へ戻す
        if (restoreRotationOnExit && _hasSavedRotation)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp))
                lp.TeleportTo(lp.GetPosition(), _savedRotation);
        }

        if (enableDebugLog) Debug.Log("[MET_DesktopCameraSystem] システムOFF", this);
    }

    // 指定番号のカメラだけを有効にする
    private void _SelectCamera(int index)
    {
        if (viewCameras == null) return;
        if (index < 0 || index >= viewCameras.Length) return;
        if (viewCameras[index] == null) return;

        for (int i = 0; i < viewCameras.Length; i++)
        {
            Camera c = viewCameras[i];
            if (c == null) continue;
            c.gameObject.SetActive(i == index);
        }

        _currentIndex = index;

        // 選んだカメラの RenderTexture を全画面 RawImage へ映す
        if (screenView != null && cameraTextures != null &&
            index < cameraTextures.Length && cameraTextures[index] != null)
        {
            screenView.texture = cameraTextures[index];
        }
        _ShowScreen(true);

        // 前回このカメラを見ていた時の向き・画角を復元する
        _ApplyRotation();
        _ApplyZoom();
        _UpdateHud();

        if (enableDebugLog) Debug.Log($"[MET_DesktopCameraSystem] カメラ{index + 1}へ切り替え", this);
    }

    // カメラ映像の表示/非表示。OFF中は描画コストを持たせない。
    private void _ShowScreen(bool show)
    {
        if (screenView == null) return;
        screenView.gameObject.SetActive(show);
    }

    private void _DisableAllCameras()
    {
        if (viewCameras == null) return;
        for (int i = 0; i < viewCameras.Length; i++)
        {
            Camera c = viewCameras[i];
            if (c != null) c.gameObject.SetActive(false);
        }
    }

    // ============================================================
    // カメラへの反映
    // ============================================================

    private void _ApplyRotation()
    {
        if (_currentIndex < 0) return;
        Camera c = viewCameras[_currentIndex];
        if (c == null) return;

        c.transform.rotation = Quaternion.Euler(_pitch[_currentIndex], _yaw[_currentIndex], 0f);
    }

    private void _ApplyZoom()
    {
        if (_currentIndex < 0) return;
        Camera c = viewCameras[_currentIndex];
        if (c == null) return;

        c.fieldOfView = _fov[_currentIndex];
    }

    // ============================================================
    // 入力イベント（マウス移動＝視点入力として届く）
    // ============================================================
    //
    // ※ Input.GetAxis("Mouse X") ではなく Udon の入力イベントを使う。
    //   デスクトップではこれがマウス移動そのものであり、カーソルロック状態でも
    //   確実に値が取れるため。値の保持だけ行い、実際の反映は Update で行う。

    public override void InputLookHorizontal(float value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        _lookH = value;
    }

    public override void InputLookVertical(float value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        _lookV = value;
    }

    // ============================================================
    // HUD（案内表示）
    // ============================================================

    private void _UpdateHud()
    {
        if (hudText == null) return;

        if (!_active)
        {
            hudText.text = _WithGestureHint(hudMessageOff);
            return;
        }

        // 例）[TAB] 終了
        //     【1：玄関】 2：中庭  3：屋上
        //     右ドラッグ: 向き  ホイール: ズーム
        //
        // ※ 現在選択中は【】で囲って示す。矢印記号(▶など)は日本語フォントに
        //   収録されていないことが多く豆腐(□)になるため使わない。
        string s = "[TAB] 終了\n";

        int n = viewCameras.Length;
        if (n > MAX_CAMERAS) n = MAX_CAMERAS;

        for (int i = 0; i < n; i++)
        {
            if (viewCameras[i] == null) continue;

            string entry = (i + 1) + "：" + _LabelFor(i);
            if (i == _currentIndex) entry = "【" + entry + "】";
            s += entry + "  ";
        }

        s += "\n" + hudMessageOn;
        hudText.text = _WithGestureHint(s);
    }

    // ジェスチャーの案内を末尾に足す。
    // ジェスチャーシステムが未設定（＝シーンに無い）場合や、
    // キーボード入力が無効な場合は、使えない操作を案内しないよう何も足さない。
    private string _WithGestureHint(string body)
    {
        if (gestureManager == null) return body;
        if (!gestureManager.enableKeyboardInput) return body;
        if (gestureMessage == null || gestureMessage.Length == 0) return body;

        return body + "\n" + gestureMessage;
    }

    // 案内文に出すカメラ名。未設定なら「カメラ1」等を自動生成する
    private string _LabelFor(int index)
    {
        if (cameraLabels != null && index < cameraLabels.Length)
        {
            string l = cameraLabels[index];
            if (l != null && l.Length > 0) return l;
        }
        return "カメラ" + (index + 1);
    }

    // ============================================================
    // ヘルパー
    // ============================================================

    // カメラ番号に対応するキー。既定は 1〜9。
    // ※ 別キーにしたい場合はここだけ書き換えれば全体へ反映される。
    private KeyCode _CameraKeyFor(int index)
    {
        switch (index)
        {
            case 0: return KeyCode.Alpha1;
            case 1: return KeyCode.Alpha2;
            case 2: return KeyCode.Alpha3;
            case 3: return KeyCode.Alpha4;
            case 4: return KeyCode.Alpha5;
            case 5: return KeyCode.Alpha6;
            case 6: return KeyCode.Alpha7;
            case 7: return KeyCode.Alpha8;
            case 8: return KeyCode.Alpha9;
        }
        return KeyCode.None;
    }

    // 0〜360 の角度を -180〜180 に直す（Clamp を正しく効かせるため）
    private float _NormalizeAngle(float angle)
    {
        angle = Mathf.Repeat(angle, 360f);
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
