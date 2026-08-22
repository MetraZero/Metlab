/*
 * MET_VoiceZone
 * バージョン: 1.4.0
 *
 * 概要:
 *   VRCワールド音声ゾーンの設定スクリプト。
 *   IsTrigger付きColliderと同じGameObjectに配置する。
 *   ローカルプレイヤーがコライダーに入退場した際、MET_VoiceZoneManagerに通知し、
 *   Voice / AvatarAudio / WorldAudioSource の倍率設定を反映させる。
 *
 * 使い方:
 *   1. 空のGameObjectを作成し、Collider(IsTrigger=ON)を追加
 *   2. 本スクリプトをアタッチ
 *   3. manager欄にシーン内のMET_VoiceZoneManagerを登録
 *   4. 各倍率をInspectorで調整
 *   5. GameObjectをOFF/ONで動的に有効・無効化できる
 *
 * WorldAudio の2つの管理方式:
 *   [targetAudioSources]
 *     - yamaplayer等の外部コントローラーを持たないAudioSourceに使用
 *     - キャッシュ方式: 入室時に音量を記録し、退出時に正確に復元する
 *   [yamaAdapters]
 *     - yamaplayerが制御するAudioSourceに使用（MET_YamaPlayerZoneAdapterを経由）
 *     - アダプタ方式: 常に「controller.Volume × zoneMult」を設定するため正確
 *     - yamaplayerとゾーンシステムの競合を防ぐ
 *
 * 更新履歴:
 *   1.4.0 - yamaAdapters（MET_YamaPlayerZoneAdapter）対応を追加
 *           yamaplayerの音量変更とゾーン倍率を正確に組み合わせるアダプタ方式を導入
 *           targetAudioSourcesをv1.2.0のシンプルなキャッシュ方式に戻す（外部変更検出を廃止）
 *   1.3.0 - ResetWorldAudioに「外部変更検出」を追加（yamaplayerとの競合で限界があったため廃止）
 *   1.2.0 - WorldAudioのキャッシュ方式を再導入
 *   1.1.0 - WorldAudioをキャッシュ廃止・割り算方式に変更（廃止）
 *   1.0.2 - WorldAudio無音バグ修正
 *   1.0.1 - WorldAudioキャッシュをStart->ApplyWorldAudio時に変更
 *   1.0.0 - 初版
 */

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_VoiceZone : UdonSharpBehaviour
{
    // ─────────────────────────────────────────
    // ゾーン基本設定
    // ─────────────────────────────────────────
    [Header("── ゾーン基本設定 ──")]

    [Tooltip("ゾーンの名前（識別・デバッグ用）")]
    public string zoneName = "NewZone";

    [Tooltip("優先度。数値が高いゾーンが優先される。同点は後から入ったゾーンが優先")]
    public int priority = 0;

    [Tooltip("MET_VoiceZoneManagerへの参照（必須）")]
    public MET_VoiceZoneManager manager;

    // ─────────────────────────────────────────
    // Voice 倍率設定
    // ─────────────────────────────────────────
    [Header("── Voice 倍率（Managerのデフォルト値に乗算） ──")]

    [Tooltip("音声ゲインの倍率。1.0=変化なし / 0.5=半分 / 0=無音")]
    [Range(0f, 4f)]
    public float voiceGainMult = 1f;

    [Tooltip("声のフル音量距離の倍率。Near=0の場合は効果なし（触らなくてOK）")]
    [Range(0f, 4f)]
    public float voiceNearMult = 1f;

    [Tooltip("声の最大距離の倍率。0.3=近くの人しか聞こえない / 2.0=広く聞こえる")]
    [Range(0f, 4f)]
    public float voiceFarMult = 1f;

    [Tooltip("ローパスフィルター（こもり感）ON=こもった音 / OFF=クリアな音")]
    public bool voiceLowpass = true;

    // ─────────────────────────────────────────
    // AvatarAudio 倍率設定
    // ─────────────────────────────────────────
    [Header("── AvatarAudio 倍率（Managerのデフォルト値に乗算） ──")]

    [Tooltip("アバター音源ゲインの倍率")]
    [Range(0f, 4f)]
    public float avatarGainMult = 1f;

    [Tooltip("アバター音源 近距離の倍率。Near=0の場合は効果なし（触らなくてOK）")]
    [Range(0f, 4f)]
    public float avatarNearMult = 1f;

    [Tooltip("アバター音源 最大距離の倍率")]
    [Range(0f, 4f)]
    public float avatarFarMult = 1f;

    // ─────────────────────────────────────────
    // ワールド AudioSource 設定（外部コントローラーなしのソース）
    // ─────────────────────────────────────────
    [Header("── ワールド AudioSource（外部コントローラーなし） ──")]

    [Tooltip("このゾーン入室中に音量を変えたいAudioSourceを登録（yamaplayer管理外のもの）")]
    public AudioSource[] targetAudioSources;

    [Tooltip("登録したAudioSourceに掛ける音量倍率。0=実質無音 / 1=変化なし")]
    [Range(0f, 2f)]
    public float worldAudioMult = 1f;

    // ─────────────────────────────────────────
    // YamaPlayer アダプタ設定
    // ─────────────────────────────────────────
    [Header("── YamaPlayer連携（MET_YamaPlayerZoneAdapter） ──")]

    [Tooltip("yamaplayerのAudioSourceを制御するアダプタを登録（複数可）\n" +
             "yamaplayerが管理するAudioSourceはtargetAudioSourcesではなくここに登録する")]
    public MET_YamaPlayerZoneAdapter[] yamaAdapters;

    // ─────────────────────────────────────────
    // 内部状態
    // ─────────────────────────────────────────

    // worldAudioMult=0のとき0除算防止として使用（実質無音）
    private const float SAFE_MIN_MULT = 0.0001f;

    // ApplyWorldAudio時にキャッシュした音量（ResetWorldAudioで復元する）
    private float[] _cachedVolumes;

    // targetAudioSourcesのWorldAudioが現在適用されているか
    private bool _isApplied = false;

    // 現在ローカルプレイヤーがこのゾーン内にいるか
    private bool _localPlayerInside = false;

    // ─────────────────────────────────────────
    // Unityイベント
    // ─────────────────────────────────────────

    private void Start()
    {
        if (manager == null)
        {
            Debug.LogError("[MET_VoiceZone] '" + zoneName + "': managerが未設定です。");
            return;
        }
        manager.RegisterZone(this);
    }

    private void OnDisable()
    {
        if (!_localPlayerInside) return;
        _localPlayerInside = false;
        if (manager != null)
            manager.OnZoneExited(this);
    }

    // ─────────────────────────────────────────
    // VRChatトリガーイベント
    // ─────────────────────────────────────────

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        _localPlayerInside = true;
        manager.OnZoneEntered(this);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        _localPlayerInside = false;
        manager.OnZoneExited(this);
    }

    // ─────────────────────────────────────────
    // WorldAudio操作（MET_VoiceZoneManagerから呼ばれる）
    // ─────────────────────────────────────────

    /// <summary>
    /// ゾーン入室時の音量処理を実行する。
    ///
    /// [targetAudioSources]
    ///   現在のvolumeをキャッシュしてからworldAudioMultを乗算する。
    ///   _isAppliedフラグで二重適用を防止。
    ///
    /// [yamaAdapters]
    ///   アダプタにzoneMult（worldAudioMult）を通知する。
    ///   アダプタ側で「controller.Volume × mult」が設定される。
    /// </summary>
    public void ApplyWorldAudio()
    {
        float mult = worldAudioMult < SAFE_MIN_MULT ? SAFE_MIN_MULT : worldAudioMult;

        // targetAudioSources: キャッシュ方式（二重適用防止）
        if (!_isApplied && targetAudioSources != null && targetAudioSources.Length > 0)
        {
            if (_cachedVolumes == null || _cachedVolumes.Length != targetAudioSources.Length)
                _cachedVolumes = new float[targetAudioSources.Length];

            for (int i = 0; i < targetAudioSources.Length; i++)
            {
                if (targetAudioSources[i] != null)
                {
                    _cachedVolumes[i] = targetAudioSources[i].volume; // 入室前の値を保存
                    targetAudioSources[i].volume = Mathf.Clamp01(_cachedVolumes[i] * mult);
                }
            }
            _isApplied = true;
        }

        // yamaAdapters: アダプタ方式（常に実行・idempotent）
        if (yamaAdapters != null)
        {
            for (int i = 0; i < yamaAdapters.Length; i++)
            {
                if (yamaAdapters[i] != null)
                    yamaAdapters[i].SetZoneMult(mult);
            }
        }
    }

    /// <summary>
    /// ゾーン退出時の音量復元を実行する。
    ///
    /// [targetAudioSources]
    ///   キャッシュした入室前の値に正確に復元する。
    ///
    /// [yamaAdapters]
    ///   アダプタのzoneMultを1.0に戻す（yamaplayer本来の音量に戻る）。
    /// </summary>
    public void ResetWorldAudio()
    {
        // targetAudioSources: キャッシュから復元（適用済みのときのみ）
        if (_isApplied)
        {
            if (targetAudioSources != null && _cachedVolumes != null)
            {
                for (int i = 0; i < targetAudioSources.Length; i++)
                {
                    if (targetAudioSources[i] != null && i < _cachedVolumes.Length)
                        targetAudioSources[i].volume = _cachedVolumes[i]; // キャッシュから正確に復元
                }
            }
            _isApplied = false;
        }

        // yamaAdapters: ゾーン倍率を1.0に戻す（常に実行）
        if (yamaAdapters != null)
        {
            for (int i = 0; i < yamaAdapters.Length; i++)
            {
                if (yamaAdapters[i] != null)
                    yamaAdapters[i].SetZoneMult(1.0f);
            }
        }
    }
}
