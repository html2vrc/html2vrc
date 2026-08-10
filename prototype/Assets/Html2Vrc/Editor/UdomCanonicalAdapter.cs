using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
            if (viewport.TryGetValue("fit", out var fitValue))
            {
                var fit = RequireString(fitValue, "$.viewport.fit");
                if (!string.Equals(fit, "contain", StringComparison.Ordinal))
                {
                    warnings.Add(new UdomParseWarning(
                        "$.viewport.fit",
                        $"Unity Canvas does not apply canonical viewport fit '{fit}'; viewport dimensions are used directly."));
                }
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

            return new UdomDocument
            {
                schemaVersion = version,
                id = rootId + "-document",
                name = GetString(asset, "generator") ?? rootId,
                canvas = new UdomCanvas
                {
                    renderMode = "WorldSpace",
                    size = canvasSize,
                    scale = 0.01f
                },
                root = MapNode(rootValue, "$.root", 0, canvasSize, true, styles, resources, warnings)
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
                node.text = RequireString(value, "value", path + ".value");
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

                return node;
            }

            MapElementProperties(node, elementName, value, path, mappedStyle, resources, warnings);
            ValidateEventMap(elementName, value, path, warnings);

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

                    if (!mappedStyle.HasBackground)
                    {
                        node.style.backgroundColor = "#FFFFFFFF";
                    }

                    var imageFit = GetString(properties, "fit") ?? "fill";
                    if (!string.Equals(imageFit, "contain", StringComparison.Ordinal)
                        && !string.Equals(imageFit, "fill", StringComparison.Ordinal))
                    {
                        warnings.Add(new UdomParseWarning(
                            path + ".properties.fit",
                            $"Canonical image fit '{imageFit}' is approximated by the Unity Image preserve-aspect behavior."));
                    }

                    if (properties.ContainsKey("position"))
                    {
                        warnings.Add(new UdomParseWarning(
                            path + ".properties.position",
                            "Canonical image positioning is not applied by the Unity Image fallback."));
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
                            node.textInputValue = RequireString(inputValue, path + ".properties.value");
                        }

                        if (properties.TryGetValue("placeholder", out var placeholderValue))
                        {
                            node.textInputPlaceholder = RequireString(
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
                        targetSlot = RequireString(properties, "object", path + ".properties.object")
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
            string supportedEvent;
            if (string.Equals(elementName, "button", StringComparison.Ordinal))
            {
                supportedEvent = "activate";
            }
            else if (string.Equals(elementName, "toggle", StringComparison.Ordinal))
            {
                supportedEvent = "change";
            }
            else if (string.Equals(elementName, "slider", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "change", "focus", "blur");
                AddEventWarning(events, "change", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            else if (string.Equals(elementName, "text-input", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "change", "submit", "focus", "blur");
                AddEventWarning(events, "change", path, warnings);
                AddEventWarning(events, "submit", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            else if (string.Equals(elementName, "scroll", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(events, path + ".on", "scroll", "focus", "blur");
                AddEventWarning(events, "scroll", path, warnings);
                AddEventWarning(events, "focus", path, warnings);
                AddEventWarning(events, "blur", path, warnings);
                return;
            }
            else
            {
                throw new FormatException($"{path}.on: canonical events are not supported for this element yet.");
            }

            EnsureOnlyKeys(events, path + ".on", supportedEvent);
            if (events.TryGetValue(supportedEvent, out var eventValue))
            {
                var eventId = RequireString(eventValue, path + ".on." + supportedEvent);
                warnings.Add(new UdomParseWarning(
                    path + ".on." + supportedEvent,
                    $"Symbolic event '{eventId}' is not executed until a safe Unity/Udon binding manifest is supplied."));
            }
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

            RejectNonEmptyObject(value, "transform", path + ".transform", "canonical transforms are not supported yet");
            return result;
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

            RejectPresent(value, path, "minWidth", "minimum and maximum sizes are not supported yet");
            RejectPresent(value, path, "minHeight", "minimum and maximum sizes are not supported yet");
            RejectPresent(value, path, "maxWidth", "minimum and maximum sizes are not supported yet");
            RejectPresent(value, path, "maxHeight", "minimum and maximum sizes are not supported yet");
            RejectPresent(value, path, "zIndex", "z-index is not supported yet");
            RejectPresent(value, path, "aspectRatio", "aspect ratio constraints are not supported yet");
            RequireDefaultString(value, "overflowX", "visible", path + ".overflowX");
            RequireDefaultString(value, "overflowY", "visible", path + ".overflowY");

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
                if (width <= 0f)
                {
                    throw new FormatException($"{path}.width: resolved width must be greater than zero.");
                }

                result.Style.size[0] = width;
                result.HasWidth = true;
            }

            if (value.TryGetValue("height", out var heightValue)
                && TryResolveLength(heightValue, parentHeight, path + ".height", out var height))
            {
                if (height <= 0f)
                {
                    throw new FormatException($"{path}.height: resolved height must be greater than zero.");
                }

                result.Style.size[1] = height;
                result.HasHeight = true;
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

            var mode = GetString(value, "mode") ?? "none";
            if (string.Equals(mode, "flex", StringComparison.Ordinal))
            {
                MapFlex(value, path, result);
            }
            else if (string.Equals(mode, "absolute", StringComparison.Ordinal)
                     || string.Equals(mode, "none", StringComparison.Ordinal))
            {
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
                RejectPresent(flexItem, path + ".flexItem", "shrink", "flex shrink is not supported yet");
                RejectPresent(flexItem, path + ".flexItem", "basis", "flex basis is not supported yet");
                RejectPresent(flexItem, path + ".flexItem", "alignSelf", "flex alignSelf is not supported yet");
                RejectPresent(flexItem, path + ".flexItem", "order", "flex order is not supported yet");
                if (flexItem.TryGetValue("grow", out var growValue))
                {
                    var grow = RequireFloat(growValue, path + ".flexItem.grow");
                    if (grow < 0f)
                    {
                        throw new FormatException($"{path}.flexItem.grow: flex grow cannot be negative.");
                    }

                    result.Style.flexibleWidth = grow;
                    result.Style.flexibleHeight = grow;
                }
            }
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
            RequireDefaultString(flex, "justify", "start", path + ".flex.justify");
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

            if (direction.EndsWith("-reverse", StringComparison.Ordinal))
            {
                throw new FormatException($"{path}.flex.direction: reverse flex directions are not supported yet.");
            }

            result.Style.layout = isVertical ? "Vertical" : "Horizontal";
            result.Style.childAlignment = MapChildAlignment(
                isVertical,
                GetString(flex, "alignItems") ?? "start",
                path + ".flex.alignItems");
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

        private static string MapChildAlignment(bool isVertical, string alignItems, string path)
        {
            if (isVertical)
            {
                switch (alignItems)
                {
                    case "start":
                    case "stretch":
                        return "UpperLeft";
                    case "center":
                        return "UpperCenter";
                    case "end":
                        return "UpperRight";
                    default:
                        throw new FormatException($"{path}: unsupported cross-axis alignment '{alignItems}'.");
                }
            }

            switch (alignItems)
            {
                case "start":
                case "stretch":
                    return "UpperLeft";
                case "center":
                    return "MiddleLeft";
                case "end":
                    return "LowerLeft";
                default:
                    throw new FormatException($"{path}: unsupported cross-axis alignment '{alignItems}'.");
            }
        }

        private static void MapPaint(
            Dictionary<string, object> value,
            string path,
            StyleMapping result,
            List<UdomParseWarning> warnings)
        {
            EnsureOnlyKeys(value, path, "visible", "opacity", "backgrounds", "border", "radius", "shadows");
            if (!GetBoolean(value, "visible", true, path + ".visible"))
            {
                throw new FormatException($"{path}.visible: hidden canonical nodes are not supported yet.");
            }

            if (value.TryGetValue("opacity", out var opacityValue)
                && Math.Abs(RequireFloat(opacityValue, path + ".opacity") - 1f) > 0.0001f)
            {
                throw new FormatException($"{path}.opacity: paint opacity is not supported yet.");
            }

            RejectPresent(value, path, "border", "canonical borders are not supported yet");
            RejectNonEmptyArray(value, "shadows", path + ".shadows", "canonical shadows are not supported yet");
            if (value.TryGetValue("radius", out var radiusValue))
            {
                ValidateCornerRadius(RequireObject(radiusValue, path + ".radius"), path + ".radius");
                warnings.Add(new UdomParseWarning(
                    path + ".radius",
                    "Canonical corner radii are rendered as square corners by the current Unity fallback."));
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
                result.Style.backgroundColor = MapGradientFallback(
                    background,
                    path + ".backgrounds[0]",
                    type);
                warnings.Add(new UdomParseWarning(
                    path + ".backgrounds[0]",
                    $"Canonical {type} is approximated with its first stop color until a gradient backend is available."));
            }
            else
            {
                throw new FormatException($"{path}.backgrounds[0].type: unsupported canonical background '{type}'.");
            }

            result.HasBackground = true;
        }

        private static void ValidateCornerRadius(Dictionary<string, object> value, string path)
        {
            EnsureOnlyKeys(value, path, "topLeft", "topRight", "bottomRight", "bottomLeft");
            foreach (var pair in value)
            {
                if (TryResolveLength(pair.Value, 100f, path + "." + pair.Key, out var radius)
                    && radius < 0f)
                {
                    throw new FormatException($"{path}.{pair.Key}: corner radius cannot be negative.");
                }
            }
        }

        private static string MapGradientFallback(
            Dictionary<string, object> value,
            string path,
            string type)
        {
            if (string.Equals(type, "linear-gradient", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(value, path, "type", "angle", "stops");
                if (value.TryGetValue("angle", out var angleValue))
                {
                    RequireFloat(angleValue, path + ".angle");
                }
            }
            else if (string.Equals(type, "radial-gradient", StringComparison.Ordinal))
            {
                EnsureOnlyKeys(value, path, "type", "center", "radius", "stops");
                ValidateOptionalXyLength(value, "center", path + ".center");
                ValidateOptionalXyLength(value, "radius", path + ".radius");
            }
            else
            {
                EnsureOnlyKeys(value, path, "type", "center", "angle", "stops");
                ValidateOptionalXyLength(value, "center", path + ".center");
                if (value.TryGetValue("angle", out var angleValue))
                {
                    RequireFloat(angleValue, path + ".angle");
                }
            }

            var stops = RequireArray(RequireValue(value, "stops", path), path + ".stops");
            if (stops.Count < 2)
            {
                throw new FormatException($"{path}.stops: a gradient requires at least two stops.");
            }

            var previousPosition = -1f;
            string firstColor = null;
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
                if (index == 0)
                {
                    firstColor = color;
                }
            }

            return firstColor;
        }

        private static void ValidateOptionalXyLength(
            Dictionary<string, object> value,
            string key,
            string path)
        {
            if (!value.TryGetValue(key, out var raw))
            {
                return;
            }

            var xy = RequireObject(raw, path);
            EnsureOnlyKeys(xy, path, "x", "y");
            if (!xy.TryGetValue("x", out var xValue) || !xy.TryGetValue("y", out var yValue))
            {
                throw new FormatException($"{path}: both x and y are required.");
            }

            TryResolveLength(xValue, 100f, path + ".x", out _);
            TryResolveLength(yValue, 100f, path + ".y", out _);
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
            RejectPresent(value, path, "lineHeight", "line height is not supported yet");
            RejectPresent(value, path, "letterSpacing", "letter spacing is not supported yet");
            RequireDefaultString(value, "wrap", "wrap", path + ".wrap");
            RequireDefaultString(value, "overflow", "ellipsis", path + ".overflow");
            RejectPresent(value, path, "preserveWhitespace", "preserveWhitespace is not supported yet");

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
            var vertical = GetString(value, "verticalAlign") ?? "middle";
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
                    throw new FormatException($"{path}.align: justified text is not supported yet.");
                default:
                    throw new FormatException($"{path}.align: unsupported text alignment '{horizontal}'.");
            }

            switch (vertical)
            {
                case "top":
                    return "Top" + horizontalSuffix;
                case "middle":
                    return string.IsNullOrEmpty(horizontalSuffix) ? "Center" : "Middle" + horizontalSuffix;
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
