using UnityEngine;

namespace Html2Vrc
{
    [DisallowMultipleComponent]
    public sealed class UdomEmbedAnchor : MonoBehaviour
    {
        [SerializeField] private UdomGeneratedRoot root;
        [SerializeField] private string targetSlot;
        [SerializeField] private GameObject fallbackVisual;

        public UdomGeneratedRoot Root => root;
        public string TargetSlot => targetSlot;
        public GameObject Target => root != null ? root.Resolve(targetSlot) : null;
        public GameObject FallbackVisual => fallbackVisual;

        public void Configure(UdomGeneratedRoot generatedRoot, string slot, GameObject fallback)
        {
            root = generatedRoot;
            targetSlot = slot;
            fallbackVisual = fallback;
            RefreshFallback();
        }

        public void RefreshFallback()
        {
            if (fallbackVisual != null)
            {
                fallbackVisual.SetActive(Target == null);
            }
        }

        private void OnEnable()
        {
            RefreshFallback();
        }
    }
}
