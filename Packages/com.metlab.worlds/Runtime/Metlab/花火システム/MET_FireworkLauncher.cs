// ============================================================
// MET_FireworkLauncher
// 概要: 打ち上げ花火の発射エンジン。登録された「花火ユニット(MET_Firework)」
//       の中から1発ごとにランダムに1つ選び、連続再生する。
//       「花火大会モード(ON/OFF)」だけをネットワーク同期し、実際の各発射は
//       各クライアントがローカルで再生する（＝パーティクル自体は同期しない＝軽量）。
//       発射のたびに「間隔・角度・発射位置」をランダムに揺らして単調にならない
//       ようにする。ジェスチャーコマンドシステムのアクション6から
//       Start/Stop/Toggle で呼び出して使う。
//
//       花火ユニット(MET_Firework):
//         1発ぶんの上昇＋複数の炸裂（しだれ＋丸など）＋音を自己完結で持つ。
//         炸裂の数・組み合わせ・時差・色・音はユニット側で設定する。
//         このランチャーは「どのユニットを・いつ・どこで」発射するかだけを担う。
//
//       同期方針:
//         [UdonSynced] bool _active … 花火大会モードのON/OFF。
//         OnDeserialization で全員（後入りのLate-Joinerも）に反映し、
//         各自ローカルで発射ループを開始/停止する。
//
//       発射ループ:
//         Update() は使わず SendCustomEventDelayedSeconds の自己ループ。
//         _pending で予約数を管理し、ON/OFFの高速連打でも多重起動しない。
//
//       ブレの与え方:
//         発射のたびに、選んだ花火ユニットの localPosition / localRotation を
//         基準値からランダムにずらしてから _Fire() する。
//
// バージョン: 2.1.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
//   ※v1.x のフラット配列(fireworks/burstParticles/riseSounds/burstSounds)方式から、
//     花火ユニット(MET_Firework)配列方式へ再構成（破壊的変更）。
//   ※2.1.0: 試作用に「シーン開始で自動発射(autoStart)」を追加。
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_FireworkLauncher : UdonSharpBehaviour
{
    [Header("■ 花火ユニット")]
    [Tooltip("打ち上げる花火ユニット(MET_Firework)群。1発ごとにこの中から\n" +
             "ランダムに1つ選んで発射します。しだれ・丸・組み合わせなど、\n" +
             "種類ちがいを複数登録すると花火大会らしくなります。\n" +
             "※各ユニットはこのランチャーの子オブジェクトにするのが扱いやすいです。")]
    [SerializeField] private MET_Firework[] fireworks;

    [Header("■ 発射間隔")]
    [Tooltip("次の花火までの基準間隔（秒）")]
    [SerializeField] private float interval = 1.0f;

    [Tooltip("発射間隔のブレ幅（±秒）。0でブレなし（等間隔）")]
    [SerializeField] private float intervalJitter = 0.5f;

    [Header("■ 発射のブレ")]
    [Tooltip("発射角度のブレ（度）。真上からこの範囲でランダムに傾いて飛びます。\n" +
             "0で常に真っ直ぐ。10〜20 くらいが自然")]
    [SerializeField] private float angleJitter = 12f;

    [Tooltip("発射元のブレ（m）。ランチャー位置を中心に、この半径内の水平位置\n" +
             "からランダムに打ち上げます。0で常に同じ位置から")]
    [SerializeField] private float positionJitter = 2f;

    [Header("■ 起動")]
    [Tooltip("ON=シーン開始と同時に自動で発射を始める（試作・動作確認用）。\n" +
             "各クライアントがローカルで発射します（同期しない）。\n" +
             "ジェスチャーコマンド等でON/OFFしたい本番運用ではOFFにしてください")]
    [SerializeField] private bool autoStart = false;

    // ---- 同期状態 ----
    // 花火大会モードのON/OFF。true の間、各クライアントが自動連続発射する。
    [UdonSynced] private bool _active;

    // ---- 内部状態（ローカル）----
    private bool _running;      // ローカルの発射ループが稼働中か
    private int _pending;       // キューに残っている _FireLoop 予約数（多重起動防止）
    private bool _initialized;

    // 各花火ユニットの基準ローカル位置・回転（ブレはここからの相対で与える）
    private Vector3[] _basePos;
    private Quaternion[] _baseRot;

    void Start()
    {
        _Initialize();

        // 試作用：シーン開始で自動発射（ローカルのみ・同期しない）
        if (autoStart)
        {
            _SetActiveLocal(true);
        }
    }

    private void _Initialize()
    {
        if (_initialized) return;
        if (fireworks == null) fireworks = new MET_Firework[0];

        // 値域の安全化
        if (interval < 0.05f) interval = 0.05f;
        if (intervalJitter < 0f) intervalJitter = 0f;
        if (angleJitter < 0f) angleJitter = 0f;
        if (positionJitter < 0f) positionJitter = 0f;

        // 各ユニットの基準姿勢をキャプチャ（以降のブレはここからの相対）
        _basePos = new Vector3[fireworks.Length];
        _baseRot = new Quaternion[fireworks.Length];
        for (int i = 0; i < fireworks.Length; i++)
        {
            if (fireworks[i] != null)
            {
                _basePos[i] = fireworks[i].transform.localPosition;
                _baseRot[i] = fireworks[i].transform.localRotation;
            }
            else
            {
                _basePos[i] = Vector3.zero;
                _baseRot[i] = Quaternion.identity;
            }
        }

        _initialized = true;
    }

    // =========================================================
    //  外部API（コマンドや他スクリプトから呼ぶ）
    // =========================================================

    /// <summary>ローカルのみで自動発射をON/OFF（同期しない・自分だけに見える）。</summary>
    public void _SetActiveLocal(bool on)
    {
        if (!_initialized) _Initialize();
        if (on == _running) return;   // 状態が変わらないなら何もしない
        _running = on;

        // ON化した時、生きている予約が無ければループを起動する。
        // 予約が残っていれば（OFF→ONの高速切替）それが継続を担うので新規起動しない。
        if (_running && _pending == 0)
        {
            _ScheduleNext(0f);
        }
    }

    /// <summary>ローカルのみでトグル（同期しない）。</summary>
    public void _ToggleLocal()
    {
        _SetActiveLocal(!_running);
    }

    /// <summary>全員で自動発射をON/OFF（同期・Late-Joiner対応）。</summary>
    public void _RequestSet(bool on)
    {
        if (!_initialized) _Initialize();

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _active = on;
        _ApplyActive();
        RequestSerialization();
    }

    /// <summary>全員でトグル（現在の同期状態を反転）。</summary>
    public void _RequestToggle()
    {
        _RequestSet(!_active);
    }

    // 同期状態(_active)をローカルの発射ループへ反映する
    private void _ApplyActive()
    {
        _SetActiveLocal(_active);
    }

    // 同期受信時（Late-Joinerの初回受信含む）に現在のON/OFFを反映
    public override void OnDeserialization()
    {
        if (!_initialized) _Initialize();
        _ApplyActive();
    }

    // =========================================================
    //  発射ループ（ローカル・自己遅延ループ）
    // =========================================================

    // SendCustomEventDelayedSeconds から呼ばれるため public。
    public void _FireLoop()
    {
        _pending--;                 // 自分の予約を消費
        if (!_running) return;      // 停止済みならここで途切れる（ループ終了）

        _FireOne();

        // 次の発射までの間隔をブレさせる
        float delay = interval + Random.Range(-intervalJitter, intervalJitter);
        if (delay < 0.05f) delay = 0.05f;
        _ScheduleNext(delay);
    }

    // 次の発射を予約する（予約数を数えておき多重起動を防ぐ）
    private void _ScheduleNext(float delay)
    {
        _pending++;
        SendCustomEventDelayedSeconds(nameof(_FireLoop), delay);
    }

    // 花火を1発打つ（ランダムに1ユニット選び、位置・角度をずらして発射する）
    private void _FireOne()
    {
        if (fireworks.Length == 0) return;

        int idx = Random.Range(0, fireworks.Length);
        MET_Firework unit = fireworks[idx];
        if (unit == null) return;

        // 発射元のブレ：水平円内のランダム位置へ（円内一様分布）
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float rad = positionJitter * Mathf.Sqrt(Random.value);
        Vector3 offsetPos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
        unit.transform.localPosition = _basePos[idx] + offsetPos;

        // 発射角度のブレ：真上から前後左右にランダムに傾ける
        // （打ち上げ軸まわりの回転は見た目に影響しないので与えない）
        Quaternion offsetRot = Quaternion.Euler(
            Random.Range(-angleJitter, angleJitter),
            0f,
            Random.Range(-angleJitter, angleJitter));
        unit.transform.localRotation = _baseRot[idx] * offsetRot;

        // ユニット自身に発射させる（上昇→複数炸裂→音はユニット側が担う）
        unit._Fire();
    }
}
