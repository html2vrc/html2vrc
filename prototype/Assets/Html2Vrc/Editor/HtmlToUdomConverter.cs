using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
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

        private static readonly Regex TransformFunctionPattern = new Regex(
            @"([A-Za-z][A-Za-z0-9-]*)\s*\(([^()]*)\)",
            RegexOptions.CultureInvariant);

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
            ApplyStyle(element, node, path, result, out var whiteSpaceMode);

            if (string.Equals(type, "Text", StringComparison.Ordinal))
            {
                ValidateTextChildren(element, path, result);
                node.text = GetText(element, whiteSpaceMode);
            }
            else if (string.Equals(type, "Image", StringComparison.Ordinal))
            {
                var source = element.Get("src");
                if (!string.IsNullOrWhiteSpace(source)
                    && !source.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    AddError(result, path + "/@src", "이미지는 Unity 프로젝트의 Assets/ 경로만 지원한다.");
                }

                if (!string.IsNullOrWhiteSpace(source)
                    && source.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(source) != null)
                    {
                        node.sprite = source;
                    }
                    else
                    {
                        node.texture = source;
                    }
                }
            }
            else if (string.Equals(type, "Button", StringComparison.Ordinal))
            {
                ValidateTextChildren(element, path, result);
                ConfigureButton(element, node, path, result, whiteSpaceMode);
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
                    CreateDerivedTextNode(node, GetText(element, whiteSpaceMode), "-label")
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
            HtmlToUdomResult result,
            string whiteSpaceMode)
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
                CreateDerivedTextNode(node, GetText(element, whiteSpaceMode), "-label")
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
                    lineHeight = parent.style.lineHeight,
                    letterSpacing = parent.style.letterSpacing,
                    textWrap = parent.style.textWrap,
                    textOverflow = parent.style.textOverflow,
                    preserveWhitespace = parent.style.preserveWhitespace,
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
            UdomNode node,
            string path,
            HtmlToUdomResult result,
            out string whiteSpaceMode)
        {
            var style = node.style;
            var declarations = ParseStyle(element.Get("style"), path, result);
            whiteSpaceMode = "normal";
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
            float? aspectRatio = null;
            string lineHeightSource = null;
            var widthDeclared = false;
            var heightDeclared = false;
            var widthAuto = false;
            var heightAuto = false;
            var overflowX = "visible";
            var overflowY = "visible";
            var borderWidths = new[] { 3f, 3f, 3f, 3f };
            var borderColors = new[] { "currentcolor", "currentcolor", "currentcolor", "currentcolor" };
            var borderStyles = new[] { "none", "none", "none", "none" };
            var hasBorderDeclaration = false;
            List<CssBoxShadow> boxShadows = null;
            var hasBoxShadowDeclaration = false;
            CssBackgroundGradient backgroundGradient = null;
            foreach (var declaration in declarations)
            {
                switch (declaration.Key)
                {
                    case "width":
                        widthDeclared = true;
                        SetDimension(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value =>
                            {
                                style.size[0] = value;
                                widthAuto = false;
                            },
                            () => widthAuto = true);
                        break;
                    case "height":
                        heightDeclared = true;
                        SetDimension(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value =>
                            {
                                style.size[1] = value;
                                heightAuto = false;
                            },
                            () => heightAuto = true);
                        break;
                    case "min-width":
                        SetPixelValue(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => style.minSize[0] = value);
                        break;
                    case "min-height":
                        SetPixelValue(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => style.minSize[1] = value);
                        break;
                    case "max-width":
                        SetMaximumSize(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => style.maxSize[0] = value);
                        break;
                    case "max-height":
                        SetMaximumSize(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => style.maxSize[1] = value);
                        break;
                    case "aspect-ratio":
                        SetAspectRatio(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            value => aspectRatio = value);
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
                    case "visibility":
                        SetVisibility(declaration.Value, path, result, style);
                        break;
                    case "opacity":
                        SetOpacity(declaration.Value, path, result, style);
                        break;
                    case "z-index":
                        SetZIndex(declaration.Value, path, result, style);
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
                    case "overflow":
                        SetOverflow(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            (x, y) =>
                            {
                                overflowX = x;
                                overflowY = y;
                            });
                        break;
                    case "overflow-x":
                        var normalizedOverflowX = NormalizeOverflowValue(
                            declaration.Value,
                            declaration.Key,
                            path,
                            result);
                        if (normalizedOverflowX != null)
                        {
                            overflowX = normalizedOverflowX;
                        }

                        break;
                    case "overflow-y":
                        var normalizedOverflowY = NormalizeOverflowValue(
                            declaration.Value,
                            declaration.Key,
                            path,
                            result);
                        if (normalizedOverflowY != null)
                        {
                            overflowY = normalizedOverflowY;
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
                    case "background":
                        if (TrySetCssBackground(
                                declaration.Value,
                                path,
                                result,
                                style,
                                out var shorthandGradient))
                        {
                            backgroundGradient = shorthandGradient;
                        }

                        break;
                    case "background-image":
                        if (TrySetCssBackgroundImage(
                                declaration.Value,
                                path,
                                result,
                                style,
                                out var imageGradient))
                        {
                            backgroundGradient = imageGradient;
                        }

                        break;
                    case "border":
                        hasBorderDeclaration = true;
                        SetBorderShorthand(
                            declaration.Value,
                            -1,
                            path,
                            declaration.Key,
                            result,
                            borderWidths,
                            borderColors,
                            borderStyles);
                        break;
                    case "border-top":
                        hasBorderDeclaration = true;
                        SetBorderShorthand(
                            declaration.Value,
                            1,
                            path,
                            declaration.Key,
                            result,
                            borderWidths,
                            borderColors,
                            borderStyles);
                        break;
                    case "border-right":
                        hasBorderDeclaration = true;
                        SetBorderShorthand(
                            declaration.Value,
                            2,
                            path,
                            declaration.Key,
                            result,
                            borderWidths,
                            borderColors,
                            borderStyles);
                        break;
                    case "border-bottom":
                        hasBorderDeclaration = true;
                        SetBorderShorthand(
                            declaration.Value,
                            3,
                            path,
                            declaration.Key,
                            result,
                            borderWidths,
                            borderColors,
                            borderStyles);
                        break;
                    case "border-left":
                        hasBorderDeclaration = true;
                        SetBorderShorthand(
                            declaration.Value,
                            0,
                            path,
                            declaration.Key,
                            result,
                            borderWidths,
                            borderColors,
                            borderStyles);
                        break;
                    case "border-width":
                        hasBorderDeclaration = true;
                        SetBorderWidths(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            borderWidths);
                        break;
                    case "border-color":
                        hasBorderDeclaration = true;
                        SetBorderColors(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            borderColors);
                        break;
                    case "border-style":
                        hasBorderDeclaration = true;
                        SetBorderStyles(
                            declaration.Value,
                            path,
                            declaration.Key,
                            result,
                            borderStyles);
                        break;
                    case "border-top-width":
                    case "border-right-width":
                    case "border-bottom-width":
                    case "border-left-width":
                        hasBorderDeclaration = true;
                        SetBorderEdgeWidth(
                            declaration.Value,
                            BorderEdgeIndex(declaration.Key),
                            path,
                            declaration.Key,
                            result,
                            borderWidths);
                        break;
                    case "border-top-color":
                    case "border-right-color":
                    case "border-bottom-color":
                    case "border-left-color":
                        hasBorderDeclaration = true;
                        SetBorderEdgeColor(
                            declaration.Value,
                            BorderEdgeIndex(declaration.Key),
                            path,
                            declaration.Key,
                            result,
                            borderColors);
                        break;
                    case "border-top-style":
                    case "border-right-style":
                    case "border-bottom-style":
                    case "border-left-style":
                        hasBorderDeclaration = true;
                        SetBorderEdgeStyle(
                            declaration.Value,
                            BorderEdgeIndex(declaration.Key),
                            path,
                            declaration.Key,
                            result,
                            borderStyles);
                        break;
                    case "border-radius":
                        SetBorderRadius(declaration.Value, path, declaration.Key, result, style);
                        break;
                    case "border-top-left-radius":
                        SetBorderCornerRadius(declaration.Value, 0, path, declaration.Key, result, style);
                        break;
                    case "border-top-right-radius":
                        SetBorderCornerRadius(declaration.Value, 1, path, declaration.Key, result, style);
                        break;
                    case "border-bottom-right-radius":
                        SetBorderCornerRadius(declaration.Value, 2, path, declaration.Key, result, style);
                        break;
                    case "border-bottom-left-radius":
                        SetBorderCornerRadius(declaration.Value, 3, path, declaration.Key, result, style);
                        break;
                    case "box-shadow":
                        hasBoxShadowDeclaration = true;
                        boxShadows = ParseBoxShadows(declaration.Value, path, result);
                        break;
                    case "color":
                        SetColor(declaration.Value, path, declaration.Key, result, value => style.textColor = value);
                        break;
                    case "font-size":
                        SetPixelValue(declaration.Value, path, declaration.Key, result, value => style.fontSize = value);
                        break;
                    case "line-height":
                        lineHeightSource = declaration.Value;
                        break;
                    case "letter-spacing":
                        SetLetterSpacing(declaration.Value, path, result, style);
                        break;
                    case "white-space":
                        SetWhiteSpace(
                            declaration.Value,
                            path,
                            result,
                            style,
                            ref whiteSpaceMode);
                        break;
                    case "text-overflow":
                        SetTextOverflow(declaration.Value, path, result, style);
                        break;
                    case "text-align":
                        style.alignment = TextAlignment(declaration.Value, path, result);
                        break;
                    case "object-fit":
                        SetObjectFit(declaration.Value, node, path, result);
                        break;
                    case "object-position":
                        SetObjectPosition(declaration.Value, node, path, result);
                        break;
                    case "transform-origin":
                        SetTransformOrigin(declaration.Value, path, result, style);
                        break;
                    case "transform":
                        SetTransform(declaration.Value, path, result, style);
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

            if (lineHeightSource != null)
            {
                SetLineHeight(lineHeightSource, path, result, style);
            }
            if (hasBorderDeclaration)
            {
                ApplyBorder(style, borderWidths, borderColors, borderStyles);
            }
            if (hasBoxShadowDeclaration && boxShadows != null)
            {
                ApplyBoxShadows(style, boxShadows);
            }
            if (backgroundGradient != null)
            {
                ApplyCssBackgroundGradient(style, backgroundGradient);
            }

            ConfigureOverflow(style, overflowX, overflowY, path, result);

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

            ConfigureAspectRatio(
                style,
                aspectRatio,
                widthDeclared,
                widthAuto,
                heightDeclared,
                heightAuto,
                path,
                result);
            UdomCanonicalAdapter.FinalizeCanonicalBoxSize(style);
        }

        private static void SetVisibility(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            switch (source.Trim().ToLowerInvariant())
            {
                case "visible":
                    style.visible = true;
                    break;
                case "hidden":
                    style.visible = false;
                    break;
                default:
                    AddError(
                        result,
                        path + "/@style",
                        $"visibility 값 '{source}'은 지원하지 않는다. visible 또는 hidden만 사용할 수 있다.");
                    break;
            }
        }

        private static void SetOpacity(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var value = source.Trim();
            var isPercentage = value.EndsWith("%", StringComparison.Ordinal);
            if (isPercentage)
            {
                value = value.Substring(0, value.Length - 1).Trim();
            }

            if (!TryParseNumber(value, out var opacity))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"opacity 값 '{source}'은 0~1 숫자 또는 0%~100% percentage여야 한다.");
                return;
            }

            if (isPercentage)
            {
                opacity /= 100f;
            }

            if (float.IsNaN(opacity)
                || float.IsInfinity(opacity)
                || opacity < 0f
                || opacity > 1f)
            {
                AddError(
                    result,
                    path + "/@style",
                    $"opacity 값 '{source}'은 0~1 범위여야 한다.");
                return;
            }

            style.opacity = opacity;
        }

        private static void SetZIndex(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var value = source.Trim();
            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                style.zIndex = 0;
                return;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex))
            {
                AddError(result, path + "/@style", $"z-index 값 '{source}'은 auto 또는 정수여야 한다.");
                return;
            }

            style.zIndex = zIndex;
        }

        private static void SetLineHeight(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var value = source.Trim();
            if (string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase))
            {
                style.lineHeight = -1f;
                return;
            }

            float lineHeight;
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseNumber(value.Substring(0, value.Length - 2).Trim(), out lineHeight))
                {
                    AddTextMetricError(result, path, "line-height", source);
                    return;
                }
            }
            else if (value.EndsWith("%", StringComparison.Ordinal))
            {
                if (!TryParseNumber(value.Substring(0, value.Length - 1).Trim(), out var percentage))
                {
                    AddTextMetricError(result, path, "line-height", source);
                    return;
                }

                lineHeight = style.fontSize * percentage / 100f;
            }
            else
            {
                if (!TryParseNumber(value, out var multiplier))
                {
                    AddTextMetricError(result, path, "line-height", source);
                    return;
                }

                lineHeight = style.fontSize * multiplier;
            }

            if (float.IsNaN(lineHeight)
                || float.IsInfinity(lineHeight)
                || lineHeight <= 0f)
            {
                AddTextMetricError(result, path, "line-height", source);
                return;
            }

            style.lineHeight = lineHeight;
        }

        private static void SetLetterSpacing(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            if (string.Equals(source.Trim(), "normal", StringComparison.OrdinalIgnoreCase))
            {
                style.letterSpacing = 0f;
                return;
            }

            if (!TryParseSignedPixelValue(source, out var spacing)
                || float.IsNaN(spacing)
                || float.IsInfinity(spacing))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"letter-spacing 값 '{source}'은 normal 또는 유한한 숫자·px여야 한다.");
                return;
            }

            style.letterSpacing = spacing;
        }

        private static void SetWhiteSpace(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style,
            ref string whiteSpaceMode)
        {
            var value = source.Trim().ToLowerInvariant();
            switch (value)
            {
                case "normal":
                    style.preserveWhitespace = false;
                    style.textWrap = true;
                    break;
                case "nowrap":
                    style.preserveWhitespace = false;
                    style.textWrap = false;
                    break;
                case "pre":
                    style.preserveWhitespace = true;
                    style.textWrap = false;
                    break;
                case "pre-wrap":
                    style.preserveWhitespace = true;
                    style.textWrap = true;
                    break;
                case "pre-line":
                    style.preserveWhitespace = false;
                    style.textWrap = true;
                    break;
                default:
                    AddError(
                        result,
                        path + "/@style",
                        $"white-space 값 '{source}'은 normal, nowrap, pre, pre-wrap, pre-line만 지원한다.");
                    return;
            }

            whiteSpaceMode = value;
        }

        private static void SetTextOverflow(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            switch (source.Trim().ToLowerInvariant())
            {
                case "clip":
                    style.textOverflow = "Clip";
                    break;
                case "ellipsis":
                    style.textOverflow = "Ellipsis";
                    break;
                default:
                    AddError(
                        result,
                        path + "/@style",
                        $"text-overflow 값 '{source}'은 clip 또는 ellipsis여야 한다.");
                    break;
            }
        }

        private static void AddTextMetricError(
            HtmlToUdomResult result,
            string path,
            string property,
            string source)
        {
            AddError(
                result,
                path + "/@style",
                $"{property} 값 '{source}'은 normal, 양수 배수, 양수 percentage 또는 양수 px여야 한다.");
        }

        private static void SetBorderShorthand(
            string source,
            int edge,
            string path,
            string property,
            HtmlToUdomResult result,
            float[] widths,
            string[] colors,
            string[] styles)
        {
            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 3)
            {
                AddError(
                    result,
                    path + "/@style",
                    $"{property}는 width, solid|none, color를 각각 최대 한 번만 지원한다.");
                return;
            }

            var width = 3f;
            var color = "currentcolor";
            var style = "none";
            var hasWidth = false;
            var hasColor = false;
            var hasStyle = false;
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                if (TryParseBorderWidth(part, out var parsedWidth))
                {
                    if (hasWidth)
                    {
                        AddError(result, path + "/@style", $"{property}에 border width가 두 번 지정됐다.");
                        return;
                    }

                    width = parsedWidth;
                    hasWidth = true;
                    continue;
                }

                if (TryNormalizeBorderStyle(part, out var parsedStyle))
                {
                    if (hasStyle)
                    {
                        AddError(result, path + "/@style", $"{property}에 border style이 두 번 지정됐다.");
                        return;
                    }

                    style = parsedStyle;
                    hasStyle = true;
                    continue;
                }

                if (TryNormalizeBorderColor(part, out var parsedColor))
                {
                    if (hasColor)
                    {
                        AddError(result, path + "/@style", $"{property}에 border color가 두 번 지정됐다.");
                        return;
                    }

                    color = parsedColor;
                    hasColor = true;
                    continue;
                }

                AddError(
                    result,
                    path + "/@style",
                    $"{property} 값 '{part}'은 지원하지 않는다. px width, solid|none, hex color만 사용할 수 있다.");
                return;
            }

            if (edge >= 0)
            {
                widths[edge] = width;
                colors[edge] = color;
                styles[edge] = style;
                return;
            }

            for (var index = 0; index < 4; index++)
            {
                widths[index] = width;
                colors[index] = color;
                styles[index] = style;
            }
        }

        private static void SetBorderWidths(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            float[] target)
        {
            var parts = SplitCssBoxValues(source);
            if (parts.Length < 1 || parts.Length > 4)
            {
                AddError(result, path + "/@style", $"{property}는 1~4개의 0 이상 px 값만 지원한다.");
                return;
            }

            var values = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!TryParseBorderWidth(parts[index], out values[index]))
                {
                    AddError(result, path + "/@style", $"{property}는 1~4개의 0 이상 px 값만 지원한다.");
                    return;
                }
            }

            ApplyCssBoxValues(values, target);
        }

        private static void SetBorderColors(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            string[] target)
        {
            var parts = SplitCssBoxValues(source);
            if (parts.Length < 1 || parts.Length > 4)
            {
                AddError(result, path + "/@style", $"{property}는 1~4개의 hex color만 지원한다.");
                return;
            }

            var values = new string[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!TryNormalizeBorderColor(parts[index], out values[index]))
                {
                    AddError(
                        result,
                        path + "/@style",
                        $"{property}는 1~4개의 hex color, transparent 또는 currentColor만 지원한다.");
                    return;
                }
            }

            ApplyCssBoxValues(values, target);
        }

        private static void SetBorderStyles(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            string[] target)
        {
            var parts = SplitCssBoxValues(source);
            if (parts.Length < 1 || parts.Length > 4)
            {
                AddError(result, path + "/@style", $"{property}는 1~4개의 solid 또는 none 값만 지원한다.");
                return;
            }

            var values = new string[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!TryNormalizeBorderStyle(parts[index], out values[index]))
                {
                    AddError(result, path + "/@style", $"{property}는 1~4개의 solid 또는 none 값만 지원한다.");
                    return;
                }
            }

            ApplyCssBoxValues(values, target);
        }

        private static void SetBorderEdgeWidth(
            string source,
            int edge,
            string path,
            string property,
            HtmlToUdomResult result,
            float[] target)
        {
            if (!TryParseBorderWidth(source, out var value))
            {
                AddError(result, path + "/@style", $"{property}는 0 이상의 px 값만 지원한다.");
                return;
            }

            target[edge] = value;
        }

        private static void SetBorderEdgeColor(
            string source,
            int edge,
            string path,
            string property,
            HtmlToUdomResult result,
            string[] target)
        {
            if (!TryNormalizeBorderColor(source, out var value))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"{property}는 hex color, transparent 또는 currentColor만 지원한다.");
                return;
            }

            target[edge] = value;
        }

        private static void SetBorderEdgeStyle(
            string source,
            int edge,
            string path,
            string property,
            HtmlToUdomResult result,
            string[] target)
        {
            if (!TryNormalizeBorderStyle(source, out var value))
            {
                AddError(result, path + "/@style", $"{property}는 solid 또는 none만 지원한다.");
                return;
            }

            target[edge] = value;
        }

        private static void ApplyBorder(
            UdomStyle style,
            float[] widths,
            string[] colors,
            string[] styles)
        {
            style.borderWidth = new float[4];
            style.borderColor = new string[4];
            for (var index = 0; index < 4; index++)
            {
                style.borderWidth[index] = styles[index] == "solid" ? widths[index] : 0f;
                style.borderColor[index] = colors[index] == "currentcolor"
                    ? style.textColor
                    : colors[index];
            }
        }

        private static bool TryParseBorderWidth(string source, out float value)
        {
            var normalized = source.Trim();
            if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            return TryParseNumber(normalized, out value) && value >= 0f;
        }

        private static bool TryNormalizeBorderColor(string source, out string value)
        {
            var normalized = source.Trim();
            if (string.Equals(normalized, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                value = "#00000000";
                return true;
            }

            if (string.Equals(normalized, "currentcolor", StringComparison.OrdinalIgnoreCase))
            {
                value = "currentcolor";
                return true;
            }

            if (normalized.StartsWith("#", StringComparison.Ordinal)
                && ColorUtility.TryParseHtmlString(normalized, out _))
            {
                value = normalized;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryNormalizeBorderStyle(string source, out string value)
        {
            value = source.Trim().ToLowerInvariant();
            return value == "solid" || value == "none";
        }

        private static string[] SplitCssBoxValues(string source)
        {
            return source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static void ApplyCssBoxValues(float[] source, float[] target)
        {
            target[1] = source[0];
            target[2] = source.Length == 1 ? source[0] : source[1];
            target[3] = source.Length <= 2 ? source[0] : source[2];
            target[0] = source.Length == 1
                ? source[0]
                : source.Length == 4
                    ? source[3]
                    : source[1];
        }

        private static void ApplyCssBoxValues(string[] source, string[] target)
        {
            target[1] = source[0];
            target[2] = source.Length == 1 ? source[0] : source[1];
            target[3] = source.Length <= 2 ? source[0] : source[2];
            target[0] = source.Length == 1
                ? source[0]
                : source.Length == 4
                    ? source[3]
                    : source[1];
        }

        private static int BorderEdgeIndex(string property)
        {
            if (property.StartsWith("border-left-", StringComparison.Ordinal))
            {
                return 0;
            }

            if (property.StartsWith("border-top-", StringComparison.Ordinal))
            {
                return 1;
            }

            if (property.StartsWith("border-right-", StringComparison.Ordinal))
            {
                return 2;
            }

            return 3;
        }

        private static void SetBorderRadius(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var parts = SplitCssBoxValues(source);
            if (parts.Length < 1 || parts.Length > 4)
            {
                AddError(result, path + "/@style", $"{property}는 1~4개의 0 이상 px 값만 지원한다.");
                return;
            }

            var values = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!TryParseBorderRadius(parts[index], out values[index]))
                {
                    AddError(
                        result,
                        path + "/@style",
                        $"{property}는 1~4개의 0 이상 px 원형 radius만 지원한다. percentage와 '/' 타원형 radius는 지원하지 않는다.");
                    return;
                }
            }

            style.cornerRadius = new[]
            {
                values[0],
                values.Length == 1 ? values[0] : values[1],
                values.Length == 1
                    ? values[0]
                    : values.Length == 2
                        ? values[0]
                        : values[2],
                values.Length == 1
                    ? values[0]
                    : values.Length == 4
                        ? values[3]
                        : values[1]
            };
            style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
        }

        private static void SetBorderCornerRadius(
            string source,
            int corner,
            string path,
            string property,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            if (!TryParseBorderRadius(source, out var value))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"{property}는 하나의 0 이상 px 원형 radius만 지원한다.");
                return;
            }

            style.cornerRadius[corner] = value;
            style.cornerRadiusPercent[corner] = -1f;
        }

        private static bool TryParseBorderRadius(string source, out float value)
        {
            var normalized = source.Trim();
            if (normalized.IndexOf('%') >= 0 || normalized.IndexOf('/') >= 0)
            {
                value = 0f;
                return false;
            }

            if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            return TryParseNumber(normalized, out value) && value >= 0f;
        }

        private static List<CssBoxShadow> ParseBoxShadows(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            var normalized = source.Trim();
            if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
            {
                return new List<CssBoxShadow>();
            }

            var layers = normalized.Split(',');
            var shadows = new List<CssBoxShadow>(layers.Length);
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index].Trim();
                if (layer.Length == 0
                    || string.Equals(layer, "none", StringComparison.OrdinalIgnoreCase))
                {
                    AddError(
                        result,
                        path + "/@style",
                        "box-shadow의 none은 다른 shadow layer와 함께 사용할 수 없다.");
                    return null;
                }

                if (!TryParseBoxShadowLayer(layer, index, path, result, out var shadow))
                {
                    return null;
                }

                shadows.Add(shadow);
            }

            return shadows;
        }

        private static bool TryParseBoxShadowLayer(
            string source,
            int layerIndex,
            string path,
            HtmlToUdomResult result,
            out CssBoxShadow shadow)
        {
            shadow = null;
            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            var lengths = new List<float>(4);
            var inset = false;
            var hasInset = false;
            var color = "currentcolor";
            var hasColor = false;
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                if (string.Equals(part, "inset", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasInset)
                    {
                        AddBoxShadowError(result, path, layerIndex, "inset이 두 번 지정됐다.");
                        return false;
                    }

                    inset = true;
                    hasInset = true;
                    continue;
                }

                if (TryParseSignedPixelValue(part, out var length))
                {
                    if (lengths.Count >= 4)
                    {
                        AddBoxShadowError(result, path, layerIndex, "length는 offset X/Y, blur, spread의 최대 4개다.");
                        return false;
                    }

                    lengths.Add(length);
                    continue;
                }

                if (TryNormalizeBorderColor(part, out var parsedColor))
                {
                    if (hasColor)
                    {
                        AddBoxShadowError(result, path, layerIndex, "color가 두 번 지정됐다.");
                        return false;
                    }

                    color = parsedColor;
                    hasColor = true;
                    continue;
                }

                AddBoxShadowError(
                    result,
                    path,
                    layerIndex,
                    $"값 '{part}'은 지원하지 않는다. px length, inset, hex color만 사용할 수 있다.");
                return false;
            }

            if (lengths.Count < 2)
            {
                AddBoxShadowError(result, path, layerIndex, "offset X와 offset Y 두 length가 필요하다.");
                return false;
            }

            var blur = lengths.Count >= 3 ? lengths[2] : 0f;
            if (blur < 0f)
            {
                AddBoxShadowError(result, path, layerIndex, "blur radius는 음수가 될 수 없다.");
                return false;
            }

            shadow = new CssBoxShadow
            {
                OffsetX = lengths[0],
                OffsetY = lengths[1],
                Blur = blur,
                Spread = lengths.Count >= 4 ? lengths[3] : 0f,
                Color = color,
                Inset = inset
            };
            return true;
        }

        private static void AddBoxShadowError(
            HtmlToUdomResult result,
            string path,
            int layerIndex,
            string message)
        {
            AddError(result, path + "/@style", $"box-shadow layer {layerIndex + 1}: {message}");
        }

        private static bool TryParseSignedPixelValue(string source, out float value)
        {
            var normalized = source.Trim();
            if (normalized.EndsWith("%", StringComparison.Ordinal))
            {
                value = 0f;
                return false;
            }

            if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            return TryParseNumber(normalized, out value);
        }

        private static void ApplyBoxShadows(UdomStyle style, List<CssBoxShadow> cssShadows)
        {
            var count = cssShadows.Count;
            style.shadowOffsets = new float[count * 2];
            style.shadowBlurs = new float[count];
            style.shadowSpreads = new float[count];
            style.shadowColors = new string[count];
            style.shadowInsets = new bool[count];
            for (var cssIndex = 0; cssIndex < count; cssIndex++)
            {
                var canonicalIndex = count - cssIndex - 1;
                var shadow = cssShadows[cssIndex];
                style.shadowOffsets[canonicalIndex * 2] = shadow.OffsetX;
                style.shadowOffsets[canonicalIndex * 2 + 1] = shadow.OffsetY;
                style.shadowBlurs[canonicalIndex] = shadow.Blur;
                style.shadowSpreads[canonicalIndex] = shadow.Spread;
                style.shadowColors[canonicalIndex] = shadow.Color == "currentcolor"
                    ? style.textColor
                    : shadow.Color;
                style.shadowInsets[canonicalIndex] = shadow.Inset;
            }
        }

        private sealed class CssBoxShadow
        {
            public float OffsetX;
            public float OffsetY;
            public float Blur;
            public float Spread;
            public string Color;
            public bool Inset;
        }

        private static bool TrySetCssBackground(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style,
            out CssBackgroundGradient gradient)
        {
            var value = source.Trim();
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                ClearBackgroundGradient(style);
                style.backgroundColor = "#00000000";
                gradient = null;
                return true;
            }

            if (IsSupportedCssGradientFunction(value))
            {
                gradient = ParseCssBackgroundGradient(value, path, result);
                if (gradient != null)
                {
                    style.backgroundColor = "#00000000";
                    return true;
                }

                return false;
            }

            if (TryNormalizeSolidBackgroundColor(value, out var color))
            {
                ClearBackgroundGradient(style);
                style.backgroundColor = color;
                gradient = null;
                return true;
            }

            AddError(
                result,
                path + "/@style",
                $"background 값 '{source}'은 지원하지 않는다. 단일 hex color, transparent, linear-gradient, radial-gradient, conic-gradient 또는 none만 사용할 수 있다.");
            gradient = null;
            return false;
        }

        private static bool TrySetCssBackgroundImage(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style,
            out CssBackgroundGradient gradient)
        {
            var value = source.Trim();
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                ClearBackgroundGradient(style);
                gradient = null;
                return true;
            }

            if (IsSupportedCssGradientFunction(value))
            {
                gradient = ParseCssBackgroundGradient(value, path, result);
                return gradient != null;
            }

            AddError(
                result,
                path + "/@style",
                $"background-image 값 '{source}'은 지원하지 않는다. 단일 linear-gradient, radial-gradient, conic-gradient 또는 none만 사용할 수 있다.");
            gradient = null;
            return false;
        }

        private static bool IsSupportedCssGradientFunction(string source)
        {
            return source.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)
                   || source.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase)
                   || source.StartsWith("conic-gradient", StringComparison.OrdinalIgnoreCase);
        }

        private static CssBackgroundGradient ParseCssBackgroundGradient(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            if (source.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCssLinearGradient(source, path, result);
            }

            if (source.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCssRadialGradient(source, path, result);
            }

            return ParseCssConicGradient(source, path, result);
        }

        private static CssBackgroundGradient ParseCssLinearGradient(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            const string functionName = "linear-gradient";
            var value = source.Trim();
            if (!value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase)
                || !value.EndsWith(")", StringComparison.Ordinal))
            {
                AddError(result, path + "/@style", $"linear-gradient 형식 '{source}'이 잘못됐다.");
                return null;
            }

            var body = value.Substring(functionName.Length + 1, value.Length - functionName.Length - 2);
            if (body.IndexOf('(') >= 0 || body.IndexOf(')') >= 0)
            {
                AddError(
                    result,
                    path + "/@style",
                    "linear-gradient 안의 rgb(), hsl(), calc() 또는 다중 background 함수는 지원하지 않는다.");
                return null;
            }

            var parts = body.Split(',');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part)))
            {
                AddError(result, path + "/@style", "linear-gradient에 빈 항목이 있다.");
                return null;
            }

            var angle = 180f;
            var firstStop = 0;
            if (parts.Length > 0)
            {
                var parsedDirection = TryParseCssGradientDirection(
                    parts[0],
                    out var parsedAngle,
                    out var hasDirectionSyntax);
                if (hasDirectionSyntax && !parsedDirection)
                {
                    AddError(
                        result,
                        path + "/@style",
                        $"linear-gradient 방향 '{parts[0].Trim()}'은 지원하지 않는다. cardinal 방향 또는 angle을 사용해야 한다.");
                    return null;
                }

                if (parsedDirection)
                {
                    angle = Mathf.Repeat(parsedAngle, 360f);
                    firstStop = 1;
                }
            }

            if (!TryParseCssGradientStops(
                    parts,
                    firstStop,
                    functionName,
                    path,
                    result,
                    false,
                    out var colors,
                    out var positions))
            {
                return null;
            }

            return new CssBackgroundGradient
            {
                Type = "linear-gradient",
                Angle = angle,
                Colors = colors,
                Positions = positions
            };
        }

        private static CssBackgroundGradient ParseCssRadialGradient(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            const string functionName = "radial-gradient";
            var value = source.Trim();
            if (!value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase)
                || !value.EndsWith(")", StringComparison.Ordinal))
            {
                AddError(result, path + "/@style", $"radial-gradient 형식 '{source}'이 잘못됐다.");
                return null;
            }

            var body = value.Substring(functionName.Length + 1, value.Length - functionName.Length - 2);
            if (body.IndexOf('(') >= 0 || body.IndexOf(')') >= 0)
            {
                AddError(
                    result,
                    path + "/@style",
                    "radial-gradient 안의 rgb(), hsl(), calc() 또는 다중 background 함수는 지원하지 않는다.");
                return null;
            }

            var parts = body.Split(',');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part)))
            {
                AddError(result, path + "/@style", "radial-gradient에 빈 항목이 있다.");
                return null;
            }

            var gradient = new CssBackgroundGradient { Type = "radial-gradient" };
            var firstStop = 0;
            if (parts.Length > 0 && !LooksLikeSupportedGradientStop(parts[0]))
            {
                if (!TryParseCssRadialPrelude(parts[0], path, result, gradient))
                {
                    return null;
                }

                firstStop = 1;
            }

            if (!TryParseCssGradientStops(
                    parts,
                    firstStop,
                    functionName,
                    path,
                    result,
                    false,
                    out var colors,
                    out var positions))
            {
                return null;
            }

            gradient.Colors = colors;
            gradient.Positions = positions;
            return gradient;
        }

        private static CssBackgroundGradient ParseCssConicGradient(
            string source,
            string path,
            HtmlToUdomResult result)
        {
            const string functionName = "conic-gradient";
            var value = source.Trim();
            if (!value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase)
                || !value.EndsWith(")", StringComparison.Ordinal))
            {
                AddError(result, path + "/@style", $"conic-gradient 형식 '{source}'이 잘못됐다.");
                return null;
            }

            var body = value.Substring(functionName.Length + 1, value.Length - functionName.Length - 2);
            if (body.IndexOf('(') >= 0 || body.IndexOf(')') >= 0)
            {
                AddError(
                    result,
                    path + "/@style",
                    "conic-gradient 안의 rgb(), hsl(), calc() 또는 다중 background 함수는 지원하지 않는다.");
                return null;
            }

            var parts = body.Split(',');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part)))
            {
                AddError(result, path + "/@style", "conic-gradient에 빈 항목이 있다.");
                return null;
            }

            var gradient = new CssBackgroundGradient
            {
                Type = "conic-gradient",
                Angle = 0f
            };
            var firstStop = 0;
            if (parts.Length > 0 && !LooksLikeSupportedGradientStop(parts[0]))
            {
                if (!TryParseCssConicPrelude(parts[0], path, result, gradient))
                {
                    return null;
                }

                firstStop = 1;
            }

            if (!TryParseCssGradientStops(
                    parts,
                    firstStop,
                    functionName,
                    path,
                    result,
                    true,
                    out var colors,
                    out var positions))
            {
                return null;
            }

            gradient.Colors = colors;
            gradient.Positions = positions;
            return gradient;
        }

        private static bool TryParseCssConicPrelude(
            string source,
            string path,
            HtmlToUdomResult result,
            CssBackgroundGradient gradient)
        {
            var tokens = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            if (index < tokens.Length
                && string.Equals(tokens[index], "from", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Length
                    || !TryParseTransformAngle(tokens[index + 1], out var angle)
                    || float.IsNaN(angle)
                    || float.IsInfinity(angle))
                {
                    AddError(
                        result,
                        path + "/@style",
                        $"conic-gradient 시작 각도 '{source}'은 숫자 또는 deg, rad, grad, turn angle이어야 한다.");
                    return false;
                }

                gradient.Angle = Mathf.Repeat(angle, 360f);
                index += 2;
            }

            if (index >= tokens.Length
                || !string.Equals(tokens[index], "at", StringComparison.OrdinalIgnoreCase))
            {
                if (index == tokens.Length && index > 0)
                {
                    return true;
                }

                AddError(
                    result,
                    path + "/@style",
                    $"conic-gradient prelude '{source}'은 선택적 from <angle> 다음 선택적 at <position> 순서여야 한다.");
                return false;
            }

            index++;
            var positionCount = tokens.Length - index;
            if (positionCount < 1 || positionCount > 2
                || !TryParseCssGradientPosition(
                    tokens.Skip(index).ToArray(),
                    out var center,
                    out var centerIsPercent))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"conic-gradient 중심 '{source}'은 1~2개의 px, percentage 또는 방향 keyword여야 한다.");
                return false;
            }

            gradient.Center = center;
            gradient.CenterIsPercent = centerIsPercent;
            return true;
        }

        private static bool LooksLikeSupportedGradientStop(string source)
        {
            var tokens = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 && TryNormalizeBorderColor(tokens[0], out _);
        }

        private static bool TryParseCssRadialPrelude(
            string source,
            string path,
            HtmlToUdomResult result,
            CssBackgroundGradient gradient)
        {
            var tokens = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            var atIndex = -1;
            for (var index = 0; index < tokens.Length; index++)
            {
                if (!string.Equals(tokens[index], "at", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (atIndex >= 0)
                {
                    AddError(result, path + "/@style", $"radial-gradient geometry '{source}'에 at이 두 번 있다.");
                    return false;
                }

                atIndex = index;
            }

            var geometryCount = atIndex >= 0 ? atIndex : tokens.Length;
            var geometryStart = 0;
            string shape = null;
            if (geometryCount > 0
                && (string.Equals(tokens[0], "circle", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tokens[0], "ellipse", StringComparison.OrdinalIgnoreCase)))
            {
                shape = tokens[0].ToLowerInvariant();
                geometryStart = 1;
            }

            var radiusCount = geometryCount - geometryStart;
            if (shape == "circle" || (shape == null && radiusCount == 1))
            {
                if (radiusCount != 1
                    || !TryParseCssGradientLength(tokens[geometryStart], false, out var radius, out _))
                {
                    AddError(
                        result,
                        path + "/@style",
                        "radial-gradient circle은 0 이상의 단일 px 반지름이 필요하며 percentage는 지원하지 않는다.");
                    return false;
                }

                gradient.Radius = new[] { radius, radius };
                gradient.RadiusIsPercent = new[] { false, false };
            }
            else if (shape == "ellipse" || radiusCount != 0)
            {
                if (radiusCount != 2
                    || !TryParseCssGradientLength(
                        tokens[geometryStart],
                        true,
                        out var radiusX,
                        out var radiusXIsPercent)
                    || !TryParseCssGradientLength(
                        tokens[geometryStart + 1],
                        true,
                        out var radiusY,
                        out var radiusYIsPercent))
                {
                    AddError(
                        result,
                        path + "/@style",
                        "radial-gradient ellipse는 0 이상의 px 또는 percentage 반지름 두 개가 필요하다.");
                    return false;
                }

                gradient.Radius = new[] { radiusX, radiusY };
                gradient.RadiusIsPercent = new[] { radiusXIsPercent, radiusYIsPercent };
            }

            if (atIndex < 0)
            {
                return true;
            }

            var positionCount = tokens.Length - atIndex - 1;
            if (positionCount < 1 || positionCount > 2
                || !TryParseCssGradientPosition(
                    tokens.Skip(atIndex + 1).ToArray(),
                    out var center,
                    out var centerIsPercent))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"radial-gradient 중심 '{source}'은 1~2개의 px, percentage 또는 방향 keyword여야 한다.");
                return false;
            }

            gradient.Center = center;
            gradient.CenterIsPercent = centerIsPercent;
            return true;
        }

        private static bool TryParseCssGradientLength(
            string source,
            bool allowPercentage,
            out float value,
            out bool isPercent)
        {
            if (!TryParseTransformLength(source, out value, out isPercent)
                || float.IsNaN(value)
                || float.IsInfinity(value)
                || value < 0f
                || (isPercent && !allowPercentage))
            {
                value = 0f;
                isPercent = false;
                return false;
            }

            return true;
        }

        private static bool TryParseCssGradientPosition(
            string[] tokens,
            out float[] center,
            out bool[] centerIsPercent)
        {
            string xSource;
            string ySource;
            if (tokens.Length == 1)
            {
                if (IsVerticalOriginKeyword(tokens[0]))
                {
                    xSource = "center";
                    ySource = tokens[0];
                }
                else
                {
                    xSource = tokens[0];
                    ySource = "center";
                }
            }
            else if (tokens.Length == 2
                     && (IsVerticalOriginKeyword(tokens[0])
                         || IsHorizontalOriginKeyword(tokens[1])))
            {
                xSource = tokens[1];
                ySource = tokens[0];
            }
            else if (tokens.Length == 2)
            {
                xSource = tokens[0];
                ySource = tokens[1];
            }
            else
            {
                center = null;
                centerIsPercent = null;
                return false;
            }

            if (!TryParseOriginCoordinate(xSource, 0, out var x, out var xIsPercent)
                || !TryParseOriginCoordinate(ySource, 1, out var y, out var yIsPercent)
                || float.IsNaN(x)
                || float.IsInfinity(x)
                || float.IsNaN(y)
                || float.IsInfinity(y))
            {
                center = null;
                centerIsPercent = null;
                return false;
            }

            center = new[] { x, y };
            centerIsPercent = new[] { xIsPercent, yIsPercent };
            return true;
        }

        private static bool TryParseCssGradientStops(
            string[] parts,
            int firstStop,
            string functionName,
            string path,
            HtmlToUdomResult result,
            bool allowAnglePositions,
            out string[] colors,
            out float[] resolvedPositions)
        {
            var stopCount = parts.Length - firstStop;
            if (stopCount < 2)
            {
                AddError(result, path + "/@style", $"{functionName}에는 color stop이 두 개 이상 필요하다.");
                colors = null;
                resolvedPositions = null;
                return false;
            }

            colors = new string[stopCount];
            var positions = new float?[stopCount];
            for (var index = 0; index < stopCount; index++)
            {
                var stopSource = parts[firstStop + index].Trim();
                var stopParts = stopSource.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (stopParts.Length < 1 || stopParts.Length > 2
                    || !TryNormalizeBorderColor(stopParts[0], out colors[index]))
                {
                    AddError(
                        result,
                        path + "/@style",
                        allowAnglePositions
                            ? $"{functionName} stop {index + 1} '{stopSource}'은 hex color와 선택적 percentage 또는 angle 하나만 지원한다."
                            : $"{functionName} stop {index + 1} '{stopSource}'은 hex color와 선택적 percentage 하나만 지원한다.");
                    colors = null;
                    resolvedPositions = null;
                    return false;
                }

                if (stopParts.Length != 2)
                {
                    continue;
                }

                float position;
                var positionIsValid = allowAnglePositions
                    ? TryParseConicGradientStopPosition(stopParts[1], out position)
                    : TryParseGradientStopPosition(stopParts[1], out position);
                if (!positionIsValid)
                {
                    AddError(
                        result,
                        path + "/@style",
                        allowAnglePositions
                            ? $"{functionName} stop {index + 1} 위치 '{stopParts[1]}'은 0%~100% percentage 또는 0~360도 angle이어야 한다."
                            : $"{functionName} stop {index + 1} 위치 '{stopParts[1]}'은 0%~100% percentage여야 한다.");
                    colors = null;
                    resolvedPositions = null;
                    return false;
                }

                positions[index] = position;
            }

            resolvedPositions = ResolveCssGradientStopPositions(positions);
            return true;
        }

        private static bool TryParseConicGradientStopPosition(string source, out float position)
        {
            if (TryParseGradientStopPosition(source, out position))
            {
                return true;
            }

            if (TryParseTransformAngle(source, out var degrees)
                && !float.IsNaN(degrees)
                && !float.IsInfinity(degrees)
                && degrees >= 0f
                && degrees <= 360f)
            {
                position = degrees / 360f;
                return true;
            }

            position = 0f;
            return false;
        }

        private static bool TryParseCssGradientDirection(
            string source,
            out float angle,
            out bool hasDirectionSyntax)
        {
            var value = string.Join(
                " ",
                source.Trim().ToLowerInvariant().Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
            if (value.StartsWith("to ", StringComparison.Ordinal))
            {
                hasDirectionSyntax = true;
                switch (value)
                {
                    case "to top":
                        angle = 0f;
                        return true;
                    case "to right":
                        angle = 90f;
                        return true;
                    case "to bottom":
                        angle = 180f;
                        return true;
                    case "to left":
                        angle = 270f;
                        return true;
                    default:
                        angle = 0f;
                        return false;
                }
            }

            if (TryParseTransformAngle(value, out angle))
            {
                hasDirectionSyntax = true;
                return true;
            }

            angle = 0f;
            hasDirectionSyntax = false;
            return false;
        }

        private static bool TryParseGradientStopPosition(string source, out float position)
        {
            var value = source.Trim();
            if (value.EndsWith("%", StringComparison.Ordinal)
                && TryParseNumber(value.Substring(0, value.Length - 1).Trim(), out var percentage)
                && percentage >= 0f
                && percentage <= 100f)
            {
                position = percentage / 100f;
                return true;
            }

            if (TryParseNumber(value, out var zero) && Mathf.Approximately(zero, 0f))
            {
                position = 0f;
                return true;
            }

            position = 0f;
            return false;
        }

        private static float[] ResolveCssGradientStopPositions(float?[] source)
        {
            var result = new float?[source.Length];
            Array.Copy(source, result, source.Length);
            if (!result[0].HasValue)
            {
                result[0] = 0f;
            }
            if (!result[result.Length - 1].HasValue)
            {
                result[result.Length - 1] = 1f;
            }

            var previous = result[0].Value;
            for (var index = 1; index < result.Length; index++)
            {
                if (!result[index].HasValue)
                {
                    continue;
                }

                result[index] = Mathf.Max(previous, result[index].Value);
                previous = result[index].Value;
            }

            var left = 0;
            while (left < result.Length - 1)
            {
                var right = left + 1;
                while (!result[right].HasValue)
                {
                    right++;
                }

                var start = result[left].Value;
                var end = result[right].Value;
                for (var index = left + 1; index < right; index++)
                {
                    result[index] = Mathf.LerpUnclamped(
                        start,
                        end,
                        (index - left) / (float)(right - left));
                }

                left = right;
            }

            return result.Select(item => item.Value).ToArray();
        }

        private static void ApplyCssBackgroundGradient(UdomStyle style, CssBackgroundGradient gradient)
        {
            var colors = new string[gradient.Colors.Length];
            for (var index = 0; index < colors.Length; index++)
            {
                colors[index] = gradient.Colors[index] == "currentcolor"
                    ? style.textColor
                    : gradient.Colors[index];
            }

            ClearBackgroundGradient(style);
            style.backgroundType = gradient.Type;
            style.backgroundGradientAngle = gradient.Angle;
            style.backgroundGradientPositions = gradient.Positions;
            style.backgroundGradientColors = colors;
            style.backgroundGradientCenter = gradient.Center;
            style.backgroundGradientCenterIsPercent = gradient.CenterIsPercent;
            style.backgroundGradientRadius = gradient.Radius;
            style.backgroundGradientRadiusIsPercent = gradient.RadiusIsPercent;
            style.backgroundColor = colors[0];
        }

        private static void ClearBackgroundGradient(UdomStyle style)
        {
            style.backgroundType = "color";
            style.backgroundGradientAngle = 180f;
            style.backgroundGradientPositions = Array.Empty<float>();
            style.backgroundGradientColors = Array.Empty<string>();
            style.backgroundGradientCenter = new[] { 50f, 50f };
            style.backgroundGradientCenterIsPercent = new[] { true, true };
            style.backgroundGradientRadius = new[] { 50f, 50f };
            style.backgroundGradientRadiusIsPercent = new[] { true, true };
        }

        private static bool TryNormalizeSolidBackgroundColor(string source, out string color)
        {
            var value = source.Trim();
            if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = "#00000000";
                return true;
            }

            if (value.StartsWith("#", StringComparison.Ordinal)
                && ColorUtility.TryParseHtmlString(value, out _))
            {
                color = value;
                return true;
            }

            color = null;
            return false;
        }

        private sealed class CssBackgroundGradient
        {
            public string Type;
            public float Angle = 180f;
            public float[] Positions;
            public string[] Colors;
            public float[] Center = { 50f, 50f };
            public bool[] CenterIsPercent = { true, true };
            public float[] Radius = { 50f, 50f };
            public bool[] RadiusIsPercent = { true, true };
        }

        private static void SetObjectFit(
            string source,
            UdomNode node,
            string path,
            HtmlToUdomResult result)
        {
            if (!string.Equals(node.type, "Image", StringComparison.Ordinal))
            {
                AddError(result, path + "/@style", "object-fit은 <img> 요소에서만 지원한다.");
                return;
            }

            var value = source.Trim().ToLowerInvariant();
            if (value != "fill" && value != "contain" && value != "cover" && value != "none")
            {
                AddError(
                    result,
                    path + "/@style",
                    $"object-fit 값 '{source}'은 지원하지 않는다. fill, contain, cover, none만 사용할 수 있다.");
                return;
            }

            node.imageFit = value;
        }

        private static void SetObjectPosition(
            string source,
            UdomNode node,
            string path,
            HtmlToUdomResult result)
        {
            if (!string.Equals(node.type, "Image", StringComparison.Ordinal))
            {
                AddError(result, path + "/@style", "object-position은 <img> 요소에서만 지원한다.");
                return;
            }

            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
            {
                AddError(
                    result,
                    path + "/@style",
                    "object-position은 1~2개의 px, percentage 또는 방향 키워드만 지원한다.");
                return;
            }

            if (!TryNormalizeObjectPositionToken(parts[0], out var firstValue, out var firstAxis)
                || (parts.Length == 2
                    && !TryNormalizeObjectPositionToken(parts[1], out _, out _)))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"object-position 값 '{source}'을 해석할 수 없다.");
                return;
            }

            if (parts.Length == 1)
            {
                if (firstAxis == ObjectPositionAxis.Vertical)
                {
                    node.imagePositionX = "50%";
                    node.imagePositionY = firstValue;
                }
                else
                {
                    node.imagePositionX = firstValue;
                    node.imagePositionY = "50%";
                }

                return;
            }

            TryNormalizeObjectPositionToken(parts[1], out var secondValue, out var secondAxis);
            if ((firstAxis == ObjectPositionAxis.Horizontal
                 && secondAxis == ObjectPositionAxis.Horizontal)
                || (firstAxis == ObjectPositionAxis.Vertical
                    && secondAxis == ObjectPositionAxis.Vertical))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"object-position 값 '{source}'은 같은 축을 두 번 지정한다.");
                return;
            }

            if (firstAxis == ObjectPositionAxis.Horizontal)
            {
                node.imagePositionX = firstValue;
                node.imagePositionY = secondValue;
            }
            else if (firstAxis == ObjectPositionAxis.Vertical)
            {
                node.imagePositionX = secondValue;
                node.imagePositionY = firstValue;
            }
            else if (secondAxis == ObjectPositionAxis.Horizontal)
            {
                node.imagePositionX = secondValue;
                node.imagePositionY = firstValue;
            }
            else if (secondAxis == ObjectPositionAxis.Vertical)
            {
                node.imagePositionX = firstValue;
                node.imagePositionY = secondValue;
            }
            else
            {
                node.imagePositionX = firstValue;
                node.imagePositionY = secondValue;
            }
        }

        private static bool TryNormalizeObjectPositionToken(
            string source,
            out string value,
            out ObjectPositionAxis axis)
        {
            switch (source.Trim().ToLowerInvariant())
            {
                case "left":
                    value = "0%";
                    axis = ObjectPositionAxis.Horizontal;
                    return true;
                case "right":
                    value = "100%";
                    axis = ObjectPositionAxis.Horizontal;
                    return true;
                case "top":
                    value = "0%";
                    axis = ObjectPositionAxis.Vertical;
                    return true;
                case "bottom":
                    value = "100%";
                    axis = ObjectPositionAxis.Vertical;
                    return true;
                case "center":
                    value = "50%";
                    axis = ObjectPositionAxis.Either;
                    return true;
            }

            var normalized = source.Trim();
            var isPercentage = normalized.EndsWith("%", StringComparison.Ordinal);
            if (isPercentage)
            {
                normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            }
            else if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            if (!TryParseNumber(normalized, out var number))
            {
                value = null;
                axis = ObjectPositionAxis.Either;
                return false;
            }

            value = number.ToString("0.########", CultureInfo.InvariantCulture)
                    + (isPercentage ? "%" : string.Empty);
            axis = ObjectPositionAxis.Either;
            return true;
        }

        private enum ObjectPositionAxis
        {
            Either,
            Horizontal,
            Vertical
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

        private sealed class CssTransformOperation
        {
            public string Type;
            public float X;
            public float Y;
            public bool XIsPercent;
            public bool YIsPercent;
        }

        private static void SetTransformOrigin(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
            {
                AddError(result, path + "/@style", "transform-origin은 1~2개의 위치 값만 지원한다.");
                return;
            }

            string xSource;
            string ySource;
            if (parts.Length == 1)
            {
                if (IsVerticalOriginKeyword(parts[0]))
                {
                    xSource = "center";
                    ySource = parts[0];
                }
                else
                {
                    xSource = parts[0];
                    ySource = "center";
                }
            }
            else if (IsVerticalOriginKeyword(parts[0])
                     || IsHorizontalOriginKeyword(parts[1]))
            {
                xSource = parts[1];
                ySource = parts[0];
            }
            else
            {
                xSource = parts[0];
                ySource = parts[1];
            }

            if (!TryParseOriginCoordinate(xSource, 0, out var x, out var xIsPercent)
                || !TryParseOriginCoordinate(ySource, 1, out var y, out var yIsPercent))
            {
                AddError(
                    result,
                    path + "/@style",
                    $"transform-origin 값 '{source}'은 지원하지 않는다. px, 숫자, percentage와 방향 keyword만 사용할 수 있다.");
                return;
            }

            style.transformOrigin = new[] { x, y };
            style.transformOriginIsPercent = new[] { xIsPercent, yIsPercent };
        }

        private static bool IsHorizontalOriginKeyword(string source)
        {
            var value = source.Trim().ToLowerInvariant();
            return value == "left" || value == "right";
        }

        private static bool IsVerticalOriginKeyword(string source)
        {
            var value = source.Trim().ToLowerInvariant();
            return value == "top" || value == "bottom";
        }

        private static bool TryParseOriginCoordinate(
            string source,
            int axis,
            out float value,
            out bool isPercent)
        {
            var normalized = source.Trim().ToLowerInvariant();
            if (normalized == "center")
            {
                value = 50f;
                isPercent = true;
                return true;
            }

            if ((axis == 0 && normalized == "left")
                || (axis == 1 && normalized == "top"))
            {
                value = 0f;
                isPercent = true;
                return true;
            }

            if ((axis == 0 && normalized == "right")
                || (axis == 1 && normalized == "bottom"))
            {
                value = 100f;
                isPercent = true;
                return true;
            }

            return TryParseTransformLength(source, out value, out isPercent);
        }

        private static void SetTransform(
            string source,
            string path,
            HtmlToUdomResult result,
            UdomStyle style)
        {
            var normalized = source.Trim();
            if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
            {
                style.transformOperationTypes = Array.Empty<string>();
                style.transformOperationValues = Array.Empty<float>();
                style.transformOperationValuesArePercent = Array.Empty<bool>();
                return;
            }

            var matches = TransformFunctionPattern.Matches(normalized);
            if (matches.Count == 0)
            {
                AddError(result, path + "/@style", $"transform 값 '{source}'에서 함수를 찾을 수 없다.");
                return;
            }

            var operations = new List<CssTransformOperation>();
            var cursor = 0;
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                if (!string.IsNullOrWhiteSpace(normalized.Substring(cursor, match.Index - cursor)))
                {
                    AddError(result, path + "/@style", $"transform 값 '{source}'의 함수 사이 문법이 잘못됐다.");
                    return;
                }

                if (!TryParseTransformOperation(
                        match.Groups[1].Value,
                        match.Groups[2].Value,
                        path,
                        result,
                        out var operation))
                {
                    return;
                }

                operations.Add(operation);
                cursor = match.Index + match.Length;
            }

            if (!string.IsNullOrWhiteSpace(normalized.Substring(cursor)))
            {
                AddError(result, path + "/@style", $"transform 값 '{source}'의 끝 문법이 잘못됐다.");
                return;
            }

            // CSS composite transforms affect points from right to left. Canonical arrays are applied in array order.
            operations.Reverse();
            style.transformOperationTypes = new string[operations.Count];
            style.transformOperationValues = new float[operations.Count * 2];
            style.transformOperationValuesArePercent = new bool[operations.Count * 2];
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                var valueIndex = index * 2;
                style.transformOperationTypes[index] = operation.Type;
                style.transformOperationValues[valueIndex] = operation.X;
                style.transformOperationValues[valueIndex + 1] = operation.Y;
                style.transformOperationValuesArePercent[valueIndex] = operation.XIsPercent;
                style.transformOperationValuesArePercent[valueIndex + 1] = operation.YIsPercent;
            }
        }

        private static bool TryParseTransformOperation(
            string function,
            string argumentSource,
            string path,
            HtmlToUdomResult result,
            out CssTransformOperation operation)
        {
            operation = null;
            if (!TrySplitTransformArguments(argumentSource, out var arguments))
            {
                AddError(result, path + "/@style", $"transform 함수 {function}(...)의 인수 문법이 잘못됐다.");
                return false;
            }

            var name = function.Trim().ToLowerInvariant();
            if (name == "translate" || name == "translatex" || name == "translatey")
            {
                var expectedMaximum = name == "translate" ? 2 : 1;
                if (arguments.Length < 1 || arguments.Length > expectedMaximum)
                {
                    AddError(result, path + "/@style", $"{function} 함수의 인수 개수가 잘못됐다.");
                    return false;
                }

                var x = 0f;
                var y = 0f;
                var xIsPercent = false;
                var yIsPercent = false;
                if (name == "translatey")
                {
                    if (!TryParseTransformLength(arguments[0], out y, out yIsPercent))
                    {
                        AddError(result, path + "/@style", $"{function} 값 '{arguments[0]}'은 지원하지 않는다.");
                        return false;
                    }
                }
                else
                {
                    if (!TryParseTransformLength(arguments[0], out x, out xIsPercent))
                    {
                        AddError(result, path + "/@style", $"{function} 값 '{arguments[0]}'은 지원하지 않는다.");
                        return false;
                    }

                    if (name == "translate" && arguments.Length == 2
                        && !TryParseTransformLength(arguments[1], out y, out yIsPercent))
                    {
                        AddError(result, path + "/@style", $"{function} 값 '{arguments[1]}'은 지원하지 않는다.");
                        return false;
                    }
                }

                operation = new CssTransformOperation
                {
                    Type = "translate",
                    X = x,
                    Y = y,
                    XIsPercent = xIsPercent,
                    YIsPercent = yIsPercent
                };
                return true;
            }

            if (name == "rotate")
            {
                if (arguments.Length != 1 || !TryParseTransformAngle(arguments[0], out var degrees))
                {
                    AddError(result, path + "/@style", $"{function}은 하나의 숫자 또는 deg/rad/grad/turn 각도를 요구한다.");
                    return false;
                }

                operation = new CssTransformOperation { Type = "rotate", X = degrees };
                return true;
            }

            if (name == "scale" || name == "scalex" || name == "scaley")
            {
                var maximum = name == "scale" ? 2 : 1;
                if (arguments.Length < 1 || arguments.Length > maximum
                    || !TryParseNumber(arguments[0], out var first))
                {
                    AddError(result, path + "/@style", $"{function}은 1~{maximum}개의 숫자 인수를 요구한다.");
                    return false;
                }

                var x = name == "scaley" ? 1f : first;
                var y = name == "scalex" ? 1f : first;
                if (name == "scale" && arguments.Length == 2
                    && !TryParseNumber(arguments[1], out y))
                {
                    AddError(result, path + "/@style", $"{function} 값 '{arguments[1]}'은 숫자여야 한다.");
                    return false;
                }

                operation = new CssTransformOperation { Type = "scale", X = x, Y = y };
                return true;
            }

            AddError(result, path + "/@style", $"transform 함수 '{function}'은 지원하지 않는다.");
            return false;
        }

        private static bool TrySplitTransformArguments(string source, out string[] arguments)
        {
            if (source.IndexOf(',') >= 0)
            {
                var commaParts = source.Split(',');
                var result = new string[commaParts.Length];
                for (var index = 0; index < commaParts.Length; index++)
                {
                    var trimmed = commaParts[index].Trim();
                    if (trimmed.Length == 0
                        || trimmed.Split(
                            new[] { ' ', '\t', '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries).Length != 1)
                    {
                        arguments = Array.Empty<string>();
                        return false;
                    }

                    result[index] = trimmed;
                }

                arguments = result;
                return true;
            }

            arguments = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            return arguments.Length > 0;
        }

        private static bool TryParseTransformLength(
            string source,
            out float value,
            out bool isPercent)
        {
            var normalized = source.Trim();
            isPercent = normalized.EndsWith("%", StringComparison.Ordinal);
            if (isPercent)
            {
                normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            }
            else if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }

            return TryParseNumber(normalized, out value);
        }

        private static bool TryParseTransformAngle(string source, out float degrees)
        {
            var normalized = source.Trim().ToLowerInvariant();
            var multiplier = 1f;
            var suffixLength = 0;
            if (normalized.EndsWith("grad", StringComparison.Ordinal))
            {
                multiplier = 0.9f;
                suffixLength = 4;
            }
            else if (normalized.EndsWith("turn", StringComparison.Ordinal))
            {
                multiplier = 360f;
                suffixLength = 4;
            }
            else if (normalized.EndsWith("rad", StringComparison.Ordinal))
            {
                multiplier = 180f / Mathf.PI;
                suffixLength = 3;
            }
            else if (normalized.EndsWith("deg", StringComparison.Ordinal))
            {
                suffixLength = 3;
            }

            if (suffixLength > 0)
            {
                normalized = normalized.Substring(0, normalized.Length - suffixLength).Trim();
            }

            if (!TryParseNumber(normalized, out var value))
            {
                degrees = 0f;
                return false;
            }

            degrees = value * multiplier;
            return !float.IsNaN(degrees) && !float.IsInfinity(degrees);
        }

        private static void SetDimension(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float> valueSetter,
            Action autoSetter)
        {
            if (string.Equals(source.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                autoSetter();
                return;
            }

            SetPixelValue(source, path, property, result, valueSetter);
        }

        private static void SetMaximumSize(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float> setter)
        {
            if (string.Equals(source.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                setter(-1f);
                return;
            }

            SetPixelValue(source, path, property, result, setter);
        }

        private static void SetAspectRatio(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<float> setter)
        {
            var parts = source.Split('/');
            if (parts.Length < 1 || parts.Length > 2
                || !TryParseNumber(parts[0].Trim(), out var numerator)
                || numerator <= 0f)
            {
                AddError(result, path + "/@style", $"{property}는 0보다 큰 숫자 또는 'width / height' 비율이어야 한다.");
                return;
            }

            var denominator = 1f;
            if (parts.Length == 2
                && (!TryParseNumber(parts[1].Trim(), out denominator) || denominator <= 0f))
            {
                AddError(result, path + "/@style", $"{property}는 0보다 큰 숫자 또는 'width / height' 비율이어야 한다.");
                return;
            }

            var ratio = numerator / denominator;
            if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f)
            {
                AddError(result, path + "/@style", $"{property} 결과는 0보다 큰 유한한 비율이어야 한다.");
                return;
            }

            setter(ratio);
        }

        private static void ConfigureAspectRatio(
            UdomStyle style,
            float? aspectRatio,
            bool widthDeclared,
            bool widthAuto,
            bool heightDeclared,
            bool heightAuto,
            string path,
            HtmlToUdomResult result)
        {
            if (!aspectRatio.HasValue)
            {
                if (widthAuto || heightAuto)
                {
                    AddError(
                        result,
                        path + "/@style",
                        "width/height의 auto 값은 반대 축과 aspect-ratio가 함께 지정될 때만 지원한다.");
                }

                return;
            }

            var widthFixed = widthDeclared && !widthAuto;
            var heightFixed = heightDeclared && !heightAuto;
            style.aspectRatio = aspectRatio.Value;
            if (widthFixed && heightFixed)
            {
                style.autoSize = new[] { false, false };
                style.aspectRatioMode = "None";
                return;
            }

            if (widthFixed && !heightFixed)
            {
                style.autoSize = new[] { false, true };
                style.aspectRatioMode = "WidthControlsHeight";
                return;
            }

            if (heightFixed && !widthFixed)
            {
                style.autoSize = new[] { true, false };
                style.aspectRatioMode = "HeightControlsWidth";
                return;
            }

            AddError(
                result,
                path + "/@style",
                "aspect-ratio는 width 또는 height 중 정확히 한 축이 고정 크기일 때 auto 축을 계산할 수 있다.");
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

        private static void SetOverflow(
            string source,
            string path,
            string property,
            HtmlToUdomResult result,
            Action<string, string> setter)
        {
            var parts = source.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
            {
                AddError(result, path + "/@style", $"{property}는 1~2개의 overflow 값만 지원한다.");
                return;
            }

            var x = NormalizeOverflowValue(parts[0], property, path, result);
            var y = parts.Length == 1
                ? x
                : NormalizeOverflowValue(parts[1], property, path, result);
            if (x != null && y != null)
            {
                setter(x, y);
            }
        }

        private static string NormalizeOverflowValue(
            string source,
            string property,
            string path,
            HtmlToUdomResult result)
        {
            var value = source.Trim().ToLowerInvariant();
            if (value == "visible" || value == "hidden" || value == "scroll")
            {
                return value;
            }

            AddError(
                result,
                path + "/@style",
                $"{property} 값 '{source}'은 지원하지 않는다. visible, hidden, scroll만 해석할 수 있다.");
            return null;
        }

        private static void ConfigureOverflow(
            UdomStyle style,
            string overflowX,
            string overflowY,
            string path,
            HtmlToUdomResult result)
        {
            if (overflowX == "hidden" && overflowY == "hidden")
            {
                style.clipContent = true;
                return;
            }

            if (overflowX == "visible" && overflowY == "visible")
            {
                style.clipContent = false;
                return;
            }

            AddError(
                result,
                path + "/@style",
                $"overflow는 양축 visible 또는 양축 hidden 조합만 지원한다. 현재 '{overflowX}'/'{overflowY}'이다.");
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
                case "start":
                    return "MiddleLeft";
                case "center":
                    return "Center";
                case "right":
                case "end":
                    return "MiddleRight";
                case "justify":
                    return "Justified";
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

        private static string GetText(HtmlElement element, string whiteSpaceMode)
        {
            var segments = new List<StringBuilder> { new StringBuilder() };
            AppendText(element, segments);
            var normalizedSegments = segments
                .Select(segment => segment.ToString()
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n'))
                .ToArray();
            if (whiteSpaceMode == "pre" || whiteSpaceMode == "pre-wrap")
            {
                return string.Join("\n", normalizedSegments);
            }

            if (whiteSpaceMode == "pre-line")
            {
                return CollapsePreLineWhitespace(string.Join("\n", normalizedSegments));
            }

            return string.Join(
                "\n",
                normalizedSegments.Select(CollapseWhitespace).ToArray());
        }

        private static void AppendText(HtmlElement element, List<StringBuilder> segments)
        {
            for (var index = 0; index < element.Content.Count; index++)
            {
                var part = element.Content[index];
                if (part.Text != null)
                {
                    segments[segments.Count - 1].Append(part.Text);
                    continue;
                }

                var child = part.Child;
                if (string.Equals(child.Tag, "br", StringComparison.OrdinalIgnoreCase))
                {
                    segments.Add(new StringBuilder());
                }
                else
                {
                    AppendText(child, segments);
                }
            }
        }

        private static string CollapsePreLineWhitespace(string value)
        {
            var lines = value.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                lines[index] = Regex.Replace(lines[index], @"[ \t\f]+", " ").Trim(' ', '\t', '\f');
            }

            return string.Join("\n", lines);
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

        private sealed class HtmlContentPart
        {
            public string Text;
            public HtmlElement Child;
        }

        private sealed class HtmlElement
        {
            public string Tag { get; }
            public Dictionary<string, string> Attributes { get; }
            public List<HtmlElement> Children { get; } = new List<HtmlElement>();
            public List<HtmlContentPart> Content { get; } = new List<HtmlContentPart>();
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
                        var decoded = DecodeEntities(token);
                        stack.Peek().Text.Append(decoded);
                        stack.Peek().Content.Add(new HtmlContentPart { Text = decoded });
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
                    stack.Peek().Content.Add(new HtmlContentPart { Child = element });
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
