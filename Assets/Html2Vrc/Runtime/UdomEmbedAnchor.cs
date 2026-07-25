using UnityEngine;

namespace Html2Vrc
{
    [DisallowMultipleComponent]
    public sealed class UdomEmbedAnchor : MonoBehaviour
    {
        [SerializeField] private UdomGeneratedRoot root;
        [SerializeField] private string targetSlot;

        public UdomGeneratedRoot Root => root;
        public string TargetSlot => targetSlot;
        public GameObject Target => root != null ? root.Resolve(targetSlot) : null;

        public void Configure(UdomGeneratedRoot generatedRoot, string slot)
        {
            root = generatedRoot;
            targetSlot = slot;
        }
    }
}
