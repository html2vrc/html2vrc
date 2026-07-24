using UnityEngine;

namespace Html2Vrc
{
    [DisallowMultipleComponent]
    public sealed class UdomGeneratedNode : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private string sourceType;
        [SerializeField] private bool generatedInternal;

        public string StableId => stableId;
        public string SourceType => sourceType;
        public bool GeneratedInternal => generatedInternal;

        public void Configure(string id, string type, bool isInternal)
        {
            stableId = id;
            sourceType = type;
            generatedInternal = isInternal;
        }
    }
}
