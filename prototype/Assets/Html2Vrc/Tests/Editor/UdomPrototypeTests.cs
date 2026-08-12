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
using UnityEngine.TestTools.Utils;
using UnityEngine.UI;

#if UDONSHARP
using Html2Vrc.VRChat;
using VRC.SDKBase.Validation;
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

            if (sample != null)
            {
                UdomGradientAssetUtility.DeleteGeneratedAssets(sample, "gradient-panel");
                UdomGradientAssetUtility.DeleteGeneratedAssets(sample, "radial-panel");
                UdomGradientAssetUtility.DeleteGeneratedAssets(sample, "conic-panel");
                UdomGradientAssetUtility.DeleteGeneratedAssets(sample, "radius-panel");
                UdomRoundedCornerAssetUtility.DeleteGeneratedAssets(sample, "radial-panel");
                UdomRoundedCornerAssetUtility.DeleteGeneratedAssets(sample, "conic-panel");
                UdomRoundedCornerAssetUtility.DeleteGeneratedAssets(sample, "radius-panel");
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
            var logoRoot = UdomBuilder.FindNode(root, "logo");
            var logo = UdomBuilder.FindNode(root, "logo::__image-content").GetComponent<Image>();
            var continueButton = UdomBuilder.FindNode(root, "continue").GetComponent<Button>();
            var continueLabel = UdomBuilder.FindNode(root, "continue-label").GetComponent<TextMeshProUGUI>();

            Assert.That(title, Is.Not.Null);
            Assert.That(title.text, Is.EqualTo("Hello VRChat"));
            Assert.That(title.fontSize, Is.EqualTo(48f));
            Assert.That(logoRoot.GetComponent<RectMask2D>(), Is.Not.Null);
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

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
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
            Assert.That(validation.Format(), Does.Not.Contain("linear-gradient"));
            Assert.That(validation.Format(), Does.Not.Contain("square corners"));
            Assert.That(validation.Format(), Does.Contain("project default TMP font"));
            Assert.That(validation.Format(), Does.Contain("Symbolic binding 'settings.musicEnabled'"));
            Assert.That(validation.Format(), Does.Contain("Symbolic event 'settings.setMusicEnabled'"));

            var document = validation.Document;
            var titleNode = document.root.children[0];
            var profileCard = document.root.children[1];
            var profileImageNode = profileCard.children[0];
            var toggleNode = profileCard.children[2];
            Assert.That(document.root.style.backgroundColor, Is.EqualTo("#171A2BFF"));
            Assert.That(document.root.style.backgroundType, Is.EqualTo("linear-gradient"));
            Assert.That(document.root.style.backgroundGradientAngle, Is.EqualTo(135f));
            Assert.That(document.root.style.backgroundGradientPositions, Is.EqualTo(new[] { 0f, 1f }));
            Assert.That(
                document.root.style.backgroundGradientColors,
                Is.EqualTo(new[] { "#171A2BFF", "#35245DFF" }));
            Assert.That(document.root.style.stretchChildrenWidth, Is.True);
            Assert.That(profileCard.style.childAlignment, Is.EqualTo("MiddleLeft"));
            Assert.That(profileCard.style.cornerRadius, Is.EqualTo(new[] { 24f, 24f, 24f, 24f }));
            Assert.That(profileImageNode.style.cornerRadius, Is.EqualTo(new[] { 52f, 52f, 52f, 52f }));
            Assert.That(titleNode.style.fontStyle, Is.EqualTo("Bold"));
            Assert.That(toggleNode.type, Is.EqualTo("Toggle"));
            Assert.That(toggleNode.toggleValue, Is.True);

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var rootImage = UdomBuilder.FindNode(build.Root, "settings-screen").GetComponent<Image>();
            var title = UdomBuilder.FindNode(build.Root, "settings-title").GetComponent<TextMeshProUGUI>();
            var profileImage = UdomBuilder.FindNode(
                build.Root,
                "profile-image::__image-content").GetComponent<Image>();
            var profileCardObject = UdomBuilder.FindNode(build.Root, "profile-card");
            var profileImageObject = UdomBuilder.FindNode(build.Root, "profile-image");
            var toggle = UdomBuilder.FindNode(build.Root, "music-toggle").GetComponent<Toggle>();
            var checkmark = UdomBuilder.FindNode(build.Root, "music-toggle::__toggle-checkmark");
            var rootLayout = UdomBuilder.FindNode(
                build.Root,
                "settings-screen").GetComponent<VerticalLayoutGroup>();
            Assert.That(rootImage.material.shader.name, Is.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(rootImage.material.GetTexture("_GradientTex"), Is.Not.Null);
            Assert.That(rootLayout.childControlWidth, Is.True);
            Assert.That(rootLayout.childForceExpandWidth, Is.True);
            Assert.That(title.text, Is.EqualTo("Settings"));
            Assert.That((title.fontStyle & FontStyles.Bold) != 0, Is.True);
            Assert.That(profileImage, Is.Not.Null);
            Assert.That(
                profileCardObject.GetComponent<Image>().material.shader.name,
                Is.EqualTo(UdomRoundedCornerAssetUtility.ShaderName));
            var profileCardSize = profileCardObject.GetComponent<RectTransform>().rect.size;
            var profileCardMaterialSize = profileCardObject.GetComponent<Image>().material.GetVector("_RectSize");
            Assert.That(profileCardSize.x, Is.GreaterThan(100f));
            Assert.That(profileCardMaterialSize.x, Is.EqualTo(profileCardSize.x).Within(0.001f));
            Assert.That(profileCardMaterialSize.y, Is.EqualTo(profileCardSize.y).Within(0.001f));
            Assert.That(profileCardObject.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(
                profileImageObject.GetComponent<Image>().material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(52f, 52f, 52f, 52f)).Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(profileImageObject.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(profileImageObject.GetComponent<Mask>().showMaskGraphic, Is.False);
            Assert.That(
                profileImageObject.GetComponent<Image>().color,
                Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(profileImageObject.GetComponent<RectMask2D>(), Is.Null);
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
            var image = UdomBuilder.FindNode(
                build.Root,
                "relative-resource-image::__image-content").GetComponent<RawImage>();
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
        public void CanonicalImageFit_MapsAllModesAndObjectPosition()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-image-fit.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var document = validation.Document;
            var fillNode = document.root.children.Single(node => node.id == "image-fill");
            var containNode = document.root.children.Single(node => node.id == "image-contain");
            var coverNode = document.root.children.Single(node => node.id == "image-cover");
            var noneNode = document.root.children.Single(node => node.id == "image-none");
            Assert.That(fillNode.imageFit, Is.EqualTo("fill"));
            Assert.That(containNode.imageFit, Is.EqualTo("contain"));
            Assert.That(coverNode.imagePositionX, Is.EqualTo("25%"));
            Assert.That(coverNode.imagePositionY, Is.EqualTo("75%"));
            Assert.That(noneNode.imagePositionX, Is.EqualTo("10"));
            Assert.That(noneNode.imagePositionY, Is.EqualTo("25%"));
            Assert.That(fillNode.imageIntrinsicSize, Is.EqualTo(new[] { 512f, 512f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            AssertImageContent(root, "image-fill", new Vector2(200f, 100f), Vector2.zero);
            AssertImageContent(root, "image-contain", new Vector2(100f, 100f), new Vector2(50f, 0f));
            AssertImageContent(root, "image-cover", new Vector2(200f, 200f), new Vector2(0f, 75f));
            AssertImageContent(root, "image-none", new Vector2(512f, 512f), new Vector2(10f, 103f));

            var originalCoverContent = UdomBuilder.FindNode(root, "image-cover::__image-content");
            coverNode.imageFit = "contain";
            coverNode.imagePositionX = "100%";
            coverNode.imagePositionY = "auto";
            UdomBuilder.GenerateOrRegenerate(document, root);

            Assert.That(
                UdomBuilder.FindNode(root, "image-cover::__image-content"),
                Is.SameAs(originalCoverContent));
            AssertImageContent(root, "image-cover", new Vector2(100f, 100f), new Vector2(100f, 0f));
        }

        [Test]
        public void CanonicalBorder_GeneratesPerEdgeImagesAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-border.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "border-panel");
            Assert.That(panelNode.style.borderWidth, Is.EqualTo(new[] { 20f, 8f, 12f, 16f }));
            Assert.That(
                panelNode.style.borderColor,
                Is.EqualTo(new[] { "#FFD84DFF", "#FF4D4DFF", "#4DFF88FF", "#4D88FFFF" }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "border-panel");
            var label = UdomBuilder.FindNode(root, "border-label");
            var border = UdomBuilder.FindNode(root, "border-panel::__border");
            var top = AssertBorderEdge(
                root,
                "border-panel::__border-top",
                "#FF4D4DFF",
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0f, -8f),
                Vector2.zero);
            var right = AssertBorderEdge(
                root,
                "border-panel::__border-right",
                "#4DFF88FF",
                new Vector2(1f, 0f),
                Vector2.one,
                new Vector2(-12f, 16f),
                new Vector2(0f, -8f));
            var bottom = AssertBorderEdge(
                root,
                "border-panel::__border-bottom",
                "#4D88FFFF",
                Vector2.zero,
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, 16f));
            var left = AssertBorderEdge(
                root,
                "border-panel::__border-left",
                "#FFD84DFF",
                Vector2.zero,
                new Vector2(0f, 1f),
                new Vector2(0f, 16f),
                new Vector2(20f, -8f));

            Assert.That(panel.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(border.GetComponent<LayoutElement>().ignoreLayout, Is.True);
            Assert.That(border.transform.GetSiblingIndex(), Is.GreaterThan(label.transform.GetSiblingIndex()));
            Assert.That(top.transform.parent, Is.SameAs(border.transform));
            Assert.That(right.transform.parent, Is.SameAs(border.transform));
            Assert.That(bottom.transform.parent, Is.SameAs(border.transform));
            Assert.That(left.transform.parent, Is.SameAs(border.transform));

            panelNode.style.borderWidth[1] = 10f;
            panelNode.style.borderWidth[2] = 0f;
            panelNode.style.borderColor[0] = "#00000000";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);

            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-top"), Is.SameAs(top));
            Assert.That(
                UdomBuilder.FindNode(root, "border-panel::__border-top").GetComponent<RectTransform>().offsetMin.y,
                Is.EqualTo(-10f).Within(0.001f));
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-bottom"), Is.SameAs(bottom));
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-right"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-left"), Is.Null);

            panelNode.style.borderWidth = new[] { 0f, 0f, 0f, 0f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-top"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-bottom"), Is.Null);
        }

        [Test]
        public void CanonicalLinearGradient_GeneratesMultiStopLutMaterialAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-linear-gradient.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "gradient-panel");
            Assert.That(panelNode.style.backgroundType, Is.EqualTo("linear-gradient"));
            Assert.That(panelNode.style.backgroundGradientAngle, Is.EqualTo(90f));
            Assert.That(panelNode.style.backgroundGradientPositions, Is.EqualTo(new[] { 0f, 0.5f, 1f }));
            Assert.That(
                panelNode.style.backgroundGradientColors,
                Is.EqualTo(new[] { "#FF0000FF", "#00FF00FF", "#0000FFFF" }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "gradient-panel");
            var image = panel.GetComponent<Image>();
            var material = image.material;
            var texture = material.GetTexture("_GradientTex") as Texture2D;
            Assert.That(texture, Is.Not.Null);
            var materialPath = AssetDatabase.GetAssetPath(material);
            var texturePath = AssetDatabase.GetAssetPath(texture);
            var materialGuid = AssetDatabase.AssetPathToGUID(materialPath);
            var textureGuid = AssetDatabase.AssetPathToGUID(texturePath);
            Assert.That(image.color, Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(material.shader.name, Is.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(
                materialPath,
                Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(
                texturePath,
                Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(texture.width, Is.EqualTo(UdomGradientAssetUtility.LutWidth));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(
                UdomGradientAssetUtility.Evaluate(
                    0.25f,
                    panelNode.style.backgroundGradientPositions,
                    panelNode.style.backgroundGradientColors),
                Is.EqualTo(new Color(0.5f, 0.5f, 0f, 1f)).Using(ColorComparer.Instance));
            Assert.That(
                UdomGradientAssetUtility.Evaluate(
                    0.75f,
                    panelNode.style.backgroundGradientPositions,
                    panelNode.style.backgroundGradientColors),
                Is.EqualTo(new Color(0f, 0.5f, 0.5f, 1f)).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo(Color.red).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width / 2, 0), Is.EqualTo(Color.green).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width - 1, 0), Is.EqualTo(Color.blue).Using(ColorComparer.Instance));
            var axis = material.GetVector("_GradientAxis");
            Assert.That(axis.x, Is.EqualTo(360f).Within(0.001f));
            Assert.That(axis.y, Is.EqualTo(0f).Within(0.001f));

#if UDONSHARP
            var validationClone = Object.Instantiate(panel.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                var validatedImage = validationClone.GetComponent<Image>();
                Assert.That(validatedImage, Is.Not.Null);
                Assert.That(
                    validatedImage.material.shader.name,
                    Is.EqualTo(UdomGradientAssetUtility.ShaderName));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.backgroundGradientAngle = 180f;
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            var regeneratedMaterial = image.material;
            var regeneratedTexture = regeneratedMaterial.GetTexture("_GradientTex") as Texture2D;
            Assert.That(AssetDatabase.GetAssetPath(regeneratedMaterial), Is.EqualTo(materialPath));
            Assert.That(AssetDatabase.GetAssetPath(regeneratedTexture), Is.EqualTo(texturePath));
            Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(texturePath), Is.EqualTo(textureGuid));
            axis = regeneratedMaterial.GetVector("_GradientAxis");
            Assert.That(axis.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(axis.y, Is.EqualTo(-160f).Within(0.001f));

            panelNode.style.backgroundType = "color";
            panelNode.style.backgroundColor = "#123456FF";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(image.material.shader.name, Is.Not.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(
                image.color,
                Is.EqualTo(UdomBuilderUtility.ParseColor("#123456FF", Color.clear)).Using(ColorComparer.Instance));
        }

        [Test]
        public void CanonicalRadialGradient_GeneratesEllipticalLutMaterialAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-radial-gradient.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "radial-panel");
            Assert.That(panelNode.style.backgroundType, Is.EqualTo("radial-gradient"));
            Assert.That(panelNode.style.backgroundGradientCenter, Is.EqualTo(new[] { 25f, 40f }));
            Assert.That(panelNode.style.backgroundGradientCenterIsPercent, Is.EqualTo(new[] { true, false }));
            Assert.That(panelNode.style.backgroundGradientRadius, Is.EqualTo(new[] { 50f, 60f }));
            Assert.That(panelNode.style.backgroundGradientRadiusIsPercent, Is.EqualTo(new[] { true, false }));
            Assert.That(panelNode.style.backgroundGradientPositions, Is.EqualTo(new[] { 0f, 0.5f, 1f }));
            Assert.That(
                panelNode.style.backgroundGradientColors,
                Is.EqualTo(new[] { "#FF0000FF", "#00FF00FF", "#0000FFFF" }));

            const string defaultGeometryJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 200, ""height"": 100 },
              ""root"": {
                ""type"": ""element"", ""id"": ""radial-defaults"", ""name"": ""view"",
                ""style"": { ""paint"": { ""backgrounds"": [{
                  ""type"": ""radial-gradient"",
                  ""center"": { ""x"": ""auto"", ""y"": ""auto"" },
                  ""radius"": { ""x"": ""auto"", ""y"": ""auto"" },
                  ""stops"": [
                    { ""position"": 0, ""color"": ""#FFFFFFFF"" },
                    { ""position"": 1, ""color"": ""#000000FF"" }
                  ]
                }] } }
              }
            }";
            var defaultGeometry = UdomValidator.Validate(defaultGeometryJson);
            Assert.That(defaultGeometry.IsValid, Is.True, defaultGeometry.Format());
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientCenter, Is.EqualTo(new[] { 50f, 50f }));
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientCenterIsPercent, Is.EqualTo(new[] { true, true }));
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientRadius, Is.EqualTo(new[] { 50f, 50f }));
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientRadiusIsPercent, Is.EqualTo(new[] { true, true }));

            var invalidRadius = UdomValidator.Validate(json.Replace("\"x\": \"50%\"", "\"x\": -1"));
            Assert.That(invalidRadius.IsValid, Is.False);
            Assert.That(invalidRadius.Format(), Does.Contain("gradient radius must be finite and non-negative"));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "radial-panel");
            var image = panel.GetComponent<Image>();
            var material = image.material;
            var texture = material.GetTexture("_GradientTex") as Texture2D;
            var materialPath = AssetDatabase.GetAssetPath(material);
            var texturePath = AssetDatabase.GetAssetPath(texture);
            var materialGuid = AssetDatabase.AssetPathToGUID(materialPath);
            var textureGuid = AssetDatabase.AssetPathToGUID(texturePath);
            Assert.That(image.color, Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(material.shader.name, Is.EqualTo(UdomGradientAssetUtility.RadialShaderName));
            Assert.That(materialPath, Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(texturePath, Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(UdomGradientAssetUtility.LutWidth));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo(Color.red).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width / 2, 0), Is.EqualTo(Color.green).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width - 1, 0), Is.EqualTo(Color.blue).Using(ColorComparer.Instance));
            Assert.That(
                material.GetVector("_GradientCenter"),
                Is.EqualTo(new Vector4(-90f, 40f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(
                material.GetVector("_GradientRadius"),
                Is.EqualTo(new Vector4(180f, 60f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(
                material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(28f, 28f, 28f, 28f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(panel.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);

#if UDONSHARP
            var validationClone = Object.Instantiate(panel.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                Assert.That(validationClone.GetComponent<Image>(), Is.Not.Null);
                Assert.That(validationClone.GetComponent<Mask>(), Is.Not.Null);
                Assert.That(
                    validationClone.GetComponent<Image>().material.shader.name,
                    Is.EqualTo(UdomGradientAssetUtility.RadialShaderName));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.backgroundGradientCenter = new[] { 75f, 25f };
            panelNode.style.backgroundGradientCenterIsPercent = new[] { true, true };
            panelNode.style.backgroundGradientRadius = new[] { 120f, 80f };
            panelNode.style.backgroundGradientRadiusIsPercent = new[] { false, false };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            var regeneratedMaterial = image.material;
            var regeneratedTexture = regeneratedMaterial.GetTexture("_GradientTex") as Texture2D;
            Assert.That(AssetDatabase.GetAssetPath(regeneratedMaterial), Is.EqualTo(materialPath));
            Assert.That(AssetDatabase.GetAssetPath(regeneratedTexture), Is.EqualTo(texturePath));
            Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(texturePath), Is.EqualTo(textureGuid));
            Assert.That(
                regeneratedMaterial.GetVector("_GradientCenter"),
                Is.EqualTo(new Vector4(90f, 40f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(
                regeneratedMaterial.GetVector("_GradientRadius"),
                Is.EqualTo(new Vector4(120f, 80f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));

            panelNode.style.backgroundType = "linear-gradient";
            panelNode.style.backgroundGradientAngle = 0f;
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(image.material.shader.name, Is.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(AssetDatabase.GetAssetPath(image.material), Is.EqualTo(materialPath));
            Assert.That(
                image.material.GetVector("_GradientAxis"),
                Is.EqualTo(new Vector4(0f, 160f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));

            panelNode.style.backgroundType = "color";
            panelNode.style.backgroundColor = "#123456FF";
            panelNode.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
            panelNode.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(UdomGradientAssetUtility.IsShader(image.material.shader.name), Is.False);
            Assert.That(panel.GetComponent<Mask>(), Is.Null);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);
        }

        [Test]
        public void CanonicalConicGradient_GeneratesClockwiseLutMaterialAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-conic-gradient.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "conic-panel");
            Assert.That(panelNode.style.backgroundType, Is.EqualTo("conic-gradient"));
            Assert.That(panelNode.style.backgroundGradientAngle, Is.EqualTo(45f));
            Assert.That(panelNode.style.backgroundGradientCenter, Is.EqualTo(new[] { 60f, 45f }));
            Assert.That(panelNode.style.backgroundGradientCenterIsPercent, Is.EqualTo(new[] { true, false }));
            Assert.That(panelNode.style.backgroundGradientPositions, Is.EqualTo(new[] { 0f, 0.25f, 0.5f, 1f }));
            Assert.That(
                panelNode.style.backgroundGradientColors,
                Is.EqualTo(new[] { "#FF0000FF", "#FFFF00FF", "#00FF00FF", "#0000FFFF" }));

            const string defaultGeometryJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 200, ""height"": 100 },
              ""root"": {
                ""type"": ""element"", ""id"": ""conic-defaults"", ""name"": ""view"",
                ""style"": { ""paint"": { ""backgrounds"": [{
                  ""type"": ""conic-gradient"",
                  ""stops"": [
                    { ""position"": 0, ""color"": ""#FFFFFFFF"" },
                    { ""position"": 1, ""color"": ""#000000FF"" }
                  ]
                }] } }
              }
            }";
            var defaultGeometry = UdomValidator.Validate(defaultGeometryJson);
            Assert.That(defaultGeometry.IsValid, Is.True, defaultGeometry.Format());
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientAngle, Is.EqualTo(0f));
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientCenter, Is.EqualTo(new[] { 50f, 50f }));
            Assert.That(defaultGeometry.Document.root.style.backgroundGradientCenterIsPercent, Is.EqualTo(new[] { true, true }));

            Assert.That(
                UdomGradientAssetUtility.EvaluateConicPosition(Vector2.up, Vector2.zero, 0f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                UdomGradientAssetUtility.EvaluateConicPosition(Vector2.right, Vector2.zero, 0f),
                Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(
                UdomGradientAssetUtility.EvaluateConicPosition(Vector2.down, Vector2.zero, 0f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                UdomGradientAssetUtility.EvaluateConicPosition(Vector2.left, Vector2.zero, 0f),
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                UdomGradientAssetUtility.EvaluateConicPosition(Vector2.right, Vector2.zero, 90f),
                Is.EqualTo(0f).Within(0.0001f));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "conic-panel");
            var image = panel.GetComponent<Image>();
            var material = image.material;
            var texture = material.GetTexture("_GradientTex") as Texture2D;
            var materialPath = AssetDatabase.GetAssetPath(material);
            var texturePath = AssetDatabase.GetAssetPath(texture);
            var materialGuid = AssetDatabase.AssetPathToGUID(materialPath);
            var textureGuid = AssetDatabase.AssetPathToGUID(texturePath);
            Assert.That(image.color, Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(material.shader.name, Is.EqualTo(UdomGradientAssetUtility.ConicShaderName));
            Assert.That(materialPath, Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(texturePath, Does.StartWith("Assets/Html2VrcGenerated/Gradients/"));
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(UdomGradientAssetUtility.LutWidth));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo(Color.red).Using(ColorComparer.Instance));
            Assert.That(
                texture.GetPixel(texture.width / 4, 0),
                Is.EqualTo(new Color(1f, 1f, 0f, 1f)).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width / 2, 0), Is.EqualTo(Color.green).Using(ColorComparer.Instance));
            Assert.That(texture.GetPixel(texture.width - 1, 0), Is.EqualTo(Color.blue).Using(ColorComparer.Instance));
            Assert.That(
                material.GetVector("_GradientCenter"),
                Is.EqualTo(new Vector4(36f, 35f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(material.GetFloat("_GradientStart"), Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(
                material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(32f, 32f, 32f, 32f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(panel.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);

#if UDONSHARP
            var validationClone = Object.Instantiate(panel.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                Assert.That(validationClone.GetComponent<Image>(), Is.Not.Null);
                Assert.That(validationClone.GetComponent<Mask>(), Is.Not.Null);
                Assert.That(
                    validationClone.GetComponent<Image>().material.shader.name,
                    Is.EqualTo(UdomGradientAssetUtility.ConicShaderName));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.backgroundGradientAngle = 90f;
            panelNode.style.backgroundGradientCenter = new[] { 10f, 20f };
            panelNode.style.backgroundGradientCenterIsPercent = new[] { false, false };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            var regeneratedMaterial = image.material;
            var regeneratedTexture = regeneratedMaterial.GetTexture("_GradientTex") as Texture2D;
            Assert.That(AssetDatabase.GetAssetPath(regeneratedMaterial), Is.EqualTo(materialPath));
            Assert.That(AssetDatabase.GetAssetPath(regeneratedTexture), Is.EqualTo(texturePath));
            Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(texturePath), Is.EqualTo(textureGuid));
            Assert.That(
                regeneratedMaterial.GetVector("_GradientCenter"),
                Is.EqualTo(new Vector4(-170f, 60f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(regeneratedMaterial.GetFloat("_GradientStart"), Is.EqualTo(0.25f).Within(0.0001f));

            panelNode.style.backgroundType = "radial-gradient";
            panelNode.style.backgroundGradientRadius = new[] { 50f, 50f };
            panelNode.style.backgroundGradientRadiusIsPercent = new[] { true, true };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(image.material.shader.name, Is.EqualTo(UdomGradientAssetUtility.RadialShaderName));
            Assert.That(AssetDatabase.GetAssetPath(image.material), Is.EqualTo(materialPath));
            Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
            Assert.That(
                image.material.GetVector("_GradientRadius"),
                Is.EqualTo(new Vector4(180f, 80f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));

            panelNode.style.backgroundType = "color";
            panelNode.style.backgroundColor = "#123456FF";
            panelNode.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
            panelNode.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(UdomGradientAssetUtility.IsShader(image.material.shader.name), Is.False);
            Assert.That(panel.GetComponent<Mask>(), Is.Null);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);
        }

        [Test]
        public void CanonicalTransform_AppliesOrderedOperationsAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-transform.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "transform-panel");
            Assert.That(panelNode.style.transformOrigin, Is.EqualTo(new[] { 25f, 20f }));
            Assert.That(panelNode.style.transformOriginIsPercent, Is.EqualTo(new[] { true, false }));
            Assert.That(
                panelNode.style.transformOperationTypes,
                Is.EqualTo(new[] { "translate", "rotate", "scale" }));
            Assert.That(
                panelNode.style.transformOperationValues,
                Is.EqualTo(new[] { 10f, 12f, 30f, 0f, 1.5f, 0.75f }));
            Assert.That(
                panelNode.style.transformOperationValuesArePercent,
                Is.EqualTo(new[] { true, false, false, false, false, false }));

            const string defaultTransformJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 200, ""height"": 100 },
              ""root"": {
                ""type"": ""element"", ""id"": ""transform-defaults"", ""name"": ""view"",
                ""style"": { ""transform"": { ""operations"": [
                  { ""type"": ""translate"", ""x"": ""auto"" },
                  { ""type"": ""scale"" }
                ] } }
              }
            }";
            var defaults = UdomValidator.Validate(defaultTransformJson);
            Assert.That(defaults.IsValid, Is.True, defaults.Format());
            Assert.That(defaults.Document.root.style.transformOrigin, Is.EqualTo(new[] { 50f, 50f }));
            Assert.That(defaults.Document.root.style.transformOriginIsPercent, Is.EqualTo(new[] { true, true }));
            Assert.That(defaults.Document.root.style.transformOperationValues, Is.EqualTo(new[] { 0f, 0f, 1f, 1f }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var root = build.Root;
            var layout = UdomBuilder.FindNode(root, "transform-panel::__transform-layout");
            var origin = UdomBuilder.FindNode(root, "transform-panel::__transform-origin");
            var translate = UdomBuilder.FindNode(root, "transform-panel::__transform-operation-0");
            var rotate = UdomBuilder.FindNode(root, "transform-panel::__transform-operation-1");
            var scale = UdomBuilder.FindNode(root, "transform-panel::__transform-operation-2");
            var panel = UdomBuilder.FindNode(root, "transform-panel");
            var reference = UdomBuilder.FindNode(root, "layout-reference");
            var documentRoot = UdomBuilder.FindNode(root, "transform-root");

            Assert.That(layout, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(translate, Is.Not.Null);
            Assert.That(rotate, Is.Not.Null);
            Assert.That(scale, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);
            Assert.That(layout.transform.parent, Is.SameAs(documentRoot.transform));
            Assert.That(reference.transform.parent, Is.SameAs(documentRoot.transform));
            Assert.That(scale.transform.parent, Is.SameAs(origin.transform));
            Assert.That(rotate.transform.parent, Is.SameAs(scale.transform));
            Assert.That(translate.transform.parent, Is.SameAs(rotate.transform));
            Assert.That(panel.transform.parent, Is.SameAs(translate.transform));

            var layoutRect = layout.GetComponent<RectTransform>();
            var originRect = origin.GetComponent<RectTransform>();
            var translateRect = translate.GetComponent<RectTransform>();
            var rotateRect = rotate.GetComponent<RectTransform>();
            var scaleRect = scale.GetComponent<RectTransform>();
            var panelRect = panel.GetComponent<RectTransform>();
            Assert.That(layoutRect.rect.width, Is.EqualTo(440f).Within(0.001f));
            Assert.That(layoutRect.rect.height, Is.EqualTo(100f).Within(0.001f));
            Assert.That(originRect.anchoredPosition, Is.EqualTo(new Vector2(-110f, 30f)));
            Assert.That(translateRect.anchoredPosition, Is.EqualTo(new Vector2(44f, -12f)));
            Assert.That(rotateRect.localEulerAngles.z, Is.EqualTo(330f).Within(0.001f));
            Assert.That(scaleRect.localScale, Is.EqualTo(new Vector3(1.5f, 0.75f, 1f)));
            Assert.That(panelRect.anchoredPosition, Is.EqualTo(new Vector2(110f, -30f)));
            Assert.That(panelRect.rect.size, Is.EqualTo(new Vector2(440f, 100f)));
            Assert.That(panel.GetComponent<LayoutElement>(), Is.Null);
            Assert.That(layout.GetComponent<LayoutElement>(), Is.Not.Null);
            Assert.That(translate.GetComponents<MonoBehaviour>().Select(component => component.GetType()),
                Is.EqualTo(new[] { typeof(UdomGeneratedNode) }));

            var expectedCenter = new Vector3(110f, -30f, 0f);
            expectedCenter += new Vector3(44f, -12f, 0f);
            expectedCenter = Quaternion.Euler(0f, 0f, -30f) * expectedCenter;
            expectedCenter = Vector3.Scale(expectedCenter, new Vector3(1.5f, 0.75f, 1f));
            expectedCenter += new Vector3(-110f, 30f, 0f);
            var actualCenter = layoutRect.InverseTransformPoint(panelRect.TransformPoint(Vector3.zero));
            Assert.That(actualCenter.x, Is.EqualTo(expectedCenter.x).Within(0.001f));
            Assert.That(actualCenter.y, Is.EqualTo(expectedCenter.y).Within(0.001f));

#if UDONSHARP
            var validationClone = Object.Instantiate(layout.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                Assert.That(validationClone.GetComponentInChildren<Image>(true), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.margin = new[] { 10f, 5f, 20f, 15f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            var margin = UdomBuilder.FindNode(root, "transform-panel::__margin");
            Assert.That(margin, Is.Not.Null);
            Assert.That(layout, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel::__transform-layout")));
            Assert.That(origin, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel::__transform-origin")));
            Assert.That(translate, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-0")));
            Assert.That(rotate, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-1")));
            Assert.That(scale, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-2")));
            Assert.That(panel, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel")));
            Assert.That(layout.transform.parent, Is.SameAs(margin.transform));
            Assert.That(layoutRect.rect.size, Is.EqualTo(new Vector2(410f, 100f)));
            Assert.That(originRect.anchoredPosition, Is.EqualTo(new Vector2(-102.5f, 30f)));
            Assert.That(translateRect.anchoredPosition, Is.EqualTo(new Vector2(41f, -12f)));
            Assert.That(panelRect.anchoredPosition, Is.EqualTo(new Vector2(102.5f, -30f)));

            panelNode.style.transformOperationTypes = System.Array.Empty<string>();
            panelNode.style.transformOperationValues = System.Array.Empty<float>();
            panelNode.style.transformOperationValuesArePercent = System.Array.Empty<bool>();
            UdomBuilder.GenerateOrRegenerate(validation.Document, root);
            Assert.That(panel, Is.SameAs(UdomBuilder.FindNode(root, "transform-panel")));
            Assert.That(panel.transform.parent, Is.SameAs(margin.transform));
            Assert.That(UdomBuilder.FindNode(root, "transform-panel::__transform-layout"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transform-panel::__transform-origin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-0"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-1"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "transform-panel::__transform-operation-2"), Is.Null);
            Assert.That(panelRect.rect.size, Is.EqualTo(new Vector2(410f, 100f)));
            Assert.That(panelRect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(panelRect.localRotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void CanonicalCornerRadius_GeneratesMaskedMaterialAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-radius.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "radius-panel");
            Assert.That(panelNode.style.cornerRadius, Is.EqualTo(new[] { 64f, 32f, 48f, 0f }));
            Assert.That(panelNode.style.cornerRadiusPercent, Is.EqualTo(new[] { -1f, -1f, -1f, 10f }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "radius-panel");
            var image = panel.GetComponent<Image>();
            var mask = panel.GetComponent<Mask>();
            var material = image.material;
            var materialPath = AssetDatabase.GetAssetPath(material);
            var materialGuid = AssetDatabase.AssetPathToGUID(materialPath);
            Assert.That(
                image.color,
                Is.EqualTo((Color)new Color32(0x31, 0x5D, 0x9F, 0xFF)).Using(ColorComparer.Instance));
            Assert.That(material.shader.name, Is.EqualTo(UdomRoundedCornerAssetUtility.ShaderName));
            Assert.That(materialPath, Does.StartWith("Assets/Html2VrcGenerated/RoundedCorners/"));
            Assert.That(material.GetVector("_RectSize").x, Is.EqualTo(360f).Within(0.001f));
            Assert.That(material.GetVector("_RectSize").y, Is.EqualTo(160f).Within(0.001f));
            Assert.That(
                material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(64f, 32f, 48f, 16f)).Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.showMaskGraphic, Is.True);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);

#if UDONSHARP
            var validationClone = Object.Instantiate(panel.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                Assert.That(validationClone.GetComponent<Image>(), Is.Not.Null);
                Assert.That(validationClone.GetComponent<Mask>(), Is.Not.Null);
                Assert.That(
                    validationClone.GetComponent<Image>().material.shader.name,
                    Is.EqualTo(UdomRoundedCornerAssetUtility.ShaderName));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
            panelNode.style.cornerRadiusPercent = new[] { 80f, 80f, 80f, 80f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            var regeneratedMaterial = image.material;
            Assert.That(AssetDatabase.GetAssetPath(regeneratedMaterial), Is.EqualTo(materialPath));
            Assert.That(AssetDatabase.AssetPathToGUID(materialPath), Is.EqualTo(materialGuid));
            Assert.That(
                regeneratedMaterial.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(80f, 80f, 80f, 80f)).Using(Vector4ComparerWithEqualsOperator.Instance));

            panelNode.style.backgroundType = "linear-gradient";
            panelNode.style.backgroundGradientAngle = 90f;
            panelNode.style.backgroundGradientPositions = new[] { 0f, 1f };
            panelNode.style.backgroundGradientColors = new[] { "#FF0000FF", "#0000FFFF" };
            panelNode.style.cornerRadius = new[] { 40f, 40f, 40f, 40f };
            panelNode.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(image.material.shader.name, Is.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(
                image.material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(40f, 40f, 40f, 40f)).Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(panel.GetComponent<Mask>(), Is.Not.Null);

            panelNode.style.backgroundType = "color";
            panelNode.style.backgroundColor = "#123456FF";
            panelNode.style.cornerRadius = new[] { 0f, 0f, 0f, 0f };
            panelNode.style.cornerRadiusPercent = new[] { -1f, -1f, -1f, -1f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(image.material.shader.name, Is.Not.EqualTo(UdomRoundedCornerAssetUtility.ShaderName));
            Assert.That(image.material.shader.name, Is.Not.EqualTo(UdomGradientAssetUtility.ShaderName));
            Assert.That(panel.GetComponent<Mask>(), Is.Null);
            Assert.That(panel.GetComponent<RectMask2D>(), Is.Null);
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
            Assert.That(conversion.Json, Does.Contain("\"borderWidth\""));
            Assert.That(conversion.Json, Does.Contain("\"borderColor\""));
            Assert.That(conversion.Json, Does.Contain("\"fontStyle\""));
            Assert.That(conversion.Json, Does.Contain("\"childAlignment\""));

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

        private static void AssertImageContent(
            UdomGeneratedRoot root,
            string nodeId,
            Vector2 expectedSize,
            Vector2 expectedPosition)
        {
            var imageRoot = UdomBuilder.FindNode(root, nodeId);
            var content = UdomBuilder.FindNode(root, nodeId + "::__image-content");
            var rect = content.GetComponent<RectTransform>();
            var rawImage = content.GetComponent<RawImage>();

            Assert.That(imageRoot.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(rawImage, Is.Not.Null);
            Assert.That(rawImage.texture, Is.Not.Null);
            Assert.That(rect.sizeDelta.x, Is.EqualTo(expectedSize.x).Within(0.001f));
            Assert.That(rect.sizeDelta.y, Is.EqualTo(expectedSize.y).Within(0.001f));
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(expectedPosition.x).Within(0.001f));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(expectedPosition.y).Within(0.001f));
        }

        private static UdomGeneratedNode AssertBorderEdge(
            UdomGeneratedRoot root,
            string stableId,
            string expectedColor,
            Vector2 expectedAnchorMin,
            Vector2 expectedAnchorMax,
            Vector2 expectedOffsetMin,
            Vector2 expectedOffsetMax)
        {
            var edge = UdomBuilder.FindNode(root, stableId);
            var rect = edge.GetComponent<RectTransform>();
            var image = edge.GetComponent<Image>();

            Assert.That(image, Is.Not.Null);
            Assert.That(
                image.color,
                Is.EqualTo(UdomBuilderUtility.ParseColor(expectedColor, Color.clear)).Using(ColorComparer.Instance));
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(edge.GetComponent<LayoutElement>(), Is.Null);
            Assert.That(rect.anchorMin, Is.EqualTo(expectedAnchorMin));
            Assert.That(rect.anchorMax, Is.EqualTo(expectedAnchorMax));
            Assert.That(rect.offsetMin.x, Is.EqualTo(expectedOffsetMin.x).Within(0.001f));
            Assert.That(rect.offsetMin.y, Is.EqualTo(expectedOffsetMin.y).Within(0.001f));
            Assert.That(rect.offsetMax.x, Is.EqualTo(expectedOffsetMax.x).Within(0.001f));
            Assert.That(rect.offsetMax.y, Is.EqualTo(expectedOffsetMax.y).Within(0.001f));
            return edge;
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
