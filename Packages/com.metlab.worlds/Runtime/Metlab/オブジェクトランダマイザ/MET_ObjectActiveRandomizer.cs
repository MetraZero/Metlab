// ============================================================================
//  MET_ObjectActiveRandomizer
//  UdonSharp / VRChat World 用
//  Unity 2022.3.22f1
//
//  ● 機能
//    インスペクタに登録したオブジェクト群を、設定した確率で「まとめて」
//    ON / OFF する。抽選は1回だけ行い、
//      ・当選  → 登録した対象を「すべて」ON
//      ・非当選 → 登録した対象を「すべて」OFF
//    ・状態はグローバル同期され、後から来たプレイヤーにも共有される。
//    ・インスタンスに「最初の一人目」が入ったときだけ抽選される。
//    ・2人目以降が入っても抽選されない。
//    ・一人目が抜けて再度入ってきても抽選されない（インスタンスが
//      空になってリセットされるまで結果は保持される）。
//    ・シーン上でオブジェクトが ON / OFF どちらでも動作する。
//
//  ● バージョン: 1.1.0
//    1.1.0: 抽選を「対象ごとの個別抽選」から「全体で1回の抽選」に変更。
//           当選時は登録した全オブジェクトをまとめてONにする。
//    1.0.0: 初版（対象ごとに個別抽選）
//
//  ● 使い方
//    1. 常にアクティブな空オブジェクトにこのスクリプトを付ける
//       （このスクリプトが載ったオブジェクト自体は非アクティブにしない）。
//    2. Probability(%) を設定（スライダー or 数値入力）。
//    3. Target Objects に抽選対象を登録する。
//       ※ 何も登録しない場合は、このオブジェクトの「直下の子」を
//         自動的に対象にする（子の並び順で処理）。
// ============================================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_ObjectActiveRandomizer : UdonSharpBehaviour
{
    [Tooltip("対象をまとめて ON にする確率 (%)。スライダー / 数値入力どちらも可。抽選は全体で1回だけ行われる。")]
    [Range(0f, 100f)]
    [SerializeField] private float probability = 50f;

    [Tooltip("抽選対象のオブジェクトを登録するスロット。空の場合はこのオブジェクトの直下の子を対象にする。当選時はここに登録した全オブジェクトがまとめてONになる。")]
    [SerializeField] private GameObject[] targetObjects;

    // 実際に使用する対象リスト（登録スロット or 直下の子から解決）
    private GameObject[] _targets;

    // --- 同期変数 ---
    [UdonSynced] private bool _rolled = false;   // 抽選済みフラグ
    [UdonSynced] private bool _activate = false; // 抽選結果（true=当選=全ON / false=非当選=全OFF）

    void Start()
    {
        ResolveTargets();

        // 一人目 = そのインスタンスの最初のマスター。
        // まだ抽選されていなければ、このマスターだけが抽選を行う。
        if (Networking.IsMaster && !_rolled)
        {
            Roll();
        }
        else
        {
            // 既に抽選済みの状態が同期されていれば適用する
            ApplyStates();
        }
    }

    // 対象リストを決定する。
    // スロットに1つでも登録があればそれを使用、無ければ直下の子を使用。
    private void ResolveTargets()
    {
        bool hasSlot = false;
        if (targetObjects != null)
        {
            for (int i = 0; i < targetObjects.Length; i++)
            {
                if (targetObjects[i] != null) { hasSlot = true; break; }
            }
        }

        if (hasSlot)
        {
            _targets = targetObjects;
        }
        else
        {
            // 直下の子オブジェクトを並び順で取得
            int c = transform.childCount;
            _targets = new GameObject[c];
            for (int i = 0; i < c; i++)
            {
                _targets[i] = transform.GetChild(i).gameObject;
            }
        }
    }

    private void Roll()
    {
        // 抽選は「全体で1回だけ」。probability(%) 未満なら当選 → 全ON。
        _activate = Random.Range(0f, 100f) < probability;

        _rolled = true;
        ApplyStates();
        RequestSerialization(); // 他プレイヤー・後入りへ同期
    }

    // 後から来たプレイヤーが同期データを受け取ったとき
    public override void OnDeserialization()
    {
        ApplyStates();
    }

    private void ApplyStates()
    {
        if (_targets == null) return;

        // 当選なら全ON、非当選なら全OFF
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] != null)
            {
                _targets[i].SetActive(_activate);
            }
        }
    }
}
