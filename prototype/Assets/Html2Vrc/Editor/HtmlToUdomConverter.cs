using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Html2Vrc.Editor
{
    public sealed class HtmlToUdomResult
    {
        public UdomDocument Document { get; internal set; }
        public string Json { get; internal set; }
        public List<UdomValidationIssue> Issues { get; } = new List<UdomValidationIssue>();
        public bool IsValid => Document != null
                               && Issues.TrueForAll(issue => issue.Severity != UdomIssueSeverity.Error);

        public string Format()
        {
            if (Issues.Count == 0)
            {
                return "HTML 변환 검증을 통과했다.";
            }

            return string.Join(Environment.NewLine, Issues.Select(issue => issue.ToString()));
        }
    }

    /// <summary>
    /// 작은 정적 HTML 부분집합을 UDOM 0.1로 바꾼다.
    /// 브라우저 호환 파서나 JavaScript 실행기는 의도적으로 포함하지 않는다.
    /// </summary>
    public static class HtmlToUdomConverter
    {
        private static readonly Regex ForbiddenElementPattern = new Regex(
            @"<\s*(script|style)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex InlineEventPattern = new Regex(
            @"\son[a-z][a-z0-9_-]*\s*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ContainerTags = new HashSet<string>(
            new[] { "main", "section", "div", "header", "footer", "nav", "article" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> TextTags = new HashSet<string>(
            new[] { "h1", "h2", "h3", "p", "span" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> InlineTextTags = new HashSet<string>(
            new[] { "strong", "b", "em", "i", "small", "br" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> WrapperTags = new HashSet<string>(
            new[] { "html", "body" },
            StringComparer.OrdinalIgnoreCase);

        public static HtmlToUdomResult Convert(string html)
        {
            var result = new HtmlToUdomResult();
            if (string.IsNullOrWhiteSpace(html))
            {
                AddError(result, "$", "HTML이 비어 있다.");
                return result;
            }

            var forbidden = ForbiddenElementPattern.Match(html);
            if (forbidden.Success)
            {
                AddError(result, "$", $"<{forbidden.Groups[1].Value}> 요소는 실행 코드 또는 외부 CSS를 포함할 수 있어 지원하지 않는다.");
            }

            if (InlineEventPattern.IsMatch(html))
            {
                AddError(result, "$", "onclick 같은 인라인 JavaScript 이벤트는 지원하지 않는다. data-action 안전 동작을 사용해야 한다.");
            }

            if (result.Issues.Count > 0)
            {
                return result;
            }

            HtmlElement parsedRoot;
            try
            {
                parsedRoot = HtmlSubsetParser.Parse(html);
            }
            catch (FormatException exception)
            {
                AddError(result, "$", exception.Message);
                return result;
            }

            var body = FindFirst(parsedRoot, "body") ?? parsedRoot;
            var roots = body.Children
                .Where(child => !string.Equals(child.Tag, "head", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (roots.Count != 1)
            {
                AddError(result, "$", $"변환할 최상위 UI 요소가 정확히 하나여야 한다. 현재 {roots.Count}개다.");
                return result;
            }

            var htmlRoot = roots[0];
            var documentId = FirstNonEmpty(
                body.Get("data-document-id"),
                htmlRoot.Get("data-document-id"),
                htmlRoot.Get("id") + "-document");
            var documentName = FirstNonEmpty(
                body.Get("data-document-name"),
                htmlRoot.Get("data-document-name"),
                htmlRoot.Get("aria-label"),
                htmlRoot.Get("id"),
                "HTML UI");

            var document = new UdomDocument
            {
                schemaVersion = "0.1",
                id = documentId,
                name = documentName,
                canvas = CreateCanvas(body, htmlRoot, result)
            };
            document.root = ConvertElement(htmlRoot, "$/" + htmlRoot.Tag, result);

            if (result.Issues.Any(issue => issue.Severity == UdomIssueSeverity.Error)
                || document.root == null)
            {
                return result;
            }

            PrepareHtmlFlexTree(document.root);
            UdomCanonicalAdapter.ResolveFlexLayoutTree(document.root);
            var json = UdomJsonWriter.Write(document);
            var udomValidation = UdomValidator.Validate(json);
            for (var index = 0; index < udomValidation.Issues.Count; index++)
            {
                result.Issues.Add(udomValidation.Issues[index]);
            }

            if (!udomValidation.IsValid)
            {
                return result;
            }

            result.Document = udomValidation.Document;
            result.Json = json;
            return result;
        }

        private static UdomCanvas CreateCanvas(
            HtmlElement body,
            HtmlElement htmlRoot,
            HtmlToUdomResult result)
        {
            var canvas = new UdomCanvas();
            var canvasSize = FirstNonEmpty(
                body.Get("data-canvas-size"),
                htmlRoot.Get("data-canvas-size"));
            if (!string.IsNullOrWhiteSpace(canvasSize))
            {
                var values = SplitNumbers(canvasSize);
                if (values == null || values.Length != 2 || values.Any(value => value <= 0f))
                {
                    AddError(result, "$.data-canvas-size", "data-canvas-size는 '1200 800'처럼 양수 두 개여야 한다.");
                }
                else
                {
                    canvas.size = values;
                }
            }

            var scaleText = FirstNonEmpty(
                body.Get("data-canvas-scale"),
                htmlRoot.Get("data-canvas-scale"));
            if (!string.IsNullOrWhiteSpace(scaleText))
            {
                if (!TryParseNumber(scaleText, out var scale) || scale <= 0f)
                {
                    AddError(result, "$.data-canvas-scale", "data-canvas-scale은 0보다 큰 숫자여야 한다.");
                }
                else
                {
                    canvas.scale = scale;
                }
            }

            return canvas;
        }

        private static UdomNode ConvertElement(
            HtmlElement element,
            string path,
            HtmlToUdomResult result)
        {
            if (WrapperTags.Contains(element.Tag))
            {
                AddError(result, path, $"<{element.Tag}>는 UI 노드 안에서 사용할 수 없다.");
                return null;
            }

            if (!TryGetNodeType(element, out var type))
            {
                AddError(result, path, $"지원하지 않는 HTML 요소 <{element.Tag}>.");
                return null;
            }

            var id = element.Get("id");
            if (string.IsNullOrWhiteSpace(id))
            {
                AddError(result, path, $"<{element.Tag}>에는 재생성을 위한 id 속성이 필요하다.");
                return null;
            }

            var node = new UdomNode
            {
                id = id,
                type = type,
                name = FirstNonEmpty(element.Get("data-name"), element.Get("aria-label"), id),
                style = CreateDefaultStyle(element, type)
            };
            ApplyStyle(element, node.style, path, result);

            if (string.Equals(type, "Text", StringComparison.Ordinal))
            {
                ValidateTextChildren(element, path, result);
                node.text = GetText(element);
            }
            else if (string.Equals(type, "Image", StringComparison.Ordinal))
            {
                var source = element.Get("src");
                if (!string.IsNullOrWhiteSpace(source)
                    && !source.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AddError(result, path + "/@src", "이미지는 Unity 프로젝트의 Assets/ 경로만 지원한다.");
                }

                node.sprite = source;
            }
            else if (string.Equals(type, "Button", StringComparison.Ordinal))
            {
                ValidateTextChildren(element, path, result);
                ConfigureButton(element, node, path, result);
            }
            else if (string.Equals(type, "Embed", StringComparison.Ordinal))
            {
                var slot = FirstNonEmpty(element.Get("data-embed-slot"), element.Get("data-target-slot"));
                node.embed = new UdomEmbed { targetSlot = slot };
                if (string.IsNullOrWhiteSpace(slot))
                {
                    AddError(result, path, "Embed에는 data-embed-slot이 필요하다.");
                }
            }
            else if (string.Equals(element.Tag, "li", StringComparison.OrdinalIgnoreCase))
            {
                ValidateTextChildren(element, path, result);
                node.children = new[]
                {
                    CreateDerivedTextNode(node, GetText(element), "-label")
                };
            }
            else
            {
                var children = new List<UdomNode>();
                for (var index = 0; index < element.Children.Count; index++)
                {
                    var child = element.Children[index];
                    if (string.Equals(child.Tag, "head", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var childNode = ConvertElement(
                        child,
                        path + "/" + child.Tag + "[" + index + "]",
                        result);
                    if (childNode != null)
                    {
                        children.Add(childNode);
                    }
                }

                if (!string.IsNullOrWhiteSpace(CollapseWhitespace(element.Text.ToString())))
                {
                    AddError(result, path, "Panel의 직접 텍스트는 지원하지 않는다. <p> 또는 <span>으로 감싸야 한다.");
                }

                node.children = children.ToArray();
            }

            return node;
        }

        private static void ConfigureButton(
            HtmlElement element,
            UdomNode node,
            string path,
            HtmlToUdomResult result)
        {
            var action = element.Get("data-action");
            if (string.IsNullOrWhiteSpace(action))
            {
                AddWarning(result, path, "버튼에 data-action이 없어 눌러도 동작하지 않는다.");
            }

            node.binding = new UdomBinding
            {
                action = action,
                targetSlot = element.Get("data-target-slot")
            };
            node.children = new[]
            {
                CreateDerivedTextNode(node, GetText(element), "-label")
            };
        }

        private static UdomNode CreateDerivedTextNode(UdomNode parent, string text, string suffix)
        {
            var width = Math.Max(1f, parent.style.size[0] - parent.style.padding[0] - parent.style.padding[2]);
            var height = Math.Max(1f, parent.style.size[1] - parent.style.padding[1] - parent.style.padding[3]);
            return new UdomNode
            {
                id = parent.id + suffix,
                type = "Text",
                name = parent.name + " Label",
                text = text,
                style = new UdomStyle
                {
                    size = new[] { width, height },
                    fontSize = parent.style.fontSize,
                    textColor = parent.style.textColor,
                    alignment = "Center",
                    backgroundColor = "#00000000"
                }
            };
        }

        private static bool TryGetNodeType(HtmlElement element, out string type)
        {
            if (!string.IsNullOrWhiteSpace(element.Get("data-embed-slot"))
                || string.Equals(element.Tag, "embed", StringComparison.OrdinalIgnoreCase))
            {
                type = "Embed";
                return true;
            }

            if (ContainerTags.Contains(element.Tag))
            {
                type = "Panel";
                return true;
            }

            if (TextTags.Contains(element.Tag))
            {
                type = "Text";
                return true;
            }

            if (string.Equals(element.Tag, "img", StringComparison.OrdinalIgnoreCase))
            {
                type = "Image";
                return true;
            }

            if (string.Equals(element.Tag, "button", StringComparison.OrdinalIgnoreCase))
            {
                type = "Button";
                return true;
            }

            if (string.Equals(element.Tag, "ul", StringComparison.OrdinalIgnoreCase)
                || string.Equals(element.Tag, "ol", StringComparison.OrdinalIgnoreCase))
            {
                type = IsTrue(element.Get("data-scroll")) ? "ScrollView" : "Panel";
                return true;
            }

            if (string.Equals(element.Tag, "li", StringComparison.OrdinalIgnoreCase))
            {
                type = "Panel";
                return true;
            }

            type = null;
            return false;
        }

        private static UdomStyle CreateDefaultStyle(HtmlElement element, string type)
        {
            var style = new UdomStyle();
            switch (type)
            {
                case "Panel":
                    style.size = string.Equals(element.Tag, "li", StringComparison.OrdinalIgnoreCase)
                        ? new[] { 900f, 64f }
                        : new[] { 1000f, 100f };
                    style.layout = string.Equals(element.Tag, "ul", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(element.Tag, "ol", StringComparison.OrdinalIgnoreCase)
                        ? "Vertical"
                        : "None";
                    style.backgroundColor = string.Equals(element.Tag, "li", StringComparison.OrdinalIgnoreCase)
                        ? "#253047FF"
                        : "#00000000";
                    if (string.Equals(element.Tag, "li", StringComparison.OrdinalIgnoreCase))
                    {
                        style.padding = new[] { 18f, 8f, 18f, 8f };
                    }

                    break;
                case "Text":
                    style.size = new[] { 1000f, DefaultTextHeight(element.Tag) };
                    style.fontSize = DefaultFontSize(element.Tag);
                    style.textColor = "#FFFFFFFF";
                    style.alignment = "MiddleLeft";
                    break;
                case "Image":
                    style.size = new[] { 100f, 100f };
                    style.backgroundColor = "#FFFFFFFF";
                    break;
                case "Button":
                    style.size = new[] { 300f, 64f };
                    style.padding = new[] { 12f, 4f, 12f, 4f };
                    style.backgroundColor = "#3A465CFF";
                    style.textColor = "#FFFFFFFF";
                    style.fontSize = 24f;
                    break;
                case "ScrollView":
                    style.size = new[] { 1000f, 300f };
                    style.layout = "Vertical";
                    style.backgroundColor = "#0E1422D9";
                    break;
                case "Embed":
                    style.size = new[] { 1000f, 28f };
                    break;
            }

            return style;
        }

        private static void ApplyStyle(
            HtmlElement element,
            UdomStyle style,
            string path,
            HtmlToUdomResult result)
        {
            var declarations = ParseStyle(element.Get("style"), path, result);
            string display = null;
            string flexDirection = null;
            string alignItems = null;
            string justifyContent = null;
            string flexWrap = null;
            string alignContent = null;
            string positionMode = null;
            float? left = null;
            float? top = null;
            float? rowGap = null;
            float? columnGap = null;
            foreach (var declaration in declarations)
            {
                switch (declaration.Key)
                {
                    case "width":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => style.size[0] = value);
                        break;
                    case "height":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => style.size[1] = value);
                        break;
                    case "left":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => left = value);
                        break;
                    case "top":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => top = value);
                        break;
                    case "display":
                        display = declaration.Value.Trim().ToLowerInvariant();
                        if (display != "flex" && display != "block" && display != "none")
                        {
                            AddError(result, path + "/@style", $"display 값 '{declaration.Value}'은 지원하지 않는다.");
                            display = null;
                        }

                        break;
                    case "flex-direction":
                        flexDirection = declaration.Value.Trim().ToLowerInvariant();
                        if (flexDirection != "row"
                            && flexDirection != "row-reverse"
                            && flexDirection != "column"
                            && flexDirection != "column-reverse")
                        {
                            AddError(result, path + "/@style", $"flex-direction 값 '{declaration.Value}'은 지원하지 않는다.");
                            flexDirection = null;
                        }

                        break;
                    case "justify-content":
                        justifyContent = NormalizeJustifyContent(declaration.Value, path, result);
                        break;
                    case "align-items":
                        alignItems = NormalizeFlexAlignment(
                            declaration.Value,
                            false,
                            declaration.Key,
                            path,
                            result);
                        break;
                    case "flex-wrap":
                        flexWrap = NormalizeFlexWrap(declaration.Value, path, result);
                        break;
                    case "align-content":
                        alignContent = NormalizeAlignContent(declaration.Value, path, result);
                        break;
                    case "align-self":
                        var alignSelf = NormalizeFlexAlignment(
                            declaration.Value,
                            true,
                            declaration.Key,
                            path,
                            result);
                        if (alignSelf != null)
                        {
                            style.alignSelf = UdomCanonicalAdapter.MapAlignSelf(
                                alignSelf,
                                path + "/@style/align-self");
                        }

                        break;
                    case "position":
                        positionMode = declaration.Value.Trim().ToLowerInvariant();
                        if (positionMode != "static" && positionMode != "absolute")
                        {
                            AddError(result, path + "/@style", $"position 값 '{declaration.Value}'은 지원하지 않는다.");
                            positionMode = null;
                        }

                        break;
                    case "gap":
                        SetGap(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            (row, column) =>
                            {
                                rowGap = row;
                                columnGap = column;
                            });
                        break;
                    case "row-gap":
                        SetPixelValue(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => rowGap = value);
                        break;
                    case "column-gap":
                        SetPixelValue(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => columnGap = value);
                        break;
                    case "padding":
                        SetEdges(declaration.Value, path, declaration.Key, result, value => style.padding = value);
                        break;
                    case "margin":
                        SetEdges(declaration.Value, path, declaration.Key, result, value => style.margin = value);
                        break;
                    case "background-color":
                        SetColor(declaration.Value, path, declaration.Key, result, value => style.backgroundColor = value);
                        break;
                    case "color":
                        SetColor(declaration.Value, path, declaration.Key, result, value => style.textColor = value);
                        break;
                    case "font-size":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => style.fontSize = value);
                        break;
                    case "text-align":
                        style.alignment = TextAlignment(declaration.Value, path, result);
                        break;
                    case "flex-grow":
                        if (!TryParseNumber(declaration.Value, out var grow) || grow < 0f)
                        {
                            AddError(result, path + "/@style", "flex-grow는 0 이상의 숫자여야 한다.");
                        }
                        else
                        {
                            style.flexibleWidth = grow;
                            style.flexibleHeight = grow;
                        }

                        break;
                    case "flex-shrink":
                        if (!TryParseNumber(declaration.Value, out var shrink) || shrink < 0f)
                        {
                            AddError(result, path + "/@style", "flex-shrink는 0 이상의 숫자여야 한다.");
                        }
                        else
                        {
                            style.flexShrink = shrink;
                        }

                        break;
                    case "flex-basis":
                        SetFlexBasis(declaration.Value, path, result, style);
                        break;
                    case "order":
                        if (!int.TryParse(
                                declaration.Value.Trim(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var order))
                        {
                            AddError(result, path + "/@style", "order는 정수여야 한다.");
                        }
                        else
                        {
                            style.flexOrder = order;
                        }

                        break;
                    default:
                        AddError(result, path + "/@style", $"지원하지 않는 CSS 속성 '{declaration.Key}'.");
                        break;
                }
            }

            var explicitLayout = element.Get("data-layout");
            var hasExplicitLayout = false;
            if (!string.IsNullOrWhiteSpace(explicitLayout))
            {
                if (string.Equals(explicitLayout, "vertical", StringComparison.OrdinalIgnoreCase))
                {
                    style.layout = "Vertical";
                    style.reverseChildren = false;
                    hasExplicitLayout = true;
                }
                else if (string.Equals(explicitLayout, "horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    style.layout = "Horizontal";
                    style.reverseChildren = false;
                    hasExplicitLayout = true;
                }
                else if (string.Equals(explicitLayout, "none", StringComparison.OrdinalIgnoreCase))
                {
                    style.layout = "None";
                    style.reverseChildren = false;
                    hasExplicitLayout = true;
                }
                else
                {
                    AddError(result, path + "/@data-layout", "data-layout은 vertical, horizontal, none만 지원한다.");
                }
            }

            if (display == "none")
            {
                style.displayNone = true;
                style.layout = "None";
                style.reverseChildren = false;
            }
            else if (!hasExplicitLayout)
            {
                if (display == "block")
                {
                    style.layout = "None";
                    style.reverseChildren = false;
                }
                else if (display == "flex")
                {
                    ApplyFlexDirection(style, flexDirection ?? "row");
                }
                else if (flexDirection != null)
                {
                    ApplyFlexDirection(style, flexDirection);
                }
            }

            var isFlexLayout = string.Equals(style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(style.layout, "Vertical", StringComparison.OrdinalIgnoreCase);
            if (isFlexLayout)
            {
                var usesCssFlexDefaults = display == "flex" || flexDirection != null;
                ConfigureFlexContainer(
                    style,
                    alignItems ?? (usesCssFlexDefaults ? "stretch" : "start"),
                    justifyContent ?? "start",
                    path);
                style.flexWrap = flexWrap ?? "NoWrap";
                style.alignContent = alignContent ?? "Stretch";
                style.rowGap = rowGap ?? 0f;
                style.columnGap = columnGap ?? 0f;
                style.spacing = string.Equals(style.layout, "Vertical", StringComparison.OrdinalIgnoreCase)
                    ? style.rowGap
                    : style.columnGap;
            }

            if (positionMode != null)
            {
                style.positionAbsolute = positionMode == "absolute";
            }

            if (left.HasValue)
            {
                style.position[0] = left.Value;
            }

            if (top.HasValue)
            {
                style.position[1] = style.positionAbsolute ? top.Value : -top.Value;
            }
        }

        private static void ApplyFlexDirection(UdomStyle style, string direction)
        {
            var isVertical = direction == "column" || direction == "column-reverse";
            style.layout = isVertical ? "Vertical" : "Horizontal";
            style.reverseChildren = direction == "row-reverse" || direction == "column-reverse";
        }

        private static void ConfigureFlexContainer(
            UdomStyle style,
            string alignItems,
            string justifyContent,
            string path)
        {
            var isVertical = string.Equals(style.layout, "Vertical", StringComparison.OrdinalIgnoreCase);
            style.justifyContent = UdomCanonicalAdapter.MapJustifyContent(
                justifyContent,
                path + "/@style/justify-content");
            style.childAlignment = UdomCanonicalAdapter.MapChildAlignment(
                isVertical,
                style.reverseChildren,
                alignItems,
                style.justifyContent,
                path + "/@style/align-items");
            var stretchesCrossAxis = alignItems == "stretch";
            style.stretchChildrenWidth = isVertical && stretchesCrossAxis;
            style.stretchChildrenHeight = !isVertical && stretchesCrossAxis;
        }

        private static string NormalizeJustifyContent(
            string value,
            string path,
            HtmlToUdomResult result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "start":
                case "flex-start":
                    return "start";
                case "center":
                    return "center";
                case "end":
                case "flex-end":
                    return "end";
                case "space-between":
                    return "space-between";
                case "space-around":
                    return "space-around";
                case "space-evenly":
                    return "space-evenly";
                default:
                    AddError(result, path + "/@style", $"justify-content 값 '{value}'은 지원하지 않는다.");
                    return null;
            }
        }

        private static string NormalizeFlexAlignment(
            string value,
            bool allowAuto,
            string property,
            string path,
            HtmlToUdomResult result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "auto" when allowAuto:
                    return "auto";
                case "normal":
                    return "stretch";
                case "start":
                case "flex-start":
                    return "start";
                case "center":
                    return "center";
                case "end":
                case "flex-end":
                    return "end";
                case "stretch":
                    return "stretch";
                default:
                    AddError(result, path + "/@style", $"{property} 값 '{value}'은 지원하지 않는다.");
                    return null;
            }
        }

        private static string NormalizeFlexWrap(
            string value,
            string path,
            HtmlToUdomResult result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "nowrap":
                    return "NoWrap";
                case "wrap":
                    return "Wrap";
                case "wrap-reverse":
                    return "WrapReverse";
                default:
                    AddError(result, path + "/@style", $"flex-wrap 값 '{value}'은 지원하지 않는다.");
                    return null;
            }
        }

        private static string NormalizeAlignContent(
            string value,
            string path,
            HtmlToUdomResult result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "normal":
                case "stretch":
                    return "Stretch";
                case "start":
                case "flex-start":
                    return "Start";
                case "center":
                    return "Center";
                case "end":
                case "flex-end":
                    return "End";
                case "space-between":
                    return "SpaceBetween";
                case "space-around":
                    return "SpaceAround";
                default:
                    AddError(result, path + "/@style", $"align-content 값 '{value}'은 지원하지 않는다.");
                    return null;
            }
        }

        private static void SetFlexBasis(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var value = source.Trim();
            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                style.flexBasis = -1f;
                style.flexBasisIsPercent = false;
                return;
            }

            var isPercent = value.EndsWith("%", StringComparison.Ordinal);
            if (isPercent)
            {
                value = value.Substring(0, value.Length - 1).Trim();
            }
            else if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2).Trim();
            }

            if (!TryParseNumber(value, out var basis) || basis < 0f)
            {
                AddError(result, path + "/@style", "flex-basis는 auto, 0 이상의 px 숫자 또는 percentage여야 한다.");
                return;
            }

            style.flexBasis = basis;
            style.flexBasisIsPercent = isPercent;
        }

        private static void PrepareHtmlFlexTree(UdomNode node)
        {
            if (node == null || (node.style != null && node.style.displayNone))
            {
                return;
            }

            var style = node.style ?? new UdomStyle();
            var isVertical = string.Equals(style.layout, "Vertical", StringComparison.OrdinalIgnoreCase);
            var isHorizontal = string.Equals(style.layout, "Horizontal", StringComparison.OrdinalIgnoreCase);
            var children = node.children ?? Array.Empty<UdomNode>();
            if (isVertical || isHorizontal)
            {
                for (var index = 0; index < children.Length; index++)
                {
                    var childStyle = children[index] != null ? children[index].style : null;
                    if (childStyle == null || childStyle.displayNone || childStyle.positionAbsolute)
                    {
                        continue;
                    }

                    if (childStyle.flexShrink < 0f)
                    {
                        childStyle.flexShrink = 1f;
                    }

                    if (isVertical)
                    {
                        childStyle.flexibleWidth = 0f;
                    }
                    else
                    {
                        childStyle.flexibleHeight = 0f;
                    }
                }
            }

            for (var index = 0; index < children.Length; index++)
            {
                PrepareHtmlFlexTree(children[index]);
            }
        }

        private static List<KeyValuePair<string, string>> ParseStyle(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            var declarations = new List<KeyValuePair<string, string>>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(source))
            {
                return declarations;
            }

            var entries = source.Split(';');
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index].Trim();
                if (entry.Length == 0)
                {
                    continue;
                }

                var separator = entry.IndexOf(':');
                if (separator <= 0 || separator == entry.Length - 1)
                {
                    AddError(result, path + "/@style", $"CSS 선언 '{entry}'의 형식이 잘못됐다.");
                    continue;
                }

                var key = entry.Substring(0, separator).Trim().ToLowerInvariant();
                var value = entry.Substring(separator + 1).Trim();
                if (!keys.Add(key))
                {
                    AddError(result, path + "/@style", $"CSS 속성 '{key}'가 중복됐다.");
                    continue;
                }

                declarations.Add(new KeyValuePair<string, string>(key, value));
            }

            return declarations;
        }

        private static void SetPixelValue(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float> setter)
        {
            var value = source.Trim();
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2).Trim();
            }

            if (!TryParseNumber(value, out var number) || number < 0f)
            {
                AddError(result, path + "/@style", $"{property}는 0 이상의 px 숫자만 지원한다.");
                return;
            }

            setter(number);
        }

        private static void SetGap(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float, float> setter)
        {
            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
            {
                AddError(result, path + "/@style", $"{property}은 1~2개의 0 이상 px 값만 지원한다.");
                return;
            }

            if (!TryParsePixelValue(parts[0], out var row))
            {
                AddError(result, path + "/@style", $"{property}은 1~2개의 0 이상 px 값만 지원한다.");
                return;
            }

            var column = row;
            if (parts.Length == 2 && !TryParsePixelValue(parts[1], out column))
            {
                AddError(result, path + "/@style", $"{property}은 1~2개의 0 이상 px 값만 지원한다.");
                return;
            }

            setter(row, column);
        }

        private static bool TryParsePixelValue(string source, out float value)
        {
            var normalized = source.Trim();
            if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            return TryParseNumber(normalized, out value) && value >= 0f;
        }

        private static void SetEdges(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float[]> setter)
        {
            var parts = source.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 4)
            {
                AddError(result, path + "/@style", $"{property}은 1~4개의 px 값만 지원한다.");
                return;
            }

            var css = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                var value = parts[index];
                if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - 2);
                }

                if (!TryParseNumber(value, out css[index]) || css[index] < 0f)
                {
                    AddError(result, path + "/@style", $"{property}은 0 이상의 px 값만 지원한다.");
                    return;
                }
            }

            float top;
            float right;
            float bottom;
            float left;
            switch (css.Length)
            {
                case 1:
                    top = right = bottom = left = css[0];
                    break;
                case 2:
                    top = bottom = css[0];
                    right = left = css[1];
                    break;
                case 3:
                    top = css[0];
                    right = left = css[1];
                    bottom = css[2];
                    break;
                default:
                    top = css[0];
                    right = css[1];
                    bottom = css[2];
                    left = css[3];
                    break;
            }

            setter(new[] { left, top, right, bottom });
        }

        private static void SetColor(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<string> setter)
        {
            if (!source.StartsWith("#", StringComparison.Ordinal)
                || !ColorUtility.TryParseHtmlString(source, out _))
            {
                AddError(result, path + "/@style", $"{property}은 #RRGGBB 또는 #RRGGBBAA 색상만 지원한다.");
                return;
            }

            setter(source);
        }

        private static string TextAlignment(string value, string path, HtmlToUdomResult result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "left":
                    return "MiddleLeft";
                case "center":
                    return "Center";
                case "right":
                    return "MiddleRight";
                default:
                    AddError(result, path + "/@style", $"text-align 값 '{value}'은 지원하지 않는다.");
                    return "MiddleLeft";
            }
        }

        private static void ValidateTextChildren(
            HtmlElement element,
            string path,
            HtmlToUdomResult result)
        {
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                if (!InlineTextTags.Contains(child.Tag)
                    && !string.Equals(child.Tag, "span", StringComparison.OrdinalIgnoreCase))
                {
                    AddError(result, path, $"텍스트 내부의 <{child.Tag}> 요소는 지원하지 않는다.");
                }
            }
        }

        private static string GetText(HtmlElement element)
        {
            var builder = new StringBuilder();
            AppendText(element, builder);
            return CollapseWhitespace(builder.ToString())
                .Replace(" \n ", "\n")
                .Replace(" \n", "\n")
                .Replace("\n ", "\n");
        }

        private static void AppendText(HtmlElement element, StringBuilder builder)
        {
            builder.Append(element.Text);
            for (var index = 0; index < element.Children.Count; index++)
            {
                var child = element.Children[index];
                if (string.Equals(child.Tag, "br", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append('\n');
                }
                else
                {
                    AppendText(child, builder);
                }
            }
        }

        private static string CollapseWhitespace(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"[ \t\r\n]+", " ").Trim();
        }

        private static float DefaultFontSize(string tag)
        {
            switch (tag.ToLowerInvariant())
            {
                case "h1":
                    return 44f;
                case "h2":
                    return 34f;
                case "h3":
                    return 28f;
                default:
                    return 24f;
            }
        }

        private static float DefaultTextHeight(string tag)
        {
            switch (tag.ToLowerInvariant())
            {
                case "h1":
                    return 64f;
                case "h2":
                    return 52f;
                case "h3":
                    return 44f;
                default:
                    return 48f;
            }
        }

        private static HtmlElement FindFirst(HtmlElement root, string tag)
        {
            if (string.Equals(root.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (var index = 0; index < root.Children.Count; index++)
            {
                var found = FindFirst(root.Children[index], tag);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static float[] SplitNumbers(string source)
        {
            var parts = source.Split(
                new[] { ' ', ',', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            var result = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!TryParseNumber(parts[index], out result[index]))
                {
                    return null;
                }
            }

            return result;
        }

        private static bool TryParseNumber(string source, out float value)
        {
            return float.TryParse(
                source,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
                   && !float.IsNaN(value)
                   && !float.IsInfinity(value);
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index];
                }
            }

            return null;
        }

        private static void AddError(HtmlToUdomResult result, string path, string message)
        {
            result.Issues.Add(new UdomValidationIssue(UdomIssueSeverity.Error, path, message));
        }

        private static void AddWarning(HtmlToUdomResult result, string path, string message)
        {
            result.Issues.Add(new UdomValidationIssue(UdomIssueSeverity.Warning, path, message));
        }

        private sealed class HtmlElement
        {
            public string Tag { get; }
            public Dictionary<string, string> Attributes { get; }
            public List<HtmlElement> Children { get; } = new List<HtmlElement>();
            public StringBuilder Text { get; } = new StringBuilder();

            public HtmlElement(string tag, Dictionary<string, string> attributes)
            {
                Tag = tag;
                Attributes = attributes;
            }

            public string Get(string key)
            {
                return Attributes.TryGetValue(key, out var value) ? value : null;
            }
        }

        private static class HtmlSubsetParser
        {
            private static readonly Regex TokenPattern = new Regex(
                @"<!--.*?-->|<!DOCTYPE.*?>|</?[A-Za-z][^>]*>|[^<]+",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            private static readonly Regex AttributePattern = new Regex(
                @"([A-Za-z_:][A-Za-z0-9_:.\-]*)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'=<>`]+)))?",
                RegexOptions.CultureInvariant);

            private static readonly HashSet<string> VoidTags = new HashSet<string>(
                new[] { "img", "br", "hr", "meta", "link", "input", "embed" },
                StringComparer.OrdinalIgnoreCase);

            public static HtmlElement Parse(string source)
            {
                var syntheticRoot = new HtmlElement("__root__", new Dictionary<string, string>());
                var stack = new Stack<HtmlElement>();
                stack.Push(syntheticRoot);
                var position = 0;
                var matches = TokenPattern.Matches(source);

                foreach (Match match in matches)
                {
                    if (match.Index != position)
                    {
                        var skipped = source.Substring(position, match.Index - position);
                        if (!string.IsNullOrWhiteSpace(skipped))
                        {
                            throw new FormatException($"HTML 위치 {position}: 해석할 수 없는 문법이 있다.");
                        }
                    }

                    position = match.Index + match.Length;
                    var token = match.Value;
                    if (token.StartsWith("<!--", StringComparison.Ordinal)
                        || token.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!token.StartsWith("<", StringComparison.Ordinal))
                    {
                        stack.Peek().Text.Append(DecodeEntities(token));
                        continue;
                    }

                    if (token.StartsWith("</", StringComparison.Ordinal))
                    {
                        var closingTag = token.Substring(2, token.Length - 3).Trim();
                        if (stack.Count <= 1
                            || !string.Equals(stack.Peek().Tag, closingTag, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new FormatException($"HTML 위치 {match.Index}: 닫는 태그 </{closingTag}>의 짝이 맞지 않는다.");
                        }

                        stack.Pop();
                        continue;
                    }

                    var selfClosing = token.EndsWith("/>", StringComparison.Ordinal);
                    var inner = token.Substring(1, token.Length - (selfClosing ? 3 : 2)).Trim();
                    var nameEnd = 0;
                    while (nameEnd < inner.Length
                           && !char.IsWhiteSpace(inner[nameEnd])
                           && inner[nameEnd] != '/')
                    {
                        nameEnd++;
                    }

                    if (nameEnd == 0)
                    {
                        throw new FormatException($"HTML 위치 {match.Index}: 요소 이름이 필요하다.");
                    }

                    var tag = inner.Substring(0, nameEnd).ToLowerInvariant();
                    var attributes = ParseAttributes(inner.Substring(nameEnd), match.Index);
                    var element = new HtmlElement(tag, attributes);
                    stack.Peek().Children.Add(element);
                    if (!selfClosing && !VoidTags.Contains(tag))
                    {
                        stack.Push(element);
                    }
                }

                if (position != source.Length && !string.IsNullOrWhiteSpace(source.Substring(position)))
                {
                    throw new FormatException($"HTML 위치 {position}: 해석할 수 없는 문법이 있다.");
                }

                if (stack.Count != 1)
                {
                    throw new FormatException($"HTML이 끝났지만 <{stack.Peek().Tag}> 태그가 닫히지 않았다.");
                }

                if (syntheticRoot.Children.Count == 1
                    && string.Equals(syntheticRoot.Children[0].Tag, "html", StringComparison.OrdinalIgnoreCase))
                {
                    return syntheticRoot.Children[0];
                }

                return syntheticRoot;
            }

            private static Dictionary<string, string> ParseAttributes(string source, int tokenPosition)
            {
                var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var position = 0;
                var matches = AttributePattern.Matches(source);
                foreach (Match match in matches)
                {
                    var skipped = source.Substring(position, match.Index - position);
                    if (!string.IsNullOrWhiteSpace(skipped) && skipped.Trim() != "/")
                    {
                        throw new FormatException($"HTML 위치 {tokenPosition}: 속성 문법 '{skipped.Trim()}'을 해석할 수 없다.");
                    }

                    position = match.Index + match.Length;
                    var name = match.Groups[1].Value;
                    if (attributes.ContainsKey(name))
                    {
                        throw new FormatException($"HTML 위치 {tokenPosition}: 속성 '{name}'이 중복됐다.");
                    }

                    var value = match.Groups[2].Success
                        ? match.Groups[2].Value
                        : match.Groups[3].Success
                            ? match.Groups[3].Value
                            : match.Groups[4].Success
                                ? match.Groups[4].Value
                                : "true";
                    attributes.Add(name, DecodeEntities(value));
                }

                if (position < source.Length)
                {
                    var tail = source.Substring(position).Trim();
                    if (tail.Length > 0 && tail != "/")
                    {
                        throw new FormatException($"HTML 위치 {tokenPosition}: 속성 문법 '{tail}'을 해석할 수 없다.");
                    }
                }

                return attributes;
            }

            private static string DecodeEntities(string source)
            {
                return Regex.Replace(source, @"&(#x[0-9a-f]+|#[0-9]+|amp|lt|gt|quot|apos|nbsp);", match =>
                {
                    var entity = match.Groups[1].Value;
                    switch (entity.ToLowerInvariant())
                    {
                        case "amp":
                            return "&";
                        case "lt":
                            return "<";
                        case "gt":
                            return ">";
                        case "quot":
                            return "\"";
                        case "apos":
                            return "'";
                        case "nbsp":
                            return " ";
                    }

                    var hex = entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase);
                    var digits = entity.Substring(hex ? 2 : 1);
                    if (int.TryParse(
                            digits,
                            hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var codePoint)
                        && codePoint >= 0
                        && codePoint <= 0x10FFFF)
                    {
                        return char.ConvertFromUtf32(codePoint);
                    }

                    return match.Value;
                }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }
    }
}
