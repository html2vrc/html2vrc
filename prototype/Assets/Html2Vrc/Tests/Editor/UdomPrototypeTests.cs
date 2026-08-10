using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Html2Vrc.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

#if UDONSHARP
using Html2Vrc.VRChat;
#endif

namespace Html2Vrc.Tests
{
    public sealed class UdomPrototypeTests
    {
        private const string SampleHtmlPath = "Assets/Html2Vrc/Samples/WorldSettings.html";
        private const string CanonicalRelativeImagePath =
            "Assets/Html2Vrc/Samples/CanonicalRelativeImage.udom.json";
        private TextAsset sample;

        [SetUp]
        public void SetUp()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            sample = AssetDatabase.LoadAssetAtPath<TextAsset>(UdomSampleSceneBuilder.SampleUdomPath);
            Assert.That(sample, Is.Not.Null, "Sample UDOM must import as TextAsset.");
        }

        [TearDown]
        public void TearDown()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void SampleUdom_ValidatesWithoutIssues()
        {
            var validation = UdomValidator.Validate(sample.text);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
        }

        [Test]
        public void CanonicalReactFixture_MapsToEditableNativeUnityUi()
        {
            var json = LoadReactFixture();
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(
                validation.Issues.Any(issue => issue.Severity == UdomIssueSeverity.Error),
                Is.False,
                validation.Format());
            Assert.That(validation.Format(), Does.Contain("relative URI 'assets/logo.png'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'controls.focusContinue'"));

            var document = validation.Document;
            Assert.That(document.schemaVersion, Is.EqualTo("0.1"));
            Assert.That(document.canvas.size, Is.EqualTo(new[] { 1200f, 720f }));
            Assert.That(document.root.id, Is.EqualTo("screen"));
            Assert.That(document.root.type, Is.EqualTo("Panel"));
            Assert.That(document.root.style.layout, Is.EqualTo("Vertical"));
            Assert.That(document.root.style.spacing, Is.EqualTo(24f));
            Assert.That(document.root.style.size, Is.EqualTo(new[] { 1200f, 720f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var title = UdomBuilder.FindNode(root, "title").GetComponent<TextMeshProUGUI>();
            var logo = UdomBuilder.FindNode(root, "logo").GetComponent<Image>();
            var continueButton = UdomBuilder.FindNode(root, "continue").GetComponent<Button>();
            var continueLabel = UdomBuilder.FindNode(root, "continue-label").GetComponent<TextMeshProUGUI>();

            Assert.That(title, Is.Not.Null);
            Assert.That(title.text, Is.EqualTo("Hello VRChat"));
            Assert.That(title.fontSize, Is.EqualTo(48f));
            Assert.That(logo, Is.Not.Null);
            Assert.That(logo.color, Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(continueLabel.text, Is.EqualTo("Continue"));
        }

        [Test]
        public void ReactControlFixture_MapsEveryInteractivePrimitiveToNativeUnityUi()
        {
            var json = LoadRepositoryFile(
                "packages",
                "react",
                "test",
                "fixtures",
                "controls.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(
                validation.Issues.Any(issue => issue.Severity == UdomIssueSeverity.Error),
                Is.False,
                validation.Format());
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'settings.music'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'profile.saveName'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'gallery.offset'"));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var root = build.Root;
            var toggle = UdomBuilder.FindNode(root, "music").GetComponent<Toggle>();
            var slider = UdomBuilder.FindNode(root, "volume").GetComponent<Slider>();
            var input = UdomBuilder.FindNode(root, "profile-name").GetComponent<TMP_InputField>();
            var scroll = UdomBuilder.FindNode(root, "gallery").GetComponent<ScrollRect>();
            var embed = UdomBuilder.FindNode(root, "avatar-preview").GetComponent<UdomEmbedAnchor>();
            var fallback = UdomBuilder.FindNode(root, "avatar-preview::__embed-fallback");

            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.isOn, Is.False);
            Assert.That(toggle.interactable, Is.True);
            Assert.That(
                UdomBuilder.FindNode(root, "music-label").GetComponent<TextMeshProUGUI>().text,
                Is.EqualTo("Music"));
            Assert.That(slider, Is.Not.Null);
            Assert.That(slider.minValue, Is.EqualTo(0f));
            Assert.That(slider.maxValue, Is.EqualTo(100f));
            Assert.That(slider.value, Is.EqualTo(0f));
            Assert.That(slider.wholeNumbers, Is.False);
            Assert.That(slider.interactable, Is.True);
            Assert.That(input, Is.Not.Null);
            Assert.That(input.text, Is.Empty);
            Assert.That(input.placeholder.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Name"));
            Assert.That(input.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            Assert.That(input.readOnly, Is.False);
            Assert.That(input.interactable, Is.True);
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.content.anchoredPosition, Is.EqualTo(new Vector2(-12f, 24f)));
            Assert.That(embed, Is.Not.Null);
            Assert.That(embed.TargetSlot, Is.EqualTo("world.avatarPreview"));
            Assert.That(embed.Target, Is.Null);
            Assert.That(fallback, Is.Not.Null);
            Assert.That(embed.FallbackVisual, Is.SameAs(fallback.gameObject));
            Assert.That(fallback.gameObject.activeSelf, Is.True);
            Assert.That(
                fallback.GetComponent<TextMeshProUGUI>().text,
                Is.EqualTo("Avatar unavailable"));
            Assert.That(
                UdomBuilder.FindNode(root, "empty-status").GetComponent<TextMeshProUGUI>().text,
                Is.Empty);

            var externalTarget = new GameObject("Avatar Preview Target");
            root.SetExternalReference("world.avatarPreview", externalTarget);
            Assert.That(embed.Target, Is.SameAs(externalTarget));
            Assert.That(fallback.gameObject.activeSelf, Is.False);
            root.SetExternalReference("world.avatarPreview", null);
            Assert.That(fallback.gameObject.activeSelf, Is.True);

            var embedNode = validation.Document.root.children.Single(node => node.id == "avatar-preview");
            embedNode.embed.fallbackLabel = string.Empty;
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            embed = UdomBuilder.FindNode(root, "avatar-preview").GetComponent<UdomEmbedAnchor>();
            Assert.That(UdomBuilder.FindNode(root, "avatar-preview::__embed-fallback"), Is.Null);
            Assert.That(embed.FallbackVisual, Is.Null);
        }

        [Test]
        public void CanonicalStyleRefs_MergeInOrderBeforeInlineStyle()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-style-refs.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var shared = validation.Document.root.children[0];
            var inline = validation.Document.root.children[1];
            Assert.That(shared.style.fontSize, Is.EqualTo(34f));
            Assert.That(shared.style.textColor, Is.EqualTo("#58C8FFFF"));
            Assert.That(inline.style.fontSize, Is.EqualTo(46f));
            Assert.That(inline.style.textColor, Is.EqualTo("#FFAA00FF"));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var sharedText = UdomBuilder.FindNode(build.Root, "shared-label").GetComponent<TextMeshProUGUI>();
            var inlineText = UdomBuilder.FindNode(build.Root, "inline-label").GetComponent<TextMeshProUGUI>();
            Assert.That(sharedText.fontSize, Is.EqualTo(34f));
            Assert.That(
                sharedText.color,
                Is.EqualTo((Color)new Color32(0x58, 0xC8, 0xFF, 0xFF)).Using(ColorComparer.Instance));
            Assert.That(inlineText.fontSize, Is.EqualTo(46f));
            Assert.That(
                inlineText.color,
                Is.EqualTo((Color)new Color32(0xFF, 0xAA, 0x00, 0xFF)).Using(ColorComparer.Instance));
        }

        [Test]
        public void CanonicalSettingsFixture_GeneratesToggleWithDiagnosedVisualFallbacks()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "settings.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Format(), Does.Contain("linear-gradient"));
            Assert.That(validation.Format(), Does.Contain("square corners"));
            Assert.That(validation.Format(), Does.Contain("project default TMP font"));
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'settings.musicEnabled'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'settings.setMusicEnabled'"));

            var document = validation.Document;
            var titleNode = document.root.children[0];
            var profileCard = document.root.children[1];
            var toggleNode = profileCard.children[2];
            Assert.That(document.root.style.backgroundColor, Is.EqualTo("#171A2BFF"));
            Assert.That(profileCard.style.childAlignment, Is.EqualTo("MiddleLeft"));
            Assert.That(titleNode.style.fontStyle, Is.EqualTo("Bold"));
            Assert.That(toggleNode.type, Is.EqualTo("Toggle"));
            Assert.That(toggleNode.toggleValue, Is.True);

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var title = UdomBuilder.FindNode(build.Root, "settings-title").GetComponent<TextMeshProUGUI>();
            var profileImage = UdomBuilder.FindNode(build.Root, "profile-image").GetComponent<Image>();
            var toggle = UdomBuilder.FindNode(build.Root, "music-toggle").GetComponent<Toggle>();
            var checkmark = UdomBuilder.FindNode(build.Root, "music-toggle::__toggle-checkmark");
            Assert.That(title.text, Is.EqualTo("Settings"));
            Assert.That((title.fontStyle & FontStyles.Bold) != 0, Is.True);
            Assert.That(profileImage, Is.Not.Null);
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.isOn, Is.True);
            Assert.That(checkmark, Is.Not.Null);
            Assert.That(toggle.graphic, Is.SameAs(checkmark.GetComponent<Image>()));
        }

        [Test]
        public void CanonicalRelativeResource_ResolvesFromSourceAssetWithoutEscapingAssets()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(CanonicalRelativeImagePath);
            Assert.That(source, Is.Not.Null, "Canonical relative-resource sample must import as TextAsset.");

            var validation = UdomValidator.Validate(source.text, CanonicalRelativeImagePath);
            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Format(), Does.Not.Contain("uses relative URI"));
            var imageNode = validation.Document.root.children[0];
            Assert.That(
                imageNode.texture,
                Is.EqualTo("Assets/TextMesh Pro/Sprites/EmojiOne.png"));
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(imageNode.texture), Is.Not.Null);

            var build = UdomBuilder.GenerateOrRegenerate(
                validation.Document,
                null,
                source);
            var image = UdomBuilder.FindNode(build.Root, "relative-resource-image").GetComponent<RawImage>();
            Assert.That(image.texture, Is.Not.Null);

            var escapingJson = source.text.Replace(
                "../../TextMesh Pro/Sprites/EmojiOne.png",
                "../../../../outside.png");
            var escapingValidation = UdomValidator.Validate(escapingJson, CanonicalRelativeImagePath);
            Assert.That(escapingValidation.IsValid, Is.True, escapingValidation.Format());
            Assert.That(escapingValidation.Document.root.children[0].texture, Is.Null);
            Assert.That(escapingValidation.Format(), Does.Contain("uses relative URI '../../../../outside.png'"));
        }

        [Test]
        public void CanonicalSlider_GeneratesNativeControlAndRejectsInvalidRange()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-slider.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'settings.volume'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'settings.setVolume'"));
            var sliderNode = validation.Document.root.children[1];
            Assert.That(sliderNode.type, Is.EqualTo("Slider"));
            Assert.That(sliderNode.sliderMin, Is.EqualTo(0f));
            Assert.That(sliderNode.sliderMax, Is.EqualTo(10f));
            Assert.That(sliderNode.sliderValue, Is.EqualTo(7f));
            Assert.That(sliderNode.sliderStep, Is.EqualTo(1f));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var slider = UdomBuilder.FindNode(build.Root, "volume-slider").GetComponent<Slider>();
            var fill = UdomBuilder.FindNode(build.Root, "volume-slider::__slider-fill");
            var handle = UdomBuilder.FindNode(build.Root, "volume-slider::__slider-handle");
            Assert.That(slider, Is.Not.Null);
            Assert.That(slider.minValue, Is.EqualTo(0f));
            Assert.That(slider.maxValue, Is.EqualTo(10f));
            Assert.That(slider.value, Is.EqualTo(7f));
            Assert.That(slider.wholeNumbers, Is.True);
            Assert.That(slider.fillRect, Is.SameAs(fill.GetComponent<RectTransform>()));
            Assert.That(slider.handleRect, Is.SameAs(handle.GetComponent<RectTransform>()));
            slider.value = 8f;
            Assert.That(slider.value, Is.EqualTo(8f));

            var invalidJson = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "invalid",
                "slider-range.udom.json");
            var invalidValidation = UdomValidator.Validate(invalidJson);
            Assert.That(invalidValidation.IsValid, Is.False);
            Assert.That(invalidValidation.Format(), Does.Contain("slider max must be greater than min"));
        }

        [Test]
        public void CanonicalTextInput_GeneratesNativeSingleAndMultilineControls()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-text-input.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'profile.displayName'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'profile.previewDisplayName'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'profile.saveDisplayName'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'profile.beginDisplayNameEdit'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'profile.endDisplayNameEdit'"));

            var singleNode = validation.Document.root.children[0];
            var multilineNode = validation.Document.root.children[1];
            Assert.That(singleNode.type, Is.EqualTo("TextInput"));
            Assert.That(singleNode.textInputValue, Is.EqualTo("Nupamo"));
            Assert.That(singleNode.textInputPlaceholder, Is.EqualTo("Display name"));
            Assert.That(singleNode.textInputMultiline, Is.False);
            Assert.That(singleNode.textInputReadOnly, Is.False);
            Assert.That(singleNode.interactable, Is.True);
            Assert.That(multilineNode.textInputMultiline, Is.True);
            Assert.That(multilineNode.textInputReadOnly, Is.True);
            Assert.That(multilineNode.interactable, Is.False);

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var single = UdomBuilder.FindNode(build.Root, "display-name-input").GetComponent<TMP_InputField>();
            var singleViewport = UdomBuilder.FindNode(build.Root, "display-name-input::__input-viewport");
            var singleText = UdomBuilder.FindNode(build.Root, "display-name-input::__input-text")
                .GetComponent<TextMeshProUGUI>();
            var singlePlaceholder = UdomBuilder.FindNode(build.Root, "display-name-input::__input-placeholder")
                .GetComponent<TextMeshProUGUI>();
            Assert.That(single, Is.Not.Null);
            Assert.That(single.text, Is.EqualTo("Nupamo"));
            Assert.That(single.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            Assert.That(single.readOnly, Is.False);
            Assert.That(single.interactable, Is.True);
            Assert.That(single.textViewport, Is.SameAs(singleViewport.GetComponent<RectTransform>()));
            Assert.That(single.textComponent, Is.SameAs(singleText));
            Assert.That(single.placeholder, Is.SameAs(singlePlaceholder));
            Assert.That(singlePlaceholder.text, Is.EqualTo("Display name"));
            single.SetTextWithoutNotify("Updated name");
            Assert.That(single.text, Is.EqualTo("Updated name"));

            var multiline = UdomBuilder.FindNode(build.Root, "locked-bio-input").GetComponent<TMP_InputField>();
            Assert.That(multiline, Is.Not.Null);
            Assert.That(multiline.text, Is.EqualTo("This profile is managed externally."));
            Assert.That(multiline.lineType, Is.EqualTo(TMP_InputField.LineType.MultiLineNewline));
            Assert.That(multiline.readOnly, Is.True);
            Assert.That(multiline.interactable, Is.False);
        }

        [Test]
        public void CanonicalScroll_GeneratesBothAxesAndAppliesDesignUnitOffset()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-scroll.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'gallery.offset'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'gallery.rememberOffset'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'gallery.beginScroll'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'gallery.endScroll'"));

            var scrollNode = validation.Document.root.children[0];
            Assert.That(scrollNode.type, Is.EqualTo("ScrollView"));
            Assert.That(scrollNode.scrollAxisExplicit, Is.True);
            Assert.That(scrollNode.scrollHorizontal, Is.True);
            Assert.That(scrollNode.scrollVertical, Is.True);
            Assert.That(scrollNode.scrollInitialOffset, Is.EqualTo(new[] { 48f, 72f }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var scroll = UdomBuilder.FindNode(build.Root, "gallery-scroll").GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.content.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null);
            var fitter = scroll.content.GetComponent<ContentSizeFitter>();
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(scroll.content.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(scroll.content.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(scroll.content.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(scroll.content.anchoredPosition, Is.EqualTo(new Vector2(-48f, 72f)));
            Assert.That(scroll.content.rect.width, Is.GreaterThan(scroll.viewport.rect.width));
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));

            var legacyDocument = new UdomDocument
            {
                schemaVersion = "0.1",
                id = "legacy-horizontal-scroll",
                root = new UdomNode
                {
                    id = "legacy-horizontal-scroll-root",
                    type = "ScrollView",
                    style = new UdomStyle
                    {
                        size = new[] { 320f, 160f },
                        layout = "Horizontal"
                    }
                }
            };
            var legacyBuild = UdomBuilder.GenerateOrRegenerate(legacyDocument);
            var legacyScroll = UdomBuilder.FindNode(legacyBuild.Root, "legacy-horizontal-scroll-root")
                .GetComponent<ScrollRect>();
            Assert.That(legacyScroll.horizontal, Is.True);
            Assert.That(legacyScroll.vertical, Is.False);
        }

        [Test]
        public void CanonicalViewportFit_MapsRendererTargetSizeAndClipping()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-viewport-fit.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            Assert.That(validation.Document.canvas.size, Is.EqualTo(new[] { 400f, 200f }));
            Assert.That(validation.Document.canvas.viewportPixelRatio, Is.EqualTo(2f));
            Assert.That(validation.Document.canvas.viewportFit, Is.EqualTo("contain"));

            var targetSize = new Vector2(300f, 300f);
            var build = UdomBuilder.GenerateOrRegenerate(
                validation.Document,
                null,
                null,
                targetSize);
            var root = build.Root;
            var canvasRect = root.GetComponent<RectTransform>();
            var viewportId = "fit-root::__viewport-fit";
            var viewport = UdomBuilder.FindNode(root, viewportId);
            var content = UdomBuilder.FindNode(root, "fit-root").GetComponent<RectTransform>();
            Assert.That(root.TargetCanvasSize, Is.EqualTo(targetSize));
            Assert.That(canvasRect.sizeDelta, Is.EqualTo(targetSize));
            Assert.That(viewport, Is.Not.Null);
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Null);
            Assert.That(content.localScale, Is.EqualTo(new Vector3(0.75f, 0.75f, 1f)));

            validation.Document.canvas.viewportFit = "cover";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            viewport = UdomBuilder.FindNode(root, viewportId);
            content = UdomBuilder.FindNode(root, "fit-root").GetComponent<RectTransform>();
            Assert.That(root.TargetCanvasSize, Is.EqualTo(targetSize));
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(content.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1f)));

            validation.Document.canvas.viewportFit = "stretch";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            viewport = UdomBuilder.FindNode(root, viewportId);
            content = UdomBuilder.FindNode(root, "fit-root").GetComponent<RectTransform>();
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Null);
            Assert.That(content.localScale, Is.EqualTo(new Vector3(0.75f, 1.5f, 1f)));

            validation.Document.canvas.viewportFit = "none";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            viewport = UdomBuilder.FindNode(root, viewportId);
            content = UdomBuilder.FindNode(root, "fit-root").GetComponent<RectTransform>();
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(content.localScale, Is.EqualTo(Vector3.one));

            UdomBuilder.GenerateOrRegenerate(validation.Document, root, null, Vector2.zero);
            content = UdomBuilder.FindNode(root, "fit-root").GetComponent<RectTransform>();
            Assert.That(root.TargetCanvasSize, Is.EqualTo(Vector2.zero));
            Assert.That(canvasRect.sizeDelta, Is.EqualTo(new Vector2(400f, 200f)));
            viewport = UdomBuilder.FindNode(root, viewportId);
            Assert.That(viewport, Is.Not.Null);
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Null);
            Assert.That(content.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void CanonicalPaintState_ComposesOpacityAndPreservesUserCanvasGroup()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-paint-state.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var document = validation.Document;
            var hiddenNode = document.root.children.Single(node => node.id == "hidden-panel");
            var translucentNode = document.root.children.Single(node => node.id == "translucent-panel");
            var transparentToggleNode = document.root.children.Single(node => node.id == "transparent-toggle");
            Assert.That(document.root.style.visible, Is.True);
            Assert.That(document.root.style.opacity, Is.EqualTo(0.5f));
            Assert.That(hiddenNode.style.visible, Is.False);
            Assert.That(hiddenNode.style.opacity, Is.EqualTo(1f));
            Assert.That(translucentNode.style.opacity, Is.EqualTo(0.25f));
            Assert.That(transparentToggleNode.style.opacity, Is.EqualTo(0f));

            translucentNode.style.opacity = 1f;
            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var documentRoot = UdomBuilder.FindNode(root, "paint-root");
            var hidden = UdomBuilder.FindNode(root, "hidden-panel");
            var translucent = UdomBuilder.FindNode(root, "translucent-panel");
            var transparentToggle = UdomBuilder.FindNode(root, "transparent-toggle");
            var rootGroup = documentRoot.GetComponent<CanvasGroup>();
            var hiddenGroup = hidden.GetComponent<CanvasGroup>();
            var transparentGroup = transparentToggle.GetComponent<CanvasGroup>();

            Assert.That(rootGroup, Is.Not.Null);
            Assert.That(rootGroup.alpha, Is.EqualTo(0.5f));
            Assert.That(documentRoot.GetComponent<UdomPaintState>().CreatedCanvasGroup, Is.True);
            Assert.That(hidden.gameObject.activeInHierarchy, Is.True);
            Assert.That(hidden.GetComponent<LayoutElement>().ignoreLayout, Is.False);
            Assert.That(UdomBuilder.FindNode(root, "hidden-label").gameObject.activeInHierarchy, Is.True);
            Assert.That(hiddenGroup.alpha, Is.EqualTo(0f));
            Assert.That(hiddenGroup.interactable, Is.False);
            Assert.That(hiddenGroup.blocksRaycasts, Is.False);
            Assert.That(transparentGroup.alpha, Is.EqualTo(0f));
            Assert.That(transparentGroup.interactable, Is.True);
            Assert.That(transparentGroup.blocksRaycasts, Is.True);
            Assert.That(transparentToggle.GetComponent<Toggle>().interactable, Is.True);
            Assert.That(translucent.GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(translucent.GetComponent<UdomPaintState>(), Is.Null);

            var userGroup = translucent.gameObject.AddComponent<CanvasGroup>();
            userGroup.alpha = 0.8f;
            userGroup.interactable = false;
            userGroup.blocksRaycasts = false;
            userGroup.ignoreParentGroups = true;
            translucentNode.style.opacity = 0.25f;
            UdomBuilder.GenerateOrRegenerate(document, root);

            translucent = UdomBuilder.FindNode(root, "translucent-panel");
            var composedGroup = translucent.GetComponent<CanvasGroup>();
            var composedState = translucent.GetComponent<UdomPaintState>();
            Assert.That(composedGroup, Is.SameAs(userGroup));
            Assert.That(composedGroup.alpha, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(composedGroup.interactable, Is.False);
            Assert.That(composedGroup.blocksRaycasts, Is.False);
            Assert.That(composedGroup.ignoreParentGroups, Is.False);
            Assert.That(composedState.CreatedCanvasGroup, Is.False);
            Assert.That(composedState.BaseAlpha, Is.EqualTo(0.8f));

            translucentNode.style.opacity = 1f;
            UdomBuilder.GenerateOrRegenerate(document, root);
            translucent = UdomBuilder.FindNode(root, "translucent-panel");
            var restoredGroup = translucent.GetComponent<CanvasGroup>();
            Assert.That(restoredGroup, Is.SameAs(userGroup));
            Assert.That(restoredGroup.alpha, Is.EqualTo(0.8f));
            Assert.That(restoredGroup.interactable, Is.False);
            Assert.That(restoredGroup.blocksRaycasts, Is.False);
            Assert.That(restoredGroup.ignoreParentGroups, Is.True);
            Assert.That(translucent.GetComponent<UdomPaintState>(), Is.Null);

            document.root.style.opacity = 1f;
            hiddenNode.style.visible = true;
            transparentToggleNode.style.opacity = 1f;
            UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(UdomBuilder.FindNode(root, "paint-root").GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-panel").GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transparent-toggle").GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "paint-root").GetComponent<UdomPaintState>(), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-panel").GetComponent<UdomPaintState>(), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transparent-toggle").GetComponent<UdomPaintState>(), Is.Null);
        }

        [Test]
        public void CanonicalUdom_RejectsUnknownElementName()
        {
            const string json = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 400, ""height"": 300 },
              ""root"": { ""type"": ""element"", ""id"": ""video"", ""name"": ""video"" }
            }";

            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Format(), Does.Contain("unknown canonical element 'video'"));
        }

        [Test]
        public void SampleHtml_ConvertsToValidUdom()
        {
            var html = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleHtmlPath);
            Assert.That(html, Is.Not.Null, "Sample HTML must import as TextAsset.");

            var conversion = HtmlToUdomConverter.Convert(html.text);

            Assert.That(conversion.IsValid, Is.True, conversion.Format());
            Assert.That(conversion.Document, Is.Not.Null);
            Assert.That(conversion.Document.id, Is.EqualTo("world-settings-html"));
            Assert.That(conversion.Document.root.type, Is.EqualTo("Panel"));
            Assert.That(conversion.Document.root.style.layout, Is.EqualTo("Vertical"));
            Assert.That(conversion.Json, Does.Contain("\"ToggleLight\""));
            Assert.That(conversion.Json, Does.Contain("\"ScrollView\""));

            var validation = UdomValidator.Validate(conversion.Json);
            Assert.That(validation.IsValid, Is.True, validation.Format());
        }

        [Test]
        public void HtmlConverter_GeneratesNativeUiAndRegeneratesWithoutDuplicates()
        {
            var html = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleHtmlPath);
            var conversion = HtmlToUdomConverter.Convert(html.text);
            Assert.That(conversion.IsValid, Is.True, conversion.Format());

            var externalLight = new GameObject("HTML External Light", typeof(Light));
            var first = UdomBuilder.GenerateOrRegenerate(conversion.Document, null, html);
            first.Root.SetExternalReference("world-light", externalLight);
            var originalButton = UdomBuilder.FindNode(first.Root, "html-toggle-light");
            var markerCount = first.Root.GetComponentsInChildren<UdomGeneratedNode>(true).Length;

            Assert.That(first.Root.SourceAsset, Is.SameAs(html));
            Assert.That(UdomBuilder.FindNode(first.Root, "html-title").GetComponent<TextMeshProUGUI>(), Is.Not.Null);
            Assert.That(originalButton.GetComponent<Button>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(first.Root, "html-settings-list").GetComponent<ScrollRect>(), Is.Not.Null);
            Assert.That(
                UdomBuilder.FindNode(first.Root, "html-world-light-embed").GetComponent<UdomEmbedAnchor>().Target,
                Is.SameAs(externalLight));

            var changed = HtmlToUdomConverter.Convert(
                html.text
                    .Replace("      World Settings", "      World Settings — HTML Regenerated")
                    .Replace("#182033F2", "#4A1538F2"));
            Assert.That(changed.IsValid, Is.True, changed.Format());

            var second = UdomBuilder.GenerateOrRegenerate(changed.Document, first.Root, html);
            Assert.That(second.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(first.Root, "html-toggle-light"), Is.SameAs(originalButton));
            Assert.That(
                UdomBuilder.FindNode(first.Root, "html-title").GetComponent<TextMeshProUGUI>().text,
                Is.EqualTo("World Settings — HTML Regenerated"));
            Assert.That(first.Root.Resolve("world-light"), Is.SameAs(externalLight));
            Assert.That(
                first.Root.GetComponentsInChildren<UdomGeneratedNode>(true).Length,
                Is.EqualTo(markerCount));
        }

        [Test]
        public void HtmlConverter_RejectsJavaScriptAndMissingStableId()
        {
            const string scriptHtml = "<main id=\"root\"><button id=\"go\" onclick=\"run()\">Go</button></main>";
            var scriptResult = HtmlToUdomConverter.Convert(scriptHtml);
            Assert.That(scriptResult.IsValid, Is.False);
            Assert.That(scriptResult.Format(), Does.Contain("인라인 JavaScript"));

            const string missingIdHtml = "<main id=\"root\"><p>No stable id</p></main>";
            var missingIdResult = HtmlToUdomConverter.Convert(missingIdHtml);
            Assert.That(missingIdResult.IsValid, Is.False);
            Assert.That(missingIdResult.Format(), Does.Contain("id 속성이 필요"));
        }

        [Test]
        public void Validator_RejectsUnknownPropertyAndDuplicateId()
        {
            const string invalidJson = @"{
              ""schemaVersion"": ""0.1"",
              ""id"": ""doc"",
              ""name"": ""Invalid"",
              ""canvas"": { ""renderMode"": ""WorldSpace"", ""size"": [100, 100], ""scale"": 0.01 },
              ""root"": {
                ""id"": ""same"",
                ""type"": ""Panel"",
                ""unsupportedCss"": ""blur(4px)"",
                ""children"": [
                  { ""id"": ""same"", ""type"": ""Text"", ""text"": ""Duplicate"" },
                  { ""id"": ""video"", ""type"": ""Video"" }
                ]
              }
            }";

            var validation = UdomValidator.Validate(invalidJson);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Format(), Does.Contain("unsupportedCss"));
            Assert.That(validation.Format(), Does.Contain("중복 안정 ID"));
            Assert.That(validation.Format(), Does.Contain("지원하지 않는 요소 'Video'"));
        }

        [Test]
        public void Generate_CreatesEditableNativeUiAndSafeBindings()
        {
            var lightObject = new GameObject("External Test Light", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.enabled = true;

            var build = UdomBuilder.GenerateOrRegenerate(sample.text);
            var root = build.Root;
            root.SetExternalReference("world-light", lightObject);

            Assert.That(root.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(root.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "title").GetComponent<TextMeshProUGUI>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "title").GetComponent<TextMeshProUGUI>().font, Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "accent").GetComponent<Image>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>(), Is.Not.Null);

            var scroll = UdomBuilder.FindNode(root, "settings-list").GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.viewport, Is.Not.Null);
            Assert.That(scroll.content, Is.Not.Null);
            Assert.That(scroll.content.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            var scrollItems = scroll.content.GetComponentsInChildren<UdomGeneratedNode>(true)
                .Where(marker => !marker.GeneratedInternal)
                .ToArray();
            TestContext.WriteLine(
                $"Scroll viewport={scroll.viewport.rect}, content={scroll.content.rect}, items={scrollItems.Length}");
            for (var index = 0; index < scrollItems.Length; index++)
            {
                var itemRect = scrollItems[index].GetComponent<RectTransform>();
                var corners = new Vector3[4];
                itemRect.GetWorldCorners(corners);
                TestContext.WriteLine(
                    $"{scrollItems[index].StableId}: active={scrollItems[index].gameObject.activeInHierarchy}, "
                    + $"position={itemRect.anchoredPosition}, size={itemRect.rect.size}, "
                    + $"worldBL={corners[0]}, worldTR={corners[2]}");
            }

            Assert.That(scroll.content.rect.height, Is.GreaterThan(0f), "Scroll content must have a visible height.");
            Assert.That(scrollItems, Is.Not.Empty, "Scroll content must contain generated items.");
            Assert.That(scrollItems.All(item => item.gameObject.activeInHierarchy), Is.True);
            var viewportCorners = new Vector3[4];
            scroll.viewport.GetWorldCorners(viewportCorners);
            var firstItemCorners = new Vector3[4];
            scrollItems[0].GetComponent<RectTransform>().GetWorldCorners(firstItemCorners);
            var firstItemIntersectsViewport =
                firstItemCorners[2].x > viewportCorners[0].x
                && firstItemCorners[0].x < viewportCorners[2].x
                && firstItemCorners[2].y > viewportCorners[0].y
                && firstItemCorners[0].y < viewportCorners[2].y;
            TestContext.WriteLine($"viewport worldBL={viewportCorners[0]}, worldTR={viewportCorners[2]}");
            Assert.That(firstItemIntersectsViewport, Is.True, "First scroll item must intersect the viewport.");

            var embed = UdomBuilder.FindNode(root, "world-light-embed").GetComponent<UdomEmbedAnchor>();
            Assert.That(embed, Is.Not.Null);
            Assert.That(embed.Target, Is.SameAs(lightObject));
            Assert.That(lightObject.transform.parent, Is.Null, "External Embed target must remain user-owned.");

            var toggleButton = UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>();
            AssertGeneratedBinding(toggleButton, lightObject);
            InvokeGeneratedActionInEditMode(toggleButton);
            Assert.That(light.enabled, Is.False, "Generated Button must toggle the referenced Light.");

            var closeButton = UdomBuilder.FindNode(root, "close-panel").GetComponent<Button>();
            var panel = UdomBuilder.FindNode(root, "settings-panel").gameObject;
            Assert.That(panel.activeSelf, Is.True);
            InvokeGeneratedActionInEditMode(closeButton);
            Assert.That(panel.activeSelf, Is.False, "ClosePanel must close the document panel.");
        }

        [Test]
        public void ToggleActiveBinding_ChangesExternalGameObject()
        {
            var externalObject = new GameObject("External Active Target");
            var toggleActiveJson = sample.text.Replace("\"ToggleLight\"", "\"ToggleActive\"");
            var build = UdomBuilder.GenerateOrRegenerate(toggleActiveJson);
            build.Root.SetExternalReference("world-light", externalObject);

            var button = UdomBuilder.FindNode(build.Root, "toggle-light").GetComponent<Button>();
            Assert.That(externalObject.activeSelf, Is.True);
            AssertGeneratedBinding(button, externalObject);
            InvokeGeneratedActionInEditMode(button);
            Assert.That(externalObject.activeSelf, Is.False);
        }

        [Test]
        public void Regenerate_UpdatesVisualsPreservesReferencesAndDoesNotDuplicate()
        {
            var lightObject = new GameObject("External Test Light", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.enabled = true;

            var first = UdomBuilder.GenerateOrRegenerate(sample.text);
            var root = first.Root;
            root.SetExternalReference("world-light", lightObject);

            var userOwned = new GameObject("User Owned Child");
            userOwned.transform.SetParent(root.transform, false);

            var originalButton = UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>();
            var additionalListenerCalls = 0;
            originalButton.onClick.AddListener(() => additionalListenerCalls++);
            var markerCount = root.GetComponentsInChildren<UdomGeneratedNode>(true).Length;

            var changedJson = sample.text
                .Replace("\"World Settings\"", "\"World Settings — Regenerated\"")
                .Replace("#182033F2", "#4A1538F2");
            var second = UdomBuilder.GenerateOrRegenerate(changedJson, root);

            var updatedTitle = UdomBuilder.FindNode(root, "title").GetComponent<TextMeshProUGUI>();
            var updatedPanel = UdomBuilder.FindNode(root, "settings-panel").GetComponent<Image>();
            var updatedButton = UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>();
            var expectedPanelColor = new Color32(0x4A, 0x15, 0x38, 0xF2);

            Assert.That(updatedTitle.text, Is.EqualTo("World Settings — Regenerated"));
            Assert.That(updatedPanel.color, Is.EqualTo((Color)expectedPanelColor).Using(ColorComparer.Instance));
            Assert.That(root.Resolve("world-light"), Is.SameAs(lightObject));
            Assert.That(UdomBuilder.FindNode(root, "world-light-embed").GetComponent<UdomEmbedAnchor>().Target,
                Is.SameAs(lightObject));
            Assert.That(userOwned, Is.Not.Null);
            Assert.That(userOwned.transform.parent, Is.EqualTo(root.transform));
            Assert.That(updatedButton, Is.SameAs(originalButton), "Stable ID must update the existing Button object.");
            Assert.That(second.Created, Is.Zero, "Regeneration of the same schema must not create new nodes.");

            updatedButton.onClick.Invoke();
            Assert.That(additionalListenerCalls, Is.EqualTo(1), "User-added listener must survive regeneration.");
            AssertGeneratedBinding(updatedButton, lightObject);
            InvokeGeneratedActionInEditMode(updatedButton);
            Assert.That(light.enabled, Is.False);

            var third = UdomBuilder.GenerateOrRegenerate(changedJson, root);
            var markers = root.GetComponentsInChildren<UdomGeneratedNode>(true);
            var duplicateIds = markers
                .GroupBy(marker => marker.StableId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.That(third.Created, Is.Zero);
            Assert.That(markers.Length, Is.EqualTo(markerCount));
            Assert.That(duplicateIds, Is.Empty, "Repeated regeneration must not duplicate stable IDs.");
            Assert.That(updatedButton.onClick.GetPersistentEventCount(), Is.EqualTo(1),
                "Generated Button listener must not accumulate.");
        }

        [Test]
        public void SavedSampleScene_ContainsConnectedEditablePanel()
        {
            var scene = EditorSceneManager.OpenScene(
                UdomSampleSceneBuilder.SampleScenePath,
                OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            var root = Object.FindObjectOfType<UdomGeneratedRoot>(true);
            Assert.That(root, Is.Not.Null);
            Assert.That(root.SourceAsset, Is.SameAs(sample));
#if UDONSHARP
            Assert.That(
                root.GetComponents<Component>().Any(component =>
                    component != null
                    && component.GetType().FullName == "VRC.SDK3.Components.VRCUiShape"),
                Is.True,
                "A VRChat world-space Canvas must include VRCUiShape to receive pointer clicks.");
#endif
            Assert.That(root.Resolve("world-light"), Is.Not.Null);
            Assert.That(root.Resolve("world-light").name, Is.EqualTo("World Light (External User Object)"));
            Assert.That(root.Resolve("world-light").transform.parent, Is.Null);
            var floor = GameObject.Find("Test Floor (User-Owned Collider)");
            Assert.That(floor, Is.Not.Null);
            Assert.That(floor.GetComponent<BoxCollider>(), Is.Not.Null,
                "Saved VRChat sample scene must contain a floor collider.");
            Assert.That(floor.GetComponent<MeshRenderer>(), Is.Not.Null,
                "The sample floor must be visible so the external Light has a lit surface.");
            var panelRect = UdomBuilder.FindNode(root, "settings-panel").GetComponent<RectTransform>();
            var panelBottom = panelRect.TransformPoint(new Vector3(0f, panelRect.rect.yMin, 0f)).y;
            var floorTop = floor.GetComponent<BoxCollider>().bounds.max.y;
            Assert.That(floorTop, Is.LessThan(panelBottom),
                "The visible floor must sit below the panel instead of crossing through it.");
            Assert.That(UdomBuilder.FindNode(root, "title").GetComponent<TextMeshProUGUI>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>(), Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "settings-list").GetComponent<ScrollRect>(), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Component>(true).Any(component => component == null), Is.False,
                "Saved sample scene must not contain missing scripts.");
#if UDONSHARP
            Assert.That(UdomVrchatSetup.HasValidSceneDescriptor(), Is.True,
                "Saved VRChat sample scene must contain a scene descriptor and spawn.");
            var toggleButton = UdomBuilder.FindNode(root, "toggle-light").GetComponent<Button>();
            AssertGeneratedBinding(toggleButton, root.Resolve("world-light"));
#endif
        }

        [UnityTest]
        public IEnumerator Buttons_OperateSceneObjectsInPlayMode()
        {
            var lightObject = new GameObject("PlayMode External Light", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.enabled = true;

            var build = UdomBuilder.GenerateOrRegenerate(sample.text);
            build.Root.SetExternalReference("world-light", lightObject);

            yield return new EnterPlayMode();

            var runtimeRoot = Object.FindObjectOfType<UdomGeneratedRoot>(true);
            Assert.That(runtimeRoot, Is.Not.Null);
            var runtimeLightObject = GameObject.Find("PlayMode External Light");
            Assert.That(runtimeLightObject, Is.Not.Null);
            var runtimeLight = runtimeLightObject.GetComponent<Light>();
            Assert.That(runtimeLight.enabled, Is.True);

            var toggle = UdomBuilder.FindNode(runtimeRoot, "toggle-light").GetComponent<Button>();
            toggle.onClick.Invoke();
            Assert.That(runtimeLight.enabled, Is.False, "Button OnClick must toggle Light during Play Mode.");

            var panel = UdomBuilder.FindNode(runtimeRoot, "settings-panel").gameObject;
            var close = UdomBuilder.FindNode(runtimeRoot, "close-panel").GetComponent<Button>();
            close.onClick.Invoke();
            Assert.That(panel.activeSelf, Is.False, "Close button must disable the panel during Play Mode.");

            yield return new ExitPlayMode();
        }

        private static void InvokeGeneratedActionInEditMode(Button button)
        {
#if UDONSHARP
            var action = button.GetComponent<UdomUdonSafeAction>();
            Assert.That(action, Is.Not.Null, "VRChat project must generate an UdonSharp action proxy.");
            action.Execute();
#else
            button.onClick.Invoke();
#endif
        }

        private static string LoadReactFixture()
        {
            return LoadRepositoryFile("packages", "react", "test", "fixtures", "basic.udom.json");
        }

        private static string LoadRepositoryFile(params string[] relativePath)
        {
            var prototypeDirectory = Directory.GetParent(Application.dataPath);
            Assert.That(prototypeDirectory, Is.Not.Null, "Unity project directory must be available.");
            Assert.That(prototypeDirectory.Parent, Is.Not.Null, "Monorepo root directory must be available.");
            var fixturePath = Path.Combine(
                new[] { prototypeDirectory.Parent.FullName }.Concat(relativePath).ToArray());
            Assert.That(File.Exists(fixturePath), Is.True, $"Repository fixture must exist: {fixturePath}");
            return File.ReadAllText(fixturePath);
        }

        private static void AssertGeneratedBinding(Button button, GameObject expectedTarget)
        {
#if UDONSHARP
            var action = button.GetComponent<UdomUdonSafeAction>();
            Assert.That(action, Is.Not.Null, "VRChat project must generate an UdonSharp action proxy.");
            Assert.That(action.Target, Is.SameAs(expectedTarget));

            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            var listenerTarget = button.onClick.GetPersistentTarget(0);
            Assert.That(listenerTarget, Is.Not.Null);
            Assert.That(listenerTarget.GetType().FullName, Is.EqualTo("VRC.Udon.UdonBehaviour"));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("SendCustomEvent"));
#else
            Assert.That(button.GetComponent<UdomSafeAction>(), Is.Not.Null);
#endif
        }

        private sealed class ColorComparer : IEqualityComparer<Color>
        {
            public static readonly ColorComparer Instance = new ColorComparer();

            public bool Equals(Color left, Color right)
            {
                return Mathf.Abs(left.r - right.r) < 0.001f
                       && Mathf.Abs(left.g - right.g) < 0.001f
                       && Mathf.Abs(left.b - right.b) < 0.001f
                       && Mathf.Abs(left.a - right.a) < 0.001f;
            }

            public int GetHashCode(Color value)
            {
                return value.GetHashCode();
            }
        }
    }

}
