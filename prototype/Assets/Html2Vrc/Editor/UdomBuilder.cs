using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UDONSHARP
using Html2Vrc.VRChat;
using UdonSharpEditor;
using VRC.SDK3.Components;
using VRC.Udon;
#endif

namespace Html2Vrc.Editor
{
    public sealed class UdomBuildResult
    {
        public UdomGeneratedRoot Root { get; internal set; }
        public int Created { get; internal set; }
        public int Updated { get; internal set; }
        public int Removed { get; internal set; }
    }

    public sealed class UdomBuildException : Exception
    {
        public UdomValidationResult Validation { get; }

        public UdomBuildException(UdomValidationResult validation)
            : base(validation != null ? validation.Format() : "UDOM validation failed.")
        {
            Validation = validation;
        }
    }

    public static class UdomBuilderUtility
    {
        public static bool TryParseAlignment(string value, out TextAlignmentOptions alignment)
        {
            switch ((value ?? "MiddleLeft").Trim().ToLowerInvariant())
            {
                case "topleft":
                    alignment = TextAlignmentOptions.TopLeft;
                    return true;
                case "top":
                    alignment = TextAlignmentOptions.Top;
                    return true;
                case "topright":
                    alignment = TextAlignmentOptions.TopRight;
                    return true;
                case "left":
                case "middleleft":
                    alignment = TextAlignmentOptions.Left;
                    return true;
                case "center":
                    alignment = TextAlignmentOptions.Center;
                    return true;
                case "right":
                case "middleright":
                    alignment = TextAlignmentOptions.Right;
                    return true;
                case "bottomleft":
                    alignment = TextAlignmentOptions.BottomLeft;
                    return true;
                case "bottom":
                    alignment = TextAlignmentOptions.Bottom;
                    return true;
                case "bottomright":
                    alignment = TextAlignmentOptions.BottomRight;
                    return true;
                default:
                    alignment = TextAlignmentOptions.Left;
                    return false;
            }
        }

        public static Color ParseColor(string value, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : fallback;
        }

        public static bool TryParseFontStyle(string value, out FontStyles fontStyle)
        {
            return Enum.TryParse((value ?? "Normal").Trim(), true, out fontStyle);
        }

        public static bool TryParseChildAlignment(string value, out TextAnchor alignment)
        {
            return Enum.TryParse((value ?? "UpperLeft").Trim(), true, out alignment);
        }
    }

    public static class UdomBuilder
    {
        private const string MarginSuffix = "::__margin";
        private const string ViewportSuffix = "::__viewport";
        private const string ContentSuffix = "::__content";
        private const string ToggleCheckmarkSuffix = "::__toggle-checkmark";
        private const string SliderFillSuffix = "::__slider-fill";
        private const string SliderHandleSuffix = "::__slider-handle";

        private static readonly Type[] ManagedComponentTypes =
        {
            typeof(Image),
            typeof(RawImage),
            typeof(Button),
            typeof(Toggle),
            typeof(Slider),
            typeof(ScrollRect),
            typeof(Mask),
            typeof(RectMask2D),
            typeof(VerticalLayoutGroup),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(LayoutElement),
            typeof(TextMeshProUGUI),
            typeof(UdomSafeAction),
#if UDONSHARP
            typeof(UdomUdonSafeAction),
#endif
            typeof(UdomEmbedAnchor)
        };

        private sealed class BuildContext
        {
            public UdomGeneratedRoot Root;
            public Dictionary<string, UdomGeneratedNode> Existing;
            public List<UdomGeneratedNode> ExistingDuplicates;
            public HashSet<string> DesiredIds;
            public UdomBuildResult Result;
            public TMP_FontAsset Font;
        }

        public static UdomBuildResult GenerateOrRegenerate(
            string json,
            UdomGeneratedRoot existingRoot = null,
            TextAsset sourceAsset = null)
        {
            var validation = UdomValidator.Validate(
                json,
                sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null);
            if (!validation.IsValid)
            {
                throw new UdomBuildException(validation);
            }

            return GenerateOrRegenerate(validation.Document, existingRoot, sourceAsset);
        }

        public static UdomBuildResult GenerateOrRegenerate(
            UdomDocument document,
            UdomGeneratedRoot existingRoot = null,
            TextAsset sourceAsset = null)
        {
#if UDONSHARP
            UdomVrchatSetup.EnsureReady();
#endif
            if (document == null || document.root == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var root = existingRoot != null ? existingRoot : CreateRoot(document);
            Undo.RecordObject(root, "Configure HTML2VRC UDOM root");
            root.Configure(sourceAsset, document);
            ConfigureCanvas(root.gameObject, document.canvas);
            EnsureEventSystem();

            var existing = new Dictionary<string, UdomGeneratedNode>(StringComparer.Ordinal);
            var duplicates = new List<UdomGeneratedNode>();
            CollectExisting(root, existing, duplicates);

            var result = new UdomBuildResult { Root = root };
            var context = new BuildContext
            {
                Root = root,
                Existing = existing,
                ExistingDuplicates = duplicates,
                DesiredIds = new HashSet<string>(StringComparer.Ordinal),
                Result = result,
                Font = GetOrCreateDefaultFont()
            };

            CollectExternalSlots(document.root, root);
            BuildNode(document.root, root.transform, null, 0, context);
            PruneStaleGeneratedNodes(context);
#if UDONSHARP
            UdomVrchatSetup.RefreshBindingTargets(root);
#endif

            EditorUtility.SetDirty(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            Canvas.ForceUpdateCanvases();
            return result;
        }

        public static UdomGeneratedRoot FindGeneratedRoot(TextAsset sourceAsset, string documentId)
        {
            var roots = UnityEngine.Object.FindObjectsOfType<UdomGeneratedRoot>(true);
            for (var index = 0; index < roots.Length; index++)
            {
                if (sourceAsset != null && roots[index].SourceAsset == sourceAsset)
                {
                    return roots[index];
                }
            }

            for (var index = 0; index < roots.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(documentId)
                    && string.Equals(roots[index].DocumentId, documentId, StringComparison.Ordinal))
                {
                    return roots[index];
                }
            }

            return null;
        }

        public static UdomGeneratedNode FindNode(UdomGeneratedRoot root, string stableId)
        {
            if (root == null)
            {
                return null;
            }

            var nodes = root.GetComponentsInChildren<UdomGeneratedNode>(true);
            for (var index = 0; index < nodes.Length; index++)
            {
                if (string.Equals(nodes[index].StableId, stableId, StringComparison.Ordinal))
                {
                    return nodes[index];
                }
            }

            return null;
        }

        private static UdomGeneratedRoot CreateRoot(UdomDocument document)
        {
            var rootObject = new GameObject(
                $"UDOM - {(string.IsNullOrWhiteSpace(document.name) ? document.id : document.name)}",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UdomGeneratedRoot));
            Undo.RegisterCreatedObjectUndo(rootObject, "Create HTML2VRC UDOM root");
            return rootObject.GetComponent<UdomGeneratedRoot>();
        }

        private static void ConfigureCanvas(GameObject rootObject, UdomCanvas canvasSettings)
        {
            var canvas = rootObject.GetComponent<Canvas>();
            var scaler = rootObject.GetComponent<CanvasScaler>();
            var rect = rootObject.GetComponent<RectTransform>();
            var size = GetVector2(canvasSettings != null ? canvasSettings.size : null, new Vector2(1200f, 800f));
            var isOverlay = canvasSettings != null
                            && string.Equals(
                                canvasSettings.renderMode,
                                "ScreenSpaceOverlay",
                                StringComparison.OrdinalIgnoreCase);

            Undo.RecordObjects(new UnityEngine.Object[] { canvas, scaler, rect }, "Configure HTML2VRC Canvas");
            canvas.renderMode = isOverlay ? RenderMode.ScreenSpaceOverlay : RenderMode.WorldSpace;
            canvas.pixelPerfect = false;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            rect.sizeDelta = size;
            rect.localScale = isOverlay
                ? Vector3.one
                : Vector3.one * Mathf.Max(0.0001f, canvasSettings != null ? canvasSettings.scale : 0.01f);

#if UDONSHARP
            if (!isOverlay && rootObject.GetComponent<VRCUiShape>() == null)
            {
                Undo.AddComponent<VRCUiShape>(rootObject);
            }
#endif
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) != null)
            {
                return;
            }

            var eventObject = new GameObject(
                "EventSystem (HTML2VRC)",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(eventObject, "Create EventSystem");
        }

        private static void CollectExisting(
            UdomGeneratedRoot root,
            Dictionary<string, UdomGeneratedNode> existing,
            List<UdomGeneratedNode> duplicates)
        {
            var markers = root.GetComponentsInChildren<UdomGeneratedNode>(true);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (string.IsNullOrWhiteSpace(marker.StableId))
                {
                    duplicates.Add(marker);
                    continue;
                }

                if (!existing.TryAdd(marker.StableId, marker))
                {
                    duplicates.Add(marker);
                }
            }
        }

        private static void CollectExternalSlots(UdomNode node, UdomGeneratedRoot root)
        {
            if (node == null)
            {
                return;
            }

            if (node.binding != null && !string.IsNullOrWhiteSpace(node.binding.targetSlot))
            {
                root.EnsureSlot(node.binding.targetSlot);
            }

            if (node.embed != null && !string.IsNullOrWhiteSpace(node.embed.targetSlot))
            {
                root.EnsureSlot(node.embed.targetSlot);
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                CollectExternalSlots(children[index], root);
            }
        }

        private static GameObject BuildNode(
            UdomNode node,
            Transform parent,
            GameObject documentPanel,
            int siblingIndex,
            BuildContext context)
        {
            var style = node.style ?? new UdomStyle();
            var actualParent = parent;
            var hasMargin = HasNonZero(style.margin);

            if (hasMargin)
            {
                var wrapperId = node.id + MarginSuffix;
                var wrapper = UpsertGeneratedObject(wrapperId, "Margin", true, parent, context);
                context.DesiredIds.Add(wrapperId);
                wrapper.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                ConfigureMarginWrapper(wrapper, style, parent);
                actualParent = wrapper.transform;
            }

            var nodeObject = UpsertGeneratedObject(node.id, node.type, false, actualParent, context);
            context.DesiredIds.Add(node.id);
            nodeObject.name = string.IsNullOrWhiteSpace(node.name) ? $"{node.type} [{node.id}]" : node.name;

            if (hasMargin)
            {
                nodeObject.transform.SetSiblingIndex(0);
                ConfigureInsideMargin(nodeObject.GetComponent<RectTransform>(), style.margin);
            }
            else
            {
                nodeObject.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                ConfigureRect(nodeObject, style, parent);
            }

            var panelForChildren = documentPanel;
            if (panelForChildren == null && string.Equals(node.type, "Panel", StringComparison.OrdinalIgnoreCase))
            {
                panelForChildren = nodeObject;
            }

            switch (node.type.ToLowerInvariant())
            {
                case "panel":
                    ConfigurePanel(nodeObject, style);
                    BuildChildren(node, nodeObject.transform, panelForChildren, context);
                    break;
                case "text":
                    ConfigureText(nodeObject, node, style, context.Font);
                    break;
                case "image":
                    ConfigureImage(nodeObject, node, style);
                    break;
                case "button":
                    ConfigureButton(nodeObject, node, style, documentPanel, context.Root);
                    BuildChildren(node, nodeObject.transform, panelForChildren, context);
                    break;
                case "toggle":
                    ConfigureToggle(nodeObject, node, style, context);
                    BuildChildren(node, nodeObject.transform, panelForChildren, context);
                    break;
                case "slider":
                    ConfigureSlider(nodeObject, node, style, context);
                    BuildChildren(node, nodeObject.transform, panelForChildren, context);
                    break;
                case "scrollview":
                    ConfigureScrollView(nodeObject, node, style, panelForChildren, context);
                    break;
                case "embed":
                    ConfigureEmbed(nodeObject, node, context.Root);
                    break;
                default:
                    throw new InvalidOperationException($"Validated node type unexpectedly unsupported: {node.type}");
            }

            return nodeObject;
        }

        private static void BuildChildren(
            UdomNode parentNode,
            Transform parent,
            GameObject documentPanel,
            BuildContext context)
        {
            var children = parentNode.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                BuildNode(children[index], parent, documentPanel, index, context);
            }
        }

        private static GameObject UpsertGeneratedObject(
            string stableId,
            string sourceType,
            bool internalNode,
            Transform parent,
            BuildContext context)
        {
            GameObject target;
            if (context.Existing.TryGetValue(stableId, out var existingMarker) && existingMarker != null)
            {
                target = existingMarker.gameObject;
                context.Result.Updated++;
                if (!string.Equals(existingMarker.SourceType, sourceType, StringComparison.OrdinalIgnoreCase))
                {
                    ResetManagedComponents(target);
                }
            }
            else
            {
                target = new GameObject(stableId, typeof(RectTransform), typeof(UdomGeneratedNode));
                Undo.RegisterCreatedObjectUndo(target, "Create UDOM node");
                context.Result.Created++;
            }

            if (target.transform.parent != parent)
            {
                Undo.SetTransformParent(target.transform, parent, "Reparent UDOM node");
            }

            var marker = target.GetComponent<UdomGeneratedNode>();
            Undo.RecordObject(marker, "Configure UDOM node marker");
            marker.Configure(stableId, sourceType, internalNode);
            return target;
        }

        private static void ResetManagedComponents(GameObject target)
        {
            for (var index = 0; index < ManagedComponentTypes.Length; index++)
            {
                var components = target.GetComponents(ManagedComponentTypes[index]);
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
#if UDONSHARP
                    if (components[componentIndex] is UdomUdonSafeAction udonAction)
                    {
                        UdonSharpUndo.DestroyImmediate(udonAction);
                        continue;
                    }
#endif
                    Undo.DestroyObjectImmediate(components[componentIndex]);
                }
            }
        }

        private static void ConfigureRect(GameObject target, UdomStyle style, Transform parent)
        {
            var rect = target.GetComponent<RectTransform>();
            var position = GetVector2(style.position, Vector2.zero);
            var size = GetVector2(style.size, new Vector2(100f, 100f));
            Undo.RecordObject(rect, "Configure UDOM RectTransform");

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var layoutElement = GetOrAdd<LayoutElement>(target);
            Undo.RecordObject(layoutElement, "Configure UDOM LayoutElement");
            layoutElement.minWidth = -1f;
            layoutElement.minHeight = -1f;
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            layoutElement.flexibleWidth = style.flexibleWidth;
            layoutElement.flexibleHeight = style.flexibleHeight;
            layoutElement.ignoreLayout = parent.GetComponent<LayoutGroup>() == null;
        }

        private static void ConfigureMarginWrapper(GameObject wrapper, UdomStyle style, Transform parent)
        {
            var margin = GetEdges(style.margin);
            var size = GetVector2(style.size, new Vector2(100f, 100f));
            var wrapperStyle = new UdomStyle
            {
                position = style.position,
                size = new[] { size.x + margin.x + margin.z, size.y + margin.y + margin.w },
                flexibleWidth = style.flexibleWidth,
                flexibleHeight = style.flexibleHeight
            };
            ConfigureRect(wrapper, wrapperStyle, parent);
        }

        private static void ConfigureInsideMargin(RectTransform rect, float[] marginValues)
        {
            var margin = GetEdges(marginValues);
            Undo.RecordObject(rect, "Configure UDOM margin");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = new Vector2(margin.x, margin.w);
            rect.offsetMax = new Vector2(-margin.z, -margin.y);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            var layout = rect.GetComponent<LayoutElement>();
            if (layout != null)
            {
                Undo.DestroyObjectImmediate(layout);
            }
        }

        private static void ConfigurePanel(GameObject target, UdomStyle style)
        {
            var image = GetOrAdd<Image>(target);
            Undo.RecordObject(image, "Configure UDOM Panel");
            image.color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.clear);
            image.raycastTarget = false;
            ConfigureLayout(target, style);
        }

        private static void ConfigureLayout(GameObject target, UdomStyle style)
        {
            var layoutName = (style.layout ?? "None").Trim();
            if (string.Equals(layoutName, "Vertical", StringComparison.OrdinalIgnoreCase))
            {
                RemoveIfPresent<HorizontalLayoutGroup>(target);
                var group = GetOrAdd<VerticalLayoutGroup>(target);
                ConfigureLayoutGroup(group, style);
            }
            else if (string.Equals(layoutName, "Horizontal", StringComparison.OrdinalIgnoreCase))
            {
                RemoveIfPresent<VerticalLayoutGroup>(target);
                var group = GetOrAdd<HorizontalLayoutGroup>(target);
                ConfigureLayoutGroup(group, style);
            }
            else
            {
                RemoveIfPresent<VerticalLayoutGroup>(target);
                RemoveIfPresent<HorizontalLayoutGroup>(target);
            }
        }

        private static void ConfigureLayoutGroup(HorizontalOrVerticalLayoutGroup group, UdomStyle style)
        {
            var padding = GetEdges(style.padding);
            Undo.RecordObject(group, "Configure UDOM LayoutGroup");
            group.padding = new RectOffset(
                Mathf.RoundToInt(padding.x),
                Mathf.RoundToInt(padding.z),
                Mathf.RoundToInt(padding.y),
                Mathf.RoundToInt(padding.w));
            group.spacing = style.spacing;
            group.childAlignment = UdomBuilderUtility.TryParseChildAlignment(
                style.childAlignment,
                out var childAlignment)
                ? childAlignment
                : TextAnchor.UpperLeft;
            group.childControlWidth = false;
            group.childControlHeight = false;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
        }

        private static void ConfigureText(GameObject target, UdomNode node, UdomStyle style, TMP_FontAsset font)
        {
            RemoveIfPresent<Image>(target);
            var text = GetOrAdd<TextMeshProUGUI>(target);
            Undo.RecordObject(text, "Configure UDOM Text");
            text.text = node.text ?? string.Empty;
            text.fontSize = style.fontSize;
            text.color = UdomBuilderUtility.ParseColor(style.textColor, Color.white);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (UdomBuilderUtility.TryParseAlignment(style.alignment, out var alignment))
            {
                text.alignment = alignment;
            }

            if (UdomBuilderUtility.TryParseFontStyle(style.fontStyle, out var fontStyle))
            {
                text.fontStyle = fontStyle;
            }

            if (font != null)
            {
                text.font = font;
            }
        }

        private static void ConfigureImage(GameObject target, UdomNode node, UdomStyle style)
        {
            var color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.white);
            if (!string.IsNullOrWhiteSpace(node.texture))
            {
                RemoveIfPresent<Image>(target);
                var rawImage = GetOrAdd<RawImage>(target);
                Undo.RecordObject(rawImage, "Configure UDOM RawImage");
                rawImage.color = color;
                rawImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(node.texture);
                rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                rawImage.raycastTarget = false;
                return;
            }

            RemoveIfPresent<RawImage>(target);
            var image = GetOrAdd<Image>(target);
            Undo.RecordObject(image, "Configure UDOM Image");
            image.color = color;
            image.sprite = string.IsNullOrWhiteSpace(node.sprite)
                ? null
                : AssetDatabase.LoadAssetAtPath<Sprite>(node.sprite);
            image.preserveAspect = image.sprite != null;
            image.raycastTarget = false;
        }

        private static void ConfigureButton(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            GameObject documentPanel,
            UdomGeneratedRoot root)
        {
            var image = GetOrAdd<Image>(target);
            var button = GetOrAdd<Button>(target);
#if UDONSHARP
            var action = GetOrAddUdonSafeAction(target);
#else
            var action = GetOrAddSafeAction(target);
#endif
            Undo.RecordObjects(new UnityEngine.Object[] { image, button, action }, "Configure UDOM Button");

            image.color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.gray);
            image.raycastTarget = true;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.interactable = node.interactable;

            var actionType = UdomActionType.None;
            var slot = string.Empty;
            if (node.binding != null)
            {
                Enum.TryParse(node.binding.action, true, out actionType);
                slot = node.binding.targetSlot;
            }

#if UDONSHARP
            action.Configure((int)actionType, slot, root.Resolve(slot), documentPanel);

            var oldUnityAction = target.GetComponent<UdomSafeAction>();
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(action);
            if (backingBehaviour == null)
            {
                throw new InvalidOperationException(
                    $"UdonSharp backing UdonBehaviour를 만들 수 없다: {target.name}");
            }

            for (var index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                var listenerTarget = button.onClick.GetPersistentTarget(index);
                if (listenerTarget == action
                    || listenerTarget == backingBehaviour
                    || listenerTarget == oldUnityAction)
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, index);
                }
            }

            if (oldUnityAction != null)
            {
                Undo.DestroyObjectImmediate(oldUnityAction);
            }

            UdonSharpEditorUtility.CopyProxyToUdon(action);
            UnityEventTools.AddStringPersistentListener(
                button.onClick,
                backingBehaviour.SendCustomEvent,
                nameof(UdomUdonSafeAction.Execute));
#else
            action.Configure(root, actionType, slot, documentPanel);

            for (var index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                if (button.onClick.GetPersistentTarget(index) == action)
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, index);
                }
            }

            UnityEventTools.AddPersistentListener(button.onClick, action.Execute);
#endif
            button.onClick.SetPersistentListenerState(
                button.onClick.GetPersistentEventCount() - 1,
                UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        }

        private static void ConfigureToggle(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            var background = GetOrAdd<Image>(target);
            var toggle = GetOrAdd<Toggle>(target);
            Undo.RecordObjects(new UnityEngine.Object[] { background, toggle }, "Configure UDOM Toggle");
            background.color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.gray);
            background.raycastTarget = true;

            var checkmarkId = node.id + ToggleCheckmarkSuffix;
            var checkmark = UpsertGeneratedObject(checkmarkId, "ToggleCheckmark", true, target.transform, context);
            context.DesiredIds.Add(checkmarkId);
            checkmark.name = "Checkmark";
            checkmark.transform.SetSiblingIndex(0);
            var checkmarkRect = checkmark.GetComponent<RectTransform>();
            Undo.RecordObject(checkmarkRect, "Configure UDOM Toggle checkmark");
            checkmarkRect.anchorMin = new Vector2(0.25f, 0.25f);
            checkmarkRect.anchorMax = new Vector2(0.75f, 0.75f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.anchoredPosition = Vector2.zero;
            checkmarkRect.sizeDelta = Vector2.zero;
            checkmarkRect.localScale = Vector3.one;
            checkmarkRect.localRotation = Quaternion.identity;
            RemoveIfPresent<LayoutElement>(checkmark);

            var checkmarkImage = GetOrAdd<Image>(checkmark);
            Undo.RecordObject(checkmarkImage, "Configure UDOM Toggle checkmark");
            checkmarkImage.color = Color.white;
            checkmarkImage.raycastTarget = false;

            toggle.targetGraphic = background;
            toggle.graphic = checkmarkImage;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.SetIsOnWithoutNotify(node.toggleValue);
            toggle.interactable = node.interactable;
        }

        private static void ConfigureSlider(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            var background = GetOrAdd<Image>(target);
            var slider = GetOrAdd<Slider>(target);
            Undo.RecordObjects(new UnityEngine.Object[] { background, slider }, "Configure UDOM Slider");
            background.color = UdomBuilderUtility.ParseColor(style.backgroundColor, new Color32(0x4A, 0x4A, 0x58, 0xFF));
            background.raycastTarget = true;

            var fillId = node.id + SliderFillSuffix;
            var fill = UpsertGeneratedObject(fillId, "SliderFill", true, target.transform, context);
            context.DesiredIds.Add(fillId);
            fill.name = "Fill";
            fill.transform.SetSiblingIndex(0);
            var fillRect = fill.GetComponent<RectTransform>();
            Undo.RecordObject(fillRect, "Configure UDOM Slider fill");
            fillRect.anchorMin = new Vector2(0f, 0.25f);
            fillRect.anchorMax = new Vector2(1f, 0.75f);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.offsetMin = new Vector2(8f, 0f);
            fillRect.offsetMax = new Vector2(-8f, 0f);
            fillRect.localScale = Vector3.one;
            fillRect.localRotation = Quaternion.identity;
            RemoveIfPresent<LayoutElement>(fill);
            var fillImage = GetOrAdd<Image>(fill);
            Undo.RecordObject(fillImage, "Configure UDOM Slider fill");
            fillImage.color = new Color32(0x58, 0xC8, 0xFF, 0xFF);
            fillImage.raycastTarget = false;

            var handleId = node.id + SliderHandleSuffix;
            var handle = UpsertGeneratedObject(handleId, "SliderHandle", true, target.transform, context);
            context.DesiredIds.Add(handleId);
            handle.name = "Handle";
            handle.transform.SetSiblingIndex(1);
            var handleRect = handle.GetComponent<RectTransform>();
            Undo.RecordObject(handleRect, "Configure UDOM Slider handle");
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(28f, 28f);
            handleRect.localScale = Vector3.one;
            handleRect.localRotation = Quaternion.identity;
            RemoveIfPresent<LayoutElement>(handle);
            var handleImage = GetOrAdd<Image>(handle);
            Undo.RecordObject(handleImage, "Configure UDOM Slider handle");
            handleImage.color = Color.white;
            handleImage.raycastTarget = true;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = node.sliderMin;
            slider.maxValue = node.sliderMax;
            slider.wholeNumbers = Mathf.Abs(node.sliderStep - 1f) < 0.0001f
                                  && Mathf.Abs(node.sliderMin - Mathf.Round(node.sliderMin)) < 0.0001f
                                  && Mathf.Abs(node.sliderMax - Mathf.Round(node.sliderMax)) < 0.0001f;
            slider.SetValueWithoutNotify(node.sliderValue);
            slider.interactable = node.interactable;
        }

        private static void ConfigureScrollView(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            GameObject documentPanel,
            BuildContext context)
        {
            var background = GetOrAdd<Image>(target);
            var scrollRect = GetOrAdd<ScrollRect>(target);
            Undo.RecordObjects(new UnityEngine.Object[] { background, scrollRect }, "Configure UDOM ScrollView");
            background.color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.clear);
            background.raycastTarget = true;

            var viewportId = node.id + ViewportSuffix;
            var viewport = UpsertGeneratedObject(viewportId, "ScrollViewport", true, target.transform, context);
            context.DesiredIds.Add(viewportId);
            viewport.name = "Viewport";
            viewport.transform.SetSiblingIndex(0);
            ConfigureStretch(viewport.GetComponent<RectTransform>());
            var viewportImage = GetOrAdd<Image>(viewport);
            RemoveIfPresent<Mask>(viewport);
            GetOrAdd<RectMask2D>(viewport);
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;

            var contentId = node.id + ContentSuffix;
            var content = UpsertGeneratedObject(contentId, "ScrollContent", true, viewport.transform, context);
            context.DesiredIds.Add(contentId);
            content.name = "Content";
            content.transform.SetSiblingIndex(0);
            ConfigureScrollContentRect(content.GetComponent<RectTransform>());
            ConfigureLayout(content, style);

            var fitter = GetOrAdd<ContentSizeFitter>(content);
            Undo.RecordObject(fitter, "Configure UDOM Scroll Content");
            var horizontal = string.Equals(style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase);
            fitter.horizontalFit = horizontal
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = horizontal
                ? ContentSizeFitter.FitMode.Unconstrained
                : ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = content.GetComponent<RectTransform>();
            scrollRect.horizontal = horizontal;
            scrollRect.vertical = !horizontal;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                BuildNode(children[index], content.transform, documentPanel, index, context);
            }
        }

        private static void ConfigureEmbed(GameObject target, UdomNode node, UdomGeneratedRoot root)
        {
            var anchor = GetOrAdd<UdomEmbedAnchor>(target);
            Undo.RecordObject(anchor, "Configure UDOM Embed");
            anchor.Configure(root, node.embed != null ? node.embed.targetSlot : string.Empty);
        }

        private static void ConfigureStretch(RectTransform rect)
        {
            Undo.RecordObject(rect, "Configure stretched RectTransform");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void ConfigureScrollContentRect(RectTransform rect)
        {
            Undo.RecordObject(rect, "Configure Scroll Content RectTransform");
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void PruneStaleGeneratedNodes(BuildContext context)
        {
            var all = context.Root.GetComponentsInChildren<UdomGeneratedNode>(true).ToList();
            all.AddRange(context.ExistingDuplicates.Where(marker => marker != null));
            var stale = all
                .Where(marker => marker != null
                                 && (!context.DesiredIds.Contains(marker.StableId)
                                     || context.ExistingDuplicates.Contains(marker)))
                .Distinct()
                .OrderByDescending(marker => GetDepth(marker.transform))
                .ToList();

            for (var index = 0; index < stale.Count; index++)
            {
                if (stale[index] == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(stale[index].gameObject);
                context.Result.Removed++;
            }
        }

        private static int GetDepth(Transform transform)
        {
            var depth = 0;
            while (transform != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        private static TMP_FontAsset GetOrCreateDefaultFont()
        {
            var candidates = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (var index = 0; index < candidates.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(candidates[index]);
                var candidate = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "TextMeshPro Font Asset을 찾을 수 없다. Window > TextMeshPro > Import TMP Essential Resources를 먼저 실행해야 한다.");
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }

        private static UdomSafeAction GetOrAddSafeAction(GameObject target)
        {
            var action = target.GetComponent<UdomSafeAction>();
            if (action != null)
            {
                return action;
            }

            return Undo.AddComponent<UdomSafeAction>(target);
        }

#if UDONSHARP
        private static UdomUdonSafeAction GetOrAddUdonSafeAction(GameObject target)
        {
            var action = target.GetComponent<UdomUdonSafeAction>();
            return action != null
                ? action
                : (UdomUdonSafeAction)UdonSharpUndo.AddComponent(
                    target,
                    typeof(UdomUdonSafeAction));
        }
#endif

        private static void RemoveIfPresent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null)
            {
                Undo.DestroyObjectImmediate(component);
            }
        }

        private static bool HasNonZero(float[] values)
        {
            if (values == null)
            {
                return false;
            }

            for (var index = 0; index < values.Length; index++)
            {
                if (!Mathf.Approximately(values[index], 0f))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 GetVector2(float[] values, Vector2 fallback)
        {
            return values != null && values.Length >= 2
                ? new Vector2(values[0], values[1])
                : fallback;
        }

        private static Vector4 GetEdges(float[] values)
        {
            return values != null && values.Length >= 4
                ? new Vector4(values[0], values[1], values[2], values[3])
                : Vector4.zero;
        }
    }
}
