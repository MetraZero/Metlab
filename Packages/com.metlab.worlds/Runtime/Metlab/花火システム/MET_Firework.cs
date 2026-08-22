// ============================================================
// MET_Firework
// 概要: 1発ぶんの「花火ユニット」。1つの上昇（打ち上げ）に対して、
//       複数の炸裂ParticleSystemを組み合わせて開かせる（例: しだれ＋丸）。
//       音は上昇音・炸裂音を各1個だけ持つ（＝1ユニット1発ぶんの音）。
//       ランチャー(MET_FireworkLauncher)から _Fire() で発射される。
//
//       発射の流れ（すべてローカル・パーティクルは同期しない＝軽量）:
//         t=0        … 上昇PS(risePS)を再生し、上昇音「ヒュ〜」を鳴らす。
//                      trimRiseSound=ON なら riseTime で自動停止（末尾ドカンをカット）。
//         t=riseTime … 登録した炸裂PSをすべて開かせる（＝炸裂）。
//                      各炸裂の burstDelays[i] を、その炸裂PSの Start Delay に反映して
//                      二段咲き・追い咲き（時差爆発）を表現する。0なら同時。
//                      同時に炸裂音「パァン」を、音速遅延ぶん遅らせて鳴らす（リアル）。
//
//       炸裂ごとの色:
//         burstRandomizeColor[i]=ON の炸裂だけ、発射のたびに Start Color を
//         ランダム化する（しだれのような固定色はOFFのまま残せる）。
//         色候補 burstColors が空ならカラフルな色を自動生成。
//
//       再入について:
//         共有カーソル等の可変状態を持たず、遅延イベントも引数を持たないため、
//         短間隔で同じユニットが再発射されても状態が壊れない設計。
//
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_Firework : UdonSharpBehaviour
{
    [Header("■ 上昇（打ち上げ）")]
    [Tooltip("打ち上げの尾を引く上昇ParticleSystem（任意）。\n" +
             "発射と同時(t=0)に再生します。未設定なら上昇演出なし。\n" +
             "※Simulation Space=World 推奨。")]
    [SerializeField] private ParticleSystem risePS;

    [Tooltip("打ち上げ〜炸裂までの秒数。上昇PSの Start Lifetime に合わせてください。\n" +
             "この時間の後に炸裂を開かせ、炸裂音を鳴らし始めます")]
    [SerializeField] private float riseTime = 1.2f;

    [Header("■ 炸裂（複数を組み合わせ）")]
    [Tooltip("この花火で開く炸裂ParticleSystem群。しだれ＋丸のように複数登録すると\n" +
             "組み合わせ花火になります。t=riseTime で一斉に Play します。\n" +
             "※各炸裂PSは Simulation Space=World 推奨。")]
    [SerializeField] private ParticleSystem[] burstParticles;

    [Tooltip("各炸裂の時差（秒）。burstParticles と同じ順番で対応させます。\n" +
             "0＝riseTimeちょうどに開く（同時）。値を入れるとその秒だけ遅れて開く\n" +
             "（＝二段咲き・追い咲き）。要素が足りない分は0として扱います。\n" +
             "※内部的に各炸裂PSの Start Delay に反映します")]
    [SerializeField] private float[] burstDelays;

    [Header("■ 炸裂の色（炸裂ごとにランダム）")]
    [Tooltip("各炸裂の色をランダム化するか。burstParticles と同じ順番で対応させます。\n" +
             "ON＝発射ごとに Start Color をランダムに変える（丸などのカラフルな炸裂向け）。\n" +
             "OFF＝色はそのまま（金色のしだれなど固定色の炸裂向け）。\n" +
             "要素が足りない分はOFFとして扱います。\n" +
             "※ランダム化する炸裂PSは Start Color を『Color（単色）』にし、\n" +
             "　Color over Lifetime は『白→透明』のフェードのみにすると色が活きます")]
    [SerializeField] private bool[] burstRandomizeColor;

    [Tooltip("ランダム化する炸裂色の候補（全炸裂共通）。1つ以上入れるとこの中から選びます。\n" +
             "空（0個）ならカラフルな色を自動生成します")]
    [SerializeField] private Color[] burstColors;

    [Header("■ 音：上昇音（ヒュ〜）")]
    [Tooltip("上昇音のAudioSource（任意・1個）。発射と同時に再生します。\n" +
             "clip・音量・3D設定はAudioSource側で設定してください")]
    [SerializeField] private AudioSource riseSound;

    [Tooltip("ON=上昇音を riseTime で自動停止する。\n" +
             "『ヒュー…ドカン』が1つになった素材の末尾ドカンを切って\n" +
             "ヒューだけ鳴らしたい時にON。純粋な上昇音だけの素材ならOFF")]
    [SerializeField] private bool trimRiseSound = true;

    [Header("■ 音：炸裂音（パァン）")]
    [Tooltip("炸裂音のAudioSource（任意・1個）。riseTime＋音速遅延の後に再生します。\n" +
             "複数炸裂でも音は1回だけ鳴らします。clip等はAudioSource側で")]
    [SerializeField] private AudioSource burstSound;

    [Tooltip("ON=『光ってから音が遅れて届く』音速遅延を入れる（リアル）。\n" +
             "OFF=炸裂と同時に鳴らす")]
    [SerializeField] private bool useSoundDelay = true;

    [Tooltip("音速（m/s）。標準は約 340。小さくすると遅延が大きく（＝より遠くに感じる）")]
    [SerializeField] private float speedOfSound = 340f;

    // ---- 内部状態（ローカル）----
    private bool _initialized;
    private VRCPlayerApi _local;

    void Start()
    {
        _Initialize();
    }

    private void _Initialize()
    {
        if (_initialized) return;
        if (_local == null) _local = Networking.LocalPlayer;
        if (burstParticles == null) burstParticles = new ParticleSystem[0];
        if (burstDelays == null) burstDelays = new float[0];
        if (burstRandomizeColor == null) burstRandomizeColor = new bool[0];
        if (burstColors == null) burstColors = new Color[0];

        if (riseTime < 0f) riseTime = 0f;
        if (speedOfSound < 1f) speedOfSound = 340f;

        _initialized = true;
    }

    // =========================================================
    //  発射API（ランチャーから呼ぶ）
    // =========================================================

    /// <summary>この花火を1発発射する（上昇→炸裂を再生）。</summary>
    public void _Fire()
    {
        if (!_initialized) _Initialize();

        // 上昇演出（尾）
        if (risePS != null)
        {
            risePS.Play();
        }

        // 上昇音「ヒュ〜」：発射と同時
        if (riseSound != null)
        {
            riseSound.Play();
            if (trimRiseSound && riseTime > 0f)
            {
                riseSound.SetScheduledEndTime(AudioSettings.dspTime + riseTime);
            }
        }

        // riseTime 後に炸裂させる。riseTime=0 なら即時に開く。
        if (riseTime > 0f)
        {
            SendCustomEventDelayedSeconds(nameof(_Burst), riseTime);
        }
        else
        {
            _Burst();
        }
    }

    // 炸裂の瞬間（SendCustomEventDelayedSeconds から呼ばれるため public）
    public void _Burst()
    {
        // 登録された炸裂をすべて開かせる（各炸裂の時差・色を反映）
        for (int i = 0; i < burstParticles.Length; i++)
        {
            ParticleSystem ps = burstParticles[i];
            if (ps == null) continue;

            // 時差爆発：この炸裂PSの Start Delay に反映（0なら即時）
            // ※通常運用では前回発射ぶんは既に停止済みなので、そのまま設定できる。
            //   万一まだ再生中でも、再生中のStartDelay変更は無視される（＝無害）だけ。
            float delay = (i < burstDelays.Length) ? burstDelays[i] : 0f;
            if (delay < 0f) delay = 0f;

            ParticleSystem.MainModule m = ps.main;
            m.startDelay = new ParticleSystem.MinMaxCurve(delay);

            // 炸裂色のランダム化（この炸裂がON指定のときだけ）
            if (i < burstRandomizeColor.Length && burstRandomizeColor[i])
            {
                m.startColor = new ParticleSystem.MinMaxGradient(_PickBurstColor());
            }

            ps.Play();
        }

        // 炸裂音「パァン」：音は1回だけ。音速遅延ぶん遅らせる。
        if (burstSound != null)
        {
            float soundDelay = 0f;
            if (useSoundDelay && _local != null)
            {
                Vector3 head = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                float dist = Vector3.Distance(head, burstSound.transform.position);
                soundDelay = dist / speedOfSound;
            }
            if (soundDelay <= 0f) burstSound.Play();
            else burstSound.PlayDelayed(soundDelay);
        }
    }

    // ランダム化する炸裂色を1つ決める（候補があれば候補から、無ければ自動生成）
    private Color _PickBurstColor()
    {
        if (burstColors.Length > 0)
        {
            return burstColors[Random.Range(0, burstColors.Length)];
        }
        // 候補未指定：彩度・明度を高めにしたカラフルな色を自動生成
        return Color.HSVToRGB(Random.value, Random.Range(0.7f, 1f), 1f);
    }
}
