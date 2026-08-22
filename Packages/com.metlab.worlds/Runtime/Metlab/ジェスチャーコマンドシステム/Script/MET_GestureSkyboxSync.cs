// =============================================================
// MET_GestureSkyboxSync.cs
// MET_ Gesture System - グローバルスカイボックス切り替えの同期管理
// Version: 1.1.0
//
// [概要]
//   グローバル切り替えするスカイボックスMaterialを一元管理する。
//   「現在どのスカイボックスか」を index (int) でManual同期し、
//   OnDeserialization で全員（後から入ってきたLate-Joiner含む）に反映。
//
//   切り替え候補のMaterialはManager側が起動時にコマンドから自動収集
//   してpushするため、通常このコンポーネントに手動登録は不要。
//   ※同期はindexで行うため、全クライアントで候補リストの順序が
//     一致している必要がある（同一シーンなので自動的に一致する）。
//
// [配置]
//   専用の空オブジェクトに付け、Manager の skyboxSync 欄に刺す。
//   同期変数を持つのでStateSync同様、単独オブジェクト推奨。
//
// [Changelog]
//   1.1.0 - A↔Bフリップ(_RequestSkyboxFlip)を追加。同期状態基準で往復。
//   1.0.0 - 初版。グローバルスカイボックスのManual同期とLate-Joiner対応。
// =============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_GestureSkyboxSync : UdonSharpBehaviour
{
    [Header("■ 切り替え候補のスカイボックス")]
    [Tooltip("グローバル切り替えで使う全スカイボックスMaterial。\n" +
             "通常はManagerが自動収集して埋めるので、手動登録は不要です。")]
    public Material[] skyboxes;

    // 現在のスカイボックスindex。-1 = 未変更（シーン初期のまま）。
    [UdonSynced] private int _index = -1;

    private bool _initialized;

    void Start()
    {
        _Initialize();
    }

    public void _Initialize()
    {
        if (_initialized) return;
        if (skyboxes == null) skyboxes = new Material[0];
        _initialized = true;
    }

    /// <summary>Manager が収集した候補を流し込む。</summary>
    public void _SetSkyboxes(Material[] mats)
    {
        skyboxes = (mats != null) ? mats : new Material[0];
        _initialized = true;
    }

    private int _FindIndex(Material mat)
    {
        for (int i = 0; i < skyboxes.Length; i++)
        {
            if (skyboxes[i] == mat) return i;
        }
        return -1;
    }

    /// <summary>グローバルにスカイボックスを切り替える。</summary>
    public void _RequestSkybox(Material mat)
    {
        if (!_initialized) _Initialize();

        int idx = _FindIndex(mat);
        if (idx < 0)
        {
            Debug.LogWarning("[MET_GestureSkyboxSync] 未登録のスカイボックスです: " +
                             (mat != null ? mat.name : "null"));
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _index = idx;
        _Apply();
        RequestSerialization();
    }

    private void _Apply()
    {
        if (_index < 0 || _index >= skyboxes.Length) return;
        if (skyboxes[_index] != null)
        {
            RenderSettings.skybox = skyboxes[_index];
        }
    }

    /// <summary>
    /// A↔B を交互に切り替える。現在の同期状態を基準に反転するため、
    /// 途中から入ってきた人が次のフリップを打っても正しく往復する。
    /// ルール：現在がA以外(初期状態やB含む)ならAへ、現在がAならBへ。
    /// </summary>
    public void _RequestSkyboxFlip(Material matA, Material matB)
    {
        if (!_initialized) _Initialize();

        int ia = _FindIndex(matA);
        int ib = _FindIndex(matB);
        if (ia < 0 || ib < 0)
        {
            Debug.LogWarning("[MET_GestureSkyboxSync] 未登録のスカイボックスです（Flip）");
            return;
        }

        Material cur = (_index >= 0 && _index < skyboxes.Length) ? skyboxes[_index] : null;
        int target = (cur == matA) ? ib : ia; // Aにいる時だけB、それ以外はA

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _index = target;
        _Apply();
        RequestSerialization();
    }

    // 同期受信時（Late-Joinerの初回受信含む）に現在のスカイボックスを反映
    public override void OnDeserialization()
    {
        _Apply();
    }
}