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
                case "topjustified":
                    alignment = TextAlignmentOptions.TopJustified;
                    return true;
                case "left":
                case "middleleft":
                    alignment = TextAlignmentOptions.Left;
                    return true;
                case "center":
                    alignment = TextAlignmentOptions.Center;
                    return true;
                case "justified":
                    alignment = TextAlignmentOptions.Justified;
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
                case "bottomjustified":
                    alignment = TextAlignmentOptions.BottomJustified;
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
        private const string TransformLayoutSuffix = "::__transform-layout";
        private const string TransformOriginSuffix = "::__transform-origin";
        private const string TransformOperationSuffix = "::__transform-operation-";
        private const string ShadowLayoutSuffix = "::__shadow-layout";
        private const string ShadowSuffix = "::__shadow-";
        private const string ViewportSuffix = "::__viewport";
        private const string ContentSuffix = "::__content";
        private const string ToggleCheckmarkSuffix = "::__toggle-checkmark";
        private const string SliderFillSuffix = "::__slider-fill";
        private const string SliderHandleSuffix = "::__slider-handle";
        private const string ImageContentSuffix = "::__image-content";
        private const string BorderSuffix = "::__border";
        private const string InputViewportSuffix = "::__input-viewport";
        private const string InputTextSuffix = "::__input-text";
        private const string InputPlaceholderSuffix = "::__input-placeholder";
        private const string EmbedFallbackSuffix = "::__embed-fallback";
        private const string ViewportFitSuffix = "::__viewport-fit";

        private static readonly Type[] ManagedComponentTypes =
        {
            typeof(Image),
            typeof(RawImage),
            typeof(Button),
            typeof(Toggle),
            typeof(Slider),
            typeof(TMP_InputField),
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
            public TextAsset SourceAsset;
        }

        private struct CanvasFitSettings
        {
            public bool RequiresClip;
            public Vector2 ContentScale;
        }

        public static UdomBuildResult GenerateOrRegenerate(
            string json,
            UdomGeneratedRoot existingRoot = null,
            TextAsset sourceAsset = null,
            Vector2? targetCanvasSize = null)
        {
            var validation = UdomValidator.Validate(
                json,
                sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null);
            if (!validation.IsValid)
            {
                throw new UdomBuildException(validation);
            }

            return GenerateOrRegenerate(validation.Document, existingRoot, sourceAsset, targetCanvasSize);
        }

        public static UdomBuildResult GenerateOrRegenerate(
            UdomDocument document,
            UdomGeneratedRoot existingRoot = null,
            TextAsset sourceAsset = null,
            Vector2? targetCanvasSize = null)
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
            if (targetCanvasSize.HasValue)
            {
                root.SetTargetCanvasSize(targetCanvasSize.Value);
            }

            root.Configure(sourceAsset, document);
            var canvasFit = ConfigureCanvas(root, document.canvas);
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
                Font = GetOrCreateDefaultFont(),
                SourceAsset = sourceAsset
            };

            CollectExternalSlots(document.root, root);
            var viewportId = document.root.id + ViewportFitSuffix;
            var viewport = UpsertGeneratedObject(
                viewportId,
                "ViewportFit",
                true,
                root.transform,
                context);
            context.DesiredIds.Add(viewportId);
            viewport.name = "Viewport Fit";
            viewport.transform.SetSiblingIndex(0);
            ConfigureStretch(viewport.GetComponent<RectTransform>());
            RemoveIfPresent<LayoutElement>(viewport);
            if (canvasFit.RequiresClip)
            {
                GetOrAdd<RectMask2D>(viewport);
            }
            else
            {
                RemoveIfPresent<RectMask2D>(viewport);
            }

            var buildParent = viewport.transform;
            var documentRoot = BuildNode(document.root, buildParent, null, 0, context);
            if (documentRoot != null)
            {
                var viewportContent = documentRoot.transform;
                while (viewportContent.parent != null && viewportContent.parent != buildParent)
                {
                    viewportContent = viewportContent.parent;
                }

                Undo.RecordObject(viewportContent, "Configure UDOM viewport fit");
                viewportContent.localScale = new Vector3(
                    canvasFit.ContentScale.x,
                    canvasFit.ContentScale.y,
                    1f);
            }
            PruneStaleGeneratedNodes(context);
#if UDONSHARP
            UdomVrchatSetup.RefreshBindingTargets(root);
#endif

            EditorUtility.SetDirty(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            Canvas.ForceUpdateCanvases();
            RefreshAbsoluteLayout(document.root, context);
            RefreshVisualLayout(document.root, context);
            Canvas.ForceUpdateCanvases();
            RefreshPaintLayout(document.root, context);
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

        private static CanvasFitSettings ConfigureCanvas(
            UdomGeneratedRoot root,
            UdomCanvas canvasSettings)
        {
            var rootObject = root.gameObject;
            var canvas = rootObject.GetComponent<Canvas>();
            var scaler = rootObject.GetComponent<CanvasScaler>();
            var rect = rootObject.GetComponent<RectTransform>();
            var designSize = GetVector2(
                canvasSettings != null ? canvasSettings.size : null,
                new Vector2(1200f, 800f));
            var requestedTarget = root.TargetCanvasSize;
            var hasTargetOverride = requestedTarget.x > 0f && requestedTarget.y > 0f;
            var targetSize = hasTargetOverride ? requestedTarget : designSize;
            var fit = canvasSettings != null && !string.IsNullOrWhiteSpace(canvasSettings.viewportFit)
                ? canvasSettings.viewportFit
                : "none";
            var ratio = new Vector2(targetSize.x / designSize.x, targetSize.y / designSize.y);
            Vector2 contentScale;
            if (string.Equals(fit, "contain", StringComparison.OrdinalIgnoreCase))
            {
                var uniform = Mathf.Min(ratio.x, ratio.y);
                contentScale = new Vector2(uniform, uniform);
            }
            else if (string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase))
            {
                var uniform = Mathf.Max(ratio.x, ratio.y);
                contentScale = new Vector2(uniform, uniform);
            }
            else if (string.Equals(fit, "stretch", StringComparison.OrdinalIgnoreCase))
            {
                contentScale = ratio;
            }
            else
            {
                contentScale = Vector2.one;
            }

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
            rect.sizeDelta = targetSize;
            rect.localScale = isOverlay
                ? Vector3.one
                : Vector3.one * Mathf.Max(0.0001f, canvasSettings != null ? canvasSettings.scale : 0.01f);

#if UDONSHARP
            if (!isOverlay && rootObject.GetComponent<VRCUiShape>() == null)
            {
                Undo.AddComponent<VRCUiShape>(rootObject);
            }
#endif
            return new CanvasFitSettings
            {
                RequiresClip = hasTargetOverride
                               && (string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(fit, "none", StringComparison.OrdinalIgnoreCase)),
                ContentScale = contentScale
            };
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
            if (node == null || (node.style != null && node.style.displayNone))
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
            if (node == null)
            {
                return null;
            }

            var style = node.style ?? new UdomStyle();
            if (style.displayNone)
            {
                return null;
            }

            var actualParent = parent;
            var effectiveMargin = GetEffectiveMargin(style);
            var hasMargin = HasNonZero(effectiveMargin);
            var hasTransform = HasTransform(style);
            var hasOuterShadows = HasRenderableOuterShadow(style);
            var hasSiblingInsetShadows = RequiresSiblingInsetShadow(node, style);
            var hasStackedShadows = hasOuterShadows || hasSiblingInsetShadows;

            if (hasMargin)
            {
                var wrapperId = node.id + MarginSuffix;
                var wrapper = UpsertGeneratedObject(wrapperId, "Margin", true, parent, context);
                context.DesiredIds.Add(wrapperId);
                wrapper.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                ConfigureMarginWrapper(wrapper, style, parent);
                actualParent = wrapper.transform;
            }

            GameObject transformLayout = null;
            GameObject shadowLayout = null;
            var nodeParent = actualParent;
            if (hasTransform)
            {
                var transformLayoutId = node.id + TransformLayoutSuffix;
                transformLayout = UpsertGeneratedObject(
                    transformLayoutId,
                    "TransformLayout",
                    true,
                    actualParent,
                    context);
                context.DesiredIds.Add(transformLayoutId);
                transformLayout.name = $"Transform Layout [{node.id}]";
                if (hasMargin)
                {
                    transformLayout.transform.SetSiblingIndex(0);
                    ConfigureInsideMargin(
                        transformLayout.GetComponent<RectTransform>(),
                        effectiveMargin);
                }
                else
                {
                    transformLayout.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                    ConfigureRect(transformLayout, style, parent);
                }

                nodeParent = ConfigureTransformHierarchy(
                    transformLayout,
                    node,
                    style,
                    context);
            }
            else if (hasStackedShadows)
            {
                var shadowLayoutId = node.id + ShadowLayoutSuffix;
                shadowLayout = UpsertGeneratedObject(
                    shadowLayoutId,
                    "ShadowLayout",
                    true,
                    actualParent,
                    context);
                context.DesiredIds.Add(shadowLayoutId);
                shadowLayout.name = $"Shadow Layout [{node.id}]";
                if (hasMargin)
                {
                    shadowLayout.transform.SetSiblingIndex(0);
                    ConfigureInsideMargin(
                        shadowLayout.GetComponent<RectTransform>(),
                        effectiveMargin);
                }
                else
                {
                    shadowLayout.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                    ConfigureRect(shadowLayout, style, parent);
                }

                nodeParent = shadowLayout.transform;
            }

            var visualLayout = transformLayout != null ? transformLayout : shadowLayout;
            var visualSize = visualLayout != null
                ? visualLayout.GetComponent<RectTransform>().rect.size
                : GetVector2(style.size, new Vector2(100f, 100f));
            if (visualSize.x <= 0f || visualSize.y <= 0f)
            {
                visualSize = GetVector2(style.size, new Vector2(100f, 100f));
            }

            if (hasOuterShadows)
            {
                ConfigureOuterShadows(nodeParent, node, style, visualSize, context);
            }
            if (hasSiblingInsetShadows)
            {
                ConfigureInsetShadows(
                    nodeParent,
                    node,
                    style,
                    visualSize,
                    CountRenderableOuterShadows(style),
                    context);
            }

            var nodeObject = UpsertGeneratedObject(node.id, node.type, false, nodeParent, context);
            context.DesiredIds.Add(node.id);
            nodeObject.name = string.IsNullOrWhiteSpace(node.name) ? $"{node.type} [{node.id}]" : node.name;

            if (hasTransform)
            {
                nodeObject.transform.SetAsLastSibling();
                ConfigureTransformedNodeRect(nodeObject, style, visualSize);
            }
            else if (hasStackedShadows)
            {
                nodeObject.transform.SetAsLastSibling();
                ConfigureStackedNodeRect(nodeObject, visualSize);
            }
            else if (hasMargin)
            {
                nodeObject.transform.SetSiblingIndex(0);
                ConfigureInsideMargin(nodeObject.GetComponent<RectTransform>(), effectiveMargin);
            }
            else
            {
                nodeObject.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
                ConfigureRect(nodeObject, style, parent);
            }

            if (hasStackedShadows)
            {
                ConfigurePaint(visualLayout, style);
                ConfigurePaint(nodeObject, null);
            }
            else
            {
                ConfigurePaint(nodeObject, style);
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
                    ConfigureText(nodeObject, node, style, ResolveFont(style, context.Font));
                    break;
                case "image":
                    ConfigureImage(nodeObject, node, style, context);
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
                case "textinput":
                    ConfigureTextInput(nodeObject, node, style, context);
                    BuildChildren(node, nodeObject.transform, panelForChildren, context);
                    break;
                case "scrollview":
                    ConfigureScrollView(nodeObject, node, style, panelForChildren, context);
                    break;
                case "embed":
                    ConfigureEmbed(nodeObject, node, context);
                    break;
                default:
                    throw new InvalidOperationException($"Validated node type unexpectedly unsupported: {node.type}");
            }

            ConfigureBackground(nodeObject, node, style, context);
            if (!hasSiblingInsetShadows)
            {
                ConfigureInsetShadows(
                    nodeObject.transform,
                    node,
                    style,
                    visualSize,
                    0,
                    context);
            }
            ConfigureBorder(nodeObject, node, style, context);

            return nodeObject;
        }

        private static void BuildChildren(
            UdomNode parentNode,
            Transform parent,
            GameObject documentPanel,
            BuildContext context)
        {
            var children = parentNode.children ?? Array.Empty<UdomNode>();
            var indexedChildren = children
                .Select((child, sourceIndex) => new { Child = child, SourceIndex = sourceIndex });
            var parentLayout = parentNode.style != null ? parentNode.style.layout : null;
            var isFlexLayout = string.Equals(parentLayout, "Horizontal", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(parentLayout, "Vertical", StringComparison.OrdinalIgnoreCase);
            var layoutOrderedChildren = isFlexLayout
                ? indexedChildren
                    .OrderBy(item => item.Child != null && item.Child.style != null
                        ? item.Child.style.flexOrder
                        : 0)
                    .ThenBy(item => item.SourceIndex)
                    .ToArray()
                : indexedChildren.ToArray();
            if (isFlexLayout && parentNode.style != null && parentNode.style.reverseChildren)
            {
                Array.Reverse(layoutOrderedChildren);
            }

            var usesZIndex = layoutOrderedChildren
                .Where(item => item.Child != null
                               && item.Child.style != null
                               && !item.Child.style.displayNone)
                .Select(item => item.Child.style.zIndex)
                .Distinct()
                .Skip(1)
                .Any();
            var orderedChildren = usesZIndex
                ? layoutOrderedChildren
                    .Select((item, layoutIndex) => new { Item = item, LayoutIndex = layoutIndex })
                    .OrderBy(entry => entry.Item.Child != null && entry.Item.Child.style != null
                        ? entry.Item.Child.style.zIndex
                        : 0)
                    .ThenBy(entry => entry.LayoutIndex)
                    .Select(entry => entry.Item)
                    .ToArray()
                : layoutOrderedChildren;

            for (var index = 0; index < orderedChildren.Length; index++)
            {
                BuildNode(orderedChildren[index].Child, parent, documentPanel, index, context);
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

            if (UsesTopLeftPosition(style))
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(position.x, -position.y);
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = position;
            }

            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var layoutElement = GetOrAdd<LayoutElement>(target);
            Undo.RecordObject(layoutElement, "Configure UDOM LayoutElement");
            layoutElement.minWidth = GetMinimumSize(style, 0);
            layoutElement.minHeight = GetMinimumSize(style, 1);
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            layoutElement.flexibleWidth = style.flexibleWidth;
            layoutElement.flexibleHeight = style.flexibleHeight;
            layoutElement.ignoreLayout = UsesTopLeftPosition(style) || parent.GetComponent<LayoutGroup>() == null;
        }

        private static Transform ConfigureTransformHierarchy(
            GameObject transformLayout,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            var size = transformLayout.GetComponent<RectTransform>().rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = GetVector2(style.size, new Vector2(100f, 100f));
            }

            var originId = node.id + TransformOriginSuffix;
            var origin = UpsertGeneratedObject(
                originId,
                "TransformOrigin",
                true,
                transformLayout.transform,
                context);
            context.DesiredIds.Add(originId);
            origin.name = $"Transform Origin [{node.id}]";
            origin.transform.SetSiblingIndex(0);
            ConfigureTransformOrigin(origin, style, size);

            var operationParent = origin.transform;
            var operationCount = style.transformOperationTypes != null
                ? style.transformOperationTypes.Length
                : 0;
            // The last operation is the outermost wrapper so points experience operations in array order.
            for (var index = operationCount - 1; index >= 0; index--)
            {
                var operationId = node.id + TransformOperationSuffix + index;
                var operationType = style.transformOperationTypes[index] ?? "unknown";
                var operation = UpsertGeneratedObject(
                    operationId,
                    "TransformOperation:" + operationType,
                    true,
                    operationParent,
                    context);
                context.DesiredIds.Add(operationId);
                operation.name = $"Transform {index}: {operationType} [{node.id}]";
                operation.transform.SetSiblingIndex(0);
                ConfigureTransformOperation(operation, style, index, size);
                operationParent = operation.transform;
            }

            return operationParent;
        }

        private static void ConfigureTransformOrigin(
            GameObject target,
            UdomStyle style,
            Vector2 size)
        {
            ConfigureTransformWrapperRect(target);
            var rect = target.GetComponent<RectTransform>();
            var origin = ResolveTransformOrigin(style, size);
            Undo.RecordObject(rect, "Configure UDOM transform origin");
            rect.anchoredPosition = origin;
        }

        private static void ConfigureTransformOperation(
            GameObject target,
            UdomStyle style,
            int operationIndex,
            Vector2 size)
        {
            ConfigureTransformWrapperRect(target);
            var rect = target.GetComponent<RectTransform>();
            var operationType = style.transformOperationTypes != null
                                && operationIndex < style.transformOperationTypes.Length
                ? style.transformOperationTypes[operationIndex]
                : string.Empty;
            var valueIndex = operationIndex * 2;
            Undo.RecordObject(rect, "Configure UDOM transform operation");
            switch (operationType)
            {
                case "translate":
                {
                    var translationX = ResolveTransformOperationValue(
                        style,
                        valueIndex,
                        size.x,
                        0f);
                    var translationY = ResolveTransformOperationValue(
                        style,
                        valueIndex + 1,
                        size.y,
                        0f);
                    rect.anchoredPosition = new Vector2(translationX, -translationY);
                    break;
                }
                case "rotate":
                {
                    rect.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        -GetTransformOperationValue(style, valueIndex, 0f));
                    break;
                }
                case "scale":
                {
                    rect.localScale = new Vector3(
                        GetTransformOperationValue(style, valueIndex, 1f),
                        GetTransformOperationValue(style, valueIndex + 1, 1f),
                        1f);
                    break;
                }
            }
        }

        private static void ConfigureTransformWrapperRect(GameObject target)
        {
            var rect = target.GetComponent<RectTransform>();
            Undo.RecordObject(rect, "Configure UDOM transform wrapper");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            RemoveIfPresent<LayoutElement>(target);
        }

        private static void ConfigureTransformedNodeRect(
            GameObject target,
            UdomStyle style,
            Vector2 size)
        {
            var rect = target.GetComponent<RectTransform>();
            var origin = ResolveTransformOrigin(style, size);
            Undo.RecordObject(rect, "Configure transformed UDOM node");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = -origin;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            RemoveIfPresent<LayoutElement>(target);
        }

        private static void ConfigureStackedNodeRect(GameObject target, Vector2 size)
        {
            var rect = target.GetComponent<RectTransform>();
            Undo.RecordObject(rect, "Configure stacked UDOM node");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            RemoveIfPresent<LayoutElement>(target);
        }

        private static void ConfigureOuterShadows(
            Transform parent,
            UdomNode node,
            UdomStyle style,
            Vector2 size,
            BuildContext context)
        {
            var baseCenter = HasTransform(style) ? -ResolveTransformOrigin(style, size) : Vector2.zero;
            var siblingIndex = 0;
            var shadowCount = UdomShadowAssetUtility.GetCount(style);
            for (var index = 0; index < shadowCount; index++)
            {
                if (!UdomShadowAssetUtility.IsOuterRenderable(style, index))
                {
                    continue;
                }

                var shadowId = node.id + ShadowSuffix + index;
                var shadow = UpsertGeneratedObject(
                    shadowId,
                    "Shadow",
                    true,
                    parent,
                    context);
                context.DesiredIds.Add(shadowId);
                shadow.name = $"Shadow {index} [{node.id}]";
                shadow.transform.SetSiblingIndex(siblingIndex++);
                ConfigureShadowRect(shadow, style, index, size, baseCenter);

                var image = GetOrAdd<Image>(shadow);
                Undo.RecordObject(image, "Configure UDOM shadow");
                UdomShadowAssetUtility.Configure(
                    image,
                    style,
                    index,
                    size,
                    context.SourceAsset,
                    shadowId);
            }
        }

        private static void ConfigureInsetShadows(
            Transform parent,
            UdomNode node,
            UdomStyle style,
            Vector2 size,
            int siblingIndex,
            BuildContext context)
        {
            var shadowCount = UdomShadowAssetUtility.GetCount(style);
            for (var index = 0; index < shadowCount; index++)
            {
                if (!UdomShadowAssetUtility.IsInsetRenderable(style, index))
                {
                    continue;
                }

                var shadowId = node.id + ShadowSuffix + index;
                var shadow = UpsertGeneratedObject(
                    shadowId,
                    "InsetShadow",
                    true,
                    parent,
                    context);
                context.DesiredIds.Add(shadowId);
                shadow.name = $"Inset Shadow {index} [{node.id}]";
                shadow.transform.SetSiblingIndex(siblingIndex++);
                ConfigureInsetShadowRect(shadow, style, index, size);

                var image = GetOrAdd<Image>(shadow);
                Undo.RecordObject(image, "Configure UDOM inset shadow");
                UdomShadowAssetUtility.Configure(
                    image,
                    style,
                    index,
                    size,
                    context.SourceAsset,
                    shadowId);
            }
        }

        private static void ConfigureShadowRect(
            GameObject target,
            UdomStyle style,
            int shadowIndex,
            Vector2 size,
            Vector2 baseCenter)
        {
            var rect = target.GetComponent<RectTransform>();
            var offset = UdomShadowAssetUtility.GetOffset(style, shadowIndex);
            var geometrySize = UdomShadowAssetUtility.GetGeometrySize(style, shadowIndex, size);
            Undo.RecordObject(rect, "Configure UDOM shadow rect");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = baseCenter + new Vector2(offset.x, -offset.y);
            rect.sizeDelta = geometrySize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            RemoveIfPresent<LayoutElement>(target);

            var image = target.GetComponent<Image>();
            if (image != null && image.material != null)
            {
                Undo.RecordObject(image, "Refresh UDOM shadow");
                image.color = UdomBuilderUtility.ParseColor(
                    style.shadowColors[shadowIndex],
                    Color.clear);
                UdomShadowAssetUtility.ApplyProperties(
                    image.material,
                    style,
                    shadowIndex,
                    size);
                SavePaintMaterial(image.material);
            }
        }

        private static void ConfigureInsetShadowRect(
            GameObject target,
            UdomStyle style,
            int shadowIndex,
            Vector2 size)
        {
            var rect = target.GetComponent<RectTransform>();
            Undo.RecordObject(rect, "Configure UDOM inset shadow rect");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

            var layout = GetOrAdd<LayoutElement>(target);
            Undo.RecordObject(layout, "Configure UDOM inset shadow layout");
            layout.ignoreLayout = true;

            var image = target.GetComponent<Image>();
            if (image != null && image.material != null)
            {
                Undo.RecordObject(image, "Refresh UDOM inset shadow");
                image.color = UdomBuilderUtility.ParseColor(
                    style.shadowColors[shadowIndex],
                    Color.clear);
                UdomShadowAssetUtility.ApplyProperties(
                    image.material,
                    style,
                    shadowIndex,
                    size);
                SavePaintMaterial(image.material);
            }
        }

        private static Vector2 ResolveTransformOrigin(UdomStyle style, Vector2 size)
        {
            var x = ResolveTransformValue(
                style.transformOrigin,
                style.transformOriginIsPercent,
                0,
                size.x,
                50f,
                defaultIsPercent: true);
            var y = ResolveTransformValue(
                style.transformOrigin,
                style.transformOriginIsPercent,
                1,
                size.y,
                50f,
                defaultIsPercent: true);
            return new Vector2(-size.x * 0.5f + x, size.y * 0.5f - y);
        }

        private static float ResolveTransformOperationValue(
            UdomStyle style,
            int valueIndex,
            float reference,
            float fallback)
        {
            return ResolveTransformValue(
                style.transformOperationValues,
                style.transformOperationValuesArePercent,
                valueIndex,
                reference,
                fallback,
                defaultIsPercent: false);
        }

        private static float ResolveTransformValue(
            float[] values,
            bool[] percentages,
            int index,
            float reference,
            float fallback,
            bool defaultIsPercent)
        {
            var value = values != null && index < values.Length ? values[index] : fallback;
            var isPercent = percentages != null && index < percentages.Length
                ? percentages[index]
                : defaultIsPercent;
            return isPercent ? reference * value / 100f : value;
        }

        private static float GetTransformOperationValue(
            UdomStyle style,
            int valueIndex,
            float fallback)
        {
            return style.transformOperationValues != null
                   && valueIndex < style.transformOperationValues.Length
                ? style.transformOperationValues[valueIndex]
                : fallback;
        }

        private static void ConfigurePaint(GameObject target, UdomStyle style)
        {
            var visible = style == null || style.visible;
            var opacity = style != null ? style.opacity : 1f;
            var requiresCanvasGroup = !visible || Mathf.Abs(opacity - 1f) > 0.0001f;
            var paintState = target.GetComponent<UdomPaintState>();

            if (!requiresCanvasGroup)
            {
                if (paintState == null)
                {
                    return;
                }

                var existingGroup = paintState.CanvasGroup;
                if (existingGroup != null)
                {
                    Undo.RecordObject(existingGroup, "Restore UDOM paint state");
                    paintState.Restore();
                }

                if (paintState.CreatedCanvasGroup && existingGroup != null)
                {
                    Undo.DestroyObjectImmediate(existingGroup);
                }

                Undo.DestroyObjectImmediate(paintState);
                return;
            }

            if (paintState != null && paintState.CanvasGroup == null)
            {
                Undo.DestroyObjectImmediate(paintState);
                paintState = null;
            }

            if (paintState == null)
            {
                var canvasGroup = target.GetComponent<CanvasGroup>();
                var createdCanvasGroup = canvasGroup == null;
                if (createdCanvasGroup)
                {
                    canvasGroup = Undo.AddComponent<CanvasGroup>(target);
                }

                paintState = Undo.AddComponent<UdomPaintState>(target);
                Undo.RecordObject(paintState, "Capture UDOM paint state");
                paintState.Capture(canvasGroup, createdCanvasGroup);
            }

            Undo.RecordObjects(
                new UnityEngine.Object[] { paintState, paintState.CanvasGroup },
                "Configure UDOM paint state");
            paintState.Apply(visible, opacity);
        }

        private static void ConfigureMarginWrapper(GameObject wrapper, UdomStyle style, Transform parent)
        {
            var margin = GetEdges(GetEffectiveMargin(style));
            var size = GetVector2(style.size, new Vector2(100f, 100f));
            var wrapperStyle = new UdomStyle
            {
                position = style.position,
                size = new[] { size.x + margin.x + margin.z, size.y + margin.y + margin.w },
                minSize = new[]
                {
                    GetMinimumSize(style, 0) + margin.x + margin.z,
                    GetMinimumSize(style, 1) + margin.y + margin.w
                },
                maxSize = new[]
                {
                    AddMarginToMaximumSize(style, 0, margin.x + margin.z),
                    AddMarginToMaximumSize(style, 1, margin.y + margin.w)
                },
                positionAbsolute = style.positionAbsolute,
                useResolvedPosition = style.useResolvedPosition,
                flexibleWidth = style.flexibleWidth,
                flexibleHeight = style.flexibleHeight
            };
            ConfigureRect(wrapper, wrapperStyle, parent);
        }

        private static float[] GetEffectiveMargin(UdomStyle style)
        {
            var margin = style.margin != null && style.margin.Length >= 4
                ? style.margin
                : new[] { 0f, 0f, 0f, 0f };
            var alignment = style.alignSelfMargin != null && style.alignSelfMargin.Length >= 4
                ? style.alignSelfMargin
                : new[] { 0f, 0f, 0f, 0f };
            return new[]
            {
                margin[0] + alignment[0],
                margin[1] + alignment[1],
                margin[2] + alignment[2],
                margin[3] + alignment[3]
            };
        }

        private static bool UsesTopLeftPosition(UdomStyle style)
        {
            return style != null && (style.positionAbsolute || style.useResolvedPosition);
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

        private static void ConfigureBackground(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            var image = target.GetComponent<Image>();
            var hasGradient = UdomGradientAssetUtility.IsGradientType(style.backgroundType);
            var hasRadius = UdomRoundedCornerAssetUtility.HasRadius(style);
            if (image == null)
            {
                return;
            }

            var boxSize = target.GetComponent<RectTransform>().rect.size;
            if (boxSize.x <= 0f || boxSize.y <= 0f)
            {
                boxSize = GetVector2(style.size, new Vector2(100f, 100f));
            }

            Undo.RecordObject(image, "Configure UDOM background paint");
            if (hasGradient)
            {
                UdomRoundedCornerAssetUtility.Clear(image);
                UdomGradientAssetUtility.Configure(
                    image,
                    style,
                    boxSize,
                    context.SourceAsset,
                    node.id);
            }
            else if (hasRadius)
            {
                UdomGradientAssetUtility.Clear(image);
                UdomRoundedCornerAssetUtility.Configure(
                    image,
                    style,
                    boxSize,
                    context.SourceAsset,
                    node.id);
            }
            else
            {
                UdomGradientAssetUtility.Clear(image);
                UdomRoundedCornerAssetUtility.Clear(image);
            }

            var backgroundColor = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.clear);
            var showMaskGraphic = hasGradient || backgroundColor.a > 0f;
            if (hasRadius && !showMaskGraphic)
            {
                image.color = Color.white;
            }

            ConfigureRadiusMask(
                target,
                node.type,
                hasRadius,
                showMaskGraphic,
                style.clipContent);
        }

        private static void ConfigureRadiusMask(
            GameObject target,
            string nodeType,
            bool hasRadius,
            bool showMaskGraphic,
            bool clipContent)
        {
            if (hasRadius)
            {
                RemoveIfPresent<RectMask2D>(target);
                var mask = GetOrAdd<Mask>(target);
                Undo.RecordObject(mask, "Configure UDOM rounded mask");
                mask.showMaskGraphic = showMaskGraphic;
                return;
            }

            RemoveIfPresent<Mask>(target);
            if (string.Equals(nodeType, "Image", StringComparison.OrdinalIgnoreCase)
                || clipContent)
            {
                GetOrAdd<RectMask2D>(target);
            }
            else
            {
                RemoveIfPresent<RectMask2D>(target);
            }
        }

        private static void RefreshVisualLayout(UdomNode node, BuildContext context)
        {
            if (node == null || (node.style != null && node.style.displayNone))
            {
                return;
            }

            var style = node.style ?? new UdomStyle();
            var hasTransform = HasTransform(style);
            var hasOuterShadows = HasRenderableOuterShadow(style);
            var hasSiblingInsetShadows = RequiresSiblingInsetShadow(node, style);
            var hasStackedShadows = hasOuterShadows || hasSiblingInsetShadows;
            if (hasTransform || hasStackedShadows)
            {
                var layout = FindNode(
                    context.Root,
                    node.id + (hasTransform ? TransformLayoutSuffix : ShadowLayoutSuffix));
                var target = FindNode(context.Root, node.id);
                if (layout != null && target != null)
                {
                    var size = layout.GetComponent<RectTransform>().rect.size;
                    if (size.x <= 0f || size.y <= 0f)
                    {
                        size = GetVector2(style.size, new Vector2(100f, 100f));
                    }

                    if (hasTransform)
                    {
                        var origin = FindNode(context.Root, node.id + TransformOriginSuffix);
                        if (origin != null)
                        {
                            ConfigureTransformOrigin(origin.gameObject, style, size);
                        }

                        var operationCount = style.transformOperationTypes != null
                            ? style.transformOperationTypes.Length
                            : 0;
                        for (var index = 0; index < operationCount; index++)
                        {
                            var operation = FindNode(
                                context.Root,
                                node.id + TransformOperationSuffix + index);
                            if (operation != null)
                            {
                                ConfigureTransformOperation(operation.gameObject, style, index, size);
                            }
                        }

                        ConfigureTransformedNodeRect(target.gameObject, style, size);
                    }
                    else
                    {
                        ConfigureStackedNodeRect(target.gameObject, size);
                    }

                    var baseCenter = hasTransform
                        ? -ResolveTransformOrigin(style, size)
                        : Vector2.zero;
                    var shadowCount = UdomShadowAssetUtility.GetCount(style);
                    for (var index = 0; index < shadowCount; index++)
                    {
                        if (!UdomShadowAssetUtility.IsOuterRenderable(style, index))
                        {
                            continue;
                        }

                        var shadow = FindNode(context.Root, node.id + ShadowSuffix + index);
                        if (shadow != null)
                        {
                            ConfigureShadowRect(shadow.gameObject, style, index, size, baseCenter);
                        }
                    }

                    if (hasSiblingInsetShadows)
                    {
                        for (var index = 0; index < shadowCount; index++)
                        {
                            if (!UdomShadowAssetUtility.IsInsetRenderable(style, index))
                            {
                                continue;
                            }

                            var shadow = FindNode(context.Root, node.id + ShadowSuffix + index);
                            if (shadow != null)
                            {
                                ConfigureInsetShadowRect(shadow.gameObject, style, index, size);
                            }
                        }
                    }
                }
            }

            var insetTarget = FindNode(context.Root, node.id);
            if (!hasSiblingInsetShadows && insetTarget != null)
            {
                var insetSize = insetTarget.GetComponent<RectTransform>().rect.size;
                if (insetSize.x <= 0f || insetSize.y <= 0f)
                {
                    insetSize = GetVector2(style.size, new Vector2(100f, 100f));
                }

                var shadowCount = UdomShadowAssetUtility.GetCount(style);
                for (var index = 0; index < shadowCount; index++)
                {
                    if (!UdomShadowAssetUtility.IsInsetRenderable(style, index))
                    {
                        continue;
                    }

                    var shadow = FindNode(context.Root, node.id + ShadowSuffix + index);
                    if (shadow != null)
                    {
                        ConfigureInsetShadowRect(shadow.gameObject, style, index, insetSize);
                    }
                }
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                RefreshVisualLayout(children[index], context);
            }
        }

        private static void RefreshAbsoluteLayout(UdomNode node, BuildContext context)
        {
            if (node == null || (node.style != null && node.style.displayNone))
            {
                return;
            }

            var style = node.style ?? new UdomStyle();
            if (UsesTopLeftPosition(style))
            {
                var hasMargin = HasNonZero(GetEffectiveMargin(style));
                var hasTransform = HasTransform(style);
                var hasStackedShadows = HasRenderableOuterShadow(style)
                                        || RequiresSiblingInsetShadow(node, style);
                var stableId = hasMargin
                    ? node.id + MarginSuffix
                    : hasTransform
                        ? node.id + TransformLayoutSuffix
                        : hasStackedShadows
                            ? node.id + ShadowLayoutSuffix
                            : node.id;
                var layout = FindNode(context.Root, stableId);
                if (layout != null && layout.transform.parent != null)
                {
                    if (hasMargin)
                    {
                        ConfigureMarginWrapper(layout.gameObject, style, layout.transform.parent);
                    }
                    else
                    {
                        ConfigureRect(layout.gameObject, style, layout.transform.parent);
                    }
                }
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                RefreshAbsoluteLayout(children[index], context);
            }
        }

        private static void RefreshPaintLayout(UdomNode node, BuildContext context)
        {
            if (node == null || (node.style != null && node.style.displayNone))
            {
                return;
            }

            var style = node.style ?? new UdomStyle();
            var marker = FindNode(context.Root, node.id);
            if (marker != null)
            {
                var image = marker.GetComponent<Image>();
                var material = image != null ? image.material : null;
                var shaderName = material != null && material.shader != null
                    ? material.shader.name
                    : string.Empty;
                var boxSize = marker.GetComponent<RectTransform>().rect.size;
                if (UdomGradientAssetUtility.IsShader(shaderName))
                {
                    UdomGradientAssetUtility.ApplyLayoutProperties(material, style, boxSize);
                    SavePaintMaterial(material);
                }
                else if (string.Equals(
                             shaderName,
                             UdomRoundedCornerAssetUtility.ShaderName,
                             StringComparison.Ordinal))
                {
                    UdomRoundedCornerAssetUtility.ApplyProperties(material, style, boxSize);
                    SavePaintMaterial(material);
                }
            }

            var borderMarker = FindNode(context.Root, node.id + BorderSuffix);
            if (borderMarker != null)
            {
                var borderImage = borderMarker.GetComponent<Image>();
                var borderMaterial = borderImage != null ? borderImage.material : null;
                var borderShaderName = borderMaterial != null && borderMaterial.shader != null
                    ? borderMaterial.shader.name
                    : string.Empty;
                if (UdomBorderAssetUtility.IsShader(borderShaderName))
                {
                    var borderSize = borderMarker.GetComponent<RectTransform>().rect.size;
                    UdomBorderAssetUtility.ApplyProperties(borderMaterial, style, borderSize);
                    SavePaintMaterial(borderMaterial);
                }
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                RefreshPaintLayout(children[index], context);
            }
        }

        private static void SavePaintMaterial(Material material)
        {
            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }
        }

        private static void ConfigureBorder(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            if (!UdomBorderAssetUtility.HasVisibleBorder(style))
            {
                return;
            }

            var borderId = node.id + BorderSuffix;
            var border = UpsertGeneratedObject(borderId, "Border", true, target.transform, context);
            context.DesiredIds.Add(borderId);
            border.name = "Border";
            var borderLayout = GetOrAdd<LayoutElement>(border);
            Undo.RecordObject(borderLayout, "Configure UDOM border layout");
            borderLayout.ignoreLayout = true;
            border.transform.SetAsLastSibling();
            ConfigureStretch(border.GetComponent<RectTransform>());

            var boxSize = target.GetComponent<RectTransform>().rect.size;
            if (boxSize.x <= 0f || boxSize.y <= 0f)
            {
                boxSize = GetVector2(style.size, new Vector2(100f, 100f));
            }

            var image = GetOrAdd<Image>(border);
            Undo.RecordObject(image, "Configure UDOM border image");
            UdomBorderAssetUtility.Configure(
                image,
                style,
                boxSize,
                context.SourceAsset,
                node.id);
        }

        private static void ConfigureLayout(GameObject target, UdomStyle style)
        {
            if (style.useResolvedChildPositions)
            {
                RemoveIfPresent<VerticalLayoutGroup>(target);
                RemoveIfPresent<HorizontalLayoutGroup>(target);
                return;
            }

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
            group.childControlWidth = style.stretchChildrenWidth && !style.useResolvedChildrenWidth;
            group.childControlHeight = style.stretchChildrenHeight && !style.useResolvedChildrenHeight;
            group.childForceExpandWidth = group.childControlWidth;
            group.childForceExpandHeight = group.childControlHeight;
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
            text.enableWordWrapping = style.textWrap;
            text.overflowMode = GetTextOverflowMode(style.textOverflow);
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

            ConfigureTextMetrics(text, style);
        }

        private static void ConfigureImage(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            RemoveIfPresent<RawImage>(target);
            var background = GetOrAdd<Image>(target);
            Undo.RecordObject(background, "Configure UDOM Image background");
            background.color = UdomBuilderUtility.ParseColor(style.backgroundColor, Color.clear);
            background.sprite = null;
            background.preserveAspect = false;
            background.raycastTarget = false;
            GetOrAdd<RectMask2D>(target);

            var contentId = node.id + ImageContentSuffix;
            var content = UpsertGeneratedObject(
                contentId,
                "ImageContent",
                true,
                target.transform,
                context);
            context.DesiredIds.Add(contentId);
            content.name = "Image Content";
            content.transform.SetSiblingIndex(0);
            RemoveIfPresent<LayoutElement>(content);

            var intrinsicSize = GetImageIntrinsicSize(node);
            if (!string.IsNullOrWhiteSpace(node.texture))
            {
                RemoveIfPresent<Image>(content);
                var rawImage = GetOrAdd<RawImage>(content);
                Undo.RecordObject(rawImage, "Configure UDOM RawImage");
                rawImage.color = Color.white;
                rawImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(node.texture);
                rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                rawImage.raycastTarget = false;
                if ((intrinsicSize.x <= 0f || intrinsicSize.y <= 0f) && rawImage.texture != null)
                {
                    intrinsicSize = new Vector2(rawImage.texture.width, rawImage.texture.height);
                }
            }
            else
            {
                RemoveIfPresent<RawImage>(content);
                var image = GetOrAdd<Image>(content);
                Undo.RecordObject(image, "Configure UDOM Image content");
                image.color = Color.white;
                image.sprite = LoadSprite(node.sprite);
                image.preserveAspect = false;
                image.raycastTarget = false;
                if ((intrinsicSize.x <= 0f || intrinsicSize.y <= 0f) && image.sprite != null)
                {
                    intrinsicSize = image.sprite.rect.size;
                }
            }

            ConfigureImageContentRect(
                content.GetComponent<RectTransform>(),
                target.GetComponent<RectTransform>().rect.size,
                intrinsicSize,
                node.imageFit,
                node.imagePositionX,
                node.imagePositionY);
        }

        private static Vector2 GetImageIntrinsicSize(UdomNode node)
        {
            var declared = GetVector2(node.imageIntrinsicSize, Vector2.zero);
            if (declared.x > 0f && declared.y > 0f)
            {
                return declared;
            }

            return Vector2.zero;
        }

        private static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        private static void ConfigureImageContentRect(
            RectTransform rect,
            Vector2 boxSize,
            Vector2 intrinsicSize,
            string fit,
            string positionX,
            string positionY)
        {
            boxSize.x = Mathf.Max(0f, boxSize.x);
            boxSize.y = Mathf.Max(0f, boxSize.y);
            var hasIntrinsicSize = intrinsicSize.x > 0f && intrinsicSize.y > 0f;
            var contentSize = boxSize;
            if (hasIntrinsicSize && string.Equals(fit, "contain", StringComparison.OrdinalIgnoreCase))
            {
                var scale = Mathf.Min(boxSize.x / intrinsicSize.x, boxSize.y / intrinsicSize.y);
                contentSize = intrinsicSize * scale;
            }
            else if (hasIntrinsicSize && string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase))
            {
                var scale = Mathf.Max(boxSize.x / intrinsicSize.x, boxSize.y / intrinsicSize.y);
                contentSize = intrinsicSize * scale;
            }
            else if (hasIntrinsicSize && string.Equals(fit, "none", StringComparison.OrdinalIgnoreCase))
            {
                contentSize = intrinsicSize;
            }

            var available = boxSize - contentSize;
            var offset = new Vector2(
                ResolveImagePosition(positionX, available.x),
                ResolveImagePosition(positionY, available.y));
            Undo.RecordObject(rect, "Configure UDOM Image content rect");
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(offset.x, -offset.y);
            rect.sizeDelta = contentSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static float ResolveImagePosition(string value, float available)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return available * 0.5f;
            }

            if (value.EndsWith("%", StringComparison.Ordinal)
                && float.TryParse(
                    value.Substring(0, value.Length - 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percentage))
            {
                return available * percentage / 100f;
            }

            return float.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset)
                ? offset
                : available * 0.5f;
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

        private static void ConfigureTextInput(
            GameObject target,
            UdomNode node,
            UdomStyle style,
            BuildContext context)
        {
            RemoveIfPresent<RawImage>(target);
            var background = GetOrAdd<Image>(target);
            var input = GetOrAdd<TMP_InputField>(target);
            Undo.RecordObjects(new UnityEngine.Object[] { background, input }, "Configure UDOM Text Input");
            background.color = UdomBuilderUtility.ParseColor(
                style.backgroundColor,
                new Color32(0x25, 0x27, 0x33, 0xFF));
            background.raycastTarget = true;

            var viewportId = node.id + InputViewportSuffix;
            var viewport = UpsertGeneratedObject(viewportId, "InputViewport", true, target.transform, context);
            context.DesiredIds.Add(viewportId);
            viewport.name = "Text Area";
            viewport.transform.SetSiblingIndex(0);
            var viewportRect = viewport.GetComponent<RectTransform>();
            ConfigureStretch(viewportRect);
            Undo.RecordObject(viewportRect, "Configure UDOM Text Input viewport");
            viewportRect.offsetMin = new Vector2(12f, 8f);
            viewportRect.offsetMax = new Vector2(-12f, -8f);
            RemoveIfPresent<LayoutElement>(viewport);
            RemoveIfPresent<Image>(viewport);
            GetOrAdd<RectMask2D>(viewport);

            var textId = node.id + InputTextSuffix;
            var textObject = UpsertGeneratedObject(textId, "InputText", true, viewport.transform, context);
            context.DesiredIds.Add(textId);
            textObject.name = "Text";
            textObject.transform.SetSiblingIndex(0);
            ConfigureStretch(textObject.GetComponent<RectTransform>());
            RemoveIfPresent<LayoutElement>(textObject);
            var text = GetOrAdd<TextMeshProUGUI>(textObject);
            var font = ResolveFont(style, context.Font);
            ConfigureInputTextGraphic(text, node.textInputValue, style, font, false);

            var placeholderId = node.id + InputPlaceholderSuffix;
            var placeholderObject = UpsertGeneratedObject(
                placeholderId,
                "InputPlaceholder",
                true,
                viewport.transform,
                context);
            context.DesiredIds.Add(placeholderId);
            placeholderObject.name = "Placeholder";
            placeholderObject.transform.SetSiblingIndex(1);
            ConfigureStretch(placeholderObject.GetComponent<RectTransform>());
            RemoveIfPresent<LayoutElement>(placeholderObject);
            var placeholder = GetOrAdd<TextMeshProUGUI>(placeholderObject);
            ConfigureInputTextGraphic(
                placeholder,
                node.textInputPlaceholder,
                style,
                font,
                true);

            input.targetGraphic = background;
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = node.textInputMultiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.readOnly = node.textInputReadOnly;
            input.interactable = node.interactable;
            input.transition = Selectable.Transition.ColorTint;
            input.customCaretColor = true;
            input.caretColor = text.color;
            input.selectionColor = new Color(text.color.r, text.color.g, text.color.b, 0.35f);
            input.SetTextWithoutNotify(node.textInputValue ?? string.Empty);
        }

        private static void ConfigureInputTextGraphic(
            TextMeshProUGUI text,
            string value,
            UdomStyle style,
            TMP_FontAsset font,
            bool placeholder)
        {
            Undo.RecordObject(text, placeholder ? "Configure UDOM Input placeholder" : "Configure UDOM Input text");
            text.text = value ?? string.Empty;
            text.fontSize = style.fontSize;
            var color = UdomBuilderUtility.ParseColor(style.textColor, Color.white);
            if (placeholder)
            {
                color.a *= 0.5f;
            }

            text.color = color;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
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

            ConfigureTextMetrics(text, style);
        }

        private static void ConfigureTextMetrics(TextMeshProUGUI text, UdomStyle style)
        {
            var fontSize = Mathf.Max(0.0001f, style.fontSize);
            text.characterSpacing = style.letterSpacing * 100f / fontSize;
            if (style.lineHeight <= 0f || text.font == null)
            {
                text.lineSpacing = 0f;
                return;
            }

            var face = text.font.faceInfo;
            var pointSize = Mathf.Max(0.0001f, face.pointSize);
            var nativeLineHeight = face.lineHeight * fontSize / pointSize * face.scale;
            var emScale = fontSize * 0.01f;
            text.lineSpacing = (style.lineHeight - nativeLineHeight) / emScale;
        }

        private static TextOverflowModes GetTextOverflowMode(string value)
        {
            if (string.Equals(value, "Visible", StringComparison.Ordinal))
            {
                return TextOverflowModes.Overflow;
            }

            if (string.Equals(value, "Clip", StringComparison.Ordinal))
            {
                return TextOverflowModes.Masking;
            }

            return TextOverflowModes.Ellipsis;
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

            var explicitAxis = node.scrollAxisExplicit || node.scrollHorizontal || !node.scrollVertical;
            var horizontal = explicitAxis
                ? node.scrollHorizontal
                : string.Equals(style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase);
            var vertical = explicitAxis ? node.scrollVertical : !horizontal;

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
            var contentRect = content.GetComponent<RectTransform>();
            ConfigureScrollContentRect(contentRect, horizontal, vertical);
            ConfigureLayout(content, style);

            var fitter = GetOrAdd<ContentSizeFitter>(content);
            Undo.RecordObject(fitter, "Configure UDOM Scroll Content");
            fitter.horizontalFit = horizontal
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            scrollRect.horizontal = horizontal;
            scrollRect.vertical = vertical;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;

            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                BuildNode(children[index], content.transform, documentPanel, index, context);
            }

            var initialOffset = GetVector2(node.scrollInitialOffset, Vector2.zero);
            contentRect.anchoredPosition = new Vector2(
                horizontal ? -initialOffset.x : 0f,
                vertical ? initialOffset.y : 0f);
        }

        private static void ConfigureEmbed(GameObject target, UdomNode node, BuildContext context)
        {
            var anchor = GetOrAdd<UdomEmbedAnchor>(target);
            Undo.RecordObject(anchor, "Configure UDOM Embed");
            GameObject fallback = null;
            if (node.embed != null && !string.IsNullOrEmpty(node.embed.fallbackLabel))
            {
                var fallbackId = node.id + EmbedFallbackSuffix;
                fallback = UpsertGeneratedObject(
                    fallbackId,
                    "EmbedFallback",
                    true,
                    target.transform,
                    context);
                context.DesiredIds.Add(fallbackId);
                fallback.name = "Fallback Label";
                fallback.transform.SetSiblingIndex(0);
                ConfigureStretch(fallback.GetComponent<RectTransform>());
                RemoveIfPresent<LayoutElement>(fallback);
                ConfigureText(
                    fallback,
                    new UdomNode { text = node.embed.fallbackLabel },
                    node.style ?? new UdomStyle(),
                    context.Font);
            }

            anchor.Configure(
                context.Root,
                node.embed != null ? node.embed.targetSlot : string.Empty,
                fallback);
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

        private static void ConfigureScrollContentRect(
            RectTransform rect,
            bool horizontal,
            bool vertical)
        {
            Undo.RecordObject(rect, "Configure Scroll Content RectTransform");
            rect.anchorMin = new Vector2(0f, vertical ? 1f : 0f);
            rect.anchorMax = new Vector2(horizontal ? 0f : 1f, 1f);
            rect.pivot = new Vector2(horizontal ? 0f : 0.5f, vertical ? 1f : 0.5f);
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
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            var candidates = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (var index = 0; index < candidates.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(candidates[index]);
                if (path.StartsWith("Assets/Html2VrcGenerated/", StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "TextMeshPro Font Asset을 찾을 수 없다. Window > TextMeshPro > Import TMP Essential Resources를 먼저 실행해야 한다.");
        }

        private static TMP_FontAsset ResolveFont(UdomStyle style, TMP_FontAsset fallback)
        {
            return style != null && !string.IsNullOrWhiteSpace(style.fontAssetPath)
                ? UdomFontAssetUtility.LoadOrCreate(style.fontAssetPath, fallback)
                : fallback;
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

        private static bool HasTransform(UdomStyle style)
        {
            return style != null
                   && style.transformOperationTypes != null
                   && style.transformOperationTypes.Length > 0;
        }

        private static bool HasRenderableOuterShadow(UdomStyle style)
        {
            var count = UdomShadowAssetUtility.GetCount(style);
            for (var index = 0; index < count; index++)
            {
                if (UdomShadowAssetUtility.IsOuterRenderable(style, index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRenderableInsetShadow(UdomStyle style)
        {
            var count = UdomShadowAssetUtility.GetCount(style);
            for (var index = 0; index < count; index++)
            {
                if (UdomShadowAssetUtility.IsInsetRenderable(style, index))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountRenderableOuterShadows(UdomStyle style)
        {
            var count = 0;
            var shadowCount = UdomShadowAssetUtility.GetCount(style);
            for (var index = 0; index < shadowCount; index++)
            {
                if (UdomShadowAssetUtility.IsOuterRenderable(style, index))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool RequiresSiblingInsetShadow(UdomNode node, UdomStyle style)
        {
            return node != null
                   && string.Equals(node.type, "Text", StringComparison.OrdinalIgnoreCase)
                   && HasRenderableInsetShadow(style);
        }

        private static Vector2 GetVector2(float[] values, Vector2 fallback)
        {
            return values != null && values.Length >= 2
                ? new Vector2(values[0], values[1])
                : fallback;
        }

        private static float GetMinimumSize(UdomStyle style, int axis)
        {
            return style != null && style.minSize != null && style.minSize.Length >= 2
                ? Mathf.Max(0f, style.minSize[axis])
                : 0f;
        }

        private static float AddMarginToMaximumSize(UdomStyle style, int axis, float margin)
        {
            return style != null
                   && style.maxSize != null
                   && style.maxSize.Length >= 2
                   && style.maxSize[axis] >= 0f
                ? style.maxSize[axis] + margin
                : -1f;
        }

        private static Vector4 GetEdges(float[] values)
        {
            return values != null && values.Length >= 4
                ? new Vector4(values[0], values[1], values[2], values[3])
                : Vector4.zero;
        }
    }
}
