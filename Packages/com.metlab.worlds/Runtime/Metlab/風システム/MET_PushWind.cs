// ============================================================
// MET_PushWind
// 概要: BoxTrigger(isTrigger)の範囲内にいるプレイヤーとGravityオブジェクトを、
//       装置のローカル軸方向へ継続的に押し出す「風」装置。
//       ・プレイヤー … ローカル自身のみを速度加算で押す（本人の移動入力も有効）
//       ・オブジェクト … 自分がOwnerのRigidbodyのみを加速で押す（範囲内で離すと飛ぶ）
//       ・距離減衰 … 装置本体に近いほど強い（ON/OFF・最遠倍率を調整可）
//       Pickupに付けて掴んで向きを変える運用を想定。
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// 押し出し方向に使う装置のローカル軸
public enum MET_PushAxis
{
    前方向,   // transform.forward (+Z)
    後方向,   // -forward (-Z)
    上方向,   // transform.up (+Y)
    下方向,   // -up (-Y)
    右方向,   // transform.right (+X)
    左方向    // -right (-X)
}

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_PushWind : UdonSharpBehaviour
{
    [Header("基本設定")]
    [SerializeField, Tooltip("押し出しに使う装置のローカル軸。掴んで回すと向きが変わる")]
    private MET_PushAxis pushAxis = MET_PushAxis.前方向;

    [SerializeField, Tooltip("押し出しの強さ。大きいほど速く/強く押す")]
    private float pushPower = 8f;

    [Header("プレイヤー押し出し")]
    [SerializeField, Tooltip("プレイヤーを押し出すか")]
    private bool affectPlayers = true;

    [Header("オブジェクト押し出し")]
    [SerializeField, Tooltip("Gravityオブジェクト(Rigidbody)を押し出すか")]
    private bool affectObjects = true;

    [SerializeField, Tooltip("質量を無視して一定の加速で押す（OFF時は質量に応じて押す）")]
    private bool ignoreMass = true;

    [Header("距離減衰")]
    [SerializeField, Tooltip("装置に近いほど強く、遠いほど弱くする")]
    private bool useFalloff = true;

    [SerializeField, Tooltip("減衰の基準となる最大距離（m）。これ以上離れると最遠倍率になる")]
    private float falloffMaxDistance = 5f;

    [SerializeField, Range(0f, 1f), Tooltip("最遠地点での強さの倍率（0=無力, 1=減衰なし）")]
    private float falloffMinMultiplier = 0.2f;

    // ------------------------------------------------------------
    // プレイヤー押し出し（ローカル自身のみ）
    // ------------------------------------------------------------
    public override void OnPlayerTriggerStay(VRCPlayerApi player)
    {
        if (!affectPlayers) return;
        if (player == null || !player.isLocal) return;

        Vector3 dir = GetPushDirection();
        float mult = GetFalloffMultiplier(player.GetPosition());

        // 現在速度へ加算 → 押され続けるが本人の移動入力も効く
        Vector3 velocity = player.GetVelocity();
        velocity += dir * pushPower * mult * Time.deltaTime;
        player.SetVelocity(velocity);
    }

    // ------------------------------------------------------------
    // オブジェクト押し出し（自分がOwnerのRigidbodyのみ）
    // OnTriggerStayは物理ステップ毎に呼ばれるためAddForceで継続加圧する
    // ------------------------------------------------------------
    private void OnTriggerStay(Collider other)
    {
        if (!affectObjects || other == null) return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        GameObject target = rb.gameObject;

        // 自分自身（装置）は押さない
        if (target == gameObject) return;

        // 3-B: 自分がOwnerの物だけ押す（他人の物には干渉しない）
        if (!Networking.IsOwner(target)) return;

        Vector3 dir = GetPushDirection();
        float mult = GetFalloffMultiplier(rb.position);

        ForceMode mode = ignoreMass ? ForceMode.Acceleration : ForceMode.Force;
        rb.AddForce(dir * pushPower * mult, mode);
    }

    // ------------------------------------------------------------
    // 押し出し方向（ワールド空間・正規化済み）
    // ------------------------------------------------------------
    private Vector3 GetPushDirection()
    {
        if (pushAxis == MET_PushAxis.前方向) return transform.forward;
        if (pushAxis == MET_PushAxis.後方向) return -transform.forward;
        if (pushAxis == MET_PushAxis.上方向) return transform.up;
        if (pushAxis == MET_PushAxis.下方向) return -transform.up;
        if (pushAxis == MET_PushAxis.右方向) return transform.right;
        return -transform.right; // 左方向
    }

    // ------------------------------------------------------------
    // 距離減衰倍率（装置本体に近いほど1.0に近づく）
    // ------------------------------------------------------------
    private float GetFalloffMultiplier(Vector3 targetPos)
    {
        if (!useFalloff) return 1f;
        if (falloffMaxDistance <= 0f) return 1f;

        float dist = Vector3.Distance(transform.position, targetPos);
        float t = Mathf.Clamp01(dist / falloffMaxDistance);
        return Mathf.Lerp(1f, falloffMinMultiplier, t);
    }
}
