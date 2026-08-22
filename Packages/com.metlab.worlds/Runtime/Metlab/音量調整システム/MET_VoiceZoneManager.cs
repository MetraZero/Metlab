/*
 * MET_VoiceZoneManager
 * バージョン: 1.1.0
 *
 * 概要:
 *   VRCワールド音声ゾーンシステムのマネージャー。
 *   シーンに1つだけ配置し、ゾーン外（デフォルト）の音声設定を保持する。
 *   ローカルプレイヤーがゾーンへ入退場した際に、最高優先度のゾーン設定を適用する。
 *   ネットワーク同期なし（自分の「耳」設定のみ変更するためSync不要）。
 *
 * 使い方:
 *   1. 空のGameObjectに本スクリプトをアタッチ
 *   2. 各MET_VoiceZoneのmanager欄にこのオブジェクトを登録
 *   3. デフォルト値をInspectorで調整（VRChatデフォルト値が初期値）
 *
 * 更新履歴:
 *   1.1.0 - WorldAudio管理をキャッシュ方式（MET_VoiceZone v1.2.0）に対応
 *           _appliedWorldAudioZoneで現在適用中のゾーンを追跡し、
 *           切り替え時は前のゾーンだけをリセットする（全ゾーン一括リセット廃止）
 *           これによりゾーン内での音量変更後の退場時・高速トグル時のバグを修正
 *   1.0.0 - 初版
 */

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_VoiceZoneManager : UdonSharpBehaviour
{
    // ─────────────────────────────────────────
    // デフォルト Voice 設定（ゾーン外の基準値）
    // ─────────────────────────────────────────
    [Header("── デフォルト Voice 設定（ゾーン外の基準値）──")]

    [Tooltip("音声ゲイン。VRChatデフォルト: 15")]
    public float defaultVoiceGain = 15f;

    [Tooltip("声がフル音量で聞こえる距離(m)。0推奨（変えると不自然になりやすい）")]
    public float defaultVoiceNear = 0f;

    [Tooltip("声が聞こえなくなる最大距離(m)。VRChatデフォルト: 25")]
    public float defaultVoiceFar = 25f;

    [Tooltip("ローパスフィルター（こもり感）のデフォルト値")]
    public bool defaultVoiceLowpass = true;

    // ─────────────────────────────────────────
    // デフォルト AvatarAudio 設定
    // ─────────────────────────────────────────
    [Header("── デフォルト AvatarAudio 設定 ──")]

    [Tooltip("アバター音源ゲイン。VRChatデフォルト: 10")]
    public float defaultAvatarGain = 10f;

    [Tooltip("アバター音源 近距離(m)。0推奨")]
    public float defaultAvatarNear = 0f;

    [Tooltip("アバター音源 最大距離(m)。VRChatデフォルト: 40")]
    public float defaultAvatarFar = 40f;

    // ─────────────────────────────────────────
    // 内部状態（Inspectorには表示しない）
    // ─────────────────────────────────────────

    // 登録されたゾーンの一覧（Start時にMET_VoiceZoneが自己登録）
    private MET_VoiceZone[] _registeredZones = new MET_VoiceZone[32];
    private int _registeredCount = 0;

    // ローカルプレイヤーが現在入っているゾーン（最大8重複まで対応）
    private MET_VoiceZone[] _activeZones = new MET_VoiceZone[8];
    private int _activeCount = 0;

    // 現在WorldAudioが適用されているゾーン（null=適用なし）
    // 切り替え時はこのゾーンのResetWorldAudioだけを呼ぶ
    private MET_VoiceZone _appliedWorldAudioZone = null;

    // プレイヤー配列（アロケーション回避のためフィールドで確保）
    // VRChatワールド最大収容人数に合わせる（80が上限）
    private VRCPlayerApi[] _players = new VRCPlayerApi[80];

    // ─────────────────────────────────────────
    // 公開メソッド（MET_VoiceZoneから呼ばれる）
    // ─────────────────────────────────────────

    /// <summary>ゾーンをシステムに登録する（MET_VoiceZone.Start()から呼ぶ）</summary>
    public void RegisterZone(MET_VoiceZone zone)
    {
        for (int i = 0; i < _registeredCount; i++)
            if (_registeredZones[i] == zone) return;

        if (_registeredCount >= _registeredZones.Length)
        {
            Debug.LogWarning("[MET_VoiceZoneManager] ゾーン登録上限(32)に達しました");
            return;
        }
        _registeredZones[_registeredCount++] = zone;
    }

    /// <summary>ローカルプレイヤーがゾーンに入ったとき呼ばれる</summary>
    public void OnZoneEntered(MET_VoiceZone zone)
    {
        // 二重登録防止
        for (int i = 0; i < _activeCount; i++)
            if (_activeZones[i] == zone) return;

        if (_activeCount >= _activeZones.Length)
        {
            Debug.LogWarning("[MET_VoiceZoneManager] アクティブゾーン上限(8)に達しました");
            return;
        }
        _activeZones[_activeCount++] = zone;
        _ApplyHighestPriority();
    }

    /// <summary>ローカルプレイヤーがゾーンから出たとき、またはゾーンが無効化されたとき呼ばれる</summary>
    public void OnZoneExited(MET_VoiceZone zone)
    {
        int idx = -1;
        for (int i = 0; i < _activeCount; i++)
        {
            if (_activeZones[i] == zone) { idx = i; break; }
        }
        if (idx < 0) return;

        // 配列を詰める
        for (int i = idx; i < _activeCount - 1; i++)
            _activeZones[i] = _activeZones[i + 1];
        _activeCount--;
        _activeZones[_activeCount] = null;

        if (_activeCount == 0)
            _ApplyDefaults();
        else
            _ApplyHighestPriority();
    }

    // ─────────────────────────────────────────
    // 内部処理
    // ─────────────────────────────────────────

    /// <summary>現在アクティブなゾーンの中から最高優先度（同点は後入り）のゾーンを適用</summary>
    private void _ApplyHighestPriority()
    {
        if (_activeCount == 0)
        {
            _ApplyDefaults();
            return;
        }

        MET_VoiceZone best = _activeZones[0];
        for (int i = 1; i < _activeCount; i++)
        {
            // >= により、同点の場合はインデックスが大きい（後に入った）方を優先
            if (_activeZones[i].priority >= best.priority)
                best = _activeZones[i];
        }
        _ApplyZoneSettings(best);
    }

    /// <summary>指定ゾーンの設定をローカルプレイヤーの耳に適用</summary>
    private void _ApplyZoneSettings(MET_VoiceZone zone)
    {
        int count = VRCPlayerApi.GetPlayerCount();
        VRCPlayerApi.GetPlayers(_players);

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _players[i];
            if (p == null || !p.IsValid()) continue;

            // Voice設定（倍率適用）
            p.SetVoiceGain(defaultVoiceGain * zone.voiceGainMult);
            p.SetVoiceDistanceNear(defaultVoiceNear * zone.voiceNearMult);
            p.SetVoiceDistanceFar(defaultVoiceFar * zone.voiceFarMult);
            p.SetVoiceLowpass(zone.voiceLowpass);

            // AvatarAudio設定（倍率適用）
            p.SetAvatarAudioGain(defaultAvatarGain * zone.avatarGainMult);
            p.SetAvatarAudioNearRadius(defaultAvatarNear * zone.avatarNearMult);
            p.SetAvatarAudioFarRadius(defaultAvatarFar * zone.avatarFarMult);
        }

        // WorldAudio: 前回適用ゾーンのみリセットし、新たなゾーンを適用
        // （同じゾーンが継続して最高優先度の場合は何もしない）
        if (_appliedWorldAudioZone != zone)
        {
            if (_appliedWorldAudioZone != null)
                _appliedWorldAudioZone.ResetWorldAudio();

            zone.ApplyWorldAudio();
            _appliedWorldAudioZone = zone;
        }
    }

    /// <summary>デフォルト値をローカルプレイヤーの耳に適用</summary>
    private void _ApplyDefaults()
    {
        int count = VRCPlayerApi.GetPlayerCount();
        VRCPlayerApi.GetPlayers(_players);

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _players[i];
            if (p == null || !p.IsValid()) continue;

            p.SetVoiceGain(defaultVoiceGain);
            p.SetVoiceDistanceNear(defaultVoiceNear);
            p.SetVoiceDistanceFar(defaultVoiceFar);
            p.SetVoiceLowpass(defaultVoiceLowpass);

            p.SetAvatarAudioGain(defaultAvatarGain);
            p.SetAvatarAudioNearRadius(defaultAvatarNear);
            p.SetAvatarAudioFarRadius(defaultAvatarFar);
        }

        // WorldAudio: 適用中のゾーンのみリセット
        if (_appliedWorldAudioZone != null)
        {
            _appliedWorldAudioZone.ResetWorldAudio();
            _appliedWorldAudioZone = null;
        }
    }
}
