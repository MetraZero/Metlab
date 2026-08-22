/*
 * MET_YamaPlayerZoneAdapter
 * バージョン: 1.2.0
 *
 * 概要:
 *   YamaStream（yamaplayer）とMET音量ゾーンシステムの橋渡しアダプタ。
 *   ゾーン適用中は「controller.Volume × zoneMult」をAudioSourceに設定することで、
 *   yamaplayerとゾーンシステムが競合しない正確な音量制御を実現する。
 *
 * 使い方:
 *   1. 空のGameObjectに本スクリプトをアタッチ
 *   2. controller欄にシーン内のYamaStream Controllerを設定
 *   3. speakers欄にyamaplayerが使用するAudioSourceと同じものを登録
 *   4. MET_VoiceZoneのyamaAdapters欄にこのオブジェクトを登録
 *   5. yamaplayerが使用していたAudioSourceをMET_VoiceZoneのtargetAudioSourcesから除外する
 *
 * 動作原理:
 *   yamaplayerはVolume変更時に audioSource.volume = sliderValue と直接書き込む。
 *   本アダプタはUpdate()内でゾーン適用中のみ監視し、
 *   yamaplayerが書き込んだ値を検知したら controller.Volume × zoneMult に即座に補正する。
 *   ゾーン外（zoneMult ≈ 1.0）はUpdate()を早期リターンするためパフォーマンス影響はほぼない。
 *
 * 更新履歴:
 *   1.2.0 - Update()をLateUpdate()に変更
 *           yamaplayerのOnVideoStart()→UpdateAudio()がUpdate()中に呼ばれる場合、
 *           LateUpdate()は同フレーム内ですべてのUpdate()後に実行されるため
 *           動画切り替え時の音量ポップを同フレーム内で補正できる
 *   1.1.0 - イベント経由のOnVolumeChangedディスパッチが不安定なためUpdate()監視方式に変更
 *           ゾーン外は早期リターンで負荷を最小化
 *           Listenerの継承を廃止しシンプルなUdonSharpBehaviourに変更
 *   1.0.0 - 初版（Listener継承・OnVolumeChangedイベント方式）
 */

using Yamadev.YamaStream;
using UnityEngine;
using UdonSharp;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_YamaPlayerZoneAdapter : UdonSharpBehaviour
{
    // ─────────────────────────────────────────
    // 設定
    // ─────────────────────────────────────────
    [Header("── YamaPlayer設定 ──")]

    [Tooltip("YamaStream の Controller コンポーネント")]
    public Controller controller;

    [Tooltip("yamaplayerが使用するAudioSource（yamaplayerに設定されているものと同一を登録）")]
    public AudioSource[] speakers;

    // ─────────────────────────────────────────
    // 内部状態
    // ─────────────────────────────────────────

    // 現在適用中のゾーン倍率（1.0 = ゾーンなし）
    private float _zoneMult = 1.0f;

    // 音量比較の許容誤差（float精度と聴覚的に無意味な誤差を吸収）
    private const float VOLUME_EPSILON = 0.001f;

    // ─────────────────────────────────────────
    // Unityイベント
    // ─────────────────────────────────────────

    private void LateUpdate()
    {
        // ゾーン倍率が1.0（適用なし）のときは処理不要
        if (_zoneMult > 1.0f - VOLUME_EPSILON) return;
        if (controller == null || speakers == null) return;

        // yamaplayerが書き込んだ値を「controller.Volume × zoneMult」に補正する
        float expected = Mathf.Clamp01(controller.Volume * _zoneMult);
        for (int i = 0; i < speakers.Length; i++)
        {
            if (speakers[i] == null) continue;
            if (Mathf.Abs(speakers[i].volume - expected) > VOLUME_EPSILON)
                speakers[i].volume = expected;
        }
    }

    // ─────────────────────────────────────────
    // 公開メソッド（MET_VoiceZoneから呼ばれる）
    // ─────────────────────────────────────────

    /// <summary>
    /// ゾーン倍率を設定し、即座にAudioSourceへ反映する。
    /// ゾーン入室時: worldAudioMultを渡す / ゾーン退出時: 1.0fを渡す
    /// </summary>
    public void SetZoneMult(float mult)
    {
        _zoneMult = mult;
        _ApplyVolume();
    }

    // ─────────────────────────────────────────
    // 内部処理
    // ─────────────────────────────────────────

    /// <summary>
    /// controller.Volume × _zoneMult を各AudioSourceに即時設定する。
    /// ゾーン変化時（SetZoneMult呼び出し時）に使用。
    /// </summary>
    private void _ApplyVolume()
    {
        if (controller == null || speakers == null) return;
        float targetVolume = Mathf.Clamp01(controller.Volume * _zoneMult);
        for (int i = 0; i < speakers.Length; i++)
        {
            if (speakers[i] != null)
                speakers[i].volume = targetVolume;
        }
    }
}
