// =============================================================
// MET_GestureAudioSE.cs
// MET_ Gesture System - 発動者位置へ移動して鳴らすSE管理
// Version: 1.2.0
//
// [概要]
//   AudioSource を発動者の位置へ移動 → 再生 → 一定時間後に元位置へ戻す。
//   複数人が同時／連続でジェスチャーしても壊れないよう設計している：
//     ・元位置は Start で一度だけ記録（移動先を"元位置"と誤認しない）
//     ・戻すタイミングは「最後の再生」基準（新しい再生が来たら延長）
//     ・グローバルは再生位置(Vector3)とカウンタをManual同期し、
//       Late-Joinerは初回同期で過去のSEを再生しないようガードする
//
// [配置]
//   鳴らしたい AudioSource の GameObject に付ける（移動対象になるので
//   固定位置SEとは別に、SE専用の AudioSource を用意するのがおすすめ）。
//   コマンドの audioSE 欄に刺す。
//
// [注意]
//   グローバル再生時、鳴らすclipは登録簿(clips)のindexで同期される。
//   ManagerがコマンドのsoundClipを起動時に自動登録するので、通常は
//   AudioSource本体にclipを入れなくても、コマンド指定のclipが全員で鳴る。
//   （登録簿に無いclipを渡した場合のみ source.clip にフォールバック）
//
// [Changelog]
//   1.2.0 - 再生音量(volume)引数を追加。コマンド指定音量を反映（グローバルは_syncVolume同期）。
//   1.1.0 - グローバル再生のclipをindex同期化。コマンド指定clipが全員で鳴る。
//   1.0.0 - 初版。
// =============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_GestureAudioSE : UdonSharpBehaviour
{
    [Header("■ 再生に使う AudioSource")]
    [Tooltip("未指定なら同じGameObjectのAudioSourceを自動取得")]
    public AudioSource source;

    [Tooltip("再生後に元位置へ戻すまでの猶予(秒)。0なら再生クリップ長から自動")]
    public float returnDelay = 0f;

    [Header("■ clip登録簿（自動収集）")]
    [Tooltip("グローバル再生でclip番号を同期するための一覧。\n" +
             "通常はManagerがコマンドのsoundClipを自動登録するので手動登録は不要。")]
    public AudioClip[] clips;

    // 元位置（Startで一度だけ記録）
    private Vector3 _origPos;
    private Quaternion _origRot;
    private bool _displaced;
    private float _returnAt;

    // グローバル同期
    [UdonSynced] private Vector3 _syncPos;
    [UdonSynced] private int _syncClipIndex = -1;
    [UdonSynced] private float _syncVolume = 1f;
    [UdonSynced] private int _syncCounter;
    private int _seenCounter;
    private bool _initSynced;

    void Start()
    {
        if (source == null) source = GetComponent<AudioSource>();
        if (clips == null) clips = new AudioClip[0];
        _origPos = transform.position;
        _origRot = transform.rotation;
    }

    /// <summary>clipを登録簿に追加（重複無視）。Managerが起動時に呼ぶ。</summary>
    public void _RegisterClip(AudioClip clip)
    {
        if (clip == null) return;
        if (clips == null) clips = new AudioClip[0];

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == clip) return; // 既登録
        }

        AudioClip[] n = new AudioClip[clips.Length + 1];
        for (int i = 0; i < clips.Length; i++) n[i] = clips[i];
        n[clips.Length] = clip;
        clips = n;
    }

    private int _FindClip(AudioClip clip)
    {
        if (clips == null) return -1;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == clip) return i;
        }
        return -1;
    }

    /// <summary>ローカル再生（自分だけ）。発動者位置へ移動して鳴らす。</summary>
    public void _PlayLocal(Vector3 pos, AudioClip clip, float volume)
    {
        _PlayInternal(pos, clip, volume);
    }

    /// <summary>グローバル再生（全員）。発動者位置へ移動し、指定clipを指定音量で鳴らす。</summary>
    public void _PlayGlobal(Vector3 pos, AudioClip clip, float volume)
    {
        // まず自分で鳴らす
        _PlayInternal(pos, clip, volume);

        // 他クライアントへ伝える（位置・clip番号・音量を同期）
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        _syncPos = pos;
        _syncClipIndex = _FindClip(clip); // 未登録なら-1（source.clipにフォールバック）
        _syncVolume = volume;
        _syncCounter++;
        _seenCounter = _syncCounter; // 自分は再生済みなので既読に
        RequestSerialization();
    }

    private void _PlayInternal(Vector3 pos, AudioClip clip, float volume)
    {
        if (source == null) return;

        transform.position = pos;
        _displaced = true;

        AudioClip c = (clip != null) ? clip : source.clip;
        float dur = (returnDelay > 0f) ? returnDelay : ((c != null) ? c.length : 3f);
        _returnAt = Time.time + dur + 0.1f; // 最後の再生基準で延長される

        if (c != null) source.PlayOneShot(c, volume);
    }

    // 同期受信：他人が鳴らしたSEを再生。Late-Joinerの初回は再生しない。
    public override void OnDeserialization()
    {
        if (!_initSynced)
        {
            _initSynced = true;
            _seenCounter = _syncCounter; // 参加時点の状態を既読扱い
            return;
        }

        if (_syncCounter != _seenCounter)
        {
            _seenCounter = _syncCounter;
            AudioClip c = (_syncClipIndex >= 0 && _syncClipIndex < clips.Length)
                ? clips[_syncClipIndex] : null;
            _PlayInternal(_syncPos, c, _syncVolume);
        }
    }

    void Update()
    {
        if (!_displaced) return;
        if (Time.time >= _returnAt)
        {
            transform.position = _origPos;
            transform.rotation = _origRot;
            _displaced = false;
        }
    }
}