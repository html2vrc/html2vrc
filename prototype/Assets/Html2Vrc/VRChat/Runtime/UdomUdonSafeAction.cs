using UdonSharp;
using UnityEngine;

namespace Html2Vrc.VRChat
{
    [DisallowMultipleComponent]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class UdomUdonSafeAction : UdonSharpBehaviour
    {
        public const int None = 0;
        public const int ToggleActive = 1;
        public const int SetActive = 2;
        public const int SetInactive = 3;
        public const int ClosePanel = 4;
        public const int ToggleLight = 5;

        [SerializeField] private int action;
        [SerializeField] private string targetSlot;
        [SerializeField] private GameObject target;
        [SerializeField] private GameObject panelToClose;

        public int Action => action;
        public string TargetSlot => targetSlot;
        public GameObject Target => target;
        public GameObject PanelToClose => panelToClose;

#if !COMPILER_UDONSHARP
        public void Configure(
            int actionType,
            string slot,
            GameObject resolvedTarget,
            GameObject containingPanel)
        {
            action = actionType;
            targetSlot = slot;
            target = resolvedTarget;
            panelToClose = containingPanel;
        }

        public void RefreshTarget(GameObject resolvedTarget)
        {
            target = resolvedTarget;
        }
#endif

        public void Execute()
        {
            switch (action)
            {
                case ToggleActive:
                    if (target != null)
                    {
                        target.SetActive(!target.activeSelf);
                    }

                    break;
                case SetActive:
                    if (target != null)
                    {
                        target.SetActive(true);
                    }

                    break;
                case SetInactive:
                    if (target != null)
                    {
                        target.SetActive(false);
                    }

                    break;
                case ClosePanel:
                    if (panelToClose != null)
                    {
                        panelToClose.SetActive(false);
                    }

                    break;
                case ToggleLight:
                    if (target != null)
                    {
                        var lightComponent = target.GetComponent<Light>();
                        if (lightComponent != null)
                        {
                            lightComponent.enabled = !lightComponent.enabled;
                            Debug.Log(
                                lightComponent.enabled
                                    ? "HTML2VRC_LIGHT_STATE: ON"
                                    : "HTML2VRC_LIGHT_STATE: OFF");
                        }
                    }

                    break;
                case None:
                default:
                    break;
            }
        }
    }
}
