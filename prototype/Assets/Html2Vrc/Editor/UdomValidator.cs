using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Html2Vrc.Editor
{
    public enum UdomIssueSeverity
    {
        Warning,
        Error
    }

    public sealed class UdomValidationIssue
    {
        public UdomIssueSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public UdomValidationIssue(UdomIssueSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }

        public override string ToString()
        {
            return $"{Severity}: {Path}: {Message}";
        }
    }

    public sealed class UdomValidationResult
    {
        public UdomDocument Document { get; internal set; }
        public List<UdomValidationIssue> Issues { get; } = new List<UdomValidationIssue>();
        public bool IsValid => Document != null && Issues.TrueForAll(issue => issue.Severity != UdomIssueSeverity.Error);

        public string Format()
        {
            if (Issues.Count == 0)
            {
                return "UDOM validation passed with no issues.";
            }

            var builder = new StringBuilder();
            for (var index = 0; index < Issues.Count; index++)
            {
                builder.AppendLine(Issues[index].ToString());
            }

            return builder.ToString().TrimEnd();
        }
    }

    public static class UdomValidator
    {
        private static readonly HashSet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Panel",
            "Text",
            "Image",
            "Button",
            "Toggle",
            "Slider",
            "TextInput",
            "ScrollView",
            "Embed"
        };

        private static readonly HashSet<string> SupportedLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "None",
            "Vertical",
            "Horizontal"
        };

        private static readonly HashSet<string> KnownProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "id", "name", "canvas", "root",
            "renderMode", "size", "scale", "viewportPixelRatio", "viewportFit",
            "type", "text", "sprite", "texture", "imageFit", "imagePositionX", "imagePositionY",
            "imageIntrinsicSize", "interactable", "toggleValue",
            "sliderValue", "sliderMin", "sliderMax", "sliderStep",
            "textInputValue", "textInputPlaceholder", "textInputMultiline", "textInputReadOnly",
            "scrollAxisExplicit", "scrollHorizontal", "scrollVertical", "scrollInitialOffset",
            "style", "binding", "embed", "children",
            "visible", "opacity", "position", "minSize", "maxSize", "autoSize", "aspectRatio", "aspectRatioMode",
            "layout", "padding", "margin", "spacing", "backgroundColor",
            "backgroundType", "backgroundGradientAngle", "backgroundGradientPositions", "backgroundGradientColors",
            "backgroundGradientCenter", "backgroundGradientCenterIsPercent",
            "backgroundGradientRadius", "backgroundGradientRadiusIsPercent",
            "cornerRadius", "cornerRadiusPercent", "borderWidth", "borderColor", "textColor", "fontSize",
            "lineHeight", "letterSpacing", "textWrap", "textOverflow", "preserveWhitespace",
            "alignment", "fontStyle", "childAlignment",
            "shadowOffsets", "shadowBlurs", "shadowSpreads", "shadowColors", "shadowInsets",
            "stretchChildrenWidth", "stretchChildrenHeight", "useResolvedChildrenWidth", "useResolvedChildrenHeight",
            "flexibleWidth", "flexibleHeight",
            "transformOrigin", "transformOriginIsPercent", "transformOperationTypes",
            "transformOperationValues", "transformOperationValuesArePercent",
            "action", "targetSlot", "fallbackLabel"
        };

        private static readonly Regex StableIdPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._-]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static UdomValidationResult Validate(string json, string sourceAssetPath = null)
        {
            var result = new UdomValidationResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                AddError(result, "$", "JSON이 비어 있다.");
                return result;
            }

            try
            {
                result.Document = UdomJsonParser.Parse(
                    json,
                    sourceAssetPath,
                    out var isCanonical,
                    out var parseWarnings);
                if (!isCanonical)
                {
                    ValidateKnownProperties(json, result);
                }

                for (var index = 0; index < parseWarnings.Count; index++)
                {
                    result.Issues.Add(new UdomValidationIssue(
                        UdomIssueSeverity.Warning,
                        parseWarnings[index].Path,
                        parseWarnings[index].Message));
                }
            }
            catch (Exception exception)
            {
                AddError(result, "$", $"JSON을 읽을 수 없다: {exception.Message}");
                return result;
            }

            var document = result.Document;
            if (document == null)
            {
                AddError(result, "$", "UDOM 문서를 만들 수 없다.");
                return result;
            }

            if (!string.Equals(document.schemaVersion, "0.1", StringComparison.Ordinal))
            {
                AddError(result, "$.schemaVersion", $"지원하지 않는 버전 '{document.schemaVersion}'. 현재 지원 버전은 0.1이다.");
            }

            ValidateId(document.id, "$.id", result);
            ValidateCanvas(document.canvas, result);

            if (document.root == null)
            {
                AddError(result, "$.root", "루트 노드가 필요하다.");
                return result;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateNode(document.root, "$.root", ids, result);
            return result;
        }

        private static void ValidateCanvas(UdomCanvas canvas, UdomValidationResult result)
        {
            if (canvas == null)
            {
                AddError(result, "$.canvas", "Canvas 설정이 필요하다.");
                return;
            }

            if (!string.Equals(canvas.renderMode, "WorldSpace", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(canvas.renderMode, "ScreenSpaceOverlay", StringComparison.OrdinalIgnoreCase))
            {
                AddError(result, "$.canvas.renderMode", "WorldSpace 또는 ScreenSpaceOverlay만 지원한다.");
            }

            ValidateVector(canvas.size, 2, "$.canvas.size", result, requirePositive: true);
            if (canvas.scale <= 0f)
            {
                AddError(result, "$.canvas.scale", "scale은 0보다 커야 한다.");
            }

            if (canvas.viewportPixelRatio <= 0f
                || float.IsNaN(canvas.viewportPixelRatio)
                || float.IsInfinity(canvas.viewportPixelRatio))
            {
                AddError(result, "$.canvas.viewportPixelRatio", "viewportPixelRatio는 유한한 양수여야 한다.");
            }

            if (!string.Equals(canvas.viewportFit, "contain", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(canvas.viewportFit, "cover", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(canvas.viewportFit, "stretch", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(canvas.viewportFit, "none", StringComparison.OrdinalIgnoreCase))
            {
                AddError(result, "$.canvas.viewportFit", $"지원하지 않는 viewport fit '{canvas.viewportFit}'.");
            }
        }

        private static void ValidateNode(
            UdomNode node,
            string path,
            HashSet<string> ids,
            UdomValidationResult result)
        {
            if (node == null)
            {
                AddError(result, path, "null 노드는 지원하지 않는다.");
                return;
            }

            ValidateId(node.id, path + ".id", result);
            if (!string.IsNullOrWhiteSpace(node.id) && !ids.Add(node.id))
            {
                AddError(result, path + ".id", $"중복 안정 ID '{node.id}'.");
            }

            if (string.IsNullOrWhiteSpace(node.type) || !SupportedTypes.Contains(node.type))
            {
                AddError(result, path + ".type", $"지원하지 않는 요소 '{node.type}'.");
                return;
            }

            ValidateStyle(node.style, path + ".style", result);

            if (string.Equals(node.type, "Text", StringComparison.OrdinalIgnoreCase) && node.text == null)
            {
                AddError(result, path + ".text", "Text 요소에는 text가 필요하다.");
            }

            if (string.Equals(node.type, "Slider", StringComparison.OrdinalIgnoreCase))
            {
                if (node.sliderMax <= node.sliderMin)
                {
                    AddError(result, path + ".sliderMax", "Slider maximum must be greater than its minimum.");
                }
                else if (node.sliderValue < node.sliderMin || node.sliderValue > node.sliderMax)
                {
                    AddError(result, path + ".sliderValue", "Slider value must be within its minimum and maximum.");
                }

                if (node.sliderStep < 0f)
                {
                    AddError(result, path + ".sliderStep", "Slider step cannot be negative.");
                }
            }

            if (string.Equals(node.type, "Image", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(node.imageFit, "fill", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(node.imageFit, "contain", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(node.imageFit, "cover", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(node.imageFit, "none", StringComparison.OrdinalIgnoreCase))
                {
                    AddError(result, path + ".imageFit", $"지원하지 않는 image fit '{node.imageFit}'.");
                }

                ValidateImagePosition(node.imagePositionX, path + ".imagePositionX", result);
                ValidateImagePosition(node.imagePositionY, path + ".imagePositionY", result);
                ValidateVector(
                    node.imageIntrinsicSize,
                    2,
                    path + ".imageIntrinsicSize",
                    result,
                    requirePositive: false,
                    requireNonNegative: true);
            }

            if (string.Equals(node.type, "ScrollView", StringComparison.OrdinalIgnoreCase))
            {
                var explicitAxis = node.scrollAxisExplicit || node.scrollHorizontal || !node.scrollVertical;
                var horizontal = explicitAxis
                    ? node.scrollHorizontal
                    : string.Equals(node.style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase);
                var vertical = explicitAxis ? node.scrollVertical : !horizontal;
                if (!horizontal && !vertical)
                {
                    AddError(result, path, "ScrollView must enable at least one axis.");
                }

                ValidateVector(node.scrollInitialOffset, 2, path + ".scrollInitialOffset", result, false);
            }

            if (!string.IsNullOrWhiteSpace(node.sprite))
            {
                if (!node.sprite.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AddError(result, path + ".sprite", "Sprite 경로는 Assets/로 시작해야 한다.");
                }
                else if (AssetDatabase.LoadAssetAtPath<Sprite>(node.sprite) == null)
                {
                    AddError(result, path + ".sprite", $"Sprite를 찾을 수 없거나 Sprite 타입이 아니다: {node.sprite}");
                }
            }

            if (!string.IsNullOrWhiteSpace(node.texture))
            {
                if (!string.IsNullOrWhiteSpace(node.sprite))
                {
                    AddError(result, path, "Image node cannot reference both a Sprite and a Texture.");
                }
                else if (!node.texture.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AddError(result, path + ".texture", "Texture path must start with Assets/.");
                }
                else if (AssetDatabase.LoadAssetAtPath<Texture2D>(node.texture) == null)
                {
                    AddError(result, path + ".texture", $"Texture2D asset was not found: {node.texture}");
                }
            }

            if (string.Equals(node.type, "Button", StringComparison.OrdinalIgnoreCase))
            {
                ValidateBinding(node.binding, path + ".binding", result);
            }
            else if (node.binding != null)
            {
                AddError(result, path + ".binding", "binding은 Button에서만 지원한다.");
            }

            if (string.Equals(node.type, "Embed", StringComparison.OrdinalIgnoreCase))
            {
                if (node.embed == null || string.IsNullOrWhiteSpace(node.embed.targetSlot))
                {
                    AddError(result, path + ".embed.targetSlot", "Embed에는 targetSlot이 필요하다.");
                }
            }
            else if (node.embed != null)
            {
                AddError(result, path + ".embed", "embed 설정은 Embed 요소에서만 지원한다.");
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            if ((string.Equals(node.type, "Text", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(node.type, "Image", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(node.type, "Embed", StringComparison.OrdinalIgnoreCase))
                && children.Length > 0)
            {
                AddError(result, path + ".children", $"{node.type} 요소는 자식을 지원하지 않는다.");
            }

            for (var index = 0; index < children.Length; index++)
            {
                ValidateNode(children[index], $"{path}.children[{index}]", ids, result);
            }
        }

        private static void ValidateBinding(UdomBinding binding, string path, UdomValidationResult result)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.action))
            {
                result.Issues.Add(new UdomValidationIssue(
                    UdomIssueSeverity.Warning,
                    path,
                    "Button에 binding이 없어 눌러도 동작하지 않는다."));
                return;
            }

            if (!Enum.TryParse(binding.action, true, out UdomActionType action) || action == UdomActionType.None)
            {
                AddError(result, path + ".action", $"지원하지 않는 안전 동작 '{binding.action}'.");
                return;
            }

            if (action != UdomActionType.ClosePanel && string.IsNullOrWhiteSpace(binding.targetSlot))
            {
                AddError(result, path + ".targetSlot", $"{action}에는 targetSlot이 필요하다.");
            }
        }

        private static void ValidateStyle(UdomStyle style, string path, UdomValidationResult result)
        {
            if (style == null)
            {
                return;
            }

            ValidateVector(style.position, 2, path + ".position", result, requirePositive: false);
            ValidateVector(style.size, 2, path + ".size", result, requirePositive: true);
            ValidateVector(style.minSize, 2, path + ".minSize", result, requirePositive: false, requireNonNegative: true);
            ValidateMaximumSize(style.maxSize, path + ".maxSize", result);
            ValidateBooleanVector(style.autoSize, 2, path + ".autoSize", result);
            ValidateVector(style.padding, 4, path + ".padding", result, requirePositive: false, requireNonNegative: true);
            ValidateVector(style.margin, 4, path + ".margin", result, requirePositive: false, requireNonNegative: true);
            ValidateVector(
                style.borderWidth,
                4,
                path + ".borderWidth",
                result,
                requirePositive: false,
                requireNonNegative: true);
            ValidateVector(
                style.cornerRadius,
                4,
                path + ".cornerRadius",
                result,
                requirePositive: false,
                requireNonNegative: true);
            ValidateCornerRadiusPercent(style.cornerRadiusPercent, path + ".cornerRadiusPercent", result);

            if (style.opacity < 0f
                || style.opacity > 1f
                || float.IsNaN(style.opacity)
                || float.IsInfinity(style.opacity))
            {
                AddError(result, path + ".opacity", "opacity는 0 이상 1 이하의 유한한 값이어야 한다.");
            }

            if (!SupportedLayouts.Contains(style.layout ?? "None"))
            {
                AddError(result, path + ".layout", $"지원하지 않는 layout '{style.layout}'.");
            }

            if (style.spacing < 0f)
            {
                AddError(result, path + ".spacing", "spacing은 음수가 될 수 없다.");
            }

            if (style.aspectRatio < 0f
                || float.IsNaN(style.aspectRatio)
                || float.IsInfinity(style.aspectRatio))
            {
                AddError(result, path + ".aspectRatio", "aspectRatio는 0 이상의 유한한 값이어야 한다.");
            }

            if (!string.Equals(style.aspectRatioMode, "None", StringComparison.Ordinal)
                && !string.Equals(style.aspectRatioMode, "WidthControlsHeight", StringComparison.Ordinal)
                && !string.Equals(style.aspectRatioMode, "HeightControlsWidth", StringComparison.Ordinal))
            {
                AddError(result, path + ".aspectRatioMode", $"지원하지 않는 aspect ratio mode '{style.aspectRatioMode}'.");
            }

            ValidateColor(style.backgroundColor, path + ".backgroundColor", result);
            ValidateBackground(style, path, result);
            ValidateColorArray(style.borderColor, path + ".borderColor", result);
            ValidateShadows(style, path, result);
            ValidateColor(style.textColor, path + ".textColor", result);

            if (style.fontSize <= 0f)
            {
                AddError(result, path + ".fontSize", "fontSize는 0보다 커야 한다.");
            }

            if ((!Mathf.Approximately(style.lineHeight, -1f) && style.lineHeight <= 0f)
                || float.IsNaN(style.lineHeight)
                || float.IsInfinity(style.lineHeight))
            {
                AddError(result, path + ".lineHeight", "lineHeight는 -1 또는 0보다 큰 유한한 값이어야 한다.");
            }

            if (float.IsNaN(style.letterSpacing) || float.IsInfinity(style.letterSpacing))
            {
                AddError(result, path + ".letterSpacing", "letterSpacing은 유한한 값이어야 한다.");
            }

            if (!string.Equals(style.textOverflow, "Visible", StringComparison.Ordinal)
                && !string.Equals(style.textOverflow, "Clip", StringComparison.Ordinal)
                && !string.Equals(style.textOverflow, "Ellipsis", StringComparison.Ordinal))
            {
                AddError(result, path + ".textOverflow", $"지원하지 않는 text overflow '{style.textOverflow}'.");
            }

            if (!UdomBuilderUtility.TryParseAlignment(style.alignment, out _))
            {
                AddError(result, path + ".alignment", $"지원하지 않는 정렬 '{style.alignment}'.");
            }

            if (!UdomBuilderUtility.TryParseFontStyle(style.fontStyle, out _))
            {
                AddError(result, path + ".fontStyle", $"지원하지 않는 font style '{style.fontStyle}'.");
            }

            if (!UdomBuilderUtility.TryParseChildAlignment(style.childAlignment, out _))
            {
                AddError(result, path + ".childAlignment", $"지원하지 않는 child alignment '{style.childAlignment}'.");
            }

            ValidateTransform(style, path, result);
        }

        private static void ValidateMaximumSize(
            float[] value,
            string path,
            UdomValidationResult result)
        {
            if (value == null || value.Length != 2)
            {
                AddError(result, path, "2개 값이 필요하다.");
                return;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (float.IsNaN(value[index])
                    || float.IsInfinity(value[index])
                    || value[index] < 0f && !Mathf.Approximately(value[index], -1f))
                {
                    AddError(result, $"{path}[{index}]", "max size는 -1 또는 0 이상의 유한한 값이어야 한다.");
                }
            }
        }

        private static void ValidateShadows(
            UdomStyle style,
            string path,
            UdomValidationResult result)
        {
            var colors = style.shadowColors;
            if (colors == null)
            {
                AddError(result, path + ".shadowColors", "shadow color 배열이 필요하다.");
                return;
            }

            var count = colors.Length;
            ValidateVector(
                style.shadowOffsets,
                count * 2,
                path + ".shadowOffsets",
                result,
                requirePositive: false);
            ValidateVector(
                style.shadowBlurs,
                count,
                path + ".shadowBlurs",
                result,
                requirePositive: false,
                requireNonNegative: true);
            ValidateVector(
                style.shadowSpreads,
                count,
                path + ".shadowSpreads",
                result,
                requirePositive: false);
            ValidateBooleanVector(
                style.shadowInsets,
                count,
                path + ".shadowInsets",
                result);
            for (var index = 0; index < colors.Length; index++)
            {
                ValidateColor(colors[index], $"{path}.shadowColors[{index}]", result);
            }
        }

        private static void ValidateTransform(
            UdomStyle style,
            string path,
            UdomValidationResult result)
        {
            ValidateVector(
                style.transformOrigin,
                2,
                path + ".transformOrigin",
                result,
                requirePositive: false);
            ValidateBooleanVector(
                style.transformOriginIsPercent,
                2,
                path + ".transformOriginIsPercent",
                result);

            var types = style.transformOperationTypes;
            if (types == null)
            {
                AddError(result, path + ".transformOperationTypes", "transform operation type 배열이 필요하다.");
                return;
            }

            ValidateVector(
                style.transformOperationValues,
                types.Length * 2,
                path + ".transformOperationValues",
                result,
                requirePositive: false);
            ValidateBooleanVector(
                style.transformOperationValuesArePercent,
                types.Length * 2,
                path + ".transformOperationValuesArePercent",
                result);

            for (var index = 0; index < types.Length; index++)
            {
                var type = types[index];
                if (!string.Equals(type, "translate", StringComparison.Ordinal)
                    && !string.Equals(type, "rotate", StringComparison.Ordinal)
                    && !string.Equals(type, "scale", StringComparison.Ordinal))
                {
                    AddError(
                        result,
                        $"{path}.transformOperationTypes[{index}]",
                        $"unsupported transform operation '{type}'.");
                }

                if (!string.Equals(type, "translate", StringComparison.Ordinal)
                    && style.transformOperationValuesArePercent != null
                    && style.transformOperationValuesArePercent.Length >= (index + 1) * 2
                    && (style.transformOperationValuesArePercent[index * 2]
                        || style.transformOperationValuesArePercent[index * 2 + 1]))
                {
                    AddError(
                        result,
                        $"{path}.transformOperationValuesArePercent[{index * 2}]",
                        "only translate operations may use percentage values.");
                }
            }
        }

        private static void ValidateKnownProperties(string json, UdomValidationResult result)
        {
            foreach (var key in ScanPropertyNames(json))
            {
                if (!KnownProperties.Contains(key))
                {
                    AddError(result, "$", $"지원하지 않는 JSON 속성 '{key}'.");
                }
            }
        }

        private static void ValidateImagePosition(
            string value,
            string path,
            UdomValidationResult result)
        {
            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                AddError(result, path, "image position은 숫자, percentage 또는 'auto'여야 한다.");
                return;
            }

            var numeric = value;
            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                numeric = value.Substring(0, value.Length - 1);
            }

            if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || float.IsNaN(parsed)
                || float.IsInfinity(parsed))
            {
                AddError(result, path, $"유효하지 않은 image position '{value}'.");
            }
        }

        private static IEnumerable<string> ScanPropertyNames(string json)
        {
            var keys = new List<string>();
            for (var index = 0; index < json.Length; index++)
            {
                if (json[index] != '"')
                {
                    continue;
                }

                var builder = new StringBuilder();
                var cursor = index + 1;
                var escaped = false;
                for (; cursor < json.Length; cursor++)
                {
                    var character = json[cursor];
                    if (escaped)
                    {
                        builder.Append(character);
                        escaped = false;
                        continue;
                    }

                    if (character == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (character == '"')
                    {
                        break;
                    }

                    builder.Append(character);
                }

                if (cursor >= json.Length)
                {
                    yield break;
                }

                var after = cursor + 1;
                while (after < json.Length && char.IsWhiteSpace(json[after]))
                {
                    after++;
                }

                if (after < json.Length && json[after] == ':')
                {
                    keys.Add(builder.ToString());
                }

                index = cursor;
            }

            for (var index = 0; index < keys.Count; index++)
            {
                yield return keys[index];
            }
        }

        private static void ValidateId(string id, string path, UdomValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                AddError(result, path, "안정 ID가 필요하다.");
            }
            else if (!StableIdPattern.IsMatch(id))
            {
                AddError(result, path, $"잘못된 안정 ID '{id}'. 영문/숫자로 시작하고 영문, 숫자, '.', '_', '-'만 사용할 수 있다.");
            }
        }

        private static void ValidateVector(
            float[] value,
            int expectedLength,
            string path,
            UdomValidationResult result,
            bool requirePositive,
            bool requireNonNegative = false)
        {
            if (value == null || value.Length != expectedLength)
            {
                AddError(result, path, $"{expectedLength}개의 숫자가 필요하다.");
                return;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (float.IsNaN(value[index]) || float.IsInfinity(value[index]))
                {
                    AddError(result, path, "NaN 또는 Infinity는 사용할 수 없다.");
                    return;
                }

                if (requirePositive && value[index] <= 0f)
                {
                    AddError(result, path, "모든 값은 0보다 커야 한다.");
                    return;
                }

                if (requireNonNegative && value[index] < 0f)
                {
                    AddError(result, path, "음수 값은 사용할 수 없다.");
                    return;
                }
            }
        }

        private static void ValidateColor(string value, string path, UdomValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value) || !ColorUtility.TryParseHtmlString(value, out _))
            {
                AddError(result, path, $"잘못된 색상 '{value}'. #RRGGBB 또는 #RRGGBBAA 형식을 사용한다.");
            }
        }

        private static void ValidateCornerRadiusPercent(
            float[] values,
            string path,
            UdomValidationResult result)
        {
            if (values == null || values.Length != 4)
            {
                AddError(result, path, "four percentage values are required in top-left, top-right, bottom-right, bottom-left order.");
                return;
            }

            for (var index = 0; index < values.Length; index++)
            {
                if (float.IsNaN(values[index])
                    || float.IsInfinity(values[index])
                    || (values[index] < 0f && values[index] != -1f))
                {
                    AddError(result, $"{path}[{index}]", "radius percentage must be finite, non-negative, or -1 for an absolute radius.");
                }
            }
        }

        private static void ValidateColorArray(string[] value, string path, UdomValidationResult result)
        {
            if (value == null || value.Length != 4)
            {
                AddError(result, path, "four colors are required in left, top, right, bottom order.");
                return;
            }

            for (var index = 0; index < value.Length; index++)
            {
                ValidateColor(value[index], $"{path}[{index}]", result);
            }
        }

        private static void ValidateBackground(UdomStyle style, string path, UdomValidationResult result)
        {
            if (string.Equals(style.backgroundType, "color", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var isLinear = string.Equals(
                style.backgroundType,
                "linear-gradient",
                StringComparison.OrdinalIgnoreCase);
            var isRadial = string.Equals(
                style.backgroundType,
                "radial-gradient",
                StringComparison.OrdinalIgnoreCase);
            var isConic = string.Equals(
                style.backgroundType,
                "conic-gradient",
                StringComparison.OrdinalIgnoreCase);
            if (!isLinear && !isRadial && !isConic)
            {
                AddError(result, path + ".backgroundType", $"unsupported background type '{style.backgroundType}'.");
                return;
            }

            if ((isLinear || isConic)
                && (float.IsNaN(style.backgroundGradientAngle)
                    || float.IsInfinity(style.backgroundGradientAngle)))
            {
                AddError(result, path + ".backgroundGradientAngle", "gradient angle must be finite.");
            }

            if (isRadial || isConic)
            {
                ValidateVector(
                    style.backgroundGradientCenter,
                    2,
                    path + ".backgroundGradientCenter",
                    result,
                    requirePositive: false);
                ValidateBooleanVector(
                    style.backgroundGradientCenterIsPercent,
                    2,
                    path + ".backgroundGradientCenterIsPercent",
                    result);
                if (isRadial)
                {
                    ValidateVector(
                        style.backgroundGradientRadius,
                        2,
                        path + ".backgroundGradientRadius",
                        result,
                        requirePositive: false,
                        requireNonNegative: true);
                    ValidateBooleanVector(
                        style.backgroundGradientRadiusIsPercent,
                        2,
                        path + ".backgroundGradientRadiusIsPercent",
                        result);
                }
            }

            var positions = style.backgroundGradientPositions;
            var colors = style.backgroundGradientColors;
            if (positions == null
                || colors == null
                || positions.Length < 2
                || positions.Length != colors.Length)
            {
                AddError(
                    result,
                    path + ".backgroundGradientPositions",
                    "gradient requires matching position and color arrays with at least two stops.");
                return;
            }

            var previous = -1f;
            for (var index = 0; index < positions.Length; index++)
            {
                var position = positions[index];
                if (float.IsNaN(position)
                    || float.IsInfinity(position)
                    || position < 0f
                    || position > 1f
                    || position < previous)
                {
                    AddError(
                        result,
                        $"{path}.backgroundGradientPositions[{index}]",
                        "gradient stop positions must be finite, ordered, and between zero and one.");
                    return;
                }

                previous = position;
                ValidateColor(colors[index], $"{path}.backgroundGradientColors[{index}]", result);
            }
        }

        private static void ValidateBooleanVector(
            bool[] value,
            int expectedLength,
            string path,
            UdomValidationResult result)
        {
            if (value == null || value.Length != expectedLength)
            {
                AddError(result, path, $"{expectedLength}개의 boolean 값이 필요하다.");
            }
        }

        private static void AddError(UdomValidationResult result, string path, string message)
        {
            result.Issues.Add(new UdomValidationIssue(UdomIssueSeverity.Error, path, message));
        }
    }
}
