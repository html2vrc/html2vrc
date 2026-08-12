using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Html2Vrc.Editor
{
    internal static class UdomJsonParser
    {
        public static UdomDocument Parse(string json)
        {
            return Parse(json, null, out _, out _);
        }

        public static UdomDocument Parse(
            string json,
            string sourceAssetPath,
            out bool isCanonical,
            out List<UdomParseWarning> warnings)
        {
            var reader = new Reader(json);
            var root = reader.ParseValue() as Dictionary<string, object>
                       ?? throw new FormatException("최상위 JSON 값은 객체여야 한다.");
            reader.EnsureEnd();
            isCanonical = root.ContainsKey("asset") || root.ContainsKey("viewport");
            warnings = new List<UdomParseWarning>();
            return isCanonical
                ? UdomCanonicalAdapter.Map(root, sourceAssetPath, warnings)
                : MapDocument(root);
        }

        private static UdomDocument MapDocument(Dictionary<string, object> value)
        {
            return new UdomDocument
            {
                schemaVersion = GetString(value, "schemaVersion"),
                id = GetString(value, "id"),
                name = GetString(value, "name"),
                canvas = value.TryGetValue("canvas", out var canvasValue)
                    ? MapCanvas(RequireObject(canvasValue, "canvas"))
                    : null,
                root = value.TryGetValue("root", out var rootValue)
                    ? MapNode(RequireObject(rootValue, "root"), "$.root", 0)
                    : null
            };
        }

        private static UdomCanvas MapCanvas(Dictionary<string, object> value)
        {
            var canvas = new UdomCanvas();
            if (value.TryGetValue("renderMode", out _))
            {
                canvas.renderMode = GetString(value, "renderMode");
            }

            if (value.TryGetValue("size", out var size))
            {
                canvas.size = GetFloatArray(size, "canvas.size");
            }

            if (value.TryGetValue("scale", out var scale))
            {
                canvas.scale = GetFloat(scale, "canvas.scale");
            }

            if (value.TryGetValue("viewportPixelRatio", out var pixelRatio))
            {
                canvas.viewportPixelRatio = GetFloat(pixelRatio, "canvas.viewportPixelRatio");
            }

            if (value.TryGetValue("viewportFit", out _))
            {
                canvas.viewportFit = GetString(value, "viewportFit");
            }

            return canvas;
        }

        private static UdomNode MapNode(Dictionary<string, object> value, string path, int depth)
        {
            if (depth > 64)
            {
                throw new FormatException($"{path}: 노드 중첩은 64단계를 넘을 수 없다.");
            }

            var node = new UdomNode
            {
                id = GetString(value, "id"),
                type = GetString(value, "type"),
                name = GetString(value, "name"),
                text = GetString(value, "text"),
                sprite = GetString(value, "sprite"),
                texture = GetString(value, "texture")
            };

            if (value.TryGetValue("imageFit", out _))
            {
                node.imageFit = GetString(value, "imageFit");
            }

            if (value.TryGetValue("imagePositionX", out _))
            {
                node.imagePositionX = GetString(value, "imagePositionX");
            }

            if (value.TryGetValue("imagePositionY", out _))
            {
                node.imagePositionY = GetString(value, "imagePositionY");
            }

            if (value.TryGetValue("imageIntrinsicSize", out var imageIntrinsicSize))
            {
                node.imageIntrinsicSize = GetFloatArray(
                    imageIntrinsicSize,
                    path + ".imageIntrinsicSize");
            }

            if (value.TryGetValue("style", out var styleValue))
            {
                node.style = MapStyle(RequireObject(styleValue, path + ".style"), path + ".style");
            }

            if (value.TryGetValue("textRuns", out var textRunsValue))
            {
                var textRuns = RequireArray(textRunsValue, path + ".textRuns");
                node.textRuns = new UdomTextRun[textRuns.Count];
                for (var index = 0; index < textRuns.Count; index++)
                {
                    var runPath = $"{path}.textRuns[{index}]";
                    var runValue = RequireObject(textRuns[index], runPath);
                    foreach (var key in runValue.Keys)
                    {
                        if (!string.Equals(key, "text", StringComparison.Ordinal)
                            && !string.Equals(key, "bold", StringComparison.Ordinal)
                            && !string.Equals(key, "italic", StringComparison.Ordinal)
                            && !string.Equals(key, "fontStyle", StringComparison.Ordinal)
                            && !string.Equals(key, "textColor", StringComparison.Ordinal)
                            && !string.Equals(key, "fontScale", StringComparison.Ordinal))
                        {
                            throw new FormatException($"{runPath}: 지원하지 않는 text run 속성 '{key}'.");
                        }
                    }

                    var run = new UdomTextRun
                    {
                        text = GetString(runValue, "text")
                    };
                    if (runValue.TryGetValue("bold", out var bold))
                    {
                        run.bold = GetBoolean(bold, runPath + ".bold");
                    }

                    if (runValue.TryGetValue("italic", out var italic))
                    {
                        run.italic = GetBoolean(italic, runPath + ".italic");
                    }

                    if (runValue.TryGetValue("fontStyle", out _))
                    {
                        run.fontStyle = GetString(runValue, "fontStyle");
                    }

                    if (runValue.TryGetValue("textColor", out _))
                    {
                        run.textColor = GetString(runValue, "textColor");
                    }

                    if (runValue.TryGetValue("fontScale", out var fontScale))
                    {
                        run.fontScale = GetFloat(fontScale, runPath + ".fontScale");
                    }

                    node.textRuns[index] = run;
                }
            }

            if (value.TryGetValue("binding", out var bindingValue))
            {
                var bindingObject = RequireObject(bindingValue, path + ".binding");
                node.binding = new UdomBinding
                {
                    action = GetString(bindingObject, "action"),
                    targetSlot = GetString(bindingObject, "targetSlot")
                };
            }

            if (value.TryGetValue("embed", out var embedValue))
            {
                var embedObject = RequireObject(embedValue, path + ".embed");
                node.embed = new UdomEmbed
                {
                    targetSlot = GetString(embedObject, "targetSlot"),
                    fallbackLabel = GetString(embedObject, "fallbackLabel")
                };
            }

            if (value.TryGetValue("children", out var childrenValue))
            {
                var children = RequireArray(childrenValue, path + ".children");
                node.children = new UdomNode[children.Count];
                for (var index = 0; index < children.Count; index++)
                {
                    node.children[index] = MapNode(
                        RequireObject(children[index], $"{path}.children[{index}]"),
                        $"{path}.children[{index}]",
                        depth + 1);
                }
            }

            return node;
        }

        private static UdomStyle MapStyle(Dictionary<string, object> value, string path)
        {
            var style = new UdomStyle();
            if (value.TryGetValue("visible", out var visible))
            {
                style.visible = GetBoolean(visible, path + ".visible");
            }

            if (value.TryGetValue("opacity", out var opacity))
            {
                style.opacity = GetFloat(opacity, path + ".opacity");
            }

            if (value.TryGetValue("position", out var position))
            {
                style.position = GetFloatArray(position, path + ".position");
            }

            if (value.TryGetValue("positionAbsolute", out var positionAbsolute))
            {
                style.positionAbsolute = GetBoolean(
                    positionAbsolute,
                    path + ".positionAbsolute");
            }

            if (value.TryGetValue("zIndex", out var zIndex))
            {
                style.zIndex = GetInteger(zIndex, path + ".zIndex");
            }

            if (value.TryGetValue("size", out var size))
            {
                style.size = GetFloatArray(size, path + ".size");
            }

            if (value.TryGetValue("minSize", out var minSize))
            {
                style.minSize = GetFloatArray(minSize, path + ".minSize");
            }

            if (value.TryGetValue("maxSize", out var maxSize))
            {
                style.maxSize = GetFloatArray(maxSize, path + ".maxSize");
            }

            if (value.TryGetValue("autoSize", out var autoSize))
            {
                style.autoSize = GetBooleanArray(autoSize, path + ".autoSize");
            }

            if (value.TryGetValue("aspectRatio", out var aspectRatio))
            {
                style.aspectRatio = GetFloat(aspectRatio, path + ".aspectRatio");
            }

            if (value.TryGetValue("aspectRatioMode", out _))
            {
                style.aspectRatioMode = GetString(value, "aspectRatioMode");
            }

            if (value.TryGetValue("displayNone", out var displayNone))
            {
                style.displayNone = GetBoolean(displayNone, path + ".displayNone");
            }

            if (value.TryGetValue("clipContent", out var clipContent))
            {
                style.clipContent = GetBoolean(clipContent, path + ".clipContent");
            }

            if (value.TryGetValue("layout", out _))
            {
                style.layout = GetString(value, "layout");
            }

            if (value.TryGetValue("padding", out var padding))
            {
                style.padding = GetFloatArray(padding, path + ".padding");
            }

            if (value.TryGetValue("margin", out var margin))
            {
                style.margin = GetFloatArray(margin, path + ".margin");
            }

            if (value.TryGetValue("spacing", out var spacing))
            {
                style.spacing = GetFloat(spacing, path + ".spacing");
            }

            if (value.TryGetValue("backgroundColor", out _))
            {
                style.backgroundColor = GetString(value, "backgroundColor");
            }

            if (value.TryGetValue("backgroundType", out _))
            {
                style.backgroundType = GetString(value, "backgroundType");
            }

            if (value.TryGetValue("backgroundGradientAngle", out var backgroundGradientAngle))
            {
                style.backgroundGradientAngle = GetFloat(
                    backgroundGradientAngle,
                    path + ".backgroundGradientAngle");
            }

            if (value.TryGetValue("backgroundGradientPositions", out var backgroundGradientPositions))
            {
                style.backgroundGradientPositions = GetFloatArray(
                    backgroundGradientPositions,
                    path + ".backgroundGradientPositions");
            }

            if (value.TryGetValue("backgroundGradientColors", out var backgroundGradientColors))
            {
                style.backgroundGradientColors = GetStringArray(
                    backgroundGradientColors,
                    path + ".backgroundGradientColors");
            }

            if (value.TryGetValue("backgroundGradientCenter", out var backgroundGradientCenter))
            {
                style.backgroundGradientCenter = GetFloatArray(
                    backgroundGradientCenter,
                    path + ".backgroundGradientCenter");
            }

            if (value.TryGetValue(
                    "backgroundGradientCenterIsPercent",
                    out var backgroundGradientCenterIsPercent))
            {
                style.backgroundGradientCenterIsPercent = GetBooleanArray(
                    backgroundGradientCenterIsPercent,
                    path + ".backgroundGradientCenterIsPercent");
            }

            if (value.TryGetValue("backgroundGradientRadius", out var backgroundGradientRadius))
            {
                style.backgroundGradientRadius = GetFloatArray(
                    backgroundGradientRadius,
                    path + ".backgroundGradientRadius");
            }

            if (value.TryGetValue(
                    "backgroundGradientRadiusIsPercent",
                    out var backgroundGradientRadiusIsPercent))
            {
                style.backgroundGradientRadiusIsPercent = GetBooleanArray(
                    backgroundGradientRadiusIsPercent,
                    path + ".backgroundGradientRadiusIsPercent");
            }

            if (value.TryGetValue("cornerRadius", out var cornerRadius))
            {
                style.cornerRadius = GetFloatArray(cornerRadius, path + ".cornerRadius");
            }

            if (value.TryGetValue("cornerRadiusPercent", out var cornerRadiusPercent))
            {
                style.cornerRadiusPercent = GetFloatArray(
                    cornerRadiusPercent,
                    path + ".cornerRadiusPercent");
            }

            if (value.TryGetValue("borderWidth", out var borderWidth))
            {
                style.borderWidth = GetFloatArray(borderWidth, path + ".borderWidth");
            }

            if (value.TryGetValue("borderColor", out var borderColor))
            {
                style.borderColor = GetStringArray(borderColor, path + ".borderColor");
            }

            if (value.TryGetValue("shadowOffsets", out var shadowOffsets))
            {
                style.shadowOffsets = GetFloatArray(shadowOffsets, path + ".shadowOffsets");
            }

            if (value.TryGetValue("shadowBlurs", out var shadowBlurs))
            {
                style.shadowBlurs = GetFloatArray(shadowBlurs, path + ".shadowBlurs");
            }

            if (value.TryGetValue("shadowSpreads", out var shadowSpreads))
            {
                style.shadowSpreads = GetFloatArray(shadowSpreads, path + ".shadowSpreads");
            }

            if (value.TryGetValue("shadowColors", out var shadowColors))
            {
                style.shadowColors = GetStringArray(shadowColors, path + ".shadowColors");
            }

            if (value.TryGetValue("shadowInsets", out var shadowInsets))
            {
                style.shadowInsets = GetBooleanArray(shadowInsets, path + ".shadowInsets");
            }

            if (value.TryGetValue("textColor", out _))
            {
                style.textColor = GetString(value, "textColor");
            }

            if (value.TryGetValue("fontSize", out var fontSize))
            {
                style.fontSize = GetFloat(fontSize, path + ".fontSize");
            }

            if (value.TryGetValue("lineHeight", out var lineHeight))
            {
                style.lineHeight = GetFloat(lineHeight, path + ".lineHeight");
            }

            if (value.TryGetValue("letterSpacing", out var letterSpacing))
            {
                style.letterSpacing = GetFloat(letterSpacing, path + ".letterSpacing");
            }

            if (value.TryGetValue("textWrap", out var textWrap))
            {
                style.textWrap = GetBoolean(textWrap, path + ".textWrap");
            }

            if (value.TryGetValue("textOverflow", out _))
            {
                style.textOverflow = GetString(value, "textOverflow");
            }

            if (value.TryGetValue("preserveWhitespace", out var preserveWhitespace))
            {
                style.preserveWhitespace = GetBoolean(
                    preserveWhitespace,
                    path + ".preserveWhitespace");
            }

            if (value.TryGetValue("alignment", out _))
            {
                style.alignment = GetString(value, "alignment");
            }

            if (value.TryGetValue("fontStyle", out _))
            {
                style.fontStyle = GetString(value, "fontStyle");
            }

            if (value.TryGetValue("fontAssetPath", out _))
            {
                style.fontAssetPath = GetString(value, "fontAssetPath");
            }

            if (value.TryGetValue("childAlignment", out _))
            {
                style.childAlignment = GetString(value, "childAlignment");
            }

            if (value.TryGetValue("justifyContent", out _))
            {
                style.justifyContent = GetString(value, "justifyContent");
            }

            if (value.TryGetValue("alignContent", out _))
            {
                style.alignContent = GetString(value, "alignContent");
            }

            if (value.TryGetValue("flexWrap", out _))
            {
                style.flexWrap = GetString(value, "flexWrap");
            }

            if (value.TryGetValue("alignSelf", out _))
            {
                style.alignSelf = GetString(value, "alignSelf");
            }

            if (value.TryGetValue("alignSelfMargin", out var alignSelfMargin))
            {
                style.alignSelfMargin = GetFloatArray(
                    alignSelfMargin,
                    path + ".alignSelfMargin");
            }

            if (value.TryGetValue("rowGap", out var rowGap))
            {
                style.rowGap = GetFloat(rowGap, path + ".rowGap");
            }

            if (value.TryGetValue("columnGap", out var columnGap))
            {
                style.columnGap = GetFloat(columnGap, path + ".columnGap");
            }

            if (value.TryGetValue("stretchChildrenWidth", out var stretchChildrenWidth))
            {
                style.stretchChildrenWidth = GetBoolean(
                    stretchChildrenWidth,
                    path + ".stretchChildrenWidth");
            }

            if (value.TryGetValue("stretchChildrenHeight", out var stretchChildrenHeight))
            {
                style.stretchChildrenHeight = GetBoolean(
                    stretchChildrenHeight,
                    path + ".stretchChildrenHeight");
            }

            if (value.TryGetValue("useResolvedChildrenWidth", out var useResolvedChildrenWidth))
            {
                style.useResolvedChildrenWidth = GetBoolean(
                    useResolvedChildrenWidth,
                    path + ".useResolvedChildrenWidth");
            }

            if (value.TryGetValue("useResolvedChildrenHeight", out var useResolvedChildrenHeight))
            {
                style.useResolvedChildrenHeight = GetBoolean(
                    useResolvedChildrenHeight,
                    path + ".useResolvedChildrenHeight");
            }

            if (value.TryGetValue("useResolvedChildPositions", out var useResolvedChildPositions))
            {
                style.useResolvedChildPositions = GetBoolean(
                    useResolvedChildPositions,
                    path + ".useResolvedChildPositions");
            }

            if (value.TryGetValue("useResolvedPosition", out var useResolvedPosition))
            {
                style.useResolvedPosition = GetBoolean(
                    useResolvedPosition,
                    path + ".useResolvedPosition");
            }

            if (value.TryGetValue("reverseChildren", out var reverseChildren))
            {
                style.reverseChildren = GetBoolean(
                    reverseChildren,
                    path + ".reverseChildren");
            }

            if (value.TryGetValue("flexOrder", out var flexOrder))
            {
                style.flexOrder = GetInteger(flexOrder, path + ".flexOrder");
            }

            if (value.TryGetValue("flexShrink", out var flexShrink))
            {
                style.flexShrink = GetFloat(flexShrink, path + ".flexShrink");
            }

            if (value.TryGetValue("flexBasis", out var flexBasis))
            {
                style.flexBasis = GetFloat(flexBasis, path + ".flexBasis");
            }

            if (value.TryGetValue("flexBasisIsPercent", out var flexBasisIsPercent))
            {
                style.flexBasisIsPercent = GetBoolean(
                    flexBasisIsPercent,
                    path + ".flexBasisIsPercent");
            }

            if (value.TryGetValue("flexibleWidth", out var flexibleWidth))
            {
                style.flexibleWidth = GetFloat(flexibleWidth, path + ".flexibleWidth");
            }

            if (value.TryGetValue("flexibleHeight", out var flexibleHeight))
            {
                style.flexibleHeight = GetFloat(flexibleHeight, path + ".flexibleHeight");
            }

            if (value.TryGetValue("transformOrigin", out var transformOrigin))
            {
                style.transformOrigin = GetFloatArray(transformOrigin, path + ".transformOrigin");
            }

            if (value.TryGetValue("transformOriginIsPercent", out var transformOriginIsPercent))
            {
                style.transformOriginIsPercent = GetBooleanArray(
                    transformOriginIsPercent,
                    path + ".transformOriginIsPercent");
            }

            if (value.TryGetValue("transformOperationTypes", out var transformOperationTypes))
            {
                style.transformOperationTypes = GetStringArray(
                    transformOperationTypes,
                    path + ".transformOperationTypes");
            }

            if (value.TryGetValue("transformOperationValues", out var transformOperationValues))
            {
                style.transformOperationValues = GetFloatArray(
                    transformOperationValues,
                    path + ".transformOperationValues");
            }

            if (value.TryGetValue(
                    "transformOperationValuesArePercent",
                    out var transformOperationValuesArePercent))
            {
                style.transformOperationValuesArePercent = GetBooleanArray(
                    transformOperationValuesArePercent,
                    path + ".transformOperationValuesArePercent");
            }

            return style;
        }

        private static string GetString(Dictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var raw) || raw == null)
            {
                return null;
            }

            return raw as string
                   ?? throw new FormatException($"'{key}' 값은 문자열이어야 한다.");
        }

        private static float[] GetFloatArray(object value, string path)
        {
            var array = RequireArray(value, path);
            var result = new float[array.Count];
            for (var index = 0; index < array.Count; index++)
            {
                result[index] = GetFloat(array[index], $"{path}[{index}]");
            }

            return result;
        }

        private static string[] GetStringArray(object value, string path)
        {
            var array = RequireArray(value, path);
            var result = new string[array.Count];
            for (var index = 0; index < array.Count; index++)
            {
                result[index] = array[index] as string
                    ?? throw new FormatException($"{path}[{index}]: a string is required.");
            }

            return result;
        }

        private static float GetFloat(object value, string path)
        {
            if (value is double number)
            {
                return (float)number;
            }

            throw new FormatException($"{path}: 숫자가 필요하다.");
        }

        private static int GetInteger(object value, string path)
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

        private static bool GetBoolean(object value, string path)
        {
            if (value is bool result)
            {
                return result;
            }

            throw new FormatException($"{path}: boolean이 필요하다.");
        }

        private static bool[] GetBooleanArray(object value, string path)
        {
            var array = RequireArray(value, path);
            var result = new bool[array.Count];
            for (var index = 0; index < array.Count; index++)
            {
                result[index] = GetBoolean(array[index], $"{path}[{index}]");
            }

            return result;
        }

        private static Dictionary<string, object> RequireObject(object value, string path)
        {
            return value as Dictionary<string, object>
                   ?? throw new FormatException($"{path}: JSON 객체가 필요하다.");
        }

        private static List<object> RequireArray(object value, string path)
        {
            return value as List<object>
                   ?? throw new FormatException($"{path}: JSON 배열이 필요하다.");
        }

        private sealed class Reader
        {
            private readonly string source;
            private int position;

            public Reader(string json)
            {
                source = json ?? string.Empty;
            }

            public object ParseValue(int depth = 0)
            {
                if (depth > 128)
                {
                    throw Error("JSON 중첩은 128단계를 넘을 수 없다.");
                }

                SkipWhitespace();
                if (position >= source.Length)
                {
                    throw Error("값이 필요한 위치에서 JSON이 끝났다.");
                }

                switch (source[position])
                {
                    case '{':
                        return ParseObject(depth + 1);
                    case '[':
                        return ParseArray(depth + 1);
                    case '"':
                        return ParseString();
                    case 't':
                        ExpectLiteral("true");
                        return true;
                    case 'f':
                        ExpectLiteral("false");
                        return false;
                    case 'n':
                        ExpectLiteral("null");
                        return null;
                    default:
                        if (source[position] == '-' || char.IsDigit(source[position]))
                        {
                            return ParseNumber();
                        }

                        throw Error($"예상하지 못한 문자 '{source[position]}'.");
                }
            }

            public void EnsureEnd()
            {
                SkipWhitespace();
                if (position != source.Length)
                {
                    throw Error("최상위 JSON 값 뒤에 불필요한 내용이 있다.");
                }
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                position++;
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (position >= source.Length || source[position] != '"')
                    {
                        throw Error("객체 속성 이름은 문자열이어야 한다.");
                    }

                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    if (result.ContainsKey(key))
                    {
                        throw Error($"중복 JSON 속성 '{key}'.");
                    }

                    result.Add(key, ParseValue(depth));
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray(int depth)
            {
                var result = new List<object>();
                position++;
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (position < source.Length)
                {
                    var character = source[position++];
                    if (character == '"')
                    {
                        return builder.ToString();
                    }

                    if (character != '\\')
                    {
                        if (character < 0x20)
                        {
                            throw Error("문자열에 제어 문자를 직접 넣을 수 없다.");
                        }

                        builder.Append(character);
                        continue;
                    }

                    if (position >= source.Length)
                    {
                        throw Error("문자열 escape가 끝나지 않았다.");
                    }

                    var escaped = source[position++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw Error($"지원하지 않는 escape '\\{escaped}'.");
                    }
                }

                throw Error("문자열이 끝나지 않았다.");
            }

            private char ParseUnicodeEscape()
            {
                if (position + 4 > source.Length)
                {
                    throw Error("유니코드 escape는 4자리여야 한다.");
                }

                var hex = source.Substring(position, 4);
                position += 4;
                if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    throw Error($"잘못된 유니코드 escape '\\u{hex}'.");
                }

                return (char)value;
            }

            private double ParseNumber()
            {
                var start = position;
                if (source[position] == '-')
                {
                    position++;
                }

                ConsumeDigits();
                if (position < source.Length && source[position] == '.')
                {
                    position++;
                    ConsumeDigits();
                }

                if (position < source.Length && (source[position] == 'e' || source[position] == 'E'))
                {
                    position++;
                    if (position < source.Length && (source[position] == '+' || source[position] == '-'))
                    {
                        position++;
                    }

                    ConsumeDigits();
                }

                var text = source.Substring(start, position - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw Error($"잘못된 숫자 '{text}'.");
                }

                return value;
            }

            private void ConsumeDigits()
            {
                var start = position;
                while (position < source.Length && char.IsDigit(source[position]))
                {
                    position++;
                }

                if (position == start)
                {
                    throw Error("숫자 자릿수가 필요하다.");
                }
            }

            private void ExpectLiteral(string literal)
            {
                if (position + literal.Length > source.Length
                    || !string.Equals(
                        source.Substring(position, literal.Length),
                        literal,
                        StringComparison.Ordinal))
                {
                    throw Error($"'{literal}'이 필요하다.");
                }

                position += literal.Length;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (!TryConsume(expected))
                {
                    throw Error($"'{expected}' 문자가 필요하다.");
                }
            }

            private bool TryConsume(char expected)
            {
                if (position < source.Length && source[position] == expected)
                {
                    position++;
                    return true;
                }

                return false;
            }

            private void SkipWhitespace()
            {
                while (position < source.Length && char.IsWhiteSpace(source[position]))
                {
                    position++;
                }
            }

            private FormatException Error(string message)
            {
                return new FormatException($"JSON 위치 {position}: {message}");
            }
        }
    }
}
