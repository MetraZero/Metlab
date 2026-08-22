// ============================================================
// MET_AutoSlideDoor
// 概要: プレイヤーの接近を検知し、Transform を直接スライドさせて
//       自動開閉する自動ドア（UdonSharp）。
//       Animator を使わないため、ワールド軽量化（Animator無効化・
//       メッシュ結合等）の影響を受けにくく、閉じ動作も毎フレーム
//       確実に駆動される。任意の複数枚ドアに対応。
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//
// 機能:
//   ① このスクリプトを付けたオブジェクト（静止したトリガー枠）で動作
//   ② ドアに近づく（トリガー範囲侵入）とスライドして開く
//   ③ 離れる（範囲退出）とスライドして閉じる
//   ④ 範囲内に誰かがいる間は閉じない
//   ⑤ 閉じ動作中／閉じ待機中に誰かが近づくと再び開く
//
// 設計メモ:
//   - プレイヤー位置は各クライアントで共有されているため、トリガー
//     検知だけで全員の見た目が一致する。ネットワーク同期は不要。
//   - スライドは進捗値 0(閉)〜1(開) を時間で駆動し、SmoothStep で
//     イーズイン・アウトさせる。各ドアごとの開オフセットは自由設定。
//
// 設定方法:
//   1. 静止した空オブジェクトに本スクリプトと Trigger 付き Collider を付ける
//   2. 「トリガー用コライダー」に上記 Collider を割り当てる
//   3. 「動かすドア」に各ドア（Cube等）の Transform を登録
//   4. 「開くオフセット」に各ドアが開く方向・距離（ローカル空間）を登録
//      ※「動かすドア」と「開くオフセット」は要素数を一致させること
//   5. 必要に応じて開閉スピード・閉じ待機・効果音を設定
//
// 注意（軽量化ツール使用時）:
//   ドアメッシュがスタティックバッチ／メッシュ結合の対象になると、
//   Transform を動かしても見た目が動かなくなる。ドアオブジェクトは
//   最適化の除外（動的オブジェクト扱い）に設定すること。
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_AutoSlideDoor : UdonSharpBehaviour
{
    [Header("ドア設定")]
    [SerializeField, Tooltip("スライドさせるドアの Transform（複数可）")]
    private Transform[] doorPanels;

    [SerializeField, Tooltip("各ドアが開くときの移動量（ローカル空間）。要素の順番は「動かすドア」と対応させる")]
    private Vector3[] openOffsets;

    [Header("トリガー設定")]
    [SerializeField, Tooltip("プレイヤー検知に使う Trigger 付き Collider（必須）。ドアとは別の静止オブジェクトに置く")]
    private Collider triggerCollider;

    [Header("動作設定")]
    [SerializeField, Tooltip("開ききる／閉じきるまでにかける時間（秒）")]
    private float slideDuration = 0.6f;

    [SerializeField, Tooltip("誰もいなくなってから閉じ始めるまでの待機時間（秒）")]
    private float closeDelay = 1.0f;

    [Header("効果音")]
    [SerializeField, Tooltip("効果音を再生する AudioSource（未設定なら無音）")]
    private AudioSource audioSource;

    [SerializeField, Tooltip("開くときに鳴らす効果音")]
    private AudioClip openSound;

    [SerializeField, Tooltip("閉じるときに鳴らす効果音")]
    private AudioClip closeSound;

    [Header("プレイヤー検出")]
    [SerializeField, Tooltip("入室直後の原点スポーン誤検知を防ぐ猶予時間（秒）")]
    private float joinGracePeriod = 2.0f;

    [Header("デバッグ設定")]
    [SerializeField, Tooltip("動作ログを Console に出力する")]
    private bool enableDebugLog = false;

    private const int MAX_PLAYERS = 82;

    // ドアの閉じ位置（Start 時点の localPosition を基準に記録）
    private Vector3[] _closedPositions;

    // スライド進捗（0 = 閉, 1 = 開）と目標
    private float _progress = 0f;
    private float _targetProgress = 0f;
    private bool _isMoving = false;

    // トリガー範囲内のプレイヤー ID 管理
    private int[] _insideIds = new int[MAX_PLAYERS];
    private int _insideCount = 0;

    // 入室直後の猶予期間管理
    private int[] _graceIds = new int[MAX_PLAYERS];
    private float[] _graceTimers = new float[MAX_PLAYERS];
    private int _graceCount = 0;

    // 閉じ待機
    private bool _isClosing = false;
    private float _closeTimer = 0f;

    private bool _isInitialized = false;
    private bool _isValidSetup = false;

    void Start()
    {
        // --- 設定の検証 ---
        if (doorPanels == null || doorPanels.Length == 0)
        {
            Debug.LogError("[MET_AutoSlideDoor] 「動かすドア」が設定されていません！", this);
            return;
        }

        if (openOffsets == null || openOffsets.Length != doorPanels.Length)
        {
            Debug.LogError("[MET_AutoSlideDoor] 「開くオフセット」の要素数が「動かすドア」と一致していません！", this);
            return;
        }

        if (triggerCollider == null)
        {
            Debug.LogError("[MET_AutoSlideDoor] 「トリガー用コライダー」が設定されていません！", this);
            return;
        }

        // --- 閉じ位置を記録し、確実に閉じ状態にする ---
        _closedPositions = new Vector3[doorPanels.Length];
        for (int i = 0; i < doorPanels.Length; i++)
        {
            if (doorPanels[i] != null)
                _closedPositions[i] = doorPanels[i].localPosition;
        }

        _progress = 0f;
        _targetProgress = 0f;
        ApplyProgress();

        _isValidSetup = true;

        // 最初のプレイヤーが参加するまでトリガーを無効化（起動時の誤検知防止）
        triggerCollider.enabled = false;

        if (enableDebugLog)
            Debug.Log("[MET_AutoSlideDoor] 起動 - 最初のプレイヤー参加を待機中...", this);
    }

    void Update()
    {
        if (!_isValidSetup) return;

        // 猶予期間タイマー（登録がある時だけ処理）
        if (_graceCount > 0)
            UpdateGraceTimers();

        // 閉じ待機（範囲内が空の時だけカウントダウン）
        if (_isClosing && _insideCount == 0)
        {
            _closeTimer -= Time.deltaTime;
            if (_closeTimer <= 0f)
            {
                _isClosing = false;
                CloseDoors();
            }
        }

        // スライド移動（動いている時だけ処理）
        if (_isMoving)
            UpdateMovement();
    }

    // ============================================================
    // プレイヤーイベント
    // ============================================================

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (!_isValidSetup) return;
        if (!Utilities.IsValid(player)) return;

        // 最初の参加でトリガーを有効化・初期化
        if (!_isInitialized)
        {
            _isInitialized = true;
            triggerCollider.enabled = true;

            if (enableDebugLog)
                Debug.Log($"[MET_AutoSlideDoor] 初期化完了: {player.displayName}", this);
        }

        // 全参加プレイヤーに猶予期間を付与（入室時の原点スポーン誤検知防止）
        AddGracePlayer(player.playerId);

        if (enableDebugLog)
            Debug.Log($"[MET_AutoSlideDoor] 参加: {player.displayName} - 猶予 {joinGracePeriod}s", this);
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!_isValidSetup) return;
        if (!Utilities.IsValid(player)) return;

        int playerId = player.playerId;

        // 切断・クラッシュで Exit が発火しなかった場合の補正
        if (RemovePlayerInside(playerId))
        {
            if (enableDebugLog)
                Debug.Log($"[MET_AutoSlideDoor] 退出（切断補正）: {player.displayName} - 計 {_insideCount} 人", this);

            if (_insideCount <= 0)
                StartClosing();
        }

        RemoveGracePlayer(playerId);
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!_isInitialized) return;
        if (!Utilities.IsValid(player)) return;

        int playerId = player.playerId;

        // 猶予期間中のプレイヤーは無視（入室時の誤検知防止）
        if (IsInGracePeriod(playerId))
        {
            if (enableDebugLog)
                Debug.Log($"[MET_AutoSlideDoor] 猶予期間中のため無視: {player.displayName}", this);
            return;
        }

        // 重複登録防止（アバター変更等で再度 Enter する場合の対応）
        if (IsPlayerInside(playerId)) return;

        AddPlayerInside(playerId);

        if (enableDebugLog)
            Debug.Log($"[MET_AutoSlideDoor] 侵入: {player.displayName} - 計 {_insideCount} 人", this);

        // 誰か入ったので必ず開く（閉じ待機・閉じ動作をキャンセル）
        _isClosing = false;
        OpenDoors();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!_isInitialized) return;
        if (!Utilities.IsValid(player)) return;

        int playerId = player.playerId;

        // inside リストにいないプレイヤーの Exit は無視
        if (!RemovePlayerInside(playerId))
        {
            if (enableDebugLog)
                Debug.Log($"[MET_AutoSlideDoor] Exit を無視（inside リストにない）: {player.displayName}", this);
            return;
        }

        if (enableDebugLog)
            Debug.Log($"[MET_AutoSlideDoor] 退出: {player.displayName} - 計 {_insideCount} 人", this);

        if (_insideCount <= 0)
            StartClosing();
    }

    // ============================================================
    // 開閉制御
    // ============================================================

    // 開き始める（すでに開き目標なら何もしない）
    private void OpenDoors()
    {
        if (_targetProgress >= 1f && !_isMoving) return; // 完全に開いている
        if (_targetProgress >= 1f && _isMoving) return;  // すでに開き動作中

        _targetProgress = 1f;
        _isMoving = true;
        PlaySound(openSound);

        if (enableDebugLog)
            Debug.Log("[MET_AutoSlideDoor] 開きます", this);
    }

    // 閉じ待機を開始（範囲内が空のときのみ）
    private void StartClosing()
    {
        // すでに閉じ切っている、または閉じ目標なら不要
        if (_targetProgress <= 0f && !_isMoving) return;

        _isClosing = true;
        _closeTimer = closeDelay;

        if (enableDebugLog)
            Debug.Log($"[MET_AutoSlideDoor] {closeDelay}秒後に閉じます", this);
    }

    // 実際に閉じ動作を開始
    private void CloseDoors()
    {
        // 念のため：この時点で誰かいるなら閉じない
        if (_insideCount > 0) return;
        if (_targetProgress <= 0f) return;

        _targetProgress = 0f;
        _isMoving = true;
        PlaySound(closeSound);

        if (enableDebugLog)
            Debug.Log("[MET_AutoSlideDoor] 閉じます", this);
    }

    private void UpdateMovement()
    {
        float speed = (slideDuration > 0.0001f) ? (1f / slideDuration) : 1000f;
        float step = speed * Time.deltaTime;

        if (_targetProgress > _progress)
        {
            _progress += step;
            if (_progress >= _targetProgress)
            {
                _progress = _targetProgress;
                _isMoving = false;
            }
        }
        else
        {
            _progress -= step;
            if (_progress <= _targetProgress)
            {
                _progress = _targetProgress;
                _isMoving = false;
            }
        }

        ApplyProgress();
    }

    // 進捗をドア位置へ反映（SmoothStep でイーズイン・アウト）
    private void ApplyProgress()
    {
        float eased = Mathf.SmoothStep(0f, 1f, _progress);
        for (int i = 0; i < doorPanels.Length; i++)
        {
            if (doorPanels[i] == null) continue;
            doorPanels[i].localPosition = _closedPositions[i] + openOffsets[i] * eased;
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // ============================================================
    // 猶予期間タイマー
    // ============================================================

    private void UpdateGraceTimers()
    {
        for (int i = _graceCount - 1; i >= 0; i--)
        {
            _graceTimers[i] -= Time.deltaTime;
            if (_graceTimers[i] <= 0f)
                RemoveGraceAt(i);
        }
    }

    // ============================================================
    // プレイヤー inside リスト ヘルパー
    // ============================================================

    private void AddPlayerInside(int playerId)
    {
        if (_insideCount >= MAX_PLAYERS) return;
        _insideIds[_insideCount] = playerId;
        _insideCount++;
    }

    // 削除できた場合 true
    private bool RemovePlayerInside(int playerId)
    {
        for (int i = 0; i < _insideCount; i++)
        {
            if (_insideIds[i] == playerId)
            {
                _insideIds[i] = _insideIds[_insideCount - 1];
                _insideCount--;
                return true;
            }
        }
        return false;
    }

    private bool IsPlayerInside(int playerId)
    {
        for (int i = 0; i < _insideCount; i++)
        {
            if (_insideIds[i] == playerId) return true;
        }
        return false;
    }

    // ============================================================
    // 猶予期間 ヘルパー
    // ============================================================

    private void AddGracePlayer(int playerId)
    {
        // 既登録ならタイマーを延長
        for (int i = 0; i < _graceCount; i++)
        {
            if (_graceIds[i] == playerId)
            {
                _graceTimers[i] = joinGracePeriod;
                return;
            }
        }
        if (_graceCount >= MAX_PLAYERS) return;
        _graceIds[_graceCount] = playerId;
        _graceTimers[_graceCount] = joinGracePeriod;
        _graceCount++;
    }

    private bool IsInGracePeriod(int playerId)
    {
        for (int i = 0; i < _graceCount; i++)
        {
            if (_graceIds[i] == playerId) return true;
        }
        return false;
    }

    private void RemoveGracePlayer(int playerId)
    {
        for (int i = 0; i < _graceCount; i++)
        {
            if (_graceIds[i] == playerId)
            {
                RemoveGraceAt(i);
                return;
            }
        }
    }

    private void RemoveGraceAt(int index)
    {
        _graceIds[index] = _graceIds[_graceCount - 1];
        _graceTimers[index] = _graceTimers[_graceCount - 1];
        _graceCount--;
    }
}
