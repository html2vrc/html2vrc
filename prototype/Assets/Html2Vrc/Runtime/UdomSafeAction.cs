using System;
using UnityEngine;

namespace Html2Vrc
{
    public enum UdomActionType
    {
        None,
        ToggleActive,
        SetActive,
        SetInactive,
        ClosePanel,
        ToggleLight
    }

    [DisallowMultipleComponent]
    public sealed class UdomSafeAction : MonoBehaviour
    {
        [SerializeField] private UdomGeneratedRoot root;
        [SerializeField] private UdomActionType action;
        [SerializeField] private string targetSlot;
        [SerializeField] private GameObject panelToClose;

        public UdomGeneratedRoot Root => root;
        public UdomActionType Action => action;
        public string TargetSlot => targetSlot;
        public GameObject PanelToClose => panelToClose;

        public void Configure(
            UdomGeneratedRoot generatedRoot,
            UdomActionType actionType,
            string slot,
            GameObject containingPanel)
        {
            root = generatedRoot;
            action = actionType;
            targetSlot = slot;
            panelToClose = containingPanel;
        }

        public void Execute()
        {
            switch (action)
            {
                case UdomActionType.ToggleActive:
                {
                    var target = ResolveTarget();
                    if (target != null)
                    {
                        target.SetActive(!target.activeSelf);
                    }

                    break;
                }
                case UdomActionType.SetActive:
                {
                    var target = ResolveTarget();
                    if (target != null)
                    {
                        target.SetActive(true);
                    }

                    break;
                }
                case UdomActionType.SetInactive:
                {
                    var target = ResolveTarget();
                    if (target != null)
                    {
                        target.SetActive(false);
                    }

                    break;
                }
                case UdomActionType.ClosePanel:
                    if (panelToClose != null)
                    {
                        panelToClose.SetActive(false);
                    }

                    break;
                case UdomActionType.ToggleLight:
                {
                    var target = ResolveTarget();
                    var lightComponent = target != null ? target.GetComponent<Light>() : null;
                    if (lightComponent != null)
                    {
                        lightComponent.enabled = !lightComponent.enabled;
                    }

                    break;
                }
                case UdomActionType.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private GameObject ResolveTarget()
        {
            return root != null ? root.Resolve(targetSlot) : null;
        }
    }
}
