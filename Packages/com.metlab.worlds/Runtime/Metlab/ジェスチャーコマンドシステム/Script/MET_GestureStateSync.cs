// =============================================================
// MET_GestureStateSync.cs
// MET_ Gesture System - グローバルトグル状態の同期管理
// Version: 1.0.0
//
// [概要]
//   グローバル同期でON/OFFするオブジェクトの状態を一元管理する。
//   Manual同期の bool 配列で状態を保持し、OnDeserialization で全員
//   （後から入ってきたLate-Joinerも含む）に反映する。
//
//   対象オブジェクトは Manager 側が起動時にコマンドから自動収集して
//   push するため、通常このコンポーネントに手動登録は不要。
//
// [Changelog]
//   1.0.0 - 初版。グローバルON/OFF状態のManual同期とLate-Joiner対応。
// =============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MET_GestureStateSync : UdonSharpBehaviour
{
    [Header("■ グローバル同期するトグル対象")]
    [Tooltip("グローバル同期でON/OFFする全オブジェクト。\n" +
             "通常はManagerが自動収集して埋めるので、手動登録は不要です。")]
    public GameObject[] targets;

    // 各targetの現在ON/OFF状態。targets と同じ長さ・同じ順序で対応。
    [UdonSynced] private bool[] _states;

    private bool _initialized;

    void Start()
    {
        _Initialize();
    }

    /// <summary>Inspectorのtargetsから初期化（既に初期化済みなら何もしない）。</summary>
    public void _Initialize()
    {
        if (_initialized) return;
        if (targets == null) targets = new GameObject[0];

        _states = new bool[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            _states[i] = (targets[i] != null) && targets[i].activeSelf;
        }
        _initialized = true;
    }

    /// <summary>Manager が収集した対象を流し込んで再初期化する。</summary>
    public void _SetTargets(GameObject[] newTargets)
    {
        targets = (newTargets != null) ? newTargets : new GameObject[0];
        _initialized = false;
        _Initialize();
    }

    // 対象のindexを線形検索（見つからなければ -1）
    private int _FindIndex(GameObject target)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == target) return i;
        }
        return -1;
    }

    /// <summary>
    /// グローバルトグル要求。
    /// toggleType : 0=反転 / 1=ONにする / 2=OFFにする
    /// </summary>
    public void _RequestToggle(GameObject target, int toggleType)
    {
        if (!_initialized) _Initialize();

        int idx = _FindIndex(target);
        if (idx < 0)
        {
            Debug.LogWarning("[MET_GestureStateSync] 未登録の対象です: " +
                             (target != null ? target.name : "null"));
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (toggleType == 1) _states[idx] = true;
        else if (toggleType == 2) _states[idx] = false;
        else _states[idx] = !_states[idx];

        _ApplyState(idx);
        RequestSerialization();
    }

    private void _ApplyState(int idx)
    {
        if (idx < 0 || idx >= targets.Length) return;
        if (targets[idx] != null)
        {
            targets[idx].SetActive(_states[idx]);
        }
    }

    private void _ApplyAll()
    {
        if (_states == null) return;
        int n = Mathf.Min(targets.Length, _states.Length);
        for (int i = 0; i < n; i++)
        {
            _ApplyState(i);
        }
    }

    // 同期受信時（Late-Joinerの初回受信含む）に全状態を反映
    public override void OnDeserialization()
    {
        if (!_initialized) _Initialize();
        _ApplyAll();
    }
}
