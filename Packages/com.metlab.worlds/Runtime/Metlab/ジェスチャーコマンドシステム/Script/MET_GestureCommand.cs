// =============================================================
// MET_GestureCommand.cs
// MET_ Gesture System - コマンドデータ（1コマンド1オブジェクト）
// Version: 1.10.1
//
// [概要]
//   「どの方向シーケンスで発動するか」と「発動時に何が起きるか」を
//   1オブジェクトにまとめて持つ。アクションは複数同時に組める。
//   Manager が方向列マッチで一致したコマンドを1つだけ発動する。
//
//   実装済みアクション：
//     1. オブジェクトのON/OFF（ローカル / グローバル）
//     2. 手元/視点前ワープ（ローカル / グローバル ※要 VRC Object Sync）
//     3. 音を鳴らす/止める（Play一発orループ / Stop、ローカル / グローバル、
//        発動者位置へ移動再生も可）
//     4. スカイボックス切り替え（ローカル / グローバル ※Late-Joiner対応）
//     5. YamaPlayer音量調整（現在値へポイント加減算・ローカル / グローバル）
//     ※（凍結）花火（打ち上げ）アクションは選択肢から除去。花火システム本体
//       （MET_FireworkLauncher / MET_Firework）は保持しており、必要時に復活可能。
//
// [Changelog]
//   1.10.1 - グローバル音・グローバル音量が他人に届かない不具合を修正。
//            BehaviourSyncMode.None では SendCustomNetworkEvent が無効化される
//            ため、NoVariableSync へ変更（同期変数は持たないので影響なし）。
//            ワープの手放し通知(1.10.0)も同じ理由で不発だったため併せて解消。
//   1.10.0 - ワープ（呼び寄せ）が2回目以降ちゃんと来ない問題を修正。
//            ①Rigidbodyの残留速度を消す（落下・投擲の勢いで飛んでいくのを防ぐ）
//            ②持たれている場合は先に手放させる（毎フレーム手の位置に上書きされるため）
//            ③VRC Object Sync に FlagDiscontinuity で不連続を通知（補間で流れるのを防ぐ）
//            ④他人が持っていた場合は所有権移譲の完了を待って再配置
//   1.9.0 - 花火システム凍結に伴い、アクション6（花火）をジェスチャーコマンドの
//           選択肢から除去（花火システム本体・enum MET_FireworkAction は保持）。
//   1.8.0 - アクション6：花火（打ち上げ）の自動連続発射 Start/Stop/Toggle を追加。
//   1.7.3 - Stopが一発再生(PlayOneShot)も止められるよう修正（AudioSource無効化でボイスをクリア）。
//   1.7.2 - グローバル音を「ローカル即再生＋Othersへ送信」に変更（All依存を解消・エディタでも鳴る）。
//   1.7.1 - soundVolumeを増幅対応(0〜3)に拡張。小さいクリップを持ち上げられるように。
//   1.7.0 - 音アクションにコマンド別音量(soundVolume)を追加。クリップ毎の音量差を調整可能に。
//   1.6.1 - soundAtPerformerの説明を明確化（描いた手/頭・3Dソースで位置が効く旨）。
//   1.6.0 - アクション5：YamaPlayer音量調整を追加（増減量%・ローカル/グローバル）。
//   1.5.1 - グローバル移動SEでもコマンド指定soundClipが鳴るように（AudioSE clip同期）。
//   1.5.0 - 音にPlay/Stopとループ指定を追加（起床ラッパ等の鳴らし止め用）。
//   1.4.0 - 音を発動者位置へ移動して鳴らすオプション(soundAtPerformer)を追加。
//   1.3.0 - スカイボックスにFlip(A↔B往復)を追加。Late-Joiner対応。
//   1.2.0 - アクション4：スカイボックス切り替えを追加（ローカル/グローバル）。
//   1.1.0 - ワープ先を視点前/手元で選択可能に（warpAnchor / warpDistance）。
//   1.0.0 - 初版。3種アクション + ローカル/グローバル切替。
// =============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Yamadev.YamaStream;

// ※ NoVariableSync：同期変数は一切持たないが SendCustomNetworkEvent は使う。
//   BehaviourSyncMode.None にすると「何も同期しない」だけでなく
//   SendCustomNetworkEvent 自体が無効化され、他人へのイベントが
//   エラーも出さず黙って捨てられる（グローバル音・音量が効かない原因になる）。
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class MET_GestureCommand : UdonSharpBehaviour
{
    [Header("■ コマンド基本設定")]
    [Tooltip("コマンド名（デバッグ表示用）")]
    public string commandName = "新規コマンド";

    [Tooltip("発動に必要な方向シーケンス。上から順に入力。\n" +
             "例）上・右・下 と並べると『上→右→下』で発動。\n" +
             "連続する同方向はManager側で1つにまとめて判定します。")]
    public MET_GestureDir[] pattern;

    // ---------------------------------------------------------
    [Header("■ アクション1：オブジェクトのON/OFF")]
    [Tooltip("このアクションを使うならチェック")]
    public bool enableToggle = false;

    [Tooltip("ON/OFFする対象オブジェクト（複数可）")]
    public GameObject[] toggleTargets;

    [Tooltip("動作：反転 / 強制ON / 強制OFF")]
    public MET_ToggleType toggleType = MET_ToggleType.Flip;

    [Tooltip("ON=全員に反映（Late-Joinerも） / OFF=自分だけ")]
    public bool toggleGlobal = false;

    // ---------------------------------------------------------
    [Header("■ アクション2：オブジェクトワープ")]
    [Tooltip("このアクションを使うならチェック")]
    public bool enableWarp = false;

    [Tooltip("発動者の元へ呼び寄せる対象（複数可）")]
    public GameObject[] warpTargets;

    [Tooltip("ワープ先の基準：ViewFront=視点(頭)の前 / Hand=描画手の位置")]
    public MET_WarpAnchor warpAnchor = MET_WarpAnchor.ViewFront;

    [Tooltip("ViewFront時、視点から前方この距離(m)に配置。0.4〜0.8 が目安")]
    public float warpDistance = 0.6f;

    [Tooltip("ON=全員に反映（対象に VRC Object Sync が必要） / OFF=自分だけ")]
    public bool warpGlobal = false;

    // ---------------------------------------------------------
    [Header("■ アクション3：音を鳴らす")]
    [Tooltip("このアクションを使うならチェック")]
    public bool enableSound = false;

    [Tooltip("Play=鳴らす / Stop=指定AudioSourceの再生を止める")]
    public MET_SoundAction soundAction = MET_SoundAction.Play;

    [Tooltip("再生／停止に使う AudioSource")]
    public AudioSource soundSource;

    [Tooltip("再生するクリップ（未指定なら AudioSource の設定音を再生）")]
    public AudioClip soundClip;

    [Range(0f, 3f)]
    [Tooltip("このコマンドの再生音量倍率。1=原音 / 0=無音 / 1超=増幅。\n" +
             "一発再生：PlayOneShotの倍率（1超で増幅・音割れ注意）\n" +
             "ループ・クリップ未指定：AudioSourceのvolumeに設定（1超は1で頭打ち）\n" +
             "※既存コマンドは追加後に1.0を確認（0だと無音になります）")]
    public float soundVolume = 1f;

    [Tooltip("Play時：ON=止めるまでループ再生 / OFF=一発だけ")]
    public bool soundLoop = false;

    [Tooltip("ON=全員に反映（鳴らす/止める両方） / OFF=自分だけ")]
    public bool soundGlobal = false;

    [Tooltip("ON=発動者の描いた手（VR時／デスクトップは頭）へ AudioSource を移動して鳴らし、\n" +
             "鳴り終わったら元位置へ戻す。※位置が効くのは AudioSource が 3D の時のみ。\n" +
             "全域に聞かせたい音は 2D、発動者位置から鳴らしたい音は 3D にする")]
    public bool soundAtPerformer = false;

    [Tooltip("soundAtPerformer=ON時に使用。AudioSource側に付けた MET_GestureAudioSE を刺す")]
    public MET_GestureAudioSE audioSE;

    // ---------------------------------------------------------
    [Header("■ アクション4：スカイボックス切り替え")]
    [Tooltip("このアクションを使うならチェック")]
    public bool enableSkybox = false;

    [Tooltip("Set=指定Materialに切り替え（一方通行） / Flip=A↔B交互（往復）")]
    public MET_SkyboxMode skyboxMode = MET_SkyboxMode.Set;

    [Tooltip("Set時：切り替え先Material。\nFlip時：スカイボックスA（最初に切り替わる先。例：夜）")]
    public Material skyboxMaterial;

    [Tooltip("Flip時のみ：スカイボックスB（戻し先。例：昼）")]
    public Material skyboxMaterialB;

    [Tooltip("ON=全員の空が変わる（Late-Joinerにも反映・skyboxSync必須） / OFF=自分だけ")]
    public bool skyboxGlobal = false;

    // ---------------------------------------------------------
    [Header("■ アクション5：YamaPlayer音量調整")]
    [Tooltip("このアクションを使うならチェック。対象YamaPlayerはManagerのyamaControllerを使用")]
    public bool enableVolume = false;

    [Tooltip("音量の増減量（％ポイント）。+10で+10%上げ、-10で-10%下げ。\n" +
             "現在の音量へ加減算し、0〜100%に自動クランプされます")]
    public float volumeDelta = 10f;

    [Tooltip("ON=全員の音量が変わる（各自の現在値へ増減） / OFF=自分だけ")]
    public bool volumeGlobal = false;

    // ---------------------------------------------------------
    // ※アクション6（花火）は凍結のため選択肢から除去。
    //   花火システム本体（MET_FireworkLauncher / MET_Firework）は保持。
    //   復活時はここへ enableFirework / fireworkLauncher / fireworkAction /
    //   fireworkGlobal と _DoFirework を戻す（Version 1.8.0 参照）。

    // Managerから配布される音量調整対象（YamaStreamのController）
    private Controller _yamaController;

    // ワープの遅延再配置用（他人が持っていた時だけ使う）
    private Vector3 _pendingPos = Vector3.zero;
    private Quaternion _pendingRot = Quaternion.identity;
    private bool _hasPending = false;

    // 手放し・所有権移譲がネットワーク越しに届くのを待つ時間（秒）
    private const float WARP_REAPPLY_DELAY = 0.35f;

    // =========================================================
    //  発動（Managerから呼ばれる）
    // =========================================================
    public void _Execute(MET_GestureSystemManager mgr)
    {
        if (enableToggle) _DoToggle(mgr);
        if (enableWarp) _DoWarp(mgr);
        if (enableSound) _DoSound(mgr);
        if (enableSkybox) _DoSkybox(mgr);
        if (enableVolume) _DoVolume(mgr);
    }

    /// <summary>音量調整対象のControllerを受け取る（Managerが起動時に配布）。</summary>
    public void _SetYamaController(Controller controller)
    {
        _yamaController = controller;
    }

    // --- アクション1：ON/OFF ---------------------------------
    private void _DoToggle(MET_GestureSystemManager mgr)
    {
        if (toggleTargets == null) return;
        int tt = (int)toggleType; // 0=反転 1=ON 2=OFF

        if (toggleGlobal && mgr.stateSync != null)
        {
            for (int i = 0; i < toggleTargets.Length; i++)
            {
                if (toggleTargets[i] != null)
                    mgr.stateSync._RequestToggle(toggleTargets[i], tt);
            }
        }
        else
        {
            for (int i = 0; i < toggleTargets.Length; i++)
            {
                GameObject o = toggleTargets[i];
                if (o == null) continue;
                bool ns = (tt == 1) ? true : (tt == 2) ? false : !o.activeSelf;
                o.SetActive(ns);
            }
        }
    }

    // --- アクション2：手元ワープ ------------------------------
    //
    // 【2回目以降ちゃんと来なかった理由】
    //   単に transform を書き換えるだけでは、以下の状態が残っていると弾かれる。
    //     ・Rigidbody の速度      … 呼び寄せ後の落下や投擲の勢いがそのまま残り、
    //                               置いた直後に飛んでいく／落ちていく
    //     ・Pickup で持たれている … 毎フレーム手の位置で上書きされ、置いても戻る。
    //                               他人が持っていると所有権の取得も弾かれる
    //     ・VRC Object Sync       … 不連続（ワープ）だと伝えないと補間で流れる
    //   1回目だけ成功していたのは、初期位置で静止していて上記どれにも当たらないため。
    private void _DoWarp(MET_GestureSystemManager mgr)
    {
        if (warpTargets == null) return;

        Vector3 pos;
        Quaternion rot;
        if (warpAnchor == MET_WarpAnchor.ViewFront)
        {
            pos = mgr._GetViewFrontPosition(warpDistance);
            rot = mgr._GetViewRotation();
        }
        else
        {
            pos = mgr._GetDrawHandPosition();
            rot = mgr._GetDrawHandRotation();
        }

        // 置く前に持ち手を離させる。自分は即実行、グローバルなら他人へも送る。
        bool heldByOther = _DropWarpTargetsLocal();
        if (warpGlobal)
        {
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_DropWarpTargetsNetworked));
            heldByOther = heldByOther || _IsHeldByOther();
        }

        for (int i = 0; i < warpTargets.Length; i++)
        {
            GameObject o = warpTargets[i];
            if (o == null) continue;

            if (warpGlobal)
            {
                // 位置同期は対象に付いた VRC Object Sync が担当する
                if (!Networking.IsOwner(o))
                    Networking.SetOwner(Networking.LocalPlayer, o);
            }
            _PlaceWarpTarget(o, pos, rot);
        }

        // 他人が持っていた場合、手放し・所有権移譲がネットワーク越しに完了するまで
        // 数フレームかかる。その間は相手のクライアントの位置が勝ってしまうため、
        // 少し待ってからもう一度置き直す（届いていれば今度こそ確定する）。
        if (heldByOther)
        {
            _pendingPos = pos;
            _pendingRot = rot;
            _hasPending = true;
            SendCustomEventDelayedSeconds(nameof(_ReapplyWarp), WARP_REAPPLY_DELAY);
        }
    }

    // 1体を実際に配置する（物理状態と同期の後始末込み）
    private void _PlaceWarpTarget(GameObject o, Vector3 pos, Quaternion rot)
    {
        Rigidbody rb = o.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            // 前回の呼び寄せ以降に付いた落下・投擲の勢いを消す。
            // 残っていると置いた瞬間に飛んでいき「近くに来ない」原因になる。
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        o.transform.SetPositionAndRotation(pos, rot);

        // 物理側の姿勢も同フレームで揃える（次の物理ステップまで旧位置が残るのを防ぐ）
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
        }

        // ワープは「移動」ではなく「不連続」だと同期側へ伝える。
        // これが無いと他クライアントで元位置から補間して流れてくる。
        VRC.SDK3.Components.VRCObjectSync sync =
            o.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
        if (sync != null) sync.FlagDiscontinuity();
    }

    // 遅延再配置の受け口（SendCustomEventDelayedSeconds から呼ばれるため public）
    public void _ReapplyWarp()
    {
        if (!_hasPending) return;
        _hasPending = false;
        if (warpTargets == null) return;

        for (int i = 0; i < warpTargets.Length; i++)
        {
            GameObject o = warpTargets[i];
            if (o == null) continue;

            // 猶予の間に誰かが拾っていたら、その人の操作を尊重して触らない
            VRC_Pickup pu = (VRC_Pickup)o.GetComponent(typeof(VRC_Pickup));
            if (pu != null && pu.IsHeld) continue;

            if (warpGlobal && !Networking.IsOwner(o)) continue; // 所有権が取れていない
            _PlaceWarpTarget(o, _pendingPos, _pendingRot);
        }
    }

    // 自分が持っている対象を手放す。戻り値=他人が持っている対象があったか
    private bool _DropWarpTargetsLocal()
    {
        if (warpTargets == null) return false;

        bool otherHolds = false;
        for (int i = 0; i < warpTargets.Length; i++)
        {
            GameObject o = warpTargets[i];
            if (o == null) continue;

            VRC_Pickup pu = (VRC_Pickup)o.GetComponent(typeof(VRC_Pickup));
            if (pu == null || !pu.IsHeld) continue;

            if (pu.currentPlayer != null && pu.currentPlayer.isLocal) pu.Drop();
            else otherHolds = true;
        }
        return otherHolds;
    }

    // グローバル時の手放し指示の受け口（他クライアントで実行される）
    public void _DropWarpTargetsNetworked()
    {
        _DropWarpTargetsLocal();
    }

    // 対象のいずれかを他人が持っているか
    private bool _IsHeldByOther()
    {
        if (warpTargets == null) return false;

        for (int i = 0; i < warpTargets.Length; i++)
        {
            GameObject o = warpTargets[i];
            if (o == null) continue;

            VRC_Pickup pu = (VRC_Pickup)o.GetComponent(typeof(VRC_Pickup));
            if (pu != null && pu.IsHeld) return true;
        }
        return false;
    }

    // --- アクション3：音 --------------------------------------
    private void _DoSound(MET_GestureSystemManager mgr)
    {
        // 停止（自分は即実行し、グローバルなら他人へも送る）
        if (soundAction == MET_SoundAction.Stop)
        {
            _StopSoundLocal();
            if (soundGlobal) SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_StopSoundNetworked));
            return;
        }

        // 再生（発動者位置へ移動して鳴らす）
        if (soundAtPerformer && audioSE != null)
        {
            Vector3 pos = mgr._GetPerformerPosition();
            if (soundGlobal) audioSE._PlayGlobal(pos, soundClip, soundVolume);
            else audioSE._PlayLocal(pos, soundClip, soundVolume);
            return;
        }

        // 再生（通常）自分は即座に鳴らし、グローバルなら他人へも送る
        _PlaySoundLocal();
        if (soundGlobal) SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_PlaySoundNetworked));
    }

    // グローバル再生の受け口（全クライアントで実行される）
    public void _PlaySoundNetworked()
    {
        _PlaySoundLocal();
    }

    // グローバル停止の受け口（全クライアントで実行される）
    public void _StopSoundNetworked()
    {
        _StopSoundLocal();
    }

    private void _PlaySoundLocal()
    {
        if (soundSource == null) return;

        if (soundLoop)
        {
            if (soundClip != null) soundSource.clip = soundClip;
            soundSource.loop = true;
            soundSource.volume = soundVolume; // ループはvolumeに直接反映
            soundSource.Play();
        }
        else
        {
            soundSource.loop = false;
            if (soundClip != null)
            {
                // 一発再生はPlayOneShotの倍率で。source.volumeを汚さず使い回せる
                soundSource.PlayOneShot(soundClip, soundVolume);
            }
            else
            {
                soundSource.volume = soundVolume; // clip未指定はvolumeに直接反映
                soundSource.Play();
            }
        }
    }

    private void _StopSoundLocal()
    {
        if (soundSource == null) return;

        // Play()系（ループ含む）を停止
        soundSource.Stop();

        // PlayOneShot で鳴らした音は Stop() では止まらない仕様のため、
        // AudioSource を一瞬無効化して全ボイス（一発再生・発動者位置SE含む）をクリアする。
        soundSource.enabled = false;
        soundSource.enabled = true;
    }

    // --- アクション5：YamaPlayer音量調整 ----------------------
    private void _DoVolume(MET_GestureSystemManager mgr)
    {
        if (_yamaController == null) return;

        if (volumeGlobal)
        {
            // 各クライアントが自分の現在値へdeltaを加算する（相対増減）。
            // Volumeはローカル設定なので状態同期・Owner権限は不要。
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_ApplyVolumeNetworked));
        }
        else
        {
            _ApplyVolumeLocal();
        }
    }

    // グローバル音量調整の受け口（全クライアントで実行される）
    public void _ApplyVolumeNetworked()
    {
        _ApplyVolumeLocal();
    }

    private void _ApplyVolumeLocal()
    {
        if (_yamaController == null) return;
        // volumeDeltaは％ポイント（+10=+0.1）。setter側で0〜1にクランプされる。
        _yamaController.Volume = _yamaController.Volume + volumeDelta * 0.01f;
    }

    // --- アクション4：スカイボックス切り替え ------------------
    private void _DoSkybox(MET_GestureSystemManager mgr)
    {
        if (skyboxMode == MET_SkyboxMode.Flip)
        {
            if (skyboxMaterial == null || skyboxMaterialB == null) return;

            if (skyboxGlobal && mgr.skyboxSync != null)
            {
                mgr.skyboxSync._RequestSkyboxFlip(skyboxMaterial, skyboxMaterialB);
            }
            else
            {
                // ローカルのみ：現在の空を見てA↔B反転
                Material cur = RenderSettings.skybox;
                RenderSettings.skybox = (cur == skyboxMaterial) ? skyboxMaterialB : skyboxMaterial;
            }
        }
        else // Set
        {
            if (skyboxMaterial == null) return;

            if (skyboxGlobal && mgr.skyboxSync != null)
            {
                mgr.skyboxSync._RequestSkybox(skyboxMaterial);
            }
            else
            {
                RenderSettings.skybox = skyboxMaterial; // ローカルのみ
            }
        }
    }
}