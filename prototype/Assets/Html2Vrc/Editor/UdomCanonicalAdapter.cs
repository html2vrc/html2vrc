using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Html2Vrc.Editor
{
    internal sealed class UdomParseWarning
    {
        public string Path { get; }
        public string Message { get; }

        public UdomParseWarning(string path, string message)
        {
            Path = path;
            Message = message;
        }
    }

    internal static class UdomCanonicalAdapter
    {
        private const int MaximumNodeDepth = 64;

        private sealed class ResourceInfo
        {
            public string Type;
            public string Uri;
            public string UnityPath;
            public float Width;
            public float Height;
        }

        private sealed class StyleMapping
        {
            public UdomStyle Style;
            public bool HasWidth;
            public bool HasHeight;
            public bool HasBackground;
        }

        public static UdomDocument Map(
            Dictionary<string, object> value,
            string sourceAssetPath,
            List<UdomParseWarning> warnings)
        {
            EnsureOnlyKeys(
                value,
                "$",
                "asset",
                "viewport",
                "root",
                "styles",
                "resources",
                "extensionsUsed",
                "extensionsRequired",
                "extensions",
                "extras");

            var asset = RequireObject(RequireValue(value, "asset", "$"), "$.asset");
            EnsureOnlyKeys(asset, "$.asset", "version", "minVersion", "generator", "copyright", "extensions", "extras");
            var version = RequireString(asset, "version", "$.asset.version");

            var viewport = RequireObject(RequireValue(value, "viewport", "$"), "$.viewport");
            EnsureOnlyKeys(viewport, "$.viewport", "width", "height", "pixelRatio", "fit", "extensions", "extras");
            var width = RequirePositiveFloat(viewport, "width", "$.viewport.width");
            var height = RequirePositiveFloat(viewport, "height", "$.viewport.height");
            var pixelRatio = 1f;
            if (viewport.TryGetValue("pixelRatio", out var pixelRatioValue))
            {
                pixelRatio = RequireFloat(pixelRatioValue, "$.viewport.pixelRatio");
                if (pixelRatio <= 0f)
                {
                    throw new FormatException("$.viewport.pixelRatio: value must be greater than zero.");
                }
            }

            var fit = GetString(viewport, "fit") ?? "contain";
            if (!string.Equals(fit, "contain", StringComparison.Ordinal)
                && !string.Equals(fit, "cover", StringComparison.Ordinal)
                && !string.Equals(fit, "stretch", StringComparison.Ordinal)
                && !string.Equals(fit, "none", StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"$.viewport.fit: expected 'contain', 'cover', 'stretch', or 'none', got '{fit}'.");
            }

            RejectNonEmptyArray(
                value,
                "extensionsRequired",
                "$.extensionsRequired",
                "required canonical extensions are not supported");
            WarnForNonEmptyArray(
                value,
                "extensionsUsed",
                "$.extensionsUsed",
                "Canonical extensions are ignored by the Unity compatibility adapter.",
                warnings);

            var styles = MapStyles(value);
            var resources = MapResources(value, sourceAssetPath);
            var rootValue = RequireObject(RequireValue(value, "root", "$"), "$.root");
            var rootId = RequireString(rootValue, "id", "$.root.id");
            var canvasSize = new[] { width, height };
            var root = MapNode(rootValue, "$.root", 0, canvasSize, true, styles, resources, warnings);
            ResolveFlexLayoutTree(root);

            return new UdomDocument
            {
                schemaVersion = version,
                id = rootId + "-document",
                name = GetString(asset, "generator") ?? rootId,
                canvas = new UdomCanvas
                {
                    renderMode = "WorldSpace",
                    size = canvasSize,
                    scale = 0.01f,
                    viewportPixelRatio = pixelRatio,
                    viewportFit = fit
                },
                root = root
            };
        }

        private static Dictionary<string, Dictionary<string, object>> MapStyles(
            Dictionary<string, object> document)
        {
            var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            if (!document.TryGetValue("styles", out var stylesValue))
            {
                return result;
            }

            var styles = RequireArray(stylesValue, "$.styles");
            for (var index = 0; index < styles.Count; index++)
            {
                var path = $"$.styles[{index}]";
                var definition = RequireObject(styles[index], path);
                EnsureOnlyKeys(definition, path, "id", "style", "extensions", "extras");
                var id = RequireString(definition, "id", path + ".id");
                if (result.ContainsKey(id))
                {
                    throw new FormatException($"{path}.id: duplicate canonical style id '{id}'.");
                }

                var style = RequireObject(RequireValue(definition, "style", path), path + ".style");
                EnsureOnlyKeys(style, path + ".style", "layout", "paint", "text", "transform");
                result.Add(id, style);
            }

            return result;
        }

        private static Dictionary<string, ResourceInfo> MapResources(
            Dictionary<string, object> document,
            string sourceAssetPath)
        {
            var result = new Dictionary<string, ResourceInfo>(StringComparer.Ordinal);
            if (!document.TryGetValue("resources", out var resourcesValue))
            {
                return result;
            }

            var resources = RequireArray(resourcesValue, "$.resources");
            for (var index = 0; index < resources.Count; index++)
            {
                var path = $"$.resources[{index}]";
                var resource = RequireObject(resources[index], path);
                EnsureOnlyKeys(
                    resource,
                    path,
                    "id",
                    "type",
                    "uri",
                    "mimeType",
                    "hash",
                    "width",
                    "height",
                    "extensions",
                    "extras");

                var id = RequireString(resource, "id", path + ".id");
                if (result.ContainsKey(id))
                {
                    throw new FormatException($"{path}.id: duplicate canonical resource id '{id}'.");
                }

                var type = RequireString(resource, "type", path + ".type");
                if (!string.Equals(type, "image", StringComparison.Ordinal)
                    && !string.Equals(type, "sprite", StringComparison.Ordinal)
                    && !string.Equals(type, "font", StringComparison.Ordinal))
                {
                    throw new FormatException($"{path}.type: unsupported canonical resource type '{type}'.");
                }

                var uri = RequireString(resource, "uri", path + ".uri");
                result.Add(id, new ResourceInfo
                {
                    Type = type,
                    Uri = uri,
                    UnityPath = ResolveUnityAssetPath(uri, sourceAssetPath),
                    Width = GetOptionalPositiveFloat(resource, "width", path + ".width"),
                    Height = GetOptionalPositiveFloat(resource, "height", path + ".height")
                });
            }

            return result;
        }

        private static string ResolveUnityAssetPath(string uri, string sourceAssetPath)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            var normalizedUri = uri.Replace('\\', '/');
            if (normalizedUri.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return NormalizeAssetSegments(normalizedUri);
            }

            if (string.IsNullOrWhiteSpace(sourceAssetPath)
                || !sourceAssetPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)
                || normalizedUri.StartsWith("/", StringComparison.Ordinal)
                || normalizedUri.Contains("://")
                || Path.IsPathRooted(uri))
            {
                return null;
            }

            var normalizedSource = sourceAssetPath.Replace('\\', '/');
            var separator = normalizedSource.LastIndexOf('/');
            if (separator < 0)
            {
                return null;
            }

            return NormalizeAssetSegments(
                normalizedSource.Substring(0, separator + 1) + normalizedUri);
        }

        private static string NormalizeAssetSegments(string path)
        {
            var result = new List<string>();
            var segments = path.Replace('\\', '/').Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (string.IsNullOrEmpty(segment) || string.Equals(segment, ".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    if (result.Count <= 1)
                    {
                        return null;
                    }

                    result.RemoveAt(result.Count - 1);
                    continue;
                }

                result.Add(segment);
            }

            if (result.Count < 2 || !string.Equals(result[0], "Assets", StringComparison.Ordinal))
            {
                return null;
            }

            return string.Join("/", result);
        }

        private static UdomNode MapNode(
            Dictionary<string, object> value,
            string path,
            int depth,
            float[] parentSize,
            bool fillParentByDefault,
            Dictionary<string, Dictionary<string, object>> styles,
            Dictionary<string, ResourceInfo> resources,
            List<UdomParseWarning> warnings)
        {
            if (depth > MaximumNodeDepth)
            {
                throw new FormatException($"{path}: node nesting cannot exceed {MaximumNodeDepth} levels.");
            }

            var canonicalType = RequireString(value, "type", path + ".type");
            var id = RequireString(value, "id", path + ".id");
            var elementName = string.Empty;
            var legacyType = string.Empty;

            if (string.Equals(canonicalType, "text", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(value, path, "type", "id", "value", "styleRefs", "style", "bind", "extensions", "extras");
                legacyType = "Text";
            }
            else if (string.Equals(canonicalType, "element", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(
                    value,
                    path,
                    "type",
                    "id",
                    "name",
                    "properties",
                    "styleRefs",
                    "style",
                    "children",
                    "bind",
                    "on",
                    "extensions",
                    "extras");
                elementName = RequireString(value, "name", path + ".name");
                legacyType = MapElementType(elementName, path + ".name");
            }
            else
            {
                throw new FormatException($"{path}.type: unsupported canonical node type '{canonicalType}'.");
            }

            ValidateBindingMap(canonicalType, elementName, value, path, warnings);

            var styleObject = ResolveStyle(value, path, styles);
            var mappedStyle = MapStyle(
                styleObject,
                path + ".style",
                parentSize,
                fillParentByDefault,
                resources,
                warnings);
            var node = new UdomNode
            {
                id = id,
                type = legacyType,
                name = id,
                style = mappedStyle.Style
            };

            if (string.Equals(canonicalType, "text", StringComparison.Ordinal))
            {
                var text = RequireText(value, "value", path + ".value");
                node.text = node.style.preserveWhitespace
                    ? text
                    : CollapseCanonicalWhitespace(text);
                if (IsNativeGradient(node.style.backgroundType))
                {
                    var gradientType = node.style.backgroundType;
                    warnings.Add(new UdomParseWarning(
                        path + ".style.paint.backgrounds[0]",
                        $"Canonical {gradientType} on a text node uses its first stop color because TMP text and its box background require separate graphics."));
                    node.style.backgroundType = "color";
                    node.style.backgroundGradientPositions = Array.Empty<float>();
                    node.style.backgroundGradientColors = Array.Empty<string>();
                }

                if (HasCornerRadius(node.style))
                {
                    warnings.Add(new UdomParseWarning(
                        path + ".style.paint.radius",
                        "Canonical corner radii on a text node use square corners because TMP text and its box background require separate graphics."));
                    node.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
                    node.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
                }

                if (!mappedStyle.HasWidth && !fillParentByDefault)
                {
                    node.style.size[0] = parentSize != null && parentSize.Length >= 1
                        ? parentSize[0]
                        : 100f;
                }

                if (!mappedStyle.HasHeight && !fillParentByDefault)
                {
                    node.style.size[1] = Math.Max(32f, node.style.fontSize * 1.5f);
                }

                FinalizeCanonicalBoxSize(node.style);
                return node;
            }

            MapElementProperties(node, elementName, value, path, mappedStyle, resources, warnings);
            ValidateEventMap(elementName, value, path, warnings);
            FinalizeCanonicalBoxSize(node.style);

            if (string.Equals(node.type, "Embed", StringComparison.OrdinalIgnoreCase)
                && IsNativeGradient(node.style.backgroundType))
            {
                var gradientType = node.style.backgroundType;
                warnings.Add(new UdomParseWarning(
                    path + ".style.paint.backgrounds[0]",
                    $"Canonical {gradientType} on an embed anchor uses its first stop color because the external object owns its graphics."));
                node.style.backgroundType = "color";
                node.style.backgroundGradientPositions = Array.Empty<float>();
                node.style.backgroundGradientColors = Array.Empty<string>();
            }

            if (string.Equals(node.type, "Embed", StringComparison.OrdinalIgnoreCase)
                && HasCornerRadius(node.style))
            {
                warnings.Add(new UdomParseWarning(
                    path + ".style.paint.radius",
                    "Canonical corner radii on an embed anchor use square corners because the external object owns its graphics."));
                node.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
                node.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
            }

            if (value.TryGetValue("children", out var childrenValue))
            {
                var children = RequireArray(childrenValue, path + ".children");
                node.children = new UdomNode[children.Count];
                for (var index = 0; index < children.Count; index++)
                {
                    var childPath = $"{path}.children[{index}]";
                    node.children[index] = MapNode(
                        RequireObject(children[index], childPath),
                        childPath,
                        depth + 1,
                        node.style.size,
                        false,
                        styles,
                        resources,
                        warnings);
                }
            }

            return node;
        }

        private static Dictionary<string, object> ResolveStyle(
            Dictionary<string, object> node,
            string path,
            Dictionary<string, Dictionary<string, object>> styles)
        {
            Dictionary<string, object> merged = null;
            if (node.TryGetValue("styleRefs", out var styleRefsValue))
            {
                var styleRefs = RequireArray(styleRefsValue, path + ".styleRefs");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                merged = new Dictionary<string, object>(StringComparer.Ordinal);
                for (var index = 0; index < styleRefs.Count; index++)
                {
                    var referencePath = $"{path}.styleRefs[{index}]";
                    var styleId = RequireString(styleRefs[index], referencePath);
                    if (!seen.Add(styleId))
                    {
                        throw new FormatException($"{referencePath}: duplicate canonical style reference '{styleId}'.");
                    }

                    if (!styles.TryGetValue(styleId, out var referencedStyle))
                    {
                        throw new FormatException($"{referencePath}: unknown canonical style '{styleId}'.");
                    }

                    MergeObjects(merged, referencedStyle);
                }
            }

            if (node.TryGetValue("style", out var inlineStyleValue))
            {
                if (merged == null)
                {
                    merged = new Dictionary<string, object>(StringComparer.Ordinal);
                }

                MergeObjects(merged, RequireObject(inlineStyleValue, path + ".style"));
            }

            return merged;
        }

        private static void MergeObjects(
            Dictionary<string, object> target,
            Dictionary<string, object> source)
        {
            foreach (var pair in source)
            {
                if (pair.Value is Dictionary<string, object> sourceObject
                    && target.TryGetValue(pair.Key, out var targetValue)
                    && targetValue is Dictionary<string, object> targetObject)
                {
                    MergeObjects(targetObject, sourceObject);
                    continue;
                }

                target[pair.Key] = CloneValue(pair.Value);
            }
        }

        private static object CloneValue(object value)
        {
            if (value is Dictionary<string, object> sourceObject)
            {
                var clone = new Dictionary<string, object>(StringComparer.Ordinal);
                MergeObjects(clone, sourceObject);
                return clone;
            }

            if (value is List<object> sourceArray)
            {
                var clone = new List<object>(sourceArray.Count);
                for (var index = 0; index < sourceArray.Count; index++)
                {
                    clone.Add(CloneValue(sourceArray[index]));
                }

                return clone;
            }

            return value;
        }

        private static string MapElementType(string elementName, string path)
        {
            switch (elementName)
            {
                case "view":
                    return "Panel";
                case "image":
                    return "Image";
                case "button":
                    return "Button";
                case "toggle":
                    return "Toggle";
                case "slider":
                    return "Slider";
                case "text-input":
                    return "TextInput";
                case "scroll":
                    return "ScrollView";
                case "embed":
                    return "Embed";
                default:
                    throw new FormatException($"{path}: unknown canonical element '{elementName}'.");
            }
        }

        private static void MapElementProperties(
            UdomNode node,
            string elementName,
            Dictionary<string, object> value,
            string path,
            StyleMapping mappedStyle,
            Dictionary<string, ResourceInfo> resources,
            List<UdomParseWarning> warnings)
        {
            var properties = value.TryGetValue("properties", out var propertiesValue)
                ? RequireObject(propertiesValue, path + ".properties")
                : null;

            switch (elementName)
            {
                case "view":
                    RequireEmptyProperties(properties, path + ".properties", elementName);
                    break;
                case "image":
                    if (properties == null)
                    {
                        throw new FormatException($"{path}.properties: canonical image properties are required.");
                    }

                    EnsureOnlyKeys(properties, path + ".properties", "resource", "fit", "position");
                    var resourceId = RequireString(properties, "resource", path + ".properties.resource");
                    if (!resources.TryGetValue(resourceId, out var resource))
                    {
                        throw new FormatException($"{path}.properties.resource: unknown resource '{resourceId}'.");
                    }

                    if (string.Equals(resource.Type, "font", StringComparison.Ordinal))
                    {
                        throw new FormatException($"{path}.properties.resource: font resource '{resourceId}' cannot be used by an image.");
                    }

                    if (!string.IsNullOrWhiteSpace(resource.UnityPath))
                    {
                        if (string.Equals(resource.Type, "sprite", StringComparison.Ordinal))
                        {
                            node.sprite = resource.UnityPath;
                        }
                        else
                        {
                            node.texture = resource.UnityPath;
                        }
                    }
                    else
                    {
                        warnings.Add(new UdomParseWarning(
                            path + ".properties.resource",
                            $"Resource '{resourceId}' uses relative URI '{resource.Uri}'. Copy it into Unity Assets/ and update the URI to import the Sprite."));
                    }

                    if (!mappedStyle.HasWidth && resource.Width > 0f)
                    {
                        node.style.size[0] = resource.Width;
                    }

                    if (!mappedStyle.HasHeight && resource.Height > 0f)
                    {
                        node.style.size[1] = resource.Height;
                    }

                    node.imageIntrinsicSize = new[] { resource.Width, resource.Height };
                    node.imageFit = GetString(properties, "fit") ?? "contain";
                    if (!string.Equals(node.imageFit, "fill", StringComparison.Ordinal)
                        && !string.Equals(node.imageFit, "contain", StringComparison.Ordinal)
                        && !string.Equals(node.imageFit, "cover", StringComparison.Ordinal)
                        && !string.Equals(node.imageFit, "none", StringComparison.Ordinal))
                    {
                        throw new FormatException(
                            $"{path}.properties.fit: expected 'fill', 'contain', 'cover', or 'none'.");
                    }

                    if (properties.TryGetValue("position", out var imagePositionValue))
                    {
                        var imagePositionPath = path + ".properties.position";
                        var imagePosition = RequireObject(imagePositionValue, imagePositionPath);
                        EnsureOnlyKeys(imagePosition, imagePositionPath, "x", "y");
                        node.imagePositionX = NormalizeImagePosition(
                            RequireValue(imagePosition, "x", imagePositionPath),
                            imagePositionPath + ".x");
                        node.imagePositionY = NormalizeImagePosition(
                            RequireValue(imagePosition, "y", imagePositionPath),
                            imagePositionPath + ".y");
                    }

                    break;
                case "button":
                    if (properties != null)
                    {
                        EnsureOnlyKeys(properties, path + ".properties", "disabled");
                        node.interactable = !GetBoolean(
                            properties,
                            "disabled",
                            false,
                            path + ".properties.disabled");
                    }

                    if (!mappedStyle.HasWidth)
                    {
                        node.style.size[0] = 240f;
                    }

                    if (!mappedStyle.HasHeight)
                    {
                        node.style.size[1] = 64f;
                    }

                    if (!mappedStyle.HasBackground)
                    {
                        node.style.backgroundColor = "#808080FF";
                    }

                    break;
                case "toggle":
                    if (properties != null)
                    {
                        EnsureOnlyKeys(properties, path + ".properties", "checked", "disabled");
                        node.toggleValue = GetBoolean(
                            properties,
                            "checked",
                            false,
                            path + ".properties.checked");
                        node.interactable = !GetBoolean(
                            properties,
                            "disabled",
                            false,
                            path + ".properties.disabled");
                    }

                    if (!mappedStyle.HasWidth)
                    {
                        node.style.size[0] = 88f;
                    }

                    if (!mappedStyle.HasHeight)
                    {
                        node.style.size[1] = 48f;
                    }

                    if (!mappedStyle.HasBackground)
                    {
                        node.style.backgroundColor = "#4A4A58FF";
                    }

                    break;
                case "slider":
                    node.sliderMin = 0f;
                    node.sliderMax = 1f;
                    node.sliderValue = 0f;
                    node.sliderStep = 0f;
                    if (properties != null)
                    {
                        EnsureOnlyKeys(properties, path + ".properties", "value", "min", "max", "step", "disabled");
                        if (properties.TryGetValue("min", out var minValue))
                        {
                            node.sliderMin = RequireFloat(minValue, path + ".properties.min");
                        }

                        if (properties.TryGetValue("max", out var maxValue))
                        {
                            node.sliderMax = RequireFloat(maxValue, path + ".properties.max");
                        }

                        if (properties.TryGetValue("value", out var valueValue))
                        {
                            node.sliderValue = RequireFloat(valueValue, path + ".properties.value");
                        }

                        if (properties.TryGetValue("step", out var stepValue))
                        {
                            node.sliderStep = RequireFloat(stepValue, path + ".properties.step");
                        }

                        node.interactable = !GetBoolean(
                            properties,
                            "disabled",
                            false,
                            path + ".properties.disabled");
                    }

                    if (node.sliderMax <= node.sliderMin)
                    {
                        throw new FormatException($"{path}.properties.max: slider max must be greater than min.");
                    }

                    if (node.sliderValue < node.sliderMin || node.sliderValue > node.sliderMax)
                    {
                        throw new FormatException($"{path}.properties.value: slider value must be between min and max.");
                    }

                    if (node.sliderStep < 0f)
                    {
                        throw new FormatException($"{path}.properties.step: slider step cannot be negative.");
                    }

                    if (node.sliderStep > 0f
                        && (Math.Abs(node.sliderStep - 1f) > 0.0001f
                            || Math.Abs(node.sliderMin - Math.Round(node.sliderMin)) > 0.0001f
                            || Math.Abs(node.sliderMax - Math.Round(node.sliderMax)) > 0.0001f))
                    {
                        warnings.Add(new UdomParseWarning(
                            path + ".properties.step",
                            $"Canonical slider step {node.sliderStep.ToString(CultureInfo.InvariantCulture)} is not enforced by the Unity Slider fallback; only step 1 with integer bounds maps to wholeNumbers."));
                    }

                    if (!mappedStyle.HasWidth)
                    {
                        node.style.size[0] = 320f;
                    }

                    if (!mappedStyle.HasHeight)
                    {
                        node.style.size[1] = 40f;
                    }

                    if (!mappedStyle.HasBackground)
                    {
                        node.style.backgroundColor = "#4A4A58FF";
                    }

                    break;
                case "text-input":
                    node.textInputValue = string.Empty;
                    node.textInputPlaceholder = string.Empty;
                    if (properties != null)
                    {
                        EnsureOnlyKeys(
                            properties,
                            path + ".properties",
                            "value",
                            "placeholder",
                            "multiline",
                            "readOnly",
                            "disabled");
                        if (properties.TryGetValue("value", out var inputValue))
                        {
                            node.textInputValue = RequireText(inputValue, path + ".properties.value");
                        }

                        if (properties.TryGetValue("placeholder", out var placeholderValue))
                        {
                            node.textInputPlaceholder = RequireText(
                                placeholderValue,
                                path + ".properties.placeholder");
                        }

                        node.textInputMultiline = GetBoolean(
                            properties,
                            "multiline",
                            false,
                            path + ".properties.multiline");
                        node.textInputReadOnly = GetBoolean(
                            properties,
                            "readOnly",
                            false,
                            path + ".properties.readOnly");
                        node.interactable = !GetBoolean(
                            properties,
                            "disabled",
                            false,
                            path + ".properties.disabled");
                    }

                    if (!mappedStyle.HasWidth)
                    {
                        node.style.size[0] = 360f;
                    }

                    if (!mappedStyle.HasHeight)
                    {
                        node.style.size[1] = node.textInputMultiline ? 128f : 56f;
                    }

                    if (!mappedStyle.HasBackground)
                    {
                        node.style.backgroundColor = "#252733FF";
                    }

                    break;
                case "scroll":
                    node.scrollAxisExplicit = true;
                    node.scrollHorizontal = false;
                    node.scrollVertical = true;
                    node.scrollInitialOffset = new[] { 0f, 0f };
                    if (properties != null)
                    {
                        EnsureOnlyKeys(properties, path + ".properties", "axis", "initialOffset");
                        var axis = GetString(properties, "axis") ?? "vertical";
                        switch (axis)
                        {
                            case "vertical":
                                break;
                            case "horizontal":
                                node.scrollHorizontal = true;
                                node.scrollVertical = false;
                                break;
                            case "both":
                                node.scrollHorizontal = true;
                                node.scrollVertical = true;
                                break;
                            default:
                                throw new FormatException(
                                    $"{path}.properties.axis: expected 'vertical', 'horizontal', or 'both'.");
                        }

                        if (properties.TryGetValue("initialOffset", out var offsetValue))
                        {
                            var offsetPath = path + ".properties.initialOffset";
                            var offset = RequireObject(offsetValue, offsetPath);
                            EnsureOnlyKeys(offset, offsetPath, "x", "y");
                            node.scrollInitialOffset = new[]
                            {
                                RequireFloat(RequireValue(offset, "x", offsetPath), offsetPath + ".x"),
                                RequireFloat(RequireValue(offset, "y", offsetPath), offsetPath + ".y")
                            };
                        }
                    }

                    break;
                case "embed":
                    if (properties == null)
                    {
                        throw new FormatException($"{path}.properties: canonical embed properties are required.");
                    }

                    EnsureOnlyKeys(properties, path + ".properties", "object", "fallbackLabel");
                    node.embed = new UdomEmbed
                    {
                        targetSlot = RequireString(properties, "object", path + ".properties.object"),
                        fallbackLabel = properties.TryGetValue("fallbackLabel", out var fallbackLabel)
                            ? RequireText(fallbackLabel, path + ".properties.fallbackLabel")
                            : string.Empty
                    };
                    break;
            }
        }

        private static void ValidateBindingMap(
            string canonicalType,
            string elementName,
            Dictionary<string, object> value,
            string path,
            List<UdomParseWarning> warnings)
        {
            if (!value.TryGetValue("bind", out var bindValue))
            {
                return;
            }

            var bindings = RequireObject(bindValue, path + ".bind");
            if (!string.Equals(canonicalType, "element", StringComparison.Ordinal))
            {
                if (bindings.Count > 0)
                {
                    throw new FormatException($"{path}.bind: canonical data bindings are not supported for this node yet.");
                }

                return;
            }

            string bindingKey;
            if (string.Equals(elementName, "toggle", StringComparison.Ordinal))
            {
                bindingKey = "checked";
            }
            else if (string.Equals(elementName, "slider", StringComparison.Ordinal)
                     || string.Equals(elementName, "text-input", StringComparison.Ordinal))
            {
                bindingKey = "value";
            }
            else if (string.Equals(elementName, "scroll", StringComparison.Ordinal))
            {
                bindingKey = "offset";
            }
            else
            {
                bindingKey = null;
            }
            if (bindingKey == null)
            {
                if (bindings.Count > 0)
                {
                    throw new FormatException($"{path}.bind: canonical data bindings are not supported for this element yet.");
                }

                return;
            }

            EnsureOnlyKeys(bindings, path + ".bind", bindingKey);
            if (bindings.TryGetValue(bindingKey, out var bindingValue))
            {
                var binding = RequireString(bindingValue, path + ".bind." + bindingKey);
                warnings.Add(new UdomParseWarning(
                    path + ".bind." + bindingKey,
                    $"Symbolic binding '{binding}' is not connected until a Unity/Udon binding manifest is supplied."));
            }
        }

        private static void ValidateEventMap(
            string elementName,
            Dictionary<string, object> value,
            string path,
            List<UdomParseWarning> warnings)
        {
            if (!value.TryGetValue("on", out var onValue))
            {
                return;
            }

            var events = RequireObject(onValue, path + ".on");
            if (string.Equals(elementName, "button", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "activate", "focus", "blur");
                AddEventWarning(events, "activate", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }

            if (string.Equals(elementName, "toggle", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "change", "focus", "blur");
                AddEventWarning(events, "change", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }

            if (string.Equals(elementName, "slider", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "change", "focus", "blur");
                AddEventWarning(events, "change", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            if (string.Equals(elementName, "text-input", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "change", "submit", "focus", "blur");
                AddEventWarning(events, "change", path, warnings);
                AddEventWarning(events, "submit", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            if (string.Equals(elementName, "scroll", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "scroll", "focus", "blur");
                AddEventWarning(events, "scroll", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            throw new FormatException($"{path}.on: canonical events are not supported for this element yet.");
        }

        private static void AddEventWarning(
            Dictionary<string, object> events,
            string eventName,
            string path,
            List<UdomParseWarning> warnings)
        {
            if (!events.TryGetValue(eventName, out var eventValue))
            {
                return;
            }

            var eventId = RequireString(eventValue, path + ".on." + eventName);
            warnings.Add(new UdomParseWarning(
                path + ".on." + eventName,
                $"Symbolic event '{eventId}' is not executed until a safe Unity/Udon binding manifest is supplied."));
        }

        private static StyleMapping MapStyle(
            Dictionary<string, object> value,
            string path,
            float[] parentSize,
            bool fillParentByDefault,
            Dictionary<string, ResourceInfo> resources,
            List<UdomParseWarning> warnings)
        {
            var style = new UdomStyle();
            style.autoSize = new[] { true, true };
            style.fontSize = 16f;
            style.textColor = "#000000FF";
            style.alignment = "TopLeft";
            style.textOverflow = "Clip";
            style.flexShrink = 1f;
            if (fillParentByDefault && parentSize != null && parentSize.Length == 2)
            {
                style.size = new[] { parentSize[0], parentSize[1] };
            }

            var result = new StyleMapping { Style = style };
            if (value == null)
            {
                return result;
            }

            EnsureOnlyKeys(value, path, "layout", "paint", "text", "transform");
            if (value.TryGetValue("layout", out var layoutValue))
            {
                MapLayout(RequireObject(layoutValue, path + ".layout"), path + ".layout", parentSize, result);
            }

            if (value.TryGetValue("paint", out var paintValue))
            {
                MapPaint(
                    RequireObject(paintValue, path + ".paint"),
                    path + ".paint",
                    result,
                    warnings);
            }

            if (value.TryGetValue("text", out var textValue))
            {
                MapTextStyle(
                    RequireObject(textValue, path + ".text"),
                    path + ".text",
                    style,
                    resources,
                    warnings);
            }

            if (value.TryGetValue("transform", out var transformValue))
            {
                MapTransform(
                    RequireObject(transformValue, path + ".transform"),
                    path + ".transform",
                    style);
            }

            return result;
        }

        private static void MapTransform(
            Dictionary<string, object> value,
            string path,
            UdomStyle style)
        {
            EnsureOnlyKeys(value, path, "origin", "operations");
            if (value.TryGetValue("origin", out var originValue))
            {
                var origin = RequireObject(originValue, path + ".origin");
                EnsureOnlyKeys(origin, path + ".origin", "x", "y");
                if (!origin.ContainsKey("x") || !origin.ContainsKey("y"))
                {
                    throw new FormatException($"{path}.origin: both x and y are required.");
                }

                style.transformOrigin = new float[2];
                style.transformOriginIsPercent = new bool[2];
                MapTransformLength(
                    origin,
                    "x",
                    path + ".origin.x",
                    50f,
                    defaultIsPercent: true,
                    out style.transformOrigin[0],
                    out style.transformOriginIsPercent[0]);
                MapTransformLength(
                    origin,
                    "y",
                    path + ".origin.y",
                    50f,
                    defaultIsPercent: true,
                    out style.transformOrigin[1],
                    out style.transformOriginIsPercent[1]);
            }

            if (!value.TryGetValue("operations", out var operationsValue))
            {
                return;
            }

            var operations = RequireArray(operationsValue, path + ".operations");
            var types = new string[operations.Count];
            var values = new float[operations.Count * 2];
            var percentages = new bool[operations.Count * 2];
            for (var index = 0; index < operations.Count; index++)
            {
                var operationPath = $"{path}.operations[{index}]";
                var operation = RequireObject(operations[index], operationPath);
                var type = RequireString(operation, "type", operationPath + ".type");
                types[index] = type;
                var valueIndex = index * 2;
                switch (type)
                {
                    case "translate":
                        EnsureOnlyKeys(operation, operationPath, "type", "x", "y");
                        MapTransformLength(
                            operation,
                            "x",
                            operationPath + ".x",
                            0f,
                            defaultIsPercent: false,
                            out values[valueIndex],
                            out percentages[valueIndex]);
                        MapTransformLength(
                            operation,
                            "y",
                            operationPath + ".y",
                            0f,
                            defaultIsPercent: false,
                            out values[valueIndex + 1],
                            out percentages[valueIndex + 1]);
                        break;
                    case "rotate":
                        EnsureOnlyKeys(operation, operationPath, "type", "degrees");
                        values[valueIndex] = RequireFloat(
                            RequireValue(operation, "degrees", operationPath),
                            operationPath + ".degrees");
                        ValidateFiniteTransformValue(values[valueIndex], operationPath + ".degrees");
                        break;
                    case "scale":
                        EnsureOnlyKeys(operation, operationPath, "type", "x", "y");
                        values[valueIndex] = GetOptionalFiniteTransformValue(
                            operation,
                            "x",
                            1f,
                            operationPath + ".x");
                        values[valueIndex + 1] = GetOptionalFiniteTransformValue(
                            operation,
                            "y",
                            1f,
                            operationPath + ".y");
                        break;
                    default:
                        throw new FormatException(
                            $"{operationPath}.type: unsupported canonical transform operation '{type}'.");
                }
            }

            style.transformOperationTypes = types;
            style.transformOperationValues = values;
            style.transformOperationValuesArePercent = percentages;
        }

        private static void MapTransformLength(
            Dictionary<string, object> value,
            string key,
            string path,
            float defaultValue,
            bool defaultIsPercent,
            out float result,
            out bool isPercent)
        {
            result = defaultValue;
            isPercent = defaultIsPercent;
            if (!value.TryGetValue(key, out var raw)
                || raw is string autoText
                && string.Equals(autoText, "auto", StringComparison.Ordinal))
            {
                return;
            }

            if (raw is string percentageText
                && percentageText.EndsWith("%", StringComparison.Ordinal))
            {
                if (!TryResolveLength(raw, 100f, path, out result))
                {
                    return;
                }

                isPercent = true;
            }
            else
            {
                if (!TryResolveLength(raw, 100f, path, out result))
                {
                    return;
                }

                isPercent = false;
            }

            ValidateFiniteTransformValue(result, path);
        }

        private static float GetOptionalFiniteTransformValue(
            Dictionary<string, object> value,
            string key,
            float defaultValue,
            string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return defaultValue;
            }

            var result = RequireFloat(raw, path);
            ValidateFiniteTransformValue(result, path);
            return result;
        }

        private static void ValidateFiniteTransformValue(float value, string path)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new FormatException($"{path}: transform value must be finite.");
            }
        }

        private static void MapLayout(
            Dictionary<string, object> value,
            string path,
            float[] parentSize,
            StyleMapping result)
        {
            EnsureOnlyKeys(
                value,
                path,
                "mode",
                "position",
                "x",
                "y",
                "width",
                "height",
                "minWidth",
                "minHeight",
                "maxWidth",
                "maxHeight",
                "margin",
                "padding",
                "overflowX",
                "overflowY",
                "zIndex",
                "aspectRatio",
                "flex",
                "flexItem");

            RejectPresent(value, path, "zIndex", "z-index is not supported yet");
            RequireDefaultString(value, "overflowX", "visible", path + ".overflowX");
            RequireDefaultString(value, "overflowY", "visible", path + ".overflowY");

            var positionMode = GetString(value, "position") ?? "flow";
            if (string.Equals(positionMode, "absolute", StringComparison.Ordinal))
            {
                result.Style.positionAbsolute = true;
            }
            else if (!string.Equals(positionMode, "flow", StringComparison.Ordinal))
            {
                throw new FormatException($"{path}.position: unsupported canonical position '{positionMode}'.");
            }

            var parentWidth = parentSize != null && parentSize.Length == 2 ? parentSize[0] : 100f;
            var parentHeight = parentSize != null && parentSize.Length == 2 ? parentSize[1] : 100f;
            if (value.TryGetValue("x", out var xValue)
                && TryResolveLength(xValue, parentWidth, path + ".x", out var x))
            {
                result.Style.position[0] = x;
            }

            if (value.TryGetValue("y", out var yValue)
                && TryResolveLength(yValue, parentHeight, path + ".y", out var y))
            {
                result.Style.position[1] = y;
            }

            if (value.TryGetValue("width", out var widthValue)
                && TryResolveLength(widthValue, parentWidth, path + ".width", out var width))
            {
                if (width < 0f)
                {
                    throw new FormatException($"{path}.width: resolved width must be non-negative.");
                }

                result.Style.size[0] = width;
                result.HasWidth = true;
                result.Style.autoSize[0] = false;
            }

            if (value.TryGetValue("height", out var heightValue)
                && TryResolveLength(heightValue, parentHeight, path + ".height", out var height))
            {
                if (height < 0f)
                {
                    throw new FormatException($"{path}.height: resolved height must be non-negative.");
                }

                result.Style.size[1] = height;
                result.HasHeight = true;
                result.Style.autoSize[1] = false;
            }

            result.Style.minSize[0] = MapConstraintLength(
                value,
                "minWidth",
                parentWidth,
                path + ".minWidth",
                0f);
            result.Style.minSize[1] = MapConstraintLength(
                value,
                "minHeight",
                parentHeight,
                path + ".minHeight",
                0f);
            result.Style.maxSize[0] = MapConstraintLength(
                value,
                "maxWidth",
                parentWidth,
                path + ".maxWidth",
                -1f);
            result.Style.maxSize[1] = MapConstraintLength(
                value,
                "maxHeight",
                parentHeight,
                path + ".maxHeight",
                -1f);

            if (value.TryGetValue("aspectRatio", out var aspectRatioValue))
            {
                var aspectRatio = RequireFloat(aspectRatioValue, path + ".aspectRatio");
                if (aspectRatio <= 0f || float.IsNaN(aspectRatio) || float.IsInfinity(aspectRatio))
                {
                    throw new FormatException($"{path}.aspectRatio: aspect ratio must be finite and greater than zero.");
                }

                result.Style.aspectRatio = aspectRatio;
                if (result.HasWidth && result.HasHeight)
                {
                    result.Style.aspectRatioMode = "None";
                }
                else if (result.HasHeight)
                {
                    result.Style.aspectRatioMode = "HeightControlsWidth";
                }
                else
                {
                    result.Style.aspectRatioMode = "WidthControlsHeight";
                }
            }

            if (value.TryGetValue("padding", out var paddingValue))
            {
                result.Style.padding = MapEdges(
                    RequireObject(paddingValue, path + ".padding"),
                    path + ".padding",
                    parentWidth,
                    parentHeight,
                    false);
            }

            if (value.TryGetValue("margin", out var marginValue))
            {
                result.Style.margin = MapEdges(
                    RequireObject(marginValue, path + ".margin"),
                    path + ".margin",
                    parentWidth,
                    parentHeight,
                    true);
            }

            var mode = GetString(value, "mode") ?? "absolute";
            if (string.Equals(mode, "flex", StringComparison.Ordinal))
            {
                MapFlex(value, path, result);
            }
            else if (string.Equals(mode, "absolute", StringComparison.Ordinal))
            {
                result.Style.layout = "None";
                RejectNonEmptyObject(value, "flex", path + ".flex", "flex settings require layout mode 'flex'");
            }
            else if (string.Equals(mode, "none", StringComparison.Ordinal))
            {
                result.Style.displayNone = true;
                result.Style.layout = "None";
                RejectNonEmptyObject(value, "flex", path + ".flex", "flex settings require layout mode 'flex'");
            }
            else
            {
                throw new FormatException($"{path}.mode: unsupported canonical layout mode '{mode}'.");
            }

            if (value.TryGetValue("flexItem", out var flexItemValue))
            {
                var flexItem = RequireObject(flexItemValue, path + ".flexItem");
                EnsureOnlyKeys(flexItem, path + ".flexItem", "grow", "shrink", "basis", "alignSelf", "order");
                RejectPresent(flexItem, path + ".flexItem", "alignSelf", "flex alignSelf is not supported yet");
                if (flexItem.TryGetValue("order", out var orderValue))
                {
                    result.Style.flexOrder = RequireInteger(orderValue, path + ".flexItem.order");
                }

                if (flexItem.TryGetValue("shrink", out var shrinkValue))
                {
                    var shrink = RequireFloat(shrinkValue, path + ".flexItem.shrink");
                    if (shrink < 0f || float.IsNaN(shrink) || float.IsInfinity(shrink))
                    {
                        throw new FormatException(
                            $"{path}.flexItem.shrink: flex shrink must be finite and non-negative.");
                    }

                    result.Style.flexShrink = shrink;
                }

                if (flexItem.TryGetValue("basis", out var basisValue))
                {
                    MapFlexBasis(
                        basisValue,
                        path + ".flexItem.basis",
                        result.Style);
                }

                if (flexItem.TryGetValue("grow", out var growValue))
                {
                    var grow = RequireFloat(growValue, path + ".flexItem.grow");
                    if (grow < 0f || float.IsNaN(grow) || float.IsInfinity(grow))
                    {
                        throw new FormatException(
                            $"{path}.flexItem.grow: flex grow must be finite and non-negative.");
                    }

                    result.Style.flexibleWidth = grow;
                    result.Style.flexibleHeight = grow;
                }
            }
        }

        private static void MapFlexBasis(object value, string path, UdomStyle style)
        {
            if (value is string text && string.Equals(text, "auto", StringComparison.Ordinal))
            {
                style.flexBasis = -1f;
                style.flexBasisIsPercent = false;
                return;
            }

            var isPercent = value is string percentageText
                            && percentageText.EndsWith("%", StringComparison.Ordinal);
            if (!TryResolveLength(value, 100f, path, out var basis)
                || basis < 0f
                || float.IsNaN(basis)
                || float.IsInfinity(basis))
            {
                throw new FormatException($"{path}: flex basis must be finite and non-negative, or 'auto'.");
            }

            style.flexBasis = basis;
            style.flexBasisIsPercent = isPercent;
        }

        private static float MapConstraintLength(
            Dictionary<string, object> value,
            string key,
            float reference,
            string path,
            float defaultValue)
        {
            if (!value.TryGetValue(key, out var raw)
                || !TryResolveLength(raw, reference, path, out var resolved))
            {
                return defaultValue;
            }

            if (resolved < 0f || float.IsNaN(resolved) || float.IsInfinity(resolved))
            {
                throw new FormatException($"{path}: size constraint must be finite and non-negative.");
            }

            return resolved;
        }

        private static void FinalizeCanonicalBoxSize(UdomStyle style)
        {
            if (style == null || style.size == null || style.size.Length < 2)
            {
                return;
            }

            var widthControlsHeight = string.Equals(
                style.aspectRatioMode,
                "WidthControlsHeight",
                StringComparison.Ordinal);
            var heightControlsWidth = string.Equals(
                style.aspectRatioMode,
                "HeightControlsWidth",
                StringComparison.Ordinal);
            var resolved = widthControlsHeight || heightControlsWidth
                ? ResolveAspectSize(
                    style,
                    style.size[0],
                    style.size[1],
                    widthControlsHeight)
                : new[]
                {
                    ClampSizeAxis(style, 0, style.size[0]),
                    ClampSizeAxis(style, 1, style.size[1])
                };
            style.size = resolved;
        }

        private static float[] ResolveAspectSize(
            UdomStyle style,
            float width,
            float height,
            bool widthControlsHeight)
        {
            var ratio = style.aspectRatio;
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                return new[]
                {
                    ClampSizeAxis(style, 0, width),
                    ClampSizeAxis(style, 1, height)
                };
            }

            var minimumWidth = Math.Max(GetMinimumSize(style, 0), GetMinimumSize(style, 1) * ratio);
            var maximumWidth = Math.Min(
                GetMaximumSize(style, 0),
                MultiplyFinite(GetMaximumSize(style, 1), ratio));
            if (maximumWidth < minimumWidth)
            {
                maximumWidth = minimumWidth;
            }

            var candidateWidth = widthControlsHeight ? width : height * ratio;
            var resolvedWidth = Clamp(candidateWidth, minimumWidth, maximumWidth);
            return new[] { resolvedWidth, resolvedWidth / ratio };
        }

        private static void ResolveFlexLayoutTree(UdomNode node)
        {
            if (node == null || (node.style != null && node.style.displayNone))
            {
                return;
            }

            ResolveFlexChildren(node);
            var children = node.children ?? Array.Empty<UdomNode>();
            for (var index = 0; index < children.Length; index++)
            {
                ResolveFlexLayoutTree(children[index]);
            }
        }

        private static void ResolveFlexChildren(UdomNode node)
        {
            var style = node.style ?? new UdomStyle();
            var isVertical = string.Equals(style.layout, "Vertical", StringComparison.OrdinalIgnoreCase);
            var isHorizontal = string.Equals(style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase);
            var children = node.children ?? Array.Empty<UdomNode>();
            if ((!isVertical && !isHorizontal) || children.Length == 0)
            {
                return;
            }

            var flowChildCount = 0;
            var requiresSizeResolution = false;
            for (var index = 0; index < children.Length; index++)
            {
                var childStyle = children[index].style;
                if (childStyle != null && (childStyle.displayNone || childStyle.positionAbsolute))
                {
                    continue;
                }

                flowChildCount++;
                if (HasLayoutConstraint(childStyle) || HasCanonicalFlexSizing(childStyle))
                {
                    requiresSizeResolution = true;
                }
            }

            if (flowChildCount == 0 || !requiresSizeResolution)
            {
                return;
            }

            var mainAxis = isVertical ? 1 : 0;
            var crossAxis = 1 - mainAxis;
            var padding = style.padding != null && style.padding.Length >= 4
                ? style.padding
                : new[] { 0f, 0f, 0f, 0f };
            var contentWidth = Math.Max(0f, style.size[0] - padding[0] - padding[2]);
            var contentHeight = Math.Max(0f, style.size[1] - padding[1] - padding[3]);
            var mainAvailable = mainAxis == 0 ? contentWidth : contentHeight;
            var crossAvailable = crossAxis == 0 ? contentWidth : contentHeight;
            var stretchCross = crossAxis == 0
                ? style.stretchChildrenWidth
                : style.stretchChildrenHeight;
            var allowsCrossOverflow = string.Equals(node.type, "ScrollView", StringComparison.OrdinalIgnoreCase)
                                      && (crossAxis == 0 && node.scrollHorizontal
                                          || crossAxis == 1 && node.scrollVertical);
            var stretchCrossForSizing = stretchCross && !allowsCrossOverflow;
            var resolveCrossAxis = stretchCrossForSizing
                                   && RequiresCrossAxisResolution(children, crossAxis);
            var allowsMainOverflow = string.Equals(node.type, "ScrollView", StringComparison.OrdinalIgnoreCase)
                                     && (mainAxis == 0 && node.scrollHorizontal
                                         || mainAxis == 1 && node.scrollVertical);
            var allocatedOuterSizes = new float[children.Length];
            var minimumOuterSizes = new float[children.Length];
            var maximumOuterSizes = new float[children.Length];
            var growWeights = new double[children.Length];
            var scaledShrinkWeights = new double[children.Length];
            var totalOuterSize = Math.Max(0, flowChildCount - 1) * style.spacing;

            for (var index = 0; index < children.Length; index++)
            {
                var childStyle = children[index].style ?? new UdomStyle();
                if (childStyle.displayNone || childStyle.positionAbsolute)
                {
                    continue;
                }

                var margins = GetAxisMargins(childStyle, mainAxis);
                var preserveAspect = HasAspectRatio(childStyle)
                                     && IsAutoSize(childStyle, crossAxis)
                                     && !stretchCrossForSizing;
                var minimum = GetEffectiveMinimumSize(childStyle, mainAxis, preserveAspect);
                var maximum = GetEffectiveMaximumSize(childStyle, mainAxis, preserveAspect);
                var fallbackSize = childStyle.size != null && childStyle.size.Length >= 2
                    ? childStyle.size[mainAxis]
                    : 100f;
                var baseSize = ResolveFlexBaseSize(childStyle, mainAvailable, fallbackSize);
                var contentSize = Clamp(baseSize, minimum, Math.Max(minimum, maximum));
                allocatedOuterSizes[index] = contentSize + margins;
                minimumOuterSizes[index] = minimum + margins;
                maximumOuterSizes[index] = float.IsPositiveInfinity(maximum)
                    ? float.PositiveInfinity
                    : maximum + margins;
                growWeights[index] = mainAxis == 0
                    ? Math.Max(0f, childStyle.flexibleWidth)
                    : Math.Max(0f, childStyle.flexibleHeight);
                scaledShrinkWeights[index] = childStyle.flexShrink >= 0f
                    ? (double)childStyle.flexShrink * contentSize
                    : 0d;
                totalOuterSize += allocatedOuterSizes[index];
            }

            var remaining = mainAvailable - totalOuterSize;
            while (remaining > 0.0001f)
            {
                var totalWeight = 0d;
                for (var index = 0; index < children.Length; index++)
                {
                    if (growWeights[index] > 0f
                        && allocatedOuterSizes[index] + 0.0001f < maximumOuterSizes[index])
                    {
                        totalWeight += growWeights[index];
                    }
                }

                if (totalWeight <= 0f)
                {
                    break;
                }

                var distributed = 0f;
                for (var index = 0; index < children.Length; index++)
                {
                    if (growWeights[index] <= 0f
                        || allocatedOuterSizes[index] + 0.0001f >= maximumOuterSizes[index])
                    {
                        continue;
                    }

                    var share = (float)(remaining * growWeights[index] / totalWeight);
                    var room = maximumOuterSizes[index] - allocatedOuterSizes[index];
                    var addition = Math.Min(share, room);
                    allocatedOuterSizes[index] += addition;
                    distributed += addition;
                }

                if (distributed <= 0.0001f)
                {
                    break;
                }

                remaining -= distributed;
            }

            var deficit = allowsMainOverflow ? 0f : -remaining;
            while (deficit > 0.0001f)
            {
                var totalWeight = 0d;
                for (var index = 0; index < children.Length; index++)
                {
                    if (scaledShrinkWeights[index] > 0f
                        && allocatedOuterSizes[index] > minimumOuterSizes[index] + 0.0001f)
                    {
                        totalWeight += scaledShrinkWeights[index];
                    }
                }

                if (totalWeight <= 0f)
                {
                    break;
                }

                var distributed = 0f;
                for (var index = 0; index < children.Length; index++)
                {
                    if (scaledShrinkWeights[index] <= 0f
                        || allocatedOuterSizes[index] <= minimumOuterSizes[index] + 0.0001f)
                    {
                        continue;
                    }

                    var share = (float)(deficit * scaledShrinkWeights[index] / totalWeight);
                    var room = allocatedOuterSizes[index] - minimumOuterSizes[index];
                    var reduction = Math.Min(share, room);
                    allocatedOuterSizes[index] -= reduction;
                    distributed += reduction;
                }

                if (distributed <= 0.0001f)
                {
                    break;
                }

                deficit -= distributed;
            }

            ApplyJustifiedSpacing(style, flowChildCount, remaining);

            for (var index = 0; index < children.Length; index++)
            {
                var childStyle = children[index].style ?? new UdomStyle();
                if (childStyle.displayNone || childStyle.positionAbsolute)
                {
                    continue;
                }

                var size = childStyle.size != null && childStyle.size.Length >= 2
                    ? new[] { childStyle.size[0], childStyle.size[1] }
                    : new[] { 100f, 100f };
                size[mainAxis] = Math.Max(
                    0f,
                    allocatedOuterSizes[index] - GetAxisMargins(childStyle, mainAxis));

                if (stretchCrossForSizing)
                {
                    size[crossAxis] = ClampSizeAxis(
                        childStyle,
                        crossAxis,
                        Math.Max(0f, crossAvailable - GetAxisMargins(childStyle, crossAxis)));
                }
                else if (!stretchCrossForSizing
                         && HasAspectRatio(childStyle)
                         && IsAutoSize(childStyle, crossAxis))
                {
                    size = ResolveAspectSize(
                        childStyle,
                        size[0],
                        size[1],
                        mainAxis == 0);
                }
                else
                {
                    size[0] = ClampSizeAxis(childStyle, 0, size[0]);
                    size[1] = ClampSizeAxis(childStyle, 1, size[1]);
                }

                childStyle.size = size;
                if (mainAxis == 0)
                {
                    childStyle.flexibleWidth = 0f;
                }
                else
                {
                    childStyle.flexibleHeight = 0f;
                }
            }

            if (resolveCrossAxis)
            {
                if (crossAxis == 0)
                {
                    style.useResolvedChildrenWidth = true;
                }
                else
                {
                    style.useResolvedChildrenHeight = true;
                }
            }
        }

        private static void ApplyJustifiedSpacing(
            UdomStyle style,
            int flowChildCount,
            float remaining)
        {
            if (remaining <= 0.0001f || flowChildCount <= 1)
            {
                return;
            }

            if (string.Equals(style.justifyContent, "SpaceBetween", StringComparison.Ordinal))
            {
                style.spacing += remaining / (flowChildCount - 1);
            }
            else if (string.Equals(style.justifyContent, "SpaceAround", StringComparison.Ordinal))
            {
                style.spacing += remaining / flowChildCount;
            }
            else if (string.Equals(style.justifyContent, "SpaceEvenly", StringComparison.Ordinal))
            {
                style.spacing += remaining / (flowChildCount + 1);
            }
        }

        private static float GetAxisMargins(UdomStyle style, int axis)
        {
            if (style.margin == null || style.margin.Length < 4)
            {
                return 0f;
            }

            return axis == 0
                ? style.margin[0] + style.margin[2]
                : style.margin[1] + style.margin[3];
        }

        private static float ResolveFlexBaseSize(
            UdomStyle style,
            float mainAvailable,
            float fallback)
        {
            if (style.flexBasis < 0f)
            {
                return fallback;
            }

            return style.flexBasisIsPercent
                ? mainAvailable * style.flexBasis / 100f
                : style.flexBasis;
        }

        private static bool HasCanonicalFlexSizing(UdomStyle style)
        {
            return style != null && (style.flexShrink >= 0f || style.flexBasis >= 0f);
        }

        private static bool HasCrossAxisConstraint(UdomStyle style, int crossAxis)
        {
            return GetMinimumSize(style, crossAxis) > 0f
                   || (style.maxSize != null
                       && style.maxSize.Length >= 2
                       && style.maxSize[crossAxis] >= 0f)
                   || (HasAspectRatio(style)
                       && IsAutoSize(style, crossAxis));
        }

        private static bool RequiresCrossAxisResolution(UdomNode[] children, int crossAxis)
        {
            for (var index = 0; index < children.Length; index++)
            {
                var childStyle = children[index] != null ? children[index].style : null;
                if (childStyle != null
                    && !childStyle.displayNone
                    && !childStyle.positionAbsolute
                    && HasCrossAxisConstraint(childStyle, crossAxis))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAspectRatio(UdomStyle style)
        {
            return style.aspectRatio > 0f
                   && !string.Equals(style.aspectRatioMode, "None", StringComparison.Ordinal);
        }

        private static bool HasLayoutConstraint(UdomStyle style)
        {
            if (style == null)
            {
                return false;
            }

            if (HasAspectRatio(style))
            {
                return true;
            }

            for (var axis = 0; axis < 2; axis++)
            {
                if (GetMinimumSize(style, axis) > 0f
                    || style.maxSize != null
                    && style.maxSize.Length >= 2
                    && style.maxSize[axis] >= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAutoSize(UdomStyle style, int axis)
        {
            return style.autoSize != null
                   && style.autoSize.Length >= 2
                   && style.autoSize[axis];
        }

        private static float GetEffectiveMinimumSize(UdomStyle style, int axis, bool preserveAspect)
        {
            var minimum = GetMinimumSize(style, axis);
            if (!preserveAspect)
            {
                return minimum;
            }

            var crossMinimum = GetMinimumSize(style, 1 - axis);
            return axis == 0
                ? Math.Max(minimum, crossMinimum * style.aspectRatio)
                : Math.Max(minimum, crossMinimum / style.aspectRatio);
        }

        private static float GetEffectiveMaximumSize(UdomStyle style, int axis, bool preserveAspect)
        {
            var maximum = GetMaximumSize(style, axis);
            if (!preserveAspect)
            {
                return Math.Max(GetMinimumSize(style, axis), maximum);
            }

            var crossMaximum = GetMaximumSize(style, 1 - axis);
            var transferredMaximum = axis == 0
                ? MultiplyFinite(crossMaximum, style.aspectRatio)
                : DivideFinite(crossMaximum, style.aspectRatio);
            return Math.Max(GetEffectiveMinimumSize(style, axis, true), Math.Min(maximum, transferredMaximum));
        }

        private static float ClampSizeAxis(UdomStyle style, int axis, float value)
        {
            var minimum = GetMinimumSize(style, axis);
            var maximum = Math.Max(minimum, GetMaximumSize(style, axis));
            return Clamp(value, minimum, maximum);
        }

        private static float GetMinimumSize(UdomStyle style, int axis)
        {
            return style.minSize != null && style.minSize.Length >= 2
                ? Math.Max(0f, style.minSize[axis])
                : 0f;
        }

        private static float GetMaximumSize(UdomStyle style, int axis)
        {
            if (style.maxSize == null || style.maxSize.Length < 2 || style.maxSize[axis] < 0f)
            {
                return float.PositiveInfinity;
            }

            return style.maxSize[axis];
        }

        private static float MultiplyFinite(float value, float multiplier)
        {
            return float.IsPositiveInfinity(value) ? value : value * multiplier;
        }

        private static float DivideFinite(float value, float divisor)
        {
            return float.IsPositiveInfinity(value) ? value : value / divisor;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        private static void MapFlex(Dictionary<string, object> layout, string path, StyleMapping result)
        {
            var flex = layout.TryGetValue("flex", out var flexValue)
                ? RequireObject(flexValue, path + ".flex")
                : new Dictionary<string, object>();
            EnsureOnlyKeys(
                flex,
                path + ".flex",
                "direction",
                "wrap",
                "justify",
                "alignItems",
                "alignContent",
                "rowGap",
                "columnGap");
            RequireDefaultString(flex, "wrap", "nowrap", path + ".flex.wrap");
            RequireDefaultString(flex, "alignContent", "start", path + ".flex.alignContent");

            var direction = GetString(flex, "direction") ?? "row";
            var isVertical = string.Equals(direction, "column", StringComparison.Ordinal)
                             || string.Equals(direction, "column-reverse", StringComparison.Ordinal);
            if (!isVertical
                && !string.Equals(direction, "row", StringComparison.Ordinal)
                && !string.Equals(direction, "row-reverse", StringComparison.Ordinal))
            {
                throw new FormatException($"{path}.flex.direction: unsupported flex direction '{direction}'.");
            }

            var reversesMainAxis = direction.EndsWith("-reverse", StringComparison.Ordinal);
            result.Style.layout = isVertical ? "Vertical" : "Horizontal";
            result.Style.reverseChildren = reversesMainAxis;
            var alignItems = GetString(flex, "alignItems") ?? "stretch";
            result.Style.justifyContent = MapJustifyContent(
                GetString(flex, "justify") ?? "start",
                path + ".flex.justify");
            result.Style.childAlignment = MapChildAlignment(
                isVertical,
                reversesMainAxis,
                alignItems,
                result.Style.justifyContent,
                path + ".flex.alignItems");
            var stretchesCrossAxis = string.Equals(alignItems, "stretch", StringComparison.Ordinal);
            result.Style.stretchChildrenWidth = isVertical && stretchesCrossAxis;
            result.Style.stretchChildrenHeight = !isVertical && stretchesCrossAxis;
            var primaryGapKey = isVertical ? "rowGap" : "columnGap";
            var crossGapKey = isVertical ? "columnGap" : "rowGap";
            if (flex.TryGetValue(primaryGapKey, out var primaryGapValue)
                && TryResolveLength(primaryGapValue, 100f, path + ".flex." + primaryGapKey, out var primaryGap))
            {
                if (primaryGap < 0f)
                {
                    throw new FormatException($"{path}.flex.{primaryGapKey}: gap cannot be negative.");
                }

                result.Style.spacing = primaryGap;
            }

            if (flex.TryGetValue(crossGapKey, out var crossGapValue)
                && TryResolveLength(crossGapValue, 100f, path + ".flex." + crossGapKey, out var crossGap)
                && crossGap != 0f)
            {
                throw new FormatException($"{path}.flex.{crossGapKey}: cross-axis gaps are not supported yet.");
            }
        }

        private static string MapChildAlignment(
            bool isVertical,
            bool reversesMainAxis,
            string alignItems,
            string justifyContent,
            string path)
        {
            if (isVertical)
            {
                var vertical = MapMainAlignment(
                    reversesMainAxis,
                    justifyContent,
                    "Upper",
                    "Middle",
                    "Lower");
                switch (alignItems)
                {
                    case "start":
                    case "stretch":
                        return vertical + "Left";
                    case "center":
                        return vertical + "Center";
                    case "end":
                        return vertical + "Right";
                    default:
                        throw new FormatException($"{path}: unsupported cross-axis alignment '{alignItems}'.");
                }
            }

            var horizontal = MapMainAlignment(
                reversesMainAxis,
                justifyContent,
                "Left",
                "Center",
                "Right");
            switch (alignItems)
            {
                case "start":
                case "stretch":
                    return "Upper" + horizontal;
                case "center":
                    return "Middle" + horizontal;
                case "end":
                    return "Lower" + horizontal;
                default:
                    throw new FormatException($"{path}: unsupported cross-axis alignment '{alignItems}'.");
            }
        }

        private static string MapJustifyContent(string value, string path)
        {
            switch (value)
            {
                case "start":
                    return "Start";
                case "center":
                    return "Center";
                case "end":
                    return "End";
                case "space-between":
                    return "SpaceBetween";
                case "space-around":
                    return "SpaceAround";
                case "space-evenly":
                    return "SpaceEvenly";
                default:
                    throw new FormatException($"{path}: unsupported flex justification '{value}'.");
            }
        }

        private static string MapMainAlignment(
            bool reversesMainAxis,
            string justifyContent,
            string physicalStart,
            string physicalCenter,
            string physicalEnd)
        {
            if (string.Equals(justifyContent, "Center", StringComparison.Ordinal)
                || string.Equals(justifyContent, "SpaceAround", StringComparison.Ordinal)
                || string.Equals(justifyContent, "SpaceEvenly", StringComparison.Ordinal))
            {
                return physicalCenter;
            }

            var usesMainEnd = string.Equals(justifyContent, "End", StringComparison.Ordinal);
            return usesMainEnd != reversesMainAxis ? physicalEnd : physicalStart;
        }

        private static void MapPaint(
            Dictionary<string, object> value,
            string path,
            StyleMapping result,
            List<UdomParseWarning> warnings)
        {
            EnsureOnlyKeys(value, path, "visible", "opacity", "backgrounds", "border", "radius", "shadows");
            result.Style.visible = GetBoolean(value, "visible", true, path + ".visible");

            if (value.TryGetValue("opacity", out var opacityValue))
            {
                result.Style.opacity = RequireFloat(opacityValue, path + ".opacity");
                if (result.Style.opacity < 0f
                    || result.Style.opacity > 1f
                    || float.IsNaN(result.Style.opacity)
                    || float.IsInfinity(result.Style.opacity))
                {
                    throw new FormatException($"{path}.opacity: value must be between zero and one.");
                }
            }

            if (value.TryGetValue("border", out var borderValue))
            {
                MapBorder(
                    RequireObject(borderValue, path + ".border"),
                    path + ".border",
                    result.Style);
            }

            if (value.TryGetValue("shadows", out var shadowsValue))
            {
                MapShadows(
                    RequireArray(shadowsValue, path + ".shadows"),
                    path + ".shadows",
                    result.Style);
            }
            if (value.TryGetValue("radius", out var radiusValue))
            {
                MapCornerRadius(
                    RequireObject(radiusValue, path + ".radius"),
                    path + ".radius",
                    result.Style);
            }

            if (!value.TryGetValue("backgrounds", out var backgroundsValue))
            {
                return;
            }

            var backgrounds = RequireArray(backgroundsValue, path + ".backgrounds");
            if (backgrounds.Count == 0)
            {
                return;
            }

            if (backgrounds.Count != 1)
            {
                throw new FormatException($"{path}.backgrounds: only one background layer is supported yet.");
            }

            var background = RequireObject(backgrounds[0], path + ".backgrounds[0]");
            var type = RequireString(background, "type", path + ".backgrounds[0].type");
            if (string.Equals(type, "color", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(background, path + ".backgrounds[0]", "type", "color");
                result.Style.backgroundColor = RequireString(
                    background,
                    "color",
                    path + ".backgrounds[0].color");
            }
            else if (string.Equals(type, "linear-gradient", StringComparison.Ordinal)
                     || string.Equals(type, "radial-gradient", StringComparison.Ordinal)
                     || string.Equals(type, "conic-gradient", StringComparison.Ordinal))
            {
                result.Style.backgroundColor = MapGradient(
                    background,
                    path + ".backgrounds[0]",
                    type,
                    out var gradientAngle,
                    out var gradientPositions,
                    out var gradientColors,
                    out var gradientCenter,
                    out var gradientCenterIsPercent,
                    out var gradientRadius,
                    out var gradientRadiusIsPercent);
                result.Style.backgroundType = type;
                result.Style.backgroundGradientAngle = gradientAngle;
                result.Style.backgroundGradientPositions = gradientPositions;
                result.Style.backgroundGradientColors = gradientColors;
                result.Style.backgroundGradientCenter = gradientCenter;
                result.Style.backgroundGradientCenterIsPercent = gradientCenterIsPercent;
                result.Style.backgroundGradientRadius = gradientRadius;
                result.Style.backgroundGradientRadiusIsPercent = gradientRadiusIsPercent;
            }
            else
            {
                throw new FormatException($"{path}.backgrounds[0].type: unsupported canonical background '{type}'.");
            }

            result.HasBackground = true;
        }

        private static void MapShadows(
            List<object> values,
            string path,
            UdomStyle style)
        {
            var count = values.Count;
            style.shadowOffsets = new float[count * 2];
            style.shadowBlurs = new float[count];
            style.shadowSpreads = new float[count];
            style.shadowColors = new string[count];
            style.shadowInsets = new bool[count];
            for (var index = 0; index < count; index++)
            {
                var shadowPath = $"{path}[{index}]";
                var shadow = RequireObject(values[index], shadowPath);
                EnsureOnlyKeys(
                    shadow,
                    shadowPath,
                    "offsetX",
                    "offsetY",
                    "blur",
                    "spread",
                    "color",
                    "inset");
                style.shadowOffsets[index * 2] = GetOptionalFiniteShadowNumber(
                    shadow,
                    "offsetX",
                    0f,
                    shadowPath + ".offsetX");
                style.shadowOffsets[index * 2 + 1] = GetOptionalFiniteShadowNumber(
                    shadow,
                    "offsetY",
                    0f,
                    shadowPath + ".offsetY");
                style.shadowBlurs[index] = GetOptionalFiniteShadowNumber(
                    shadow,
                    "blur",
                    0f,
                    shadowPath + ".blur");
                if (style.shadowBlurs[index] < 0f)
                {
                    throw new FormatException($"{shadowPath}.blur: shadow blur must be non-negative.");
                }

                style.shadowSpreads[index] = GetOptionalFiniteShadowNumber(
                    shadow,
                    "spread",
                    0f,
                    shadowPath + ".spread");
                style.shadowColors[index] = shadow.TryGetValue("color", out var colorValue)
                    ? RequireString(colorValue, shadowPath + ".color")
                    : "#00000080";
                style.shadowInsets[index] = GetBoolean(
                    shadow,
                    "inset",
                    false,
                    shadowPath + ".inset");
            }
        }

        private static float GetOptionalFiniteShadowNumber(
            Dictionary<string, object> value,
            string key,
            float defaultValue,
            string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return defaultValue;
            }

            var result = RequireFloat(raw, path);
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new FormatException($"{path}: shadow value must be finite.");
            }

            return result;
        }

        private static void MapBorder(Dictionary<string, object> value, string path, UdomStyle style)
        {
            EnsureOnlyKeys(value, path, "top", "right", "bottom", "left");
            MapBorderEdge(value, "left", 0, path, style);
            MapBorderEdge(value, "top", 1, path, style);
            MapBorderEdge(value, "right", 2, path, style);
            MapBorderEdge(value, "bottom", 3, path, style);
        }

        private static void MapBorderEdge(
            Dictionary<string, object> border,
            string key,
            int index,
            string path,
            UdomStyle style)
        {
            if (!border.TryGetValue(key, out var edgeValue))
            {
                return;
            }

            var edgePath = path + "." + key;
            var edge = RequireObject(edgeValue, edgePath);
            EnsureOnlyKeys(edge, edgePath, "width", "color", "style");
            if (edge.TryGetValue("width", out var widthValue))
            {
                var width = RequireFloat(widthValue, edgePath + ".width");
                if (width < 0f || float.IsNaN(width) || float.IsInfinity(width))
                {
                    throw new FormatException($"{edgePath}.width: border width must be finite and non-negative.");
                }

                style.borderWidth[index] = width;
            }

            if (edge.TryGetValue("color", out var colorValue))
            {
                style.borderColor[index] = RequireString(colorValue, edgePath + ".color");
            }

            if (edge.TryGetValue("style", out var styleValue)
                && !string.Equals(
                    RequireString(styleValue, edgePath + ".style"),
                    "solid",
                    StringComparison.Ordinal))
            {
                throw new FormatException($"{edgePath}.style: only solid borders are supported.");
            }
        }

        private static void MapCornerRadius(
            Dictionary<string, object> value,
            string path,
            UdomStyle style)
        {
            EnsureOnlyKeys(value, path, "topLeft", "topRight", "bottomRight", "bottomLeft");
            var keys = new[] { "topLeft", "topRight", "bottomRight", "bottomLeft" };
            for (var index = 0; index < keys.Length; index++)
            {
                if (!value.TryGetValue(keys[index], out var radiusValue))
                {
                    continue;
                }

                var radiusPath = path + "." + keys[index];
                if (radiusValue is string radiusText
                    && radiusText.EndsWith("%", StringComparison.Ordinal))
                {
                    if (!TryResolveLength(radiusValue, 100f, radiusPath, out var percentage)
                        || percentage < 0f
                        || float.IsNaN(percentage)
                        || float.IsInfinity(percentage))
                    {
                        throw new FormatException($"{radiusPath}: corner radius percentage must be finite and non-negative.");
                    }

                    style.cornerRadius[index] = 0f;
                    style.cornerRadiusPercent[index] = percentage;
                    continue;
                }

                if (!TryResolveLength(radiusValue, 100f, radiusPath, out var radius))
                {
                    continue;
                }

                if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                {
                    throw new FormatException($"{radiusPath}: corner radius must be finite and non-negative.");
                }

                style.cornerRadius[index] = radius;
                style.cornerRadiusPercent[index] = -1f;
            }
        }

        private static bool HasCornerRadius(UdomStyle style)
        {
            if (style == null)
            {
                return false;
            }

            for (var index = 0; index < 4; index++)
            {
                if ((style.cornerRadius != null
                     && index < style.cornerRadius.Length
                     && style.cornerRadius[index] > 0f)
                    || (style.cornerRadiusPercent != null
                        && index < style.cornerRadiusPercent.Length
                        && style.cornerRadiusPercent[index] > 0f))
                {
                    return true;
                }
            }

            return false;
        }

        private static string MapGradient(
            Dictionary<string, object> value,
            string path,
            string type,
            out float angle,
            out float[] positions,
            out string[] colors,
            out float[] center,
            out bool[] centerIsPercent,
            out float[] radius,
            out bool[] radiusIsPercent)
        {
            angle = string.Equals(type, "conic-gradient", StringComparison.Ordinal) ? 0f : 180f;
            center = new[] { 50f, 50f };
            centerIsPercent = new[] { true, true };
            radius = new[] { 50f, 50f };
            radiusIsPercent = new[] { true, true };
            if (string.Equals(type, "linear-gradient", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(value, path, "type", "angle", "stops");
                if (value.TryGetValue("angle", out var angleValue))
                {
                    angle = RequireFloat(angleValue, path + ".angle");
                    if (float.IsNaN(angle) || float.IsInfinity(angle))
                    {
                        throw new FormatException($"{path}.angle: gradient angle must be finite.");
                    }
                }
            }
            else if (string.Equals(type, "radial-gradient", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(value, path, "type", "center", "radius", "stops");
                MapGradientXyLength(
                    value,
                    "center",
                    path + ".center",
                    requireNonNegative: false,
                    out center,
                    out centerIsPercent);
                MapGradientXyLength(
                    value,
                    "radius",
                    path + ".radius",
                    requireNonNegative: true,
                    out radius,
                    out radiusIsPercent);
            }
            else
            {
                EnsureOnlyKeys(value, path, "type", "center", "angle", "stops");
                MapGradientXyLength(
                    value,
                    "center",
                    path + ".center",
                    requireNonNegative: false,
                    out center,
                    out centerIsPercent);
                if (value.TryGetValue("angle", out var angleValue))
                {
                    angle = RequireFloat(angleValue, path + ".angle");
                    if (float.IsNaN(angle) || float.IsInfinity(angle))
                    {
                        throw new FormatException($"{path}.angle: gradient angle must be finite.");
                    }
                }
            }

            var stops = RequireArray(RequireValue(value, "stops", path), path + ".stops");
            if (stops.Count < 2)
            {
                throw new FormatException($"{path}.stops: a gradient requires at least two stops.");
            }

            var previousPosition = -1f;
            string firstColor = null;
            positions = new float[stops.Count];
            colors = new string[stops.Count];
            for (var index = 0; index < stops.Count; index++)
            {
                var stopPath = $"{path}.stops[{index}]";
                var stop = RequireObject(stops[index], stopPath);
                EnsureOnlyKeys(stop, stopPath, "position", "color");
                var position = RequireFloat(RequireValue(stop, "position", stopPath), stopPath + ".position");
                if (position < 0f || position > 1f || position < previousPosition)
                {
                    throw new FormatException($"{stopPath}.position: gradient stops must increase from 0 to 1.");
                }

                previousPosition = position;
                var color = RequireString(stop, "color", stopPath + ".color");
                positions[index] = position;
                colors[index] = color;
                if (index == 0)
                {
                    firstColor = color;
                }
            }

            return firstColor;
        }

        private static bool IsNativeGradient(string type)
        {
            return string.Equals(type, "linear-gradient", StringComparison.Ordinal)
                   || string.Equals(type, "radial-gradient", StringComparison.Ordinal)
                   || string.Equals(type, "conic-gradient", StringComparison.Ordinal);
        }

        private static void MapGradientXyLength(
            Dictionary<string, object> container,
            string key,
            string path,
            bool requireNonNegative,
            out float[] values,
            out bool[] isPercent)
        {
            values = new[] { 50f, 50f };
            isPercent = new[] { true, true };
            if (!container.TryGetValue(key, out var raw))
            {
                return;
            }

            var xy = RequireObject(raw, path);
            EnsureOnlyKeys(xy, path, "x", "y");
            var axisKeys = new[] { "x", "y" };
            for (var index = 0; index < axisKeys.Length; index++)
            {
                var axisKey = axisKeys[index];
                if (!xy.TryGetValue(axisKey, out var axisValue))
                {
                    throw new FormatException($"{path}: both x and y are required.");
                }

                var axisPath = path + "." + axisKey;
                if (axisValue is string text
                    && string.Equals(text, "auto", StringComparison.Ordinal))
                {
                    continue;
                }

                if (axisValue is string percentageText
                    && percentageText.EndsWith("%", StringComparison.Ordinal))
                {
                    if (!TryResolveLength(axisValue, 100f, axisPath, out var percentage)
                        || float.IsNaN(percentage)
                        || float.IsInfinity(percentage)
                        || (requireNonNegative && percentage < 0f))
                    {
                        throw new FormatException(
                            $"{axisPath}: gradient {(requireNonNegative ? "radius" : "center")} must be finite"
                            + (requireNonNegative ? " and non-negative." : "."));
                    }

                    values[index] = percentage;
                    isPercent[index] = true;
                    continue;
                }

                if (!TryResolveLength(axisValue, 100f, axisPath, out var absolute))
                {
                    continue;
                }

                if (float.IsNaN(absolute)
                    || float.IsInfinity(absolute)
                    || (requireNonNegative && absolute < 0f))
                {
                    throw new FormatException(
                        $"{axisPath}: gradient {(requireNonNegative ? "radius" : "center")} must be finite"
                        + (requireNonNegative ? " and non-negative." : "."));
                }

                values[index] = absolute;
                isPercent[index] = false;
            }
        }

        private static void MapTextStyle(
            Dictionary<string, object> value,
            string path,
            UdomStyle style,
            Dictionary<string, ResourceInfo> resources,
            List<UdomParseWarning> warnings)
        {
            EnsureOnlyKeys(
                value,
                path,
                "font",
                "fontSize",
                "fontWeight",
                "fontStyle",
                "color",
                "lineHeight",
                "letterSpacing",
                "align",
                "verticalAlign",
                "wrap",
                "overflow",
                "preserveWhitespace");
            if (value.TryGetValue("lineHeight", out var lineHeightValue))
            {
                if (lineHeightValue is string lineHeightText)
                {
                    if (!string.Equals(lineHeightText, "normal", StringComparison.Ordinal))
                    {
                        throw new FormatException($"{path}.lineHeight: expected a positive number or 'normal'.");
                    }

                    style.lineHeight = -1f;
                }
                else
                {
                    style.lineHeight = RequireFloat(lineHeightValue, path + ".lineHeight");
                    if (style.lineHeight <= 0f
                        || float.IsNaN(style.lineHeight)
                        || float.IsInfinity(style.lineHeight))
                    {
                        throw new FormatException($"{path}.lineHeight: line height must be finite and greater than zero.");
                    }
                }
            }

            if (value.TryGetValue("letterSpacing", out var letterSpacingValue))
            {
                style.letterSpacing = RequireFloat(letterSpacingValue, path + ".letterSpacing");
                if (float.IsNaN(style.letterSpacing) || float.IsInfinity(style.letterSpacing))
                {
                    throw new FormatException($"{path}.letterSpacing: letter spacing must be finite.");
                }
            }

            var wrap = GetString(value, "wrap") ?? "wrap";
            if (string.Equals(wrap, "wrap", StringComparison.Ordinal))
            {
                style.textWrap = true;
            }
            else if (string.Equals(wrap, "nowrap", StringComparison.Ordinal))
            {
                style.textWrap = false;
            }
            else
            {
                throw new FormatException($"{path}.wrap: unsupported text wrap '{wrap}'.");
            }

            var overflow = GetString(value, "overflow") ?? "clip";
            switch (overflow)
            {
                case "visible":
                    style.textOverflow = "Visible";
                    break;
                case "clip":
                    style.textOverflow = "Clip";
                    break;
                case "ellipsis":
                    style.textOverflow = "Ellipsis";
                    break;
                default:
                    throw new FormatException($"{path}.overflow: unsupported text overflow '{overflow}'.");
            }

            style.preserveWhitespace = GetBoolean(
                value,
                "preserveWhitespace",
                false,
                path + ".preserveWhitespace");

            if (value.TryGetValue("font", out var fontValue))
            {
                var fontId = RequireString(fontValue, path + ".font");
                if (!resources.TryGetValue(fontId, out var fontResource))
                {
                    throw new FormatException($"{path}.font: unknown canonical font resource '{fontId}'.");
                }

                if (!string.Equals(fontResource.Type, "font", StringComparison.Ordinal))
                {
                    throw new FormatException($"{path}.font: resource '{fontId}' is not a font.");
                }

                warnings.Add(new UdomParseWarning(
                    path + ".font",
                    $"Canonical font '{fontId}' ({fontResource.Uri}) uses the project default TMP font until font asset generation is implemented."));
            }

            var bold = false;
            if (value.TryGetValue("fontWeight", out var fontWeightValue))
            {
                var fontWeight = RequireFloat(fontWeightValue, path + ".fontWeight");
                if (fontWeight < 1f || fontWeight > 1000f || Math.Abs(fontWeight - Math.Round(fontWeight)) > 0.0001f)
                {
                    throw new FormatException($"{path}.fontWeight: font weight must be an integer from 1 to 1000.");
                }

                bold = fontWeight >= 600f;
            }

            var italic = false;
            if (value.TryGetValue("fontStyle", out var fontStyleValue))
            {
                var canonicalFontStyle = RequireString(fontStyleValue, path + ".fontStyle");
                if (string.Equals(canonicalFontStyle, "italic", StringComparison.Ordinal))
                {
                    italic = true;
                }
                else if (!string.Equals(canonicalFontStyle, "normal", StringComparison.Ordinal))
                {
                    throw new FormatException($"{path}.fontStyle: unsupported font style '{canonicalFontStyle}'.");
                }
            }

            style.fontStyle = bold
                ? (italic ? "BoldItalic" : "Bold")
                : (italic ? "Italic" : "Normal");

            if (value.TryGetValue("fontSize", out var fontSizeValue))
            {
                style.fontSize = RequireFloat(fontSizeValue, path + ".fontSize");
                if (style.fontSize <= 0f)
                {
                    throw new FormatException($"{path}.fontSize: font size must be greater than zero.");
                }
            }

            if (value.TryGetValue("color", out var colorValue))
            {
                style.textColor = RequireString(colorValue, path + ".color");
            }

            var horizontal = GetString(value, "align") ?? "start";
            var vertical = GetString(value, "verticalAlign") ?? "top";
            style.alignment = MapAlignment(horizontal, vertical, path);
        }

        private static string MapAlignment(string horizontal, string vertical, string path)
        {
            string horizontalSuffix;
            switch (horizontal)
            {
                case "start":
                    horizontalSuffix = "Left";
                    break;
                case "center":
                    horizontalSuffix = string.Empty;
                    break;
                case "end":
                    horizontalSuffix = "Right";
                    break;
                case "justify":
                    horizontalSuffix = "Justified";
                    break;
                default:
                    throw new FormatException($"{path}.align: unsupported text alignment '{horizontal}'.");
            }

            switch (vertical)
            {
                case "top":
                    return "Top" + horizontalSuffix;
                case "middle":
                    if (string.IsNullOrEmpty(horizontalSuffix))
                    {
                        return "Center";
                    }

                    return string.Equals(horizontalSuffix, "Justified", StringComparison.Ordinal)
                        ? "Justified"
                        : "Middle" + horizontalSuffix;
                case "bottom":
                    return "Bottom" + horizontalSuffix;
                default:
                    throw new FormatException($"{path}.verticalAlign: unsupported vertical alignment '{vertical}'.");
            }
        }

        private static float[] MapEdges(
            Dictionary<string, object> value,
            string path,
            float width,
            float height,
            bool allowNegative)
        {
            EnsureOnlyKeys(value, path, "top", "right", "bottom", "left");
            var result = new float[4];
            ResolveEdge(value, "left", width, path, allowNegative, result, 0);
            ResolveEdge(value, "top", height, path, allowNegative, result, 1);
            ResolveEdge(value, "right", width, path, allowNegative, result, 2);
            ResolveEdge(value, "bottom", height, path, allowNegative, result, 3);
            return result;
        }

        private static void ResolveEdge(
            Dictionary<string, object> value,
            string key,
            float reference,
            string path,
            bool allowNegative,
            float[] result,
            int index)
        {
            if (!value.TryGetValue(key, out var raw)
                || !TryResolveLength(raw, reference, path + "." + key, out var resolved))
            {
                return;
            }

            if (!allowNegative && resolved < 0f)
            {
                throw new FormatException($"{path}.{key}: padding cannot be negative.");
            }

            result[index] = resolved;
        }

        private static bool TryResolveLength(object value, float reference, string path, out float result)
        {
            if (value is double number)
            {
                result = (float)number;
                return true;
            }

            if (!(value is string text))
            {
                throw new FormatException($"{path}: canonical length must be a number, percentage, or 'auto'.");
            }

            if (string.Equals(text, "auto", StringComparison.Ordinal))
            {
                result = 0f;
                return false;
            }

            if (!text.EndsWith("%", StringComparison.Ordinal)
                || !float.TryParse(
                    text.Substring(0, text.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percentage))
            {
                throw new FormatException($"{path}: invalid canonical length '{text}'.");
            }

            result = reference * percentage / 100f;
            return true;
        }

        private static string NormalizeImagePosition(object value, string path)
        {
            if (value is double number)
            {
                var result = (float)number;
                if (float.IsNaN(result) || float.IsInfinity(result))
                {
                    throw new FormatException($"{path}: image position must be finite.");
                }

                return result.ToString("0.########", CultureInfo.InvariantCulture);
            }

            if (!(value is string text))
            {
                throw new FormatException($"{path}: image position must be a number, percentage, or 'auto'.");
            }

            if (string.Equals(text, "auto", StringComparison.Ordinal))
            {
                return text;
            }

            if (!text.EndsWith("%", StringComparison.Ordinal)
                || !float.TryParse(
                    text.Substring(0, text.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percentage)
                || float.IsNaN(percentage)
                || float.IsInfinity(percentage))
            {
                throw new FormatException($"{path}: invalid image position '{text}'.");
            }

            return percentage.ToString("0.########", CultureInfo.InvariantCulture) + "%";
        }

        private static object RequireValue(Dictionary<string, object> value, string key, string path)
        {
            if (!value.TryGetValue(key, out var result) || result == null)
            {
                throw new FormatException($"{path}.{key}: required canonical property is missing.");
            }

            return result;
        }

        private static string RequireString(Dictionary<string, object> value, string key, string path)
        {
            return RequireString(RequireValue(value, key, path.Substring(0, path.LastIndexOf('.'))), path);
        }

        private static string RequireString(object value, string path)
        {
            if (value is string text && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            throw new FormatException($"{path}: a non-empty string is required.");
        }

        private static string RequireText(Dictionary<string, object> value, string key, string path)
        {
            return RequireText(RequireValue(value, key, path.Substring(0, path.LastIndexOf('.'))), path);
        }

        private static string RequireText(object value, string path)
        {
            return value as string
                   ?? throw new FormatException($"{path}: a string is required.");
        }

        private static string CollapseCanonicalWhitespace(string value)
        {
            var result = new StringBuilder(value != null ? value.Length : 0);
            var pendingSpace = false;
            for (var index = 0; value != null && index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }

                result.Append(value[index]);
            }

            return result.ToString();
        }

        private static string GetString(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var raw) || raw == null)
            {
                return null;
            }

            return raw as string
                   ?? throw new FormatException($"'{key}' must be a string.");
        }

        private static float RequirePositiveFloat(Dictionary<string, object> value, string key, string path)
        {
            var result = RequireFloat(RequireValue(value, key, path.Substring(0, path.LastIndexOf('.'))), path);
            if (result <= 0f)
            {
                throw new FormatException($"{path}: value must be greater than zero.");
            }

            return result;
        }

        private static float GetOptionalPositiveFloat(Dictionary<string, object> value, string key, string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return 0f;
            }

            var result = RequireFloat(raw, path);
            if (result <= 0f)
            {
                throw new FormatException($"{path}: value must be greater than zero.");
            }

            return result;
        }

        private static float RequireFloat(object value, string path)
        {
            if (value is double number)
            {
                return (float)number;
            }

            throw new FormatException($"{path}: a number is required.");
        }

        private static int RequireInteger(object value, string path)
        {
            if (value is double number
                && !double.IsNaN(number)
                && !double.IsInfinity(number)
                && number >= int.MinValue
                && number <= int.MaxValue
                && Math.Truncate(number) == number)
            {
                return (int)number;
            }

            throw new FormatException($"{path}: an integer is required.");
        }

        private static bool GetBoolean(
            Dictionary<string, object> value,
            string key,
            bool fallback,
            string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return fallback;
            }

            if (raw is bool result)
            {
                return result;
            }

            throw new FormatException($"{path}: a boolean is required.");
        }

        private static Dictionary<string, object> RequireObject(object value, string path)
        {
            return value as Dictionary<string, object>
                   ?? throw new FormatException($"{path}: a JSON object is required.");
        }

        private static List<object> RequireArray(object value, string path)
        {
            return value as List<object>
                   ?? throw new FormatException($"{path}: a JSON array is required.");
        }

        private static void EnsureOnlyKeys(
            Dictionary<string, object> value,
            string path,
            params string[] allowedKeys)
        {
            var allowed = new HashSet<string>(allowedKeys, StringComparer.Ordinal);
            foreach (var key in value.Keys)
            {
                if (!allowed.Contains(key))
                {
                    throw new FormatException($"{path}.{key}: unsupported canonical property.");
                }
            }
        }

        private static void RequireEmptyProperties(
            Dictionary<string, object> properties,
            string path,
            string elementName)
        {
            if (properties != null && properties.Count > 0)
            {
                throw new FormatException($"{path}: canonical '{elementName}' properties must be empty.");
            }
        }

        private static void RejectPresent(
            Dictionary<string, object> value,
            string path,
            string key,
            string reason)
        {
            if (value.ContainsKey(key))
            {
                throw new FormatException($"{path}.{key}: {reason}.");
            }
        }

        private static void RejectNonEmptyArray(
            Dictionary<string, object> value,
            string key,
            string path,
            string reason)
        {
            if (value.TryGetValue(key, out var raw) && RequireArray(raw, path).Count > 0)
            {
                throw new FormatException($"{path}: {reason}.");
            }
        }

        private static void WarnForNonEmptyArray(
            Dictionary<string, object> value,
            string key,
            string path,
            string message,
            List<UdomParseWarning> warnings)
        {
            if (value.TryGetValue(key, out var raw) && RequireArray(raw, path).Count > 0)
            {
                warnings.Add(new UdomParseWarning(path, message));
            }
        }

        private static void RejectNonEmptyObject(
            Dictionary<string, object> value,
            string key,
            string path,
            string reason)
        {
            if (value.TryGetValue(key, out var raw) && RequireObject(raw, path).Count > 0)
            {
                throw new FormatException($"{path}: {reason}.");
            }
        }

        private static void RequireDefaultString(
            Dictionary<string, object> value,
            string key,
            string expected,
            string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return;
            }

            var actual = RequireString(raw, path);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new FormatException($"{path}: only '{expected}' is supported yet (received '{actual}').");
            }
        }
    }
}
