using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CanvasTrigger : UdonSharpBehaviour
{
    void Start()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;
    }
}