using System;
using System.Collections.Generic;
using UnityEngine;

namespace Html2Vrc
{
    [Serializable]
    public sealed class UdomExternalReference
    {
        public string slot;
        public GameObject target;
    }

    [DisallowMultipleComponent]
    public sealed class UdomGeneratedRoot : MonoBehaviour
    {
        public static event Action<UdomGeneratedRoot> ExternalReferencesChanged;

        [SerializeField] private TextAsset sourceAsset;
        [SerializeField] private string documentId;
        [SerializeField] private string schemaVersion;
        [SerializeField]
        [Tooltip("Optional Renderer target size. Zero uses the UDOM viewport size.")]
        private Vector2 targetCanvasSize;
        [SerializeField] private List<UdomExternalReference> externalReferences = new List<UdomExternalReference>();

        public TextAsset SourceAsset => sourceAsset;
        public string DocumentId => documentId;
        public string SchemaVersion => schemaVersion;
        public Vector2 TargetCanvasSize => targetCanvasSize;
        public IReadOnlyList<UdomExternalReference> ExternalReferences => externalReferences;

        public void Configure(TextAsset source, UdomDocument document)
        {
            sourceAsset = source;
            documentId = document != null ? document.id : string.Empty;
            schemaVersion = document != null ? document.schemaVersion : string.Empty;
        }

        public void SetTargetCanvasSize(Vector2 size)
        {
            targetCanvasSize = size.x > 0f && size.y > 0f ? size : Vector2.zero;
        }

        public GameObject Resolve(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                return null;
            }

            for (var index = 0; index < externalReferences.Count; index++)
            {
                var entry = externalReferences[index];
                if (entry != null && string.Equals(entry.slot, slot, StringComparison.Ordinal))
                {
                    return entry.target;
                }
            }

            return null;
        }

        public void EnsureSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                return;
            }

            for (var index = 0; index < externalReferences.Count; index++)
            {
                var entry = externalReferences[index];
                if (entry != null && string.Equals(entry.slot, slot, StringComparison.Ordinal))
                {
                    return;
                }
            }

            externalReferences.Add(new UdomExternalReference { slot = slot });
        }

        public void SetExternalReference(string slot, GameObject target)
        {
            EnsureSlot(slot);
            for (var index = 0; index < externalReferences.Count; index++)
            {
                var entry = externalReferences[index];
                if (entry != null && string.Equals(entry.slot, slot, StringComparison.Ordinal))
                {
                    entry.target = target;
                    RefreshEmbedFallbacks();
                    ExternalReferencesChanged?.Invoke(this);
                    return;
                }
            }
        }

        private void RefreshEmbedFallbacks()
        {
            var anchors = GetComponentsInChildren<UdomEmbedAnchor>(true);
            for (var index = 0; index < anchors.Length; index++)
            {
                anchors[index].RefreshFallback();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RefreshEmbedFallbacks();
            ExternalReferencesChanged?.Invoke(this);
        }
#endif
    }
}
