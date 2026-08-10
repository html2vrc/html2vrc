using UnityEngine;

namespace Html2Vrc
{
    [DisallowMultipleComponent]
    public sealed class UdomPaintState : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private bool createdCanvasGroup;
        [SerializeField] private float baseAlpha = 1f;
        [SerializeField] private bool baseInteractable = true;
        [SerializeField] private bool baseBlocksRaycasts = true;
        [SerializeField] private bool baseIgnoreParentGroups;

        public CanvasGroup CanvasGroup => canvasGroup;
        public bool CreatedCanvasGroup => createdCanvasGroup;
        public float BaseAlpha => baseAlpha;
        public bool BaseInteractable => baseInteractable;
        public bool BaseBlocksRaycasts => baseBlocksRaycasts;
        public bool BaseIgnoreParentGroups => baseIgnoreParentGroups;

        public void Capture(CanvasGroup target, bool created)
        {
            canvasGroup = target;
            createdCanvasGroup = created;
            if (target == null)
            {
                return;
            }

            baseAlpha = target.alpha;
            baseInteractable = target.interactable;
            baseBlocksRaycasts = target.blocksRaycasts;
            baseIgnoreParentGroups = target.ignoreParentGroups;
        }

        public void Apply(bool visible, float opacity)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? baseAlpha * Mathf.Clamp01(opacity) : 0f;
            canvasGroup.interactable = baseInteractable && visible;
            canvasGroup.blocksRaycasts = baseBlocksRaycasts && visible;
            canvasGroup.ignoreParentGroups = false;
        }

        public void Restore()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = baseAlpha;
            canvasGroup.interactable = baseInteractable;
            canvasGroup.blocksRaycasts = baseBlocksRaycasts;
            canvasGroup.ignoreParentGroups = baseIgnoreParentGroups;
        }
    }
}
