using System;
using System.Collections.Generic;
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
            "type", "text", "sprite", "texture", "interactable", "toggleValue",
            "sliderValue", "sliderMin", "sliderMax", "sliderStep",
            "textInputValue", "textInputPlaceholder", "textInputMultiline", "textInputReadOnly",
            "scrollAxisExplicit", "scrollHorizontal", "scrollVertical", "scrollInitialOffset",
            "style", "binding", "embed", "children",
            "visible", "opacity", "position", "layout", "padding", "margin", "spacing", "backgroundColor", "textColor",
            "fontSize", "alignment", "flexibleWidth", "flexibleHeight",
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
            ValidateVector(style.padding, 4, path + ".padding", result, requirePositive: false, requireNonNegative: true);
            ValidateVector(style.margin, 4, path + ".margin", result, requirePositive: false, requireNonNegative: true);

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

            ValidateColor(style.backgroundColor, path + ".backgroundColor", result);
            ValidateColor(style.textColor, path + ".textColor", result);

            if (style.fontSize <= 0f)
            {
                AddError(result, path + ".fontSize", "fontSize는 0보다 커야 한다.");
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

        private static void AddError(UdomValidationResult result, string path, string message)
        {
            result.Issues.Add(new UdomValidationIssue(UdomIssueSeverity.Error, path, message));
        }
    }
}
