// ============================================================
// MET_SoundPlay
// 概要: Pickupしたオブジェクトを「使用(Use)」した時に音を鳴らすギミック。
//       グローバル同期し、その場の全員に同じ音が再生される。
//       モード1: 使用するたびにループ再生をトグル（もう一度で停止）
//       モード2: 使用するたびにクリップをワンショット再生（1回）
//       クリップは 単一／ランダム／順番 から選択可能。音量はインスペクタで設定。
//       ループ状態は遅参加者(Late-join)にも反映される。
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>再生モード。</summary>
public enum METSoundPlayMode
{
    LoopToggle, // モード1：ループのON/OFFトグル
    OneShot     // モード2：ワンショット（1回再生）
}

/// <summary>クリップの選び方。</summary>
public enum METClipSelectMode
{
    Single,     // 単一：指定番号のクリップのみ
    Random,     // ランダム：毎回ランダムに1つ
    Sequential  // 順番：使用のたびに次のクリップへ
}

// グローバル同期のため手動同期を使用
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_SoundPlay : UdonSharpBehaviour
{
    // ------------------------------------------------------------
    // 再生設定
    // ------------------------------------------------------------
    [Header("再生設定")]
    [SerializeField, Tooltip("モード1=ループのトグル / モード2=ワンショット（1回）")]
    private METSoundPlayMode playMode = METSoundPlayMode.LoopToggle;

    // ------------------------------------------------------------
    // オーディオ
    // ------------------------------------------------------------
    [Header("オーディオ")]
    [SerializeField, Tooltip("再生に使用するAudioSource")]
    private AudioSource audioSource;

    [SerializeField, Tooltip("再生するクリップ（複数登録可）")]
    private AudioClip[] clips;

    [SerializeField, Tooltip("クリップの選び方：単一／ランダム／順番")]
    private METClipSelectMode clipSelectMode = METClipSelectMode.Single;

    [SerializeField, Min(0), Tooltip("「単一」のとき鳴らすクリップの番号（0始まり）")]
    private int singleClipIndex = 0;

    [SerializeField, Range(0f, 1f), Tooltip("音量")]
    private float volume = 1f;

    // ------------------------------------------------------------
    // 同期変数
    // ------------------------------------------------------------
    [UdonSynced] private bool _isLooping = false;   // モード1のループ状態
    [UdonSynced] private int _currentClipIndex = 0; // 全員が鳴らすクリップ番号
    [UdonSynced] private int _playPulse = 0;        // モード2：使用ごとに増える発火カウンタ

    // ------------------------------------------------------------
    // 内部（非同期）
    // ------------------------------------------------------------
    private int _lastPulse = 0;      // 直近に処理したパルス値
    private bool _baselineSet = false; // 初回同期でパルス基準を取ったか（遅参加者の誤爆防止）
    private int _seqCounter = 0;     // 順番モード用の内部カウンタ

    private void Start()
    {
        // AudioSource側のループはスクリプトで制御するのでOFFにしておく
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.volume = volume;
        }
    }

    // ------------------------------------------------------------
    // トリガー：Pickup使用（持っているプレイヤーのローカルで発火）
    // ------------------------------------------------------------
    public override void OnPickupUseDown()
    {
        TriggerGlobal();
    }

    /// <summary>
    /// 使用操作を全員へ同期する。Owner権限を取ってから同期変数を更新する。
    /// </summary>
    private void TriggerGlobal()
    {
        if (audioSource == null) { return; }

        // 操作者がOwnerになる
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (playMode == METSoundPlayMode.LoopToggle)
        {
            // ループのON/OFFを反転。ONにするときだけクリップを選び直す
            _isLooping = !_isLooping;
            if (_isLooping)
            {
                _currentClipIndex = PickClipIndex();
            }
            RequestSerialization();
            ApplyLoopState(); // Ownerは即時反映（OnDeserializationは自分に来ないため）
        }
        else // OneShot
        {
            _currentClipIndex = PickClipIndex();
            _playPulse++;
            _lastPulse = _playPulse; // 自分は今すぐ鳴らすので基準を進める
            _baselineSet = true;
            RequestSerialization();
            PlayOneShotLocal();
        }
    }

    // ------------------------------------------------------------
    // 同期受信
    // ------------------------------------------------------------
    public override void OnDeserialization()
    {
        if (playMode == METSoundPlayMode.LoopToggle)
        {
            // 遅参加者もここで現在のループ状態に追従する
            ApplyLoopState();
        }
        else // OneShot
        {
            if (!_baselineSet)
            {
                // 初回同期（＝参加直後など）は過去のパルスで鳴らさないよう基準だけ取る
                _lastPulse = _playPulse;
                _baselineSet = true;
                return;
            }

            if (_playPulse != _lastPulse)
            {
                _lastPulse = _playPulse;
                PlayOneShotLocal();
            }
        }
    }

    // ------------------------------------------------------------
    // 実際の再生処理
    // ------------------------------------------------------------

    /// <summary>モード1：同期されたループ状態を反映する。</summary>
    private void ApplyLoopState()
    {
        if (audioSource == null) { return; }

        if (_isLooping)
        {
            AudioClip clip = GetClip(_currentClipIndex);
            if (clip == null) { return; }

            audioSource.clip = clip;
            audioSource.loop = true;
            audioSource.volume = volume;
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
        else
        {
            audioSource.Stop();
        }
    }

    /// <summary>モード2：クリップを1回だけ再生する。</summary>
    private void PlayOneShotLocal()
    {
        if (audioSource == null) { return; }

        AudioClip clip = GetClip(_currentClipIndex);
        if (clip == null) { return; }

        audioSource.loop = false;
        audioSource.clip = clip;
        audioSource.volume = volume;
        audioSource.Stop();
        audioSource.Play();
    }

    // ------------------------------------------------------------
    // クリップ選択
    // ------------------------------------------------------------

    /// <summary>設定に応じて次に鳴らすクリップ番号を決める（Ownerが呼ぶ）。</summary>
    private int PickClipIndex()
    {
        if (clips == null || clips.Length == 0) { return 0; }

        if (clipSelectMode == METClipSelectMode.Random)
        {
            return Random.Range(0, clips.Length);
        }
        else if (clipSelectMode == METClipSelectMode.Sequential)
        {
            _seqCounter = (_seqCounter + 1) % clips.Length;
            return _seqCounter;
        }
        else // Single
        {
            // 範囲外を安全にクランプ
            if (singleClipIndex < 0) { return 0; }
            if (singleClipIndex >= clips.Length) { return clips.Length - 1; }
            return singleClipIndex;
        }
    }

    /// <summary>番号からクリップを取得（範囲外はnull）。</summary>
    private AudioClip GetClip(int index)
    {
        if (clips == null || clips.Length == 0) { return null; }
        if (index < 0 || index >= clips.Length) { return null; }
        return clips[index];
    }
}
