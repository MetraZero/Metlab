// =============================================================
// MET_GestureSystemManager.cs
// MET_ Gesture System - 大元のマネージャ
// Version: 1.4.1
//
// [概要]
//   ローカルプレイヤーの手または視点の軌跡を追い、4方向(上下左右)の列に
//   変換して子オブジェクトのコマンドと照合、一致した1コマンドを発動する。
//   追従オブジェクトは使わず、GetTrackingData と入力イベントで完結。
//
//   トラッキング方式(trackMode)は3種：
//     HandWorld … 手・ワールド固定（起き上がり想定）
//     HandHead  … 手・頭相対（寝ても使えるが酔いやすい）
//     Gaze      … 視点操作（体勢無関係・手フリー・V睡向き）
//
//   記録トリガー(gestureMode)は別軸で選択（両手トリガー等）。
//   Colliderを付ければ isTrigger 範囲内でのみ有効。無ければ全域。
//
//   【キーボード入力（デスクトップ救済）】
//   デスクトップでは腕を振れず、既定の「両手トリガー」は構造的に発動不可能。
//   そこで受付キー(既定G)を押している間だけWASDで方向を直接入力し、
//   離すと確定する経路を用意した。手/視点の軌跡認識とは独立しているが、
//   方向列バッファと照合処理は共有しているため、コマンド定義(pattern)は
//   VR・デスクトップで完全に共通のまま使える。
//
// [配置]
//   GestureSystemManager (このスクリプト + 任意でCollider/isTrigger)
//     └ Commands (空オブジェクト＝フォルダ役)
//          ├ コマンドデータ (MET_GestureCommand)
//          └ ...
//
// [Changelog]
//   1.4.1 - 方向キーの既定を矢印キーからWASDへ変更（デスクトップ操作の主流に合わせる）。
//   1.4.0 - キーボードによる方向入力に対応（デスクトップからコマンドを発動可能に）。
//           受付キー押下中に方向キーで入力、離すと確定。入力中は誤移動を防ぐため
//           プレイヤーを一時的に移動不可にする（オプション）。
//   1.3.1 - 発動者位置SEを頭固定から「描いた手（VR時）／頭（デスクトップ）」へ修正。
//   1.3.0 - YamaPlayer音量調整コマンド対応（yamaController参照・全コマンドへ配布）。
//   1.2.3 - コマンドのsoundClipをAudioSEへ自動登録（グローバル移動SEのclip同期）。
//   1.2.2 - 発動者位置取得(_GetPerformerPosition)を追加（移動SE用）。
//   1.2.1 - スカイボックス収集をFlipのA/B両Material対応に拡張。
//   1.2.0 - グローバルスカイボックス切り替え対応（skyboxSync連携・自動収集）。
//   1.1.2 - idle確定を常時監視モード限定に戻す。トリガー系はトリガー解放時のみ判定。
//   1.1.1 - idle確定を全モード共通化。トリガー押しっぱなしでも古い入力が
//           次の入力に持ち越されず、描き直しがチェーンできるよう修正。
//   1.1.0 - トラッキング3方式(手ワールド/手頭相対/視点)対応。ログボード連携。
//   1.0.0 - 初版。
// =============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using Yamadev.YamaStream;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_GestureSystemManager : UdonSharpBehaviour
{
    [Header("■ トラッキング方式")]
    [Tooltip("HandWorld＝手・ワールド固定（起き上がり想定）\n" +
             "HandHead＝手・頭相対（寝ても使えるが酔いやすい）\n" +
             "Gaze＝視点操作（体勢無関係・手フリー・V睡向き）")]
    public MET_TrackMode trackMode = MET_TrackMode.HandWorld;

    [Header("■ ジェスチャーモード（記録トリガー）")]
    [Tooltip("両手トリガー＝両手のUse同時押し中（推奨）\n" +
             "片手トリガー＝描く手のUse\n" +
             "片手グリップ＝描く手のGrab\n" +
             "構え＋描画＝反対の手のGripで構え、描く手のUseで描画\n" +
             "常時監視＝トリガー不要・止まると確定（実験的）\n" +
             "※視点モードでも誤爆防止でトリガー併用を推奨")]
    public MET_GestureMode gestureMode = MET_GestureMode.BothTriggers;

    [Tooltip("ジェスチャーを描く手（手モード時のみ影響）")]
    public MET_DrawHand drawHand = MET_DrawHand.Right;

    [Header("■ キーボード入力（デスクトップ救済）")]
    [Tooltip("キーボードでの方向入力を有効にする。\n" +
             "デスクトップでは腕を振れないため、これが無いとコマンドを発動できない。\n" +
             "VRの人がキーボードを使っても発動できる（併用可）")]
    public bool enableKeyboardInput = true;

    [Tooltip("このキーを押している間だけ方向入力を受け付ける。離すと確定。\n" +
             "VRChat既定の操作と衝突しないキーを選ぶこと（W/A/S/D・Space・Shift・V・T・Esc等は避ける）")]
    public KeyCode keyboardHoldKey = KeyCode.G;

    [Tooltip("「上」として扱うキー")]
    public KeyCode keyUp = KeyCode.W;

    [Tooltip("「下」として扱うキー")]
    public KeyCode keyDown = KeyCode.S;

    [Tooltip("「左」として扱うキー")]
    public KeyCode keyLeft = KeyCode.A;

    [Tooltip("「右」として扱うキー")]
    public KeyCode keyRight = KeyCode.D;

    [Tooltip("キーボード入力中はプレイヤーを移動不可にする。\n" +
             "方向キーはVRChat側でも移動に割り当たっているため、これをOFFにすると\n" +
             "コマンド入力中に歩き回ってしまう。WASD割り当てでは必ずONにすること。\n" +
             "※視点操作は止められない")]
    public bool immobilizeWhileTyping = true;

    [Header("■ 認識パラメータ")]
    [Tooltip("【手モード】この距離(m)以上動くと1方向として確定。\n" +
             "大きいほど鈍感＝誤爆しにくい。VRの手ブレを考慮して 0.08〜0.15 推奨。")]
    public float segmentDistance = 0.10f;

    [Tooltip("【視点モード】視線をこの角度(度)以上フリックすると1方向として確定。\n" +
             "首の可動域を考えて 10〜20 度が実用的。")]
    public float gazeThresholdDeg = 15f;

    [Tooltip("『常時監視』モードで、手/視点がこの秒数止まると方向列を確定します。\n" +
             "トリガー系モードでは使いません（トリガーを離した時に判定）。")]
    public float idleCommit = 0.6f;

    [Header("■ 参照")]
    [Tooltip("コマンドを収集する親。未指定ならこのオブジェクト配下を探索します")]
    public Transform commandsRoot;

    [Tooltip("グローバル同期用の状態管理。グローバルトグルを使うなら必須")]
    public MET_GestureStateSync stateSync;

    [Tooltip("グローバルスカイボックス切り替え用の同期。グローバルスカイボックスを使うなら必須")]
    public MET_GestureSkyboxSync skyboxSync;

    [Tooltip("ログボード（任意）。指定すると狭間メトラのみモード状態と方向列を表示")]
    public MET_GestureLogBoard logBoard;

    [Tooltip("音量調整コマンドの対象YamaPlayer（YamaStreamのController）。\n" +
             "音量調整アクションを使うなら設定必須。起動時に全コマンドへ配布されます")]
    public Controller yamaController;

    [Header("■ デバッグ")]
    [Tooltip("描いた方向列と発動コマンドをConsoleに出力（登録時に便利）")]
    public bool debugLog = true;

    // ---- 内部状態 ----
    private VRCPlayerApi _local;
    private MET_GestureCommand[] _commands;

    private bool _hasCollider;
    private bool _inRange;

    private bool _recording;
    private Vector3 _axisR;      // 記録開始時にラッチした右方向
    private Vector3 _axisU;      // 記録開始時にラッチした上方向
    private Vector2 _lastPoint;  // 前回確定した平面座標
    private float _threshold;    // 方向確定に必要な移動量（モードで単位が変わる）
    private int _lastDir;        // -1 = 未確定
    private float _lastMoveTime;

    private int[] _buffer;
    private int _bufCount;
    private const int MAX_LEN = 32;

    // 入力フラグ
    private bool _leftUse, _rightUse, _leftGrip, _rightGrip;

    // キーボード入力の状態（手/視点の軌跡記録とは独立して動く）
    private bool _keyRecording;
    private bool _immobilized;

    // =========================================================
    void Start()
    {
        _local = Networking.LocalPlayer;
        _buffer = new int[MAX_LEN];

        Collider col = GetComponent<Collider>();
        _hasCollider = (col != null);
        _inRange = !_hasCollider; // Colliderなければ常に範囲内

        _CollectCommands();
        _CollectGlobalToggleTargets();
        _CollectGlobalSkyboxMaterials();
        _CollectAudioSEClips();
        _AssignYamaController();
    }

    // =========================================================
    //  入力イベント（追従オブジェクト不要・ローカル完結）
    // =========================================================
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (args.handType == HandType.RIGHT) _rightUse = value;
        else _leftUse = value;
        _Evaluate();
    }

    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (args.handType == HandType.RIGHT) _rightGrip = value;
        else _leftGrip = value;
        _Evaluate();
    }

    // モードに応じて記録の開始/終了を判定
    private void _Evaluate()
    {
        if (gestureMode == MET_GestureMode.Always) return; // トリガー不要

        bool draw = (drawHand == MET_DrawHand.Right);
        bool want = false;

        if (gestureMode == MET_GestureMode.BothTriggers)
        {
            want = _leftUse && _rightUse;
        }
        else if (gestureMode == MET_GestureMode.OneTrigger)
        {
            want = draw ? _rightUse : _leftUse;
        }
        else if (gestureMode == MET_GestureMode.OneGrip)
        {
            want = draw ? _rightGrip : _leftGrip;
        }
        else if (gestureMode == MET_GestureMode.GripThenTrigger)
        {
            bool stance = draw ? _leftGrip : _rightGrip; // 反対の手で構え
            bool pull = draw ? _rightUse : _leftUse;     // 描く手で描画
            want = stance && pull;
        }

        if (want && !_recording) _StartRecording();
        else if (!want && _recording) _StopRecording();
    }

    // =========================================================
    void Update()
    {
        // キーボード入力は手/視点の記録とは独立した経路。先に処理する。
        if (enableKeyboardInput) _UpdateKeyboard();

        // キーボードで入力中は、手/視点の軌跡記録は動かさない（二重入力の防止）
        if (_keyRecording) return;

        // 常時監視モードは範囲に応じて自動で記録を開始/停止
        if (gestureMode == MET_GestureMode.Always)
        {
            if (_inRange && !_recording) _StartRecording();
            else if (!_inRange && _recording) _StopRecording();
        }

        if (!_recording) return;

        Vector2 cur = _GetPoint();
        Vector2 delta = cur - _lastPoint;
        float mag = delta.magnitude;

        if (mag >= _threshold)
        {
            int dir = _Quantize(delta.x, delta.y);
            if (dir != _lastDir) // 連続同方向は畳む
            {
                if (_bufCount < MAX_LEN)
                {
                    _buffer[_bufCount] = dir;
                    _bufCount++;
                    if (logBoard != null) logBoard._AppendDir(dir);
                }
                _lastDir = dir;
            }
            _lastPoint = cur;
            _lastMoveTime = Time.time;
        }

        // 常時監視モードのみ：止まったら確定してバッファをリセット（記録は継続）。
        // トリガー系モードは記録を止めた時（トリガー解放時）にのみ判定する。
        if (gestureMode == MET_GestureMode.Always)
        {
            if (_bufCount > 0 && (Time.time - _lastMoveTime) >= idleCommit)
            {
                _Match();
                _bufCount = 0;
                _lastDir = -1;
                if (logBoard != null) logBoard._BeginGesture();
            }
        }
    }

    // =========================================================
    //  キーボード入力（デスクトップ救済）
    // =========================================================
    //
    // 受付キーを押している間だけ方向キー(既定WASD)で方向を直接入力し、離すと確定する。
    // 手/視点の軌跡認識と違い閾値や座標変換が要らないため、認識ミスが起きない。
    // 方向列バッファ(_buffer)と照合(_Match)は共有しているので、コマンド定義は
    // VR・デスクトップで完全に共通のまま使える。
    //
    // ※ GetKeyDown/GetKeyUp ではなく GetKey で毎フレーム状態を見る。
    //   メニューを開くと Udon は入力を取れなくなり、押しっぱなしのキーが
    //   離された扱いになる。状態監視ならその場合も確定処理へ落ちる。
    private void _UpdateKeyboard()
    {
        bool held = Input.GetKey(keyboardHoldKey);

        if (held && !_keyRecording) _StartKeyRecording();
        else if (!held && _keyRecording) _StopKeyRecording();

        if (_keyRecording) _PollDirectionKeys();
    }

    private void _StartKeyRecording()
    {
        if (!_inRange) return;
        if (_recording) return; // 手/視点で記録中なら割り込まない

        _keyRecording = true;
        _bufCount = 0;
        _lastDir = -1;

        // 方向キー(既定WASD)は VRChat 側でも移動に割り当たっているため、
        // 入力中は歩き出さないよう一時的に固定する（視点操作は止められない）
        _SetImmobilized(true);

        if (logBoard != null)
        {
            logBoard._SetMode(true);
            logBoard._BeginGesture();
        }
    }

    private void _StopKeyRecording()
    {
        _keyRecording = false;
        _SetImmobilized(false);

        _Match();
        _bufCount = 0;
        _lastDir = -1;

        if (logBoard != null) logBoard._SetMode(false);
    }

    // 押された方向キーをバッファへ積む。
    // 連続する同方向は手/視点と同じく1つに畳む（コマンド定義の互換を保つため）。
    private void _PollDirectionKeys()
    {
        int dir = -1;
        if (Input.GetKeyDown(keyUp)) dir = (int)MET_GestureDir.Up;
        else if (Input.GetKeyDown(keyDown)) dir = (int)MET_GestureDir.Down;
        else if (Input.GetKeyDown(keyLeft)) dir = (int)MET_GestureDir.Left;
        else if (Input.GetKeyDown(keyRight)) dir = (int)MET_GestureDir.Right;

        if (dir < 0) return;
        if (dir == _lastDir) return;
        if (_bufCount >= MAX_LEN) return;

        _buffer[_bufCount] = dir;
        _bufCount++;
        _lastDir = dir;

        if (logBoard != null) logBoard._AppendDir(dir);
    }

    // プレイヤーの移動固定。二重呼び出しを避けて状態を管理する。
    private void _SetImmobilized(bool value)
    {
        // 解除は設定に関わらず必ず通す（設定を切り替えた際に固定が残らないように）
        if (value && !immobilizeWhileTyping) return;
        if (_immobilized == value) return;
        if (!Utilities.IsValid(_local)) return;

        _local.Immobilize(value);
        _immobilized = value;
    }

    // 範囲外へ出た／無効化された時に固定が残らないようにする保険
    void OnDisable()
    {
        if (_keyRecording)
        {
            _keyRecording = false;
            _bufCount = 0;
            _lastDir = -1;
        }
        _SetImmobilized(false);
    }

    // =========================================================
    //  記録開始/終了
    // =========================================================
    private void _StartRecording()
    {
        if (!_inRange) return;

        _recording = true;
        _bufCount = 0;
        _lastDir = -1;
        _lastMoveTime = Time.time;

        // 記録開始時の基準軸をラッチ
        VRCPlayerApi.TrackingData head =
            _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Quaternion hr = head.rotation;

        if (trackMode == MET_TrackMode.HandWorld)
        {
            // Yawのみのプレイヤー右方向、上下は世界Y
            Vector3 fwd = hr * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            _axisR = Vector3.Cross(Vector3.up, fwd).normalized;
            _axisU = Vector3.up;
            _threshold = segmentDistance;
        }
        else if (trackMode == MET_TrackMode.HandHead)
        {
            // 頭のright/upを基準（寝ても使える）
            _axisR = (hr * Vector3.right).normalized;
            _axisU = (hr * Vector3.up).normalized;
            _threshold = segmentDistance;
        }
        else // Gaze
        {
            // 頭のright/upを基準に、視線forwardの向き変化を追う
            _axisR = (hr * Vector3.right).normalized;
            _axisU = (hr * Vector3.up).normalized;
            _threshold = Mathf.Sin(gazeThresholdDeg * Mathf.Deg2Rad);
        }

        _lastPoint = _GetPoint();

        if (logBoard != null)
        {
            logBoard._SetMode(true);
            logBoard._BeginGesture();
        }
    }

    private void _StopRecording()
    {
        _recording = false;
        _Match();
        _bufCount = 0;
        _lastDir = -1;
        if (logBoard != null) logBoard._SetMode(false);
    }

    // =========================================================
    //  現在の平面座標（モードで取り方が変わる）
    // =========================================================
    private Vector2 _GetPoint()
    {
        if (trackMode == MET_TrackMode.Gaze)
        {
            VRCPlayerApi.TrackingData h =
                _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 f = h.rotation * Vector3.forward;
            return new Vector2(Vector3.Dot(f, _axisR), Vector3.Dot(f, _axisU));
        }

        Vector3 hand = _GetDrawHandPosition();
        if (trackMode == MET_TrackMode.HandHead)
        {
            return new Vector2(Vector3.Dot(hand, _axisR), Vector3.Dot(hand, _axisU));
        }
        // HandWorld
        return new Vector2(Vector3.Dot(hand, _axisR), hand.y);
    }

    // =========================================================
    //  4方向量子化・照合
    // =========================================================
    private int _Quantize(float x, float y)
    {
        if (Mathf.Abs(x) >= Mathf.Abs(y))
            return (x >= 0f) ? (int)MET_GestureDir.Right : (int)MET_GestureDir.Left;
        else
            return (y >= 0f) ? (int)MET_GestureDir.Up : (int)MET_GestureDir.Down;
    }

    private void _Match()
    {
        if (_bufCount == 0) return;

        if (debugLog)
            Debug.Log("[MET_GestureSystem] 認識した方向列: " + _DirString());

        if (!_inRange) return;
        if (_commands == null) return;

        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand cmd = _commands[i];
            if (cmd == null) continue;
            if (_IsMatch(cmd.pattern))
            {
                if (debugLog)
                    Debug.Log("[MET_GestureSystem] コマンド発動: " + cmd.commandName);
                cmd._Execute(this);
                return; // 一致するのは1コマンドのみ
            }
        }
    }

    private bool _IsMatch(MET_GestureDir[] pattern)
    {
        if (pattern == null) return false;
        if (pattern.Length != _bufCount) return false;
        for (int i = 0; i < _bufCount; i++)
        {
            if ((int)pattern[i] != _buffer[i]) return false;
        }
        return true;
    }

    // =========================================================
    //  手の位置・回転（発動者＝ローカルプレイヤーの描画手）
    // =========================================================
    public Vector3 _GetDrawHandPosition()
    {
        VRCPlayerApi.TrackingDataType t = (drawHand == MET_DrawHand.Right)
            ? VRCPlayerApi.TrackingDataType.RightHand
            : VRCPlayerApi.TrackingDataType.LeftHand;
        return _local.GetTrackingData(t).position;
    }

    public Quaternion _GetDrawHandRotation()
    {
        VRCPlayerApi.TrackingDataType t = (drawHand == MET_DrawHand.Right)
            ? VRCPlayerApi.TrackingDataType.RightHand
            : VRCPlayerApi.TrackingDataType.LeftHand;
        return _local.GetTrackingData(t).rotation;
    }

    // 視点(頭)の前方 distance(m) の位置
    public Vector3 _GetViewFrontPosition(float distance)
    {
        VRCPlayerApi.TrackingData h =
            _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        return h.position + (h.rotation * Vector3.forward) * distance;
    }

    // 視点(頭)の回転
    public Quaternion _GetViewRotation()
    {
        return _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
    }

    // 発動者(＝ローカルプレイヤー)の音を鳴らす位置。
    // 基本は描いた手（drawHand）から鳴らす。ただしデスクトップは手トラッキングが
    // 不正確なため、VRでない場合は頭の位置へフォールバックする。
    public Vector3 _GetPerformerPosition()
    {
        if (_local != null && _local.IsUserInVR())
        {
            return _GetDrawHandPosition();
        }
        return _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
    }

    // =========================================================
    //  範囲判定（isTrigger Collider）
    // =========================================================
    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!_hasCollider || player != _local) return;
        _inRange = true;
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!_hasCollider || player != _local) return;
        _inRange = false;
        if (_recording) _StopRecording();
        if (_keyRecording) _StopKeyRecording(); // 固定を残さず解除する
    }

    // =========================================================
    //  起動時の収集処理
    // =========================================================
    private void _CollectCommands()
    {
        Transform root = (commandsRoot != null) ? commandsRoot : transform;
        _commands = root.GetComponentsInChildren<MET_GestureCommand>(true);
    }

    // グローバルトグル対象を全コマンドから集めて stateSync に登録（重複除去）
    private void _CollectGlobalToggleTargets()
    {
        if (stateSync == null || _commands == null) return;

        int cap = 0;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            if (c.enableToggle && c.toggleGlobal && c.toggleTargets != null)
                cap += c.toggleTargets.Length;
        }

        GameObject[] temp = new GameObject[cap];
        int n = 0;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            if (!(c.enableToggle && c.toggleGlobal && c.toggleTargets != null)) continue;

            for (int j = 0; j < c.toggleTargets.Length; j++)
            {
                GameObject o = c.toggleTargets[j];
                if (o == null) continue;

                bool exists = false;
                for (int k = 0; k < n; k++)
                {
                    if (temp[k] == o) { exists = true; break; }
                }
                if (!exists) { temp[n] = o; n++; }
            }
        }

        GameObject[] result = new GameObject[n];
        for (int i = 0; i < n; i++) result[i] = temp[i];
        stateSync._SetTargets(result);
    }

    // グローバルスカイボックスのMaterialを全コマンドから集めて skyboxSync に登録（重複除去）
    private void _CollectGlobalSkyboxMaterials()
    {
        if (skyboxSync == null || _commands == null) return;

        int cap = 0;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            if (!(c.enableSkybox && c.skyboxGlobal)) continue;
            if (c.skyboxMaterial != null) cap++;
            if (c.skyboxMode == MET_SkyboxMode.Flip && c.skyboxMaterialB != null) cap++;
        }

        Material[] temp = new Material[cap];
        int n = 0;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            if (!(c.enableSkybox && c.skyboxGlobal)) continue;

            n = _AddMaterial(temp, n, c.skyboxMaterial);
            if (c.skyboxMode == MET_SkyboxMode.Flip)
                n = _AddMaterial(temp, n, c.skyboxMaterialB);
        }

        Material[] result = new Material[n];
        for (int i = 0; i < n; i++) result[i] = temp[i];
        skyboxSync._SetSkyboxes(result);
    }

    // 重複しなければ temp に追加して新しい件数を返す
    private int _AddMaterial(Material[] temp, int n, Material m)
    {
        if (m == null) return n;
        for (int k = 0; k < n; k++)
        {
            if (temp[k] == m) return n;
        }
        temp[n] = m;
        return n + 1;
    }

    // 音量調整コマンドの対象YamaPlayer(Controller)を全コマンドへ配布する。
    // Start は全クライアントで走るため、リモート側でも参照が張られ、
    // グローバル音量調整のネットワークイベントが正しく動作する。
    private void _AssignYamaController()
    {
        if (_commands == null) return;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            c._SetYamaController(yamaController);
        }
    }

    // コマンドのsoundClipを、参照先のAudioSEへ登録（グローバル移動SEのclip同期用）
    private void _CollectAudioSEClips()
    {
        if (_commands == null) return;
        for (int i = 0; i < _commands.Length; i++)
        {
            MET_GestureCommand c = _commands[i];
            if (c == null) continue;
            if (c.enableSound && c.soundAtPerformer &&
                c.audioSE != null && c.soundClip != null)
            {
                c.audioSE._RegisterClip(c.soundClip);
            }
        }
    }

    // =========================================================
    //  デバッグ用：方向列を日本語文字列に
    // =========================================================
    private string _DirString()
    {
        string s = "";
        for (int i = 0; i < _bufCount; i++)
        {
            s += _DirName(_buffer[i]);
            if (i < _bufCount - 1) s += "→";
        }
        return s;
    }

    private string _DirName(int d)
    {
        if (d == (int)MET_GestureDir.Up) return "上";
        if (d == (int)MET_GestureDir.Down) return "下";
        if (d == (int)MET_GestureDir.Left) return "左";
        return "右";
    }
}