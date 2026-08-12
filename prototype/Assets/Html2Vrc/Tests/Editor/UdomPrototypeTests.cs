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
        private const string CanonicalRelativeFontPath =
            "Assets/Html2Vrc/Samples/CanonicalRelativeFont.udom.json";
        private const string LiberationSourceFontPath =
            "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
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
                UdomRoundedCornerAssetUtility.DeleteGeneratedAssets(sample, "shadow-panel");
                UdomRoundedCornerAssetUtility.DeleteGeneratedAssets(sample, "border-panel");
                UdomBorderAssetUtility.DeleteGeneratedAssets(sample, "border-panel");
                UdomShadowAssetUtility.DeleteGeneratedAssets(sample, "shadow-panel::__shadow-0");
                UdomShadowAssetUtility.DeleteGeneratedAssets(sample, "shadow-panel::__shadow-1");
                UdomShadowAssetUtility.DeleteGeneratedAssets(sample, "shadow-panel::__shadow-2");
            }

            UdomFontAssetUtility.DeleteGeneratedAsset(LiberationSourceFontPath);
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
        public void CanonicalFontResource_LoadsTmpAssetAndGeneratesStableSourceFontAsset()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(CanonicalRelativeFontPath);
            Assert.That(source, Is.Not.Null, "Canonical relative-font sample must import as TextAsset.");

            var validation = UdomValidator.Validate(source.text, CanonicalRelativeFontPath);
            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var generatedNode = validation.Document.root.children[0];
            var directNode = validation.Document.root.children[1];
            var inputNode = validation.Document.root.children[2];
            Assert.That(generatedNode.style.fontAssetPath, Is.EqualTo(LiberationSourceFontPath));
            Assert.That(
                directNode.style.fontAssetPath,
                Is.EqualTo("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset"));
            Assert.That(inputNode.style.fontAssetPath, Is.EqualTo(LiberationSourceFontPath));

            var normalized = UdomJsonWriter.Write(validation.Document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(
                roundTrip.Document.root.children[0].style.fontAssetPath,
                Is.EqualTo(LiberationSourceFontPath));

            var generatedAssetPath = UdomFontAssetUtility.GetGeneratedAssetPath(LiberationSourceFontPath);
            Assert.That(generatedAssetPath, Does.StartWith("Assets/Html2VrcGenerated/Fonts/"));
            Assert.That(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generatedAssetPath), Is.Null);

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, source);
            var generatedText = UdomBuilder.FindNode(build.Root, "generated-font-text")
                .GetComponent<TextMeshProUGUI>();
            var directText = UdomBuilder.FindNode(build.Root, "direct-font-text")
                .GetComponent<TextMeshProUGUI>();
            var inputText = UdomBuilder.FindNode(build.Root, "font-text-input::__input-text")
                .GetComponent<TextMeshProUGUI>();
            var placeholder = UdomBuilder.FindNode(build.Root, "font-text-input::__input-placeholder")
                .GetComponent<TextMeshProUGUI>();
            var generatedAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generatedAssetPath);
            var directAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(directNode.style.fontAssetPath);
            Assert.That(generatedAsset, Is.Not.Null);
            Assert.That(generatedAsset.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(
                generatedAsset.sourceFontFile,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<Font>(LiberationSourceFontPath)));
            var generatedSubAssets = AssetDatabase.LoadAllAssetsAtPath(generatedAssetPath);
            Assert.That(generatedSubAssets.OfType<Material>().Count(), Is.EqualTo(1));
            Assert.That(generatedSubAssets.OfType<Texture2D>().Count(), Is.EqualTo(1));
            Assert.That(generatedText.font, Is.SameAs(generatedAsset));
            Assert.That(inputText.font, Is.SameAs(generatedAsset));
            Assert.That(placeholder.font, Is.SameAs(generatedAsset));
            Assert.That(directAsset, Is.Not.Null);
            Assert.That(directText.font, Is.SameAs(directAsset));

            var generatedGuid = AssetDatabase.AssetPathToGUID(generatedAssetPath);
            var originalGeneratedObject = generatedText.gameObject;
            var repeated = UdomBuilder.GenerateOrRegenerate(roundTrip.Document, build.Root, source);
            Assert.That(repeated.Created, Is.Zero);
            Assert.That(
                UdomBuilder.FindNode(build.Root, "generated-font-text").gameObject,
                Is.SameAs(originalGeneratedObject));
            Assert.That(AssetDatabase.AssetPathToGUID(generatedAssetPath), Is.EqualTo(generatedGuid));
            Assert.That(
                UdomBuilder.FindNode(build.Root, "generated-font-text").GetComponent<TextMeshProUGUI>().font,
                Is.SameAs(generatedAsset));

            var missingJson = source.text.Replace(
                "../../TextMesh Pro/Fonts/LiberationSans.ttf",
                "missing.ttf");
            var missingValidation = UdomValidator.Validate(missingJson, CanonicalRelativeFontPath);
            Assert.That(missingValidation.IsValid, Is.True, missingValidation.Format());
            Assert.That(missingValidation.Format(), Does.Contain("uses the project default TMP font"));
            Assert.That(
                missingValidation.Document.root.children[0].style.fontAssetPath,
                Is.EqualTo("Assets/Html2Vrc/Samples/missing.ttf"));
            var missingBuild = UdomBuilder.GenerateOrRegenerate(missingValidation.Document, build.Root, source);
            var missingText = UdomBuilder.FindNode(missingBuild.Root, "generated-font-text")
                .GetComponent<TextMeshProUGUI>();
            Assert.That(missingText.font, Is.Not.Null);
            Assert.That(missingText.font, Is.Not.SameAs(generatedAsset));
            Object.DestroyImmediate(build.Root.gameObject);
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
        public void CanonicalLayoutConstraints_ClampFlexGrowthAndPreserveAspectRatio()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-layout-constraints.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var boundedStyle = document.root.children[0].style;
            var remainingStyle = document.root.children[1].style;
            var ratioStyle = document.root.children[2].style;
            Assert.That(boundedStyle.minSize, Is.EqualTo(new[] { 200f, 120f }));
            Assert.That(boundedStyle.maxSize[0], Is.EqualTo(300f).Within(0.001f));
            Assert.That(boundedStyle.size, Is.EqualTo(new[] { 300f, 120f }));
            Assert.That(remainingStyle.size, Is.EqualTo(new[] { 280f, 80f }).Within(0.001f));
            Assert.That(ratioStyle.aspectRatio, Is.EqualTo(2f));
            Assert.That(ratioStyle.aspectRatioMode, Is.EqualTo("WidthControlsHeight"));
            Assert.That(ratioStyle.autoSize, Is.EqualTo(new[] { false, true }));
            Assert.That(ratioStyle.size, Is.EqualTo(new[] { 180f, 90f }).Within(0.001f));
            Assert.That(ratioStyle.useResolvedChildrenWidth, Is.True);
            Assert.That(ratioStyle.stretchChildrenWidth, Is.True);
            Assert.That(ratioStyle.layout, Is.EqualTo("Vertical"));
            Assert.That(ratioStyle.useResolvedChildrenHeight, Is.False);
            Assert.That(ratioStyle.flexibleWidth, Is.Zero);
            Assert.That(document.root.children[2].children[0].style.size, Is.EqualTo(new[] { 120f, 40f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var bounded = UdomBuilder.FindNode(root, "bounded-grow");
            var remaining = UdomBuilder.FindNode(root, "remaining-grow");
            var ratio = UdomBuilder.FindNode(root, "ratio-card");
            var crossBounded = UdomBuilder.FindNode(root, "cross-bounded");
            var originalBounded = bounded;
            Assert.That(bounded.GetComponent<RectTransform>().rect.size, Is.EqualTo(new Vector2(300f, 120f)));
            Assert.That(remaining.GetComponent<RectTransform>().rect.size, Is.EqualTo(new Vector2(280f, 80f)));
            Assert.That(ratio.GetComponent<RectTransform>().rect.size, Is.EqualTo(new Vector2(180f, 90f)));
            Assert.That(crossBounded.GetComponent<RectTransform>().rect.size, Is.EqualTo(new Vector2(120f, 40f)));
            var ratioLayout = ratio.GetComponent<VerticalLayoutGroup>();
            Assert.That(ratioLayout.childControlWidth, Is.False);
            Assert.That(ratioLayout.childForceExpandWidth, Is.False);

            var boundedLayout = bounded.GetComponent<LayoutElement>();
            Assert.That(boundedLayout.minWidth, Is.EqualTo(200f));
            Assert.That(boundedLayout.minHeight, Is.EqualTo(120f));
            Assert.That(boundedLayout.preferredWidth, Is.EqualTo(300f));
            Assert.That(boundedLayout.flexibleWidth, Is.Zero);

            var regenerated = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "bounded-grow"), Is.SameAs(originalBounded));
            Assert.That(
                UdomBuilder.FindNode(root, "ratio-card").GetComponent<RectTransform>().rect.size,
                Is.EqualTo(new Vector2(180f, 90f)));

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace("\"aspectRatio\": 2", "\"aspectRatio\": 0"));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("aspectRatio"));
        }

        [Test]
        public void CanonicalTextFlow_MapsMetricsWrappingOverflowAndWhitespace()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-text-flow.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var metricsNode = document.root.children[0];
            var preservedNode = document.root.children[1];
            var ellipsisNode = document.root.children[2];
            var defaultsNode = document.root.children[3];
            Assert.That(metricsNode.text, Is.EqualTo("Alpha Beta"));
            Assert.That(metricsNode.style.fontSize, Is.EqualTo(32f));
            Assert.That(metricsNode.style.lineHeight, Is.EqualTo(48f));
            Assert.That(metricsNode.style.letterSpacing, Is.EqualTo(4f));
            Assert.That(metricsNode.style.textWrap, Is.False);
            Assert.That(metricsNode.style.textOverflow, Is.EqualTo("Clip"));
            Assert.That(metricsNode.style.alignment, Is.EqualTo("TopJustified"));
            Assert.That(preservedNode.text, Is.EqualTo("  First  \nSecond   line  "));
            Assert.That(preservedNode.style.preserveWhitespace, Is.True);
            Assert.That(preservedNode.style.textOverflow, Is.EqualTo("Visible"));
            Assert.That(ellipsisNode.style.textOverflow, Is.EqualTo("Ellipsis"));
            Assert.That(ellipsisNode.style.lineHeight, Is.EqualTo(-1f));
            Assert.That(ellipsisNode.style.alignment, Is.EqualTo("BottomRight"));
            Assert.That(defaultsNode.text, Is.EqualTo("Canonical defaults collapse"));
            Assert.That(defaultsNode.style.fontSize, Is.EqualTo(16f));
            Assert.That(defaultsNode.style.textColor, Is.EqualTo("#000000FF"));
            Assert.That(defaultsNode.style.alignment, Is.EqualTo("TopLeft"));
            Assert.That(defaultsNode.style.textWrap, Is.True);
            Assert.That(defaultsNode.style.textOverflow, Is.EqualTo("Clip"));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var metrics = UdomBuilder.FindNode(root, "text-metrics").GetComponent<TextMeshProUGUI>();
            var preserved = UdomBuilder.FindNode(root, "text-preserved").GetComponent<TextMeshProUGUI>();
            var ellipsis = UdomBuilder.FindNode(root, "text-ellipsis").GetComponent<TextMeshProUGUI>();
            var defaults = UdomBuilder.FindNode(root, "text-defaults").GetComponent<TextMeshProUGUI>();
            var originalMetrics = metrics.gameObject;

            Assert.That(metrics.text, Is.EqualTo("Alpha Beta"));
            Assert.That(metrics.characterSpacing, Is.EqualTo(12.5f).Within(0.001f));
            Assert.That(metrics.enableWordWrapping, Is.False);
            Assert.That(metrics.overflowMode, Is.EqualTo(TextOverflowModes.Masking));
            Assert.That(metrics.alignment, Is.EqualTo(TextAlignmentOptions.TopJustified));
            Assert.That(preserved.text, Is.EqualTo("  First  \nSecond   line  "));
            Assert.That(preserved.enableWordWrapping, Is.True);
            Assert.That(preserved.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(ellipsis.enableWordWrapping, Is.False);
            Assert.That(ellipsis.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis));
            Assert.That(ellipsis.alignment, Is.EqualTo(TextAlignmentOptions.BottomRight));
            Assert.That(defaults.fontSize, Is.EqualTo(16f));
            Assert.That(defaults.color, Is.EqualTo(Color.black).Using(ColorComparer.Instance));
            Assert.That(defaults.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(defaults.overflowMode, Is.EqualTo(TextOverflowModes.Masking));

            Canvas.ForceUpdateCanvases();
            preserved.ForceMeshUpdate();
            Assert.That(preserved.textInfo.lineCount, Is.EqualTo(2));
            var baselineDistance = Mathf.Abs(
                preserved.textInfo.lineInfo[0].baseline
                - preserved.textInfo.lineInfo[1].baseline);
            Assert.That(baselineDistance, Is.EqualTo(45f).Within(0.01f));

            metricsNode.style.lineHeight = -1f;
            metricsNode.style.letterSpacing = 0f;
            metricsNode.style.textWrap = true;
            metricsNode.style.textOverflow = "Visible";
            metricsNode.style.alignment = "BottomRight";
            var regenerated = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "text-metrics").gameObject, Is.SameAs(originalMetrics));
            Assert.That(metrics.lineSpacing, Is.Zero);
            Assert.That(metrics.characterSpacing, Is.Zero);
            Assert.That(metrics.enableWordWrapping, Is.True);
            Assert.That(metrics.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(metrics.alignment, Is.EqualTo(TextAlignmentOptions.BottomRight));

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace("\"lineHeight\": 48", "\"lineHeight\": 0"));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("lineHeight"));
        }

        [Test]
        public void CanonicalFlexOrder_AppliesStableOrderAndReverseDirections()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-flex-order.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var rowNode = document.root.children[0];
            var columnNode = document.root.children[1];
            Assert.That(rowNode.style.layout, Is.EqualTo("Horizontal"));
            Assert.That(rowNode.style.reverseChildren, Is.True);
            Assert.That(rowNode.style.childAlignment, Is.EqualTo("MiddleRight"));
            Assert.That(rowNode.children.Select(child => child.id), Is.EqualTo(new[]
            {
                "order-a", "order-b", "order-c", "order-d"
            }));
            Assert.That(rowNode.children.Select(child => child.style.flexOrder), Is.EqualTo(new[]
            {
                2, -1, 2, 0
            }));
            Assert.That(columnNode.style.layout, Is.EqualTo("Vertical"));
            Assert.That(columnNode.style.reverseChildren, Is.True);
            Assert.That(columnNode.style.childAlignment, Is.EqualTo("LowerRight"));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[0].style.reverseChildren, Is.True);
            Assert.That(roundTrip.Document.root.children[0].children[0].style.flexOrder, Is.EqualTo(2));
            Assert.That(roundTrip.Document.root.children[0].children[0].style.minSize[0], Is.EqualTo(90f));
            Assert.That(roundTrip.Document.root.children[0].children[0].style.maxSize[0], Is.EqualTo(110f));
            Assert.That(roundTrip.Document.root.children[0].children[1].style.aspectRatio, Is.EqualTo(1.5f));
            Assert.That(roundTrip.Document.root.children[0].children[1].style.autoSize[0], Is.True);
            var roundTripText = roundTrip.Document.root.children[0].children[3].children[0].style;
            Assert.That(roundTripText.lineHeight, Is.EqualTo(27f));
            Assert.That(roundTripText.letterSpacing, Is.EqualTo(2f));
            Assert.That(roundTripText.textWrap, Is.False);
            Assert.That(roundTripText.textOverflow, Is.EqualTo("Clip"));
            Assert.That(roundTripText.preserveWhitespace, Is.True);

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var row = UdomBuilder.FindNode(root, "row-reverse").gameObject;
            var column = UdomBuilder.FindNode(root, "column-reverse").gameObject;
            var orderAWrapper = UdomBuilder.FindNode(root, "order-a::__margin").gameObject;
            var orderBWrapper = UdomBuilder.FindNode(root, "order-b::__transform-layout").gameObject;
            var orderCWrapper = UdomBuilder.FindNode(root, "order-c::__shadow-layout").gameObject;
            var orderD = UdomBuilder.FindNode(root, "order-d").gameObject;
            var columnFirst = UdomBuilder.FindNode(root, "column-first").gameObject;
            var columnSecond = UdomBuilder.FindNode(root, "column-second").gameObject;
            Assert.That(row.GetComponent<HorizontalLayoutGroup>().childAlignment, Is.EqualTo(TextAnchor.MiddleRight));
            Assert.That(orderCWrapper.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(orderAWrapper.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(orderD.transform.GetSiblingIndex(), Is.EqualTo(2));
            Assert.That(orderBWrapper.transform.GetSiblingIndex(), Is.EqualTo(3));
            Assert.That(column.GetComponent<VerticalLayoutGroup>().childAlignment, Is.EqualTo(TextAnchor.LowerRight));
            Assert.That(columnSecond.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(columnFirst.transform.GetSiblingIndex(), Is.EqualTo(1));

            rowNode.style.reverseChildren = false;
            rowNode.style.childAlignment = "MiddleLeft";
            rowNode.children[0].style.flexOrder = -2;
            var regenerated = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "order-a::__margin").gameObject, Is.SameAs(orderAWrapper));
            Assert.That(UdomBuilder.FindNode(root, "order-b::__transform-layout").gameObject, Is.SameAs(orderBWrapper));
            Assert.That(UdomBuilder.FindNode(root, "order-c::__shadow-layout").gameObject, Is.SameAs(orderCWrapper));
            Assert.That(orderAWrapper.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(orderBWrapper.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(orderD.transform.GetSiblingIndex(), Is.EqualTo(2));
            Assert.That(orderCWrapper.transform.GetSiblingIndex(), Is.EqualTo(3));
            Assert.That(row.GetComponent<HorizontalLayoutGroup>().childAlignment, Is.EqualTo(TextAnchor.MiddleLeft));

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace("\"order\": 2", "\"order\": 1.5"));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("order"));
        }

        [Test]
        public void CanonicalFlexSizing_ResolvesShrinkBasisAndConstraintRedistribution()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-flex-sizing.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var shrinkRow = document.root.children[0];
            var basisRow = document.root.children[1];
            var shrinkDefault = shrinkRow.children[0].style;
            var shrinkDouble = shrinkRow.children[1].style;
            var shrinkFixed = shrinkRow.children[2].style;
            var basisAbsolute = basisRow.children[0].style;
            var basisPercent = basisRow.children[1].style;
            var basisAuto = basisRow.children[2].style;

            Assert.That(shrinkDefault.flexShrink, Is.EqualTo(1f));
            Assert.That(shrinkDouble.flexShrink, Is.EqualTo(2f));
            Assert.That(shrinkFixed.flexShrink, Is.Zero);
            Assert.That(shrinkDefault.size[0], Is.EqualTo(257.1429f).Within(0.001f));
            Assert.That(shrinkDouble.size[0], Is.EqualTo(142.8571f).Within(0.001f));
            Assert.That(shrinkFixed.size[0], Is.EqualTo(100f).Within(0.001f));

            Assert.That(basisAbsolute.flexBasis, Is.EqualTo(100f));
            Assert.That(basisAbsolute.flexBasisIsPercent, Is.False);
            Assert.That(basisPercent.flexBasis, Is.EqualTo(25f));
            Assert.That(basisPercent.flexBasisIsPercent, Is.True);
            Assert.That(basisAuto.flexBasis, Is.EqualTo(-1f));
            Assert.That(basisAuto.flexBasisIsPercent, Is.False);
            Assert.That(basisAbsolute.size[0], Is.EqualTo(260f).Within(0.001f));
            Assert.That(basisPercent.size[0], Is.EqualTo(150f).Within(0.001f));
            Assert.That(basisAuto.size[0], Is.EqualTo(80f).Within(0.001f));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[0].children[1].style.flexShrink, Is.EqualTo(2f));
            Assert.That(roundTrip.Document.root.children[1].children[0].style.flexBasis, Is.EqualTo(100f));
            Assert.That(
                roundTrip.Document.root.children[1].children[1].style.flexBasisIsPercent,
                Is.True);

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var shrinkRowObject = UdomBuilder.FindNode(root, "shrink-row").gameObject;
            var basisRowObject = UdomBuilder.FindNode(root, "basis-row").gameObject;
            var shrinkDefaultObject = UdomBuilder.FindNode(root, "shrink-default").gameObject;
            var shrinkDoubleObject = UdomBuilder.FindNode(root, "shrink-double").gameObject;
            var shrinkFixedObject = UdomBuilder.FindNode(root, "shrink-fixed").gameObject;
            var basisAbsoluteObject = UdomBuilder.FindNode(root, "basis-absolute").gameObject;
            var basisPercentObject = UdomBuilder.FindNode(root, "basis-percent").gameObject;
            var basisAutoObject = UdomBuilder.FindNode(root, "basis-auto").gameObject;
            var basisAutoMargin = UdomBuilder.FindNode(root, "basis-auto::__margin").gameObject;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                shrinkRowObject.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                basisRowObject.GetComponent<RectTransform>());
            Assert.That(
                shrinkDefaultObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(257.1429f).Within(0.001f));
            Assert.That(
                shrinkDoubleObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(142.8571f).Within(0.001f));
            Assert.That(
                shrinkFixedObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(100f).Within(0.001f));
            Assert.That(
                basisAbsoluteObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(260f).Within(0.001f));
            Assert.That(
                basisPercentObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(150f).Within(0.001f));
            Assert.That(
                basisAutoObject.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(80f).Within(0.001f));
            Assert.That(
                basisAutoMargin.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(90f).Within(0.001f));
            Assert.That(
                basisAbsoluteObject.GetComponent<LayoutElement>().flexibleWidth,
                Is.Zero);

            var regenerated = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(
                UdomBuilder.FindNode(root, "basis-absolute").gameObject,
                Is.SameAs(basisAbsoluteObject));
            Assert.That(
                UdomBuilder.FindNode(root, "basis-auto::__margin").gameObject,
                Is.SameAs(basisAutoMargin));

            Object.DestroyImmediate(root.gameObject);

            var invalidShrink = UdomValidator.Validate(json.Replace(
                "\"shrink\": 2",
                "\"shrink\": -1"));
            Assert.That(invalidShrink.IsValid, Is.False);
            Assert.That(invalidShrink.Format(), Does.Contain("shrink"));
            var invalidBasis = UdomValidator.Validate(json.Replace(
                "\"basis\": \"25%\"",
                "\"basis\": \"-5%\""));
            Assert.That(invalidBasis.IsValid, Is.False);
            Assert.That(invalidBasis.Format(), Does.Contain("basis"));

            const string zeroShrinkJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 100, ""height"": 50 },
              ""root"": {
                ""type"": ""element"", ""id"": ""zero-root"", ""name"": ""view"",
                ""style"": { ""layout"": {
                  ""mode"": ""flex"", ""width"": 100, ""height"": 50,
                  ""flex"": { ""direction"": ""row"", ""alignItems"": ""start"" }
                } },
                ""children"": [
                  {
                    ""type"": ""element"", ""id"": ""zero-shrunk"", ""name"": ""view"",
                    ""style"": { ""layout"": { ""width"": 100, ""height"": 20 } }
                  },
                  {
                    ""type"": ""element"", ""id"": ""zero-fixed"", ""name"": ""view"",
                    ""style"": { ""layout"": {
                      ""width"": 100, ""height"": 20,
                      ""flexItem"": { ""shrink"": 0 }
                    } }
                  }
                ]
              }
            }";
            var zeroShrink = UdomValidator.Validate(zeroShrinkJson);
            Assert.That(zeroShrink.IsValid, Is.True, zeroShrink.Format());
            Assert.That(zeroShrink.Document.root.children[0].style.size[0], Is.Zero);
            Assert.That(zeroShrink.Document.root.children[1].style.size[0], Is.EqualTo(100f));
        }

        [Test]
        public void CanonicalFlexJustify_DistributesFreeSpaceAndSupportsReverseEnd()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-flex-justify.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var rows = validation.Document.root.children;
            Assert.That(rows.Select(row => row.style.justifyContent), Is.EqualTo(new[]
            {
                "Start",
                "Center",
                "End",
                "SpaceBetween",
                "SpaceAround",
                "SpaceEvenly",
                "End"
            }));
            Assert.That(rows.Select(row => row.style.childAlignment), Is.EqualTo(new[]
            {
                "MiddleLeft",
                "MiddleCenter",
                "MiddleRight",
                "MiddleLeft",
                "MiddleCenter",
                "MiddleCenter",
                "MiddleLeft"
            }));
            Assert.That(rows.Select(row => row.style.spacing), Is.EqualTo(new[]
            {
                10f,
                10f,
                10f,
                360f,
                185f,
                126.6667f,
                10f
            }).Within(0.001f));
            Assert.That(rows[6].style.reverseChildren, Is.True);

            var normalized = UdomJsonWriter.Write(validation.Document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[5].style.justifyContent, Is.EqualTo("SpaceEvenly"));
            Assert.That(roundTrip.Document.root.children[5].style.spacing, Is.EqualTo(126.6667f).Within(0.001f));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document);
            var root = build.Root;
            var rootRect = UdomBuilder.FindNode(root, "justify-root").GetComponent<RectTransform>();
            var rowIds = new[]
            {
                "justify-start",
                "justify-center",
                "justify-end",
                "justify-between",
                "justify-around",
                "justify-evenly",
                "justify-reverse-end"
            };
            var pairIds = new[]
            {
                new[] { "start-a", "start-b" },
                new[] { "center-a", "center-b" },
                new[] { "end-a", "end-b" },
                new[] { "between-a", "between-b" },
                new[] { "around-a", "around-b" },
                new[] { "evenly-a", "evenly-b" }
            };
            var expectedMidpoints = new[] { 125f, 300f, 475f, 300f, 300f, 300f };
            var expectedDistances = new[] { 110f, 110f, 110f, 460f, 285f, 226.6667f };

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            for (var index = 0; index < pairIds.Length; index++)
            {
                var row = UdomBuilder.FindNode(root, rowIds[index]).gameObject;
                LayoutRebuilder.ForceRebuildLayoutImmediate(row.GetComponent<RectTransform>());
                var first = UdomBuilder.FindNode(root, pairIds[index][0]).GetComponent<RectTransform>();
                var second = UdomBuilder.FindNode(root, pairIds[index][1]).GetComponent<RectTransform>();
                Assert.That(
                    (first.anchoredPosition.x + second.anchoredPosition.x) * 0.5f,
                    Is.EqualTo(expectedMidpoints[index]).Within(0.001f),
                    rowIds[index] + " midpoint");
                Assert.That(
                    second.anchoredPosition.x - first.anchoredPosition.x,
                    Is.EqualTo(expectedDistances[index]).Within(0.001f),
                    rowIds[index] + " distance");
            }

            var reverseRow = UdomBuilder.FindNode(root, rowIds[6]).gameObject;
            LayoutRebuilder.ForceRebuildLayoutImmediate(reverseRow.GetComponent<RectTransform>());
            var reverseA = UdomBuilder.FindNode(root, "reverse-a").GetComponent<RectTransform>();
            var reverseB = UdomBuilder.FindNode(root, "reverse-b").GetComponent<RectTransform>();
            Assert.That(reverseRow.GetComponent<HorizontalLayoutGroup>().childAlignment, Is.EqualTo(TextAnchor.MiddleLeft));
            Assert.That(reverseB.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(reverseA.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(
                (reverseA.anchoredPosition.x + reverseB.anchoredPosition.x) * 0.5f,
                Is.EqualTo(125f).Within(0.001f));
            Assert.That(
                reverseA.anchoredPosition.x - reverseB.anchoredPosition.x,
                Is.EqualTo(110f).Within(0.001f));

            var originalAround = UdomBuilder.FindNode(root, "around-a").gameObject;
            var repeatedValidation = UdomValidator.Validate(json);
            var regenerated = UdomBuilder.GenerateOrRegenerate(repeatedValidation.Document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "around-a").gameObject, Is.SameAs(originalAround));

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace(
                "\"justify\": \"center\"",
                "\"justify\": \"stretch\""));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("justify"));
        }

        [Test]
        public void CanonicalFlexAlignSelf_OverridesCrossAxisAndReusesMarginWrappers()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-flex-align-self.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var stretchParent = document.root.children[0];
            var centerParent = document.root.children[1];
            var columnParent = document.root.children[2];
            Assert.That(stretchParent.style.useResolvedChildrenHeight, Is.True);
            Assert.That(centerParent.style.useResolvedChildrenHeight, Is.True);
            Assert.That(columnParent.style.reverseChildren, Is.True);
            Assert.That(stretchParent.children.Select(child => child.style.alignSelf), Is.EqualTo(new[]
            {
                "Auto", "Start", "Center", "End", "Stretch"
            }));
            Assert.That(stretchParent.children[0].style.size[1], Is.EqualTo(120f));
            Assert.That(stretchParent.children[1].style.margin, Is.EqualTo(new[] { 0f, 5f, 0f, 5f }));
            Assert.That(stretchParent.children[1].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, 0f, 70f }));
            Assert.That(stretchParent.children[2].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 35f, 0f, 35f }));
            Assert.That(stretchParent.children[3].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 80f, 0f, 0f }));
            Assert.That(stretchParent.children[4].style.size[1], Is.EqualTo(70f));
            Assert.That(stretchParent.children[4].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, 0f, 40f }));
            Assert.That(centerParent.children[1].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, 0f, 60f }));
            Assert.That(centerParent.children[2].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 60f, 0f, 0f }));
            Assert.That(centerParent.children[3].style.size[1], Is.EqualTo(100f));
            Assert.That(columnParent.children[0].style.alignSelfMargin, Is.EqualTo(new[] { 60f, 0f, 60f, 0f }));
            Assert.That(columnParent.children[1].style.alignSelfMargin, Is.EqualTo(new[] { 120f, 0f, 0f, 0f }));
            Assert.That(columnParent.children[2].style.size[0], Is.EqualTo(90f));
            Assert.That(columnParent.children[2].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, 60f, 0f }));
            Assert.That(columnParent.children[3].style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, -40f, 0f }));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[0].children[2].style.alignSelf, Is.EqualTo("Center"));
            Assert.That(
                roundTrip.Document.root.children[2].children[3].style.alignSelfMargin,
                Is.EqualTo(new[] { 0f, 0f, -40f, 0f }));
            Assert.That(
                roundTrip.Document.root.children[0].children[1].style.margin,
                Is.EqualTo(new[] { 0f, 5f, 0f, 5f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var stretchParentObject = UdomBuilder.FindNode(root, "stretch-parent").gameObject;
            var centerParentObject = UdomBuilder.FindNode(root, "center-parent").gameObject;
            var columnParentObject = UdomBuilder.FindNode(root, "column-parent").gameObject;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                stretchParentObject.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                centerParentObject.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                columnParentObject.GetComponent<RectTransform>());

            var stretchGroup = stretchParentObject.GetComponent<HorizontalLayoutGroup>();
            Assert.That(stretchGroup.childControlHeight, Is.False);
            Assert.That(stretchGroup.childForceExpandHeight, Is.False);
            Assert.That(
                UdomBuilder.FindNode(root, "stretch-auto").GetComponent<RectTransform>().rect.height,
                Is.EqualTo(120f).Within(0.001f));
            AssertAlignedWithinMargin(root, "stretch-start", new Vector2(90f, 40f), new Vector2(0f, 35f), new Vector2(90f, 120f));
            AssertAlignedWithinMargin(
                root,
                "stretch-center",
                new Vector2(90f, 40f),
                new Vector2(0f, -5f),
                new Vector2(90f, 120f),
                "::__transform-layout");
            AssertAlignedWithinMargin(root, "stretch-end", new Vector2(90f, 40f), new Vector2(0f, -40f), new Vector2(90f, 120f));
            AssertAlignedWithinMargin(root, "stretch-limited", new Vector2(90f, 70f), new Vector2(0f, 20f), new Vector2(90f, 120f));

            Assert.That(
                UdomBuilder.FindNode(root, "center-auto").GetComponent<RectTransform>().localPosition.y,
                Is.Zero.Within(0.001f));
            AssertAlignedWithinMargin(root, "center-start", new Vector2(90f, 40f), new Vector2(0f, 30f), new Vector2(90f, 100f));
            AssertAlignedWithinMargin(root, "center-end", new Vector2(90f, 40f), new Vector2(0f, -30f), new Vector2(90f, 100f));
            Assert.That(UdomBuilder.FindNode(root, "center-stretch::__margin"), Is.Null);
            Assert.That(
                UdomBuilder.FindNode(root, "center-stretch").GetComponent<RectTransform>().rect.height,
                Is.EqualTo(100f).Within(0.001f));

            AssertAlignedWithinMargin(root, "column-center", new Vector2(40f, 50f), Vector2.zero, new Vector2(160f, 50f));
            AssertAlignedWithinMargin(root, "column-end", new Vector2(40f, 50f), new Vector2(60f, 0f), new Vector2(160f, 50f));
            AssertAlignedWithinMargin(root, "column-stretch", new Vector2(90f, 50f), new Vector2(-30f, 0f), new Vector2(160f, 50f));
            AssertAlignedWithinMargin(root, "column-overflow-start", new Vector2(200f, 50f), new Vector2(20f, 0f), new Vector2(160f, 50f));
            Assert.That(UdomBuilder.FindNode(root, "column-overflow-start::__margin").transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(UdomBuilder.FindNode(root, "column-center::__margin").transform.GetSiblingIndex(), Is.EqualTo(3));

            var originalCenterStart = UdomBuilder.FindNode(root, "center-start").gameObject;
            var originalCenterStartMargin = UdomBuilder.FindNode(root, "center-start::__margin").gameObject;
            var repeated = UdomValidator.Validate(json);
            var regenerated = UdomBuilder.GenerateOrRegenerate(repeated.Document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "center-start").gameObject, Is.SameAs(originalCenterStart));
            Assert.That(UdomBuilder.FindNode(root, "center-start::__margin").gameObject, Is.SameAs(originalCenterStartMargin));

            var autoJson = json.Replace("\"alignSelf\": \"start\"", "\"alignSelf\": \"auto\"");
            var autoValidation = UdomValidator.Validate(autoJson);
            Assert.That(autoValidation.IsValid, Is.True, autoValidation.Format());
            var autoBuild = UdomBuilder.GenerateOrRegenerate(autoValidation.Document, root);
            Assert.That(autoBuild.Removed, Is.GreaterThan(0));
            Assert.That(UdomBuilder.FindNode(root, "center-start").gameObject, Is.SameAs(originalCenterStart));
            Assert.That(UdomBuilder.FindNode(root, "center-start::__margin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "column-overflow-start::__margin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "stretch-start::__margin"), Is.Not.Null);

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace(
                "\"alignSelf\": \"center\"",
                "\"alignSelf\": \"baseline\""));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("alignSelf"));
        }

        [Test]
        public void CanonicalFlexWrap_BakesLinesGapsAlignmentAndReverseAxes()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-flex-wrap.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var document = validation.Document;
            var row = document.root.children[0];
            var column = document.root.children[1];
            var stretch = document.root.children[2];
            Assert.That(row.style.flexWrap, Is.EqualTo("Wrap"));
            Assert.That(row.style.alignContent, Is.EqualTo("SpaceBetween"));
            Assert.That(row.style.rowGap, Is.EqualTo(20f));
            Assert.That(row.style.columnGap, Is.EqualTo(10f));
            Assert.That(row.style.useResolvedChildPositions, Is.True);
            Assert.That(row.children.All(child => child.style.useResolvedPosition), Is.True);
            Assert.That(row.children[0].style.position, Is.EqualTo(new[] { 20f, 30f }));
            Assert.That(row.children[1].style.position, Is.EqualTo(new[] { 240f, 20f }));
            Assert.That(row.children[2].style.position, Is.EqualTo(new[] { 20f, 190f }));
            Assert.That(row.children[3].style.position, Is.EqualTo(new[] { 210f, 200f }));
            Assert.That(row.children[3].style.size, Is.EqualTo(new[] { 130f, 30f }));

            Assert.That(column.style.flexWrap, Is.EqualTo("WrapReverse"));
            Assert.That(column.style.alignContent, Is.EqualTo("Center"));
            Assert.That(column.style.reverseChildren, Is.True);
            Assert.That(column.children[0].style.position, Is.EqualTo(new[] { 160f, 135f }));
            Assert.That(column.children[1].style.position, Is.EqualTo(new[] { 160f, 25f }));
            Assert.That(column.children[2].style.position, Is.EqualTo(new[] { 100f, 135f }));
            Assert.That(column.children[3].style.position, Is.EqualTo(new[] { 100f, 25f }));

            Assert.That(stretch.style.alignContent, Is.EqualTo("Stretch"));
            Assert.That(stretch.children[0].style.position, Is.EqualTo(new[] { 20f, 20f }));
            Assert.That(stretch.children[0].style.size, Is.EqualTo(new[] { 170f, 60f }));
            Assert.That(stretch.children[1].style.position, Is.EqualTo(new[] { 20f, 120f }));
            Assert.That(stretch.children[1].style.size, Is.EqualTo(new[] { 170f, 80f }));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[0].style.flexWrap, Is.EqualTo("Wrap"));
            Assert.That(roundTrip.Document.root.children[0].style.alignContent, Is.EqualTo("SpaceBetween"));
            Assert.That(roundTrip.Document.root.children[1].children[0].style.useResolvedPosition, Is.True);
            Assert.That(
                roundTrip.Document.root.children[2].children[1].style.position,
                Is.EqualTo(new[] { 20f, 120f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var rowObject = UdomBuilder.FindNode(root, "row-wrap").gameObject;
            var columnObject = UdomBuilder.FindNode(root, "column-wrap-reverse").gameObject;
            var stretchObject = UdomBuilder.FindNode(root, "stretch-wrap").gameObject;
            Canvas.ForceUpdateCanvases();
            Assert.That(rowObject.GetComponent<HorizontalLayoutGroup>(), Is.Null);
            Assert.That(columnObject.GetComponent<VerticalLayoutGroup>(), Is.Null);
            Assert.That(stretchObject.GetComponent<HorizontalLayoutGroup>(), Is.Null);
            AssertResolvedTopLeftRect(root, "row-a", new Vector2(20f, 30f), new Vector2(120f, 40f));
            AssertResolvedTopLeftRect(root, "row-b", new Vector2(240f, 20f), new Vector2(100f, 60f));
            AssertResolvedTopLeftRect(root, "row-c", new Vector2(20f, 190f), new Vector2(140f, 50f));
            AssertResolvedTopLeftRect(root, "row-d", new Vector2(210f, 200f), new Vector2(130f, 30f));
            AssertResolvedTopLeftRect(root, "column-a", new Vector2(160f, 135f), new Vector2(40f, 100f));
            AssertResolvedTopLeftRect(root, "column-b", new Vector2(160f, 25f), new Vector2(40f, 100f));
            AssertResolvedTopLeftRect(root, "column-c", new Vector2(100f, 135f), new Vector2(40f, 100f));
            AssertResolvedTopLeftRect(root, "column-d", new Vector2(100f, 25f), new Vector2(40f, 100f));
            Assert.That(UdomBuilder.FindNode(root, "column-d").transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(UdomBuilder.FindNode(root, "column-a").transform.GetSiblingIndex(), Is.EqualTo(3));
            AssertResolvedTopLeftRect(root, "stretch-a", new Vector2(20f, 20f), new Vector2(170f, 60f));
            AssertResolvedTopLeftRect(root, "stretch-b", new Vector2(20f, 120f), new Vector2(170f, 80f));

            var originalRowA = UdomBuilder.FindNode(root, "row-a").gameObject;
            var repeated = UdomBuilder.GenerateOrRegenerate(roundTrip.Document, root);
            Assert.That(repeated.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "row-a").gameObject, Is.SameAs(originalRowA));

            var nowrapJson = json.Replace("\"wrap\": \"wrap\"", "\"wrap\": \"nowrap\"");
            var nowrapValidation = UdomValidator.Validate(nowrapJson);
            Assert.That(nowrapValidation.IsValid, Is.True, nowrapValidation.Format());
            var nowrapBuild = UdomBuilder.GenerateOrRegenerate(nowrapValidation.Document, root);
            Assert.That(nowrapBuild.Created, Is.EqualTo(1));
            Assert.That(UdomBuilder.FindNode(root, "row-a").gameObject, Is.SameAs(originalRowA));
            Assert.That(UdomBuilder.FindNode(root, "row-b::__margin"), Is.Not.Null);
            Assert.That(rowObject.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null);
            Assert.That(stretchObject.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null);
            Object.DestroyImmediate(root.gameObject);

            var invalidWrap = UdomValidator.Validate(json.Replace("wrap-reverse", "balance"));
            Assert.That(invalidWrap.IsValid, Is.False);
            Assert.That(invalidWrap.Format(), Does.Contain("wrap"));
            var invalidAlignment = UdomValidator.Validate(json.Replace(
                "\"alignContent\": \"space-between\"",
                "\"alignContent\": \"space-evenly\""));
            Assert.That(invalidAlignment.IsValid, Is.False);
            Assert.That(invalidAlignment.Format(), Does.Contain("alignContent"));
        }

        [Test]
        public void CanonicalAbsolutePosition_LeavesFlexFlowAndUsesTopLeftCoordinates()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-absolute-position.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var firstNode = document.root.children[0];
            var overlayNode = document.root.children[1];
            var secondNode = document.root.children[2];
            var directNode = document.root.children[3];
            Assert.That(overlayNode.style.positionAbsolute, Is.True);
            Assert.That(overlayNode.style.position, Is.EqualTo(new[] { 60f, 30f }));
            Assert.That(overlayNode.style.size, Is.EqualTo(new[] { 120f, 70f }));
            Assert.That(directNode.style.positionAbsolute, Is.True);
            Assert.That(directNode.style.position, Is.EqualTo(new[] { 300f, 100f }));
            Assert.That(firstNode.style.size[1], Is.EqualTo(100f).Within(0.001f));
            Assert.That(secondNode.style.size[1], Is.EqualTo(245f).Within(0.001f));
            Assert.That(overlayNode.style.size[1], Is.EqualTo(70f));
            Assert.That(overlayNode.style.flexibleHeight, Is.EqualTo(100f));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[1].style.positionAbsolute, Is.True);
            Assert.That(roundTrip.Document.root.children[1].style.position, Is.EqualTo(new[] { 60f, 30f }));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "absolute-root").gameObject;
            var first = UdomBuilder.FindNode(root, "flow-first").gameObject;
            var second = UdomBuilder.FindNode(root, "flow-second").gameObject;
            var overlayMargin = UdomBuilder.FindNode(root, "absolute-overlay::__margin").gameObject;
            var overlayTransform = UdomBuilder.FindNode(
                root,
                "absolute-overlay::__transform-layout").gameObject;
            var direct = UdomBuilder.FindNode(root, "absolute-direct").gameObject;
            var overlayLayout = overlayMargin.GetComponent<LayoutElement>();
            var overlayRect = overlayMargin.GetComponent<RectTransform>();
            var directLayout = direct.GetComponent<LayoutElement>();
            var directRect = direct.GetComponent<RectTransform>();

            Assert.That(overlayMargin.transform.parent, Is.SameAs(panel.transform));
            Assert.That(overlayTransform.transform.parent, Is.SameAs(overlayMargin.transform));
            Assert.That(overlayLayout.ignoreLayout, Is.True);
            Assert.That(overlayRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(overlayRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(overlayRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(overlayRect.anchoredPosition, Is.EqualTo(new Vector2(60f, -30f)));
            Assert.That(overlayRect.sizeDelta, Is.EqualTo(new Vector2(134f, 88f)));
            Assert.That(directLayout.ignoreLayout, Is.True);
            Assert.That(directRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(directRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(directRect.anchoredPosition, Is.EqualTo(new Vector2(300f, -100f)));

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.GetComponent<RectTransform>());
            Assert.That(first.GetComponent<RectTransform>().rect.height, Is.EqualTo(100f).Within(0.001f));
            Assert.That(second.GetComponent<RectTransform>().rect.height, Is.EqualTo(245f).Within(0.001f));
            Assert.That(first.GetComponent<LayoutElement>().ignoreLayout, Is.False);
            Assert.That(second.GetComponent<LayoutElement>().ignoreLayout, Is.False);

            var regenerated = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(regenerated.Created, Is.Zero);
            Assert.That(
                UdomBuilder.FindNode(root, "absolute-overlay::__margin").gameObject,
                Is.SameAs(overlayMargin));
            Assert.That(UdomBuilder.FindNode(root, "absolute-direct").gameObject, Is.SameAs(direct));

            overlayNode.style.positionAbsolute = false;
            UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(overlayLayout.ignoreLayout, Is.False);
            Assert.That(overlayRect.anchoredPosition, Is.Not.EqualTo(new Vector2(60f, -30f)));

            overlayNode.style.positionAbsolute = true;
            UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(overlayLayout.ignoreLayout, Is.True);
            Assert.That(overlayRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(overlayRect.anchoredPosition, Is.EqualTo(new Vector2(60f, -30f)));

            Object.DestroyImmediate(root.gameObject);

            var invalid = UdomValidator.Validate(json.Replace(
                "\"position\": \"absolute\"",
                "\"position\": \"fixed\""));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("position"));
        }

        [Test]
        public void CanonicalDisplayNone_ExcludesAndPrunesEntireSubtree()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-display-none.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());

            var document = validation.Document;
            var firstNode = document.root.children[0];
            var hiddenNode = document.root.children[1];
            var secondNode = document.root.children[2];
            Assert.That(hiddenNode.style.displayNone, Is.True);
            Assert.That(hiddenNode.style.layout, Is.EqualTo("None"));
            Assert.That(hiddenNode.children[0].style.displayNone, Is.False);
            Assert.That(firstNode.style.size[1], Is.EqualTo(100f).Within(0.001f));
            Assert.That(secondNode.style.size[1], Is.EqualTo(200f).Within(0.001f));
            Assert.That(hiddenNode.style.size[1], Is.EqualTo(250f));

            var normalized = UdomJsonWriter.Write(document);
            var roundTrip = UdomValidator.Validate(normalized);
            Assert.That(roundTrip.IsValid, Is.True, roundTrip.Format());
            Assert.That(roundTrip.Document.root.children[1].style.displayNone, Is.True);

            var firstBuild = UdomBuilder.GenerateOrRegenerate(document);
            var root = firstBuild.Root;
            var panel = UdomBuilder.FindNode(root, "display-none-root").gameObject;
            var first = UdomBuilder.FindNode(root, "visible-first").gameObject;
            var second = UdomBuilder.FindNode(root, "visible-second").gameObject;
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch::__margin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch::__transform-layout"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch::__shadow-0"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-label"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-embed"), Is.Null);
            Assert.That(
                root.ExternalReferences.Any(reference => reference.slot == "hidden.worldObject"),
                Is.False);
            Assert.That(first.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(second.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(panel.transform.childCount, Is.EqualTo(2));

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.GetComponent<RectTransform>());
            Assert.That(first.GetComponent<RectTransform>().rect.height, Is.EqualTo(100f).Within(0.001f));
            Assert.That(second.GetComponent<RectTransform>().rect.height, Is.EqualTo(200f).Within(0.001f));

            var visibleJson = json.Replace("\"mode\": \"none\"", "\"mode\": \"absolute\"");
            var visibleValidation = UdomValidator.Validate(visibleJson);
            Assert.That(visibleValidation.IsValid, Is.True, visibleValidation.Format());
            Assert.That(visibleValidation.Document.root.children[1].style.displayNone, Is.False);
            var visibleBuild = UdomBuilder.GenerateOrRegenerate(visibleValidation.Document, root);
            var hidden = UdomBuilder.FindNode(root, "hidden-branch").gameObject;
            var hiddenMargin = UdomBuilder.FindNode(root, "hidden-branch::__margin").gameObject;
            var hiddenTransform = UdomBuilder.FindNode(
                root,
                "hidden-branch::__transform-layout").gameObject;
            var hiddenShadow = UdomBuilder.FindNode(root, "hidden-branch::__shadow-0").gameObject;
            var hiddenLabel = UdomBuilder.FindNode(root, "hidden-label").gameObject;
            var hiddenEmbed = UdomBuilder.FindNode(root, "hidden-embed").gameObject;
            Assert.That(visibleBuild.Created, Is.GreaterThan(0));
            Assert.That(hidden, Is.Not.Null);
            Assert.That(hiddenMargin, Is.Not.Null);
            Assert.That(hiddenTransform, Is.Not.Null);
            Assert.That(hiddenShadow, Is.Not.Null);
            Assert.That(hiddenLabel, Is.Not.Null);
            Assert.That(hiddenEmbed, Is.Not.Null);
            Assert.That(
                root.ExternalReferences.Any(reference => reference.slot == "hidden.worldObject"),
                Is.True);

            var stableVisible = UdomBuilder.GenerateOrRegenerate(visibleValidation.Document, root);
            Assert.That(stableVisible.Created, Is.Zero);
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch").gameObject, Is.SameAs(hidden));
            Assert.That(
                UdomBuilder.FindNode(root, "hidden-branch::__margin").gameObject,
                Is.SameAs(hiddenMargin));

            var externalTarget = new GameObject("Hidden Slot Target");
            root.SetExternalReference("hidden.worldObject", externalTarget);
            var hiddenAgain = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(hiddenAgain.Removed, Is.GreaterThan(0));
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-branch::__margin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-label"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "hidden-embed"), Is.Null);
            Assert.That(root.Resolve("hidden.worldObject"), Is.SameAs(externalTarget));
            Assert.That(externalTarget.transform.parent, Is.Null);

            document.root.style.displayNone = true;
            var hiddenRoot = UdomBuilder.GenerateOrRegenerate(document, root);
            Assert.That(hiddenRoot.Root, Is.SameAs(root));
            Assert.That(UdomBuilder.FindNode(root, "display-none-root"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "visible-first"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "visible-second"), Is.Null);
            Assert.That(
                UdomBuilder.FindNode(root, "display-none-root::__viewport-fit"),
                Is.Not.Null);

            Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(externalTarget);

            var invalid = UdomValidator.Validate(json.Replace(
                "\"mode\": \"none\"",
                "\"mode\": \"grid\""));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.Format(), Does.Contain("mode"));
        }

        [Test]
        public void CanonicalBorder_RendersAsymmetricRoundedContourAndRegeneratesStably()
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
            Assert.That(panelNode.style.cornerRadius, Is.EqualTo(new[] { 0f, 32f, 48f, 20f }));
            Assert.That(panelNode.style.cornerRadiusPercent, Is.EqualTo(new[] { 25f, -1f, -1f, -1f }));

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var panel = UdomBuilder.FindNode(root, "border-panel");
            var label = UdomBuilder.FindNode(root, "border-label");
            var border = UdomBuilder.FindNode(root, "border-panel::__border");
            var panelRect = panel.GetComponent<RectTransform>();
            var borderRect = border.GetComponent<RectTransform>();
            var borderImage = border.GetComponent<Image>();
            var borderMaterial = borderImage.material;
            var borderMaterialPath = AssetDatabase.GetAssetPath(borderMaterial);
            var borderMaterialGuid = AssetDatabase.AssetPathToGUID(borderMaterialPath);

            Assert.That(panel.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(panel.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(panelRect.rect.size, Is.EqualTo(new Vector2(480f, 180f)));
            Assert.That(border.GetComponent<LayoutElement>().ignoreLayout, Is.True);
            Assert.That(border.transform.GetSiblingIndex(), Is.GreaterThan(label.transform.GetSiblingIndex()));
            Assert.That(borderRect.rect.size, Is.EqualTo(new Vector2(480f, 180f)));
            Assert.That(borderImage.raycastTarget, Is.False);
            Assert.That(borderImage.color, Is.EqualTo(Color.white).Using(ColorComparer.Instance));
            Assert.That(borderMaterial.shader.name, Is.EqualTo(UdomBorderAssetUtility.ShaderName));
            Assert.That(borderMaterialPath, Does.StartWith("Assets/Html2VrcGenerated/Borders/"));
            Assert.That(borderMaterial.GetVector("_RectSize"),
                Is.EqualTo(new Vector4(480f, 180f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_OuterRadii"),
                Is.EqualTo(new Vector4(45f, 32f, 48f, 20f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerSize"),
                Is.EqualTo(new Vector4(448f, 156f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerCenter"),
                Is.EqualTo(new Vector4(4f, 4f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerRadiiX"),
                Is.EqualTo(new Vector4(25f, 20f, 36f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerRadiiY"),
                Is.EqualTo(new Vector4(37f, 24f, 32f, 4f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_BorderWidths"),
                Is.EqualTo(new Vector4(20f, 8f, 12f, 16f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetColor("_BorderLeftColor"),
                Is.EqualTo((Color)new Color32(0xFF, 0xD8, 0x4D, 0xFF)).Using(ColorComparer.Instance));
            Assert.That(borderMaterial.GetColor("_BorderTopColor"),
                Is.EqualTo((Color)new Color32(0xFF, 0x4D, 0x4D, 0xFF)).Using(ColorComparer.Instance));
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-top"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-right"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-bottom"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border-left"), Is.Null);

#if UDONSHARP
            var validationClone = Object.Instantiate(panel.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                var images = validationClone.GetComponentsInChildren<Image>(true);
                Assert.That(images.Length, Is.EqualTo(2));
                Assert.That(
                    images.Count(image => image.material.shader.name == UdomBorderAssetUtility.ShaderName),
                    Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.borderWidth[1] = 10f;
            panelNode.style.borderWidth[2] = 0f;
            panelNode.style.borderColor[0] = "#00000000";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);

            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border"), Is.SameAs(border));
            Assert.That(borderImage.material, Is.SameAs(borderMaterial));
            Assert.That(AssetDatabase.GetAssetPath(borderImage.material), Is.EqualTo(borderMaterialPath));
            Assert.That(AssetDatabase.AssetPathToGUID(borderMaterialPath), Is.EqualTo(borderMaterialGuid));
            Assert.That(borderMaterial.GetVector("_BorderWidths"),
                Is.EqualTo(new Vector4(20f, 10f, 0f, 16f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerSize"),
                Is.EqualTo(new Vector4(460f, 154f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerCenter"),
                Is.EqualTo(new Vector4(10f, 3f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerRadiiX"),
                Is.EqualTo(new Vector4(25f, 32f, 48f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetVector("_InnerRadiiY"),
                Is.EqualTo(new Vector4(35f, 22f, 32f, 4f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(borderMaterial.GetColor("_BorderLeftColor").a, Is.EqualTo(0f).Within(0.001f));

            panelNode.style.borderWidth = new[] { 300f, 100f, 300f, 100f };
            panelNode.style.borderColor[0] = "#FFD84DFF";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(UdomBuilder.FindNode(root, "border-panel::__border"), Is.SameAs(border));
            Assert.That(borderMaterial.GetFloat("_HasInner"), Is.EqualTo(0f).Within(0.001f));

            panelNode.style.borderWidth = new[] { 0f, 0f, 0f, 0f };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
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
        public void CanonicalShadow_RendersOuterAndInsetLayersAndRegeneratesStably()
        {
            var json = LoadRepositoryFile(
                "packages",
                "udom",
                "fixtures",
                "valid",
                "unity-shadow.udom.json");
            var validation = UdomValidator.Validate(json);

            Assert.That(validation.IsValid, Is.True, validation.Format());
            Assert.That(validation.Issues, Is.Empty, validation.Format());
            var panelNode = validation.Document.root.children.Single(node => node.id == "shadow-panel");
            Assert.That(panelNode.style.shadowOffsets, Is.EqualTo(new[] { 16f, 12f, -6f, 4f, 10f, -8f }));
            Assert.That(panelNode.style.shadowBlurs, Is.EqualTo(new[] { 18f, 0f, 12f }));
            Assert.That(panelNode.style.shadowSpreads, Is.EqualTo(new[] { 4f, -2f, 3f }));
            Assert.That(panelNode.style.shadowColors,
                Is.EqualTo(new[] { "#10204080", "#FF8040A0", "#081020B0" }));
            Assert.That(panelNode.style.shadowInsets, Is.EqualTo(new[] { false, false, true }));

            const string insetJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 200, ""height"": 100 },
              ""root"": {
                ""type"": ""element"", ""id"": ""inset-shadow"", ""name"": ""view"",
                ""style"": { ""paint"": { ""shadows"": [{ ""inset"": true }] } }
              }
            }";
            var inset = UdomValidator.Validate(insetJson);
            Assert.That(inset.IsValid, Is.True, inset.Format());
            Assert.That(inset.Document.root.style.shadowOffsets, Is.EqualTo(new[] { 0f, 0f }));
            Assert.That(inset.Document.root.style.shadowBlurs, Is.EqualTo(new[] { 0f }));
            Assert.That(inset.Document.root.style.shadowSpreads, Is.EqualTo(new[] { 0f }));
            Assert.That(inset.Document.root.style.shadowColors, Is.EqualTo(new[] { "#00000080" }));
            Assert.That(inset.Document.root.style.shadowInsets, Is.EqualTo(new[] { true }));
            Assert.That(inset.Issues, Is.Empty, inset.Format());

            var insetBuild = UdomBuilder.GenerateOrRegenerate(inset.Document);
            try
            {
                var insetNode = UdomBuilder.FindNode(insetBuild.Root, "inset-shadow");
                var insetLayer = UdomBuilder.FindNode(insetBuild.Root, "inset-shadow::__shadow-0");
                Assert.That(insetNode, Is.Not.Null);
                Assert.That(insetLayer, Is.Not.Null);
                Assert.That(UdomBuilder.FindNode(insetBuild.Root, "inset-shadow::__shadow-layout"), Is.Null);
                Assert.That(insetLayer.transform.parent, Is.SameAs(insetNode.transform));
                Assert.That(insetLayer.GetComponent<LayoutElement>().ignoreLayout, Is.True);
                Assert.That(insetLayer.GetComponent<Image>().raycastTarget, Is.False);
                Assert.That(insetLayer.GetComponent<Image>().material.GetFloat("_Inset"), Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(insetBuild.Root.gameObject);
            }

            const string textInsetJson = @"{
              ""asset"": { ""version"": ""0.1"" },
              ""viewport"": { ""width"": 200, ""height"": 100 },
              ""root"": {
                ""type"": ""element"", ""id"": ""text-inset-root"", ""name"": ""view"",
                ""style"": { ""layout"": {
                  ""mode"": ""flex"", ""width"": ""100%"", ""height"": ""100%"",
                  ""flex"": { ""direction"": ""column"" }
                } },
                ""children"": [{
                  ""type"": ""text"", ""id"": ""inset-text"", ""value"": ""Layered text"",
                  ""style"": {
                    ""layout"": { ""width"": 120, ""height"": 40 },
                    ""paint"": {
                      ""opacity"": 0.5,
                      ""shadows"": [{ ""blur"": 4, ""inset"": true }]
                    }
                  }
                }]
              }
            }";
            var textInset = UdomValidator.Validate(textInsetJson);
            Assert.That(textInset.IsValid, Is.True, textInset.Format());
            Assert.That(textInset.Issues, Is.Empty, textInset.Format());
            var textInsetBuild = UdomBuilder.GenerateOrRegenerate(textInset.Document);
            try
            {
                var textLayout = UdomBuilder.FindNode(textInsetBuild.Root, "inset-text::__shadow-layout");
                var textLayer = UdomBuilder.FindNode(textInsetBuild.Root, "inset-text::__shadow-0");
                var textNode = UdomBuilder.FindNode(textInsetBuild.Root, "inset-text");
                Assert.That(textLayout, Is.Not.Null);
                Assert.That(textLayer.transform.parent, Is.SameAs(textLayout.transform));
                Assert.That(textNode.transform.parent, Is.SameAs(textLayout.transform));
                Assert.That(textLayer.transform.GetSiblingIndex(), Is.EqualTo(0));
                Assert.That(textNode.transform.GetSiblingIndex(), Is.EqualTo(1));
                Assert.That(textNode.GetComponent<TextMeshProUGUI>(), Is.Not.Null);
                Assert.That(textNode.GetComponentInChildren<Image>(), Is.Null);
                Assert.That(textLayout.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(textInsetBuild.Root.gameObject);
            }

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, sample);
            var root = build.Root;
            var layout = UdomBuilder.FindNode(root, "shadow-panel::__shadow-layout");
            var shadow0 = UdomBuilder.FindNode(root, "shadow-panel::__shadow-0");
            var shadow1 = UdomBuilder.FindNode(root, "shadow-panel::__shadow-1");
            var insetShadow = UdomBuilder.FindNode(root, "shadow-panel::__shadow-2");
            var panel = UdomBuilder.FindNode(root, "shadow-panel");
            var label = UdomBuilder.FindNode(root, "shadow-label");
            var documentRoot = UdomBuilder.FindNode(root, "shadow-root");
            Assert.That(layout, Is.Not.Null);
            Assert.That(shadow0, Is.Not.Null);
            Assert.That(shadow1, Is.Not.Null);
            Assert.That(insetShadow, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);
            Assert.That(label, Is.Not.Null);
            Assert.That(layout.transform.parent, Is.SameAs(documentRoot.transform));
            Assert.That(shadow0.transform.parent, Is.SameAs(layout.transform));
            Assert.That(shadow1.transform.parent, Is.SameAs(layout.transform));
            Assert.That(panel.transform.parent, Is.SameAs(layout.transform));
            Assert.That(insetShadow.transform.parent, Is.SameAs(panel.transform));
            Assert.That(label.transform.parent, Is.SameAs(panel.transform));
            Assert.That(shadow0.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(shadow1.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(panel.transform.GetSiblingIndex(), Is.EqualTo(2));
            Assert.That(insetShadow.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(label.transform.GetSiblingIndex(), Is.EqualTo(1));

            var layoutRect = layout.GetComponent<RectTransform>();
            var shadow0Rect = shadow0.GetComponent<RectTransform>();
            var shadow1Rect = shadow1.GetComponent<RectTransform>();
            var insetRect = insetShadow.GetComponent<RectTransform>();
            var panelRect = panel.GetComponent<RectTransform>();
            var shadow0Image = shadow0.GetComponent<Image>();
            var shadow1Image = shadow1.GetComponent<Image>();
            var insetImage = insetShadow.GetComponent<Image>();
            var shadow0Material = shadow0Image.material;
            var insetMaterial = insetImage.material;
            var insetMaterialPath = AssetDatabase.GetAssetPath(insetMaterial);
            var insetMaterialGuid = AssetDatabase.AssetPathToGUID(insetMaterialPath);
            var shadow0MaterialPath = AssetDatabase.GetAssetPath(shadow0Material);
            var shadow0MaterialGuid = AssetDatabase.AssetPathToGUID(shadow0MaterialPath);
            Assert.That(layoutRect.rect.size, Is.EqualTo(new Vector2(440f, 140f)));
            Assert.That(layout.GetComponent<LayoutElement>(), Is.Not.Null);
            Assert.That(panel.GetComponent<LayoutElement>(), Is.Null);
            Assert.That(panelRect.rect.size, Is.EqualTo(new Vector2(440f, 140f)));
            Assert.That(shadow0Rect.anchoredPosition, Is.EqualTo(new Vector2(16f, -12f)));
            Assert.That(shadow0Rect.rect.size, Is.EqualTo(new Vector2(484f, 184f)));
            Assert.That(shadow1Rect.anchoredPosition, Is.EqualTo(new Vector2(-6f, -4f)));
            Assert.That(shadow1Rect.rect.size, Is.EqualTo(new Vector2(436f, 136f)));
            Assert.That(insetRect.rect.size, Is.EqualTo(new Vector2(440f, 140f)));
            Assert.That(insetRect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(insetShadow.GetComponent<LayoutElement>().ignoreLayout, Is.True);
            Assert.That(shadow0Image.raycastTarget, Is.False);
            Assert.That(shadow1Image.raycastTarget, Is.False);
            Assert.That(insetImage.raycastTarget, Is.False);
            Assert.That(shadow0Material.shader.name, Is.EqualTo(UdomShadowAssetUtility.ShaderName));
            Assert.That(shadow1Image.material.shader.name, Is.EqualTo(UdomShadowAssetUtility.ShaderName));
            Assert.That(insetMaterial.shader.name, Is.EqualTo(UdomShadowAssetUtility.ShaderName));
            Assert.That(shadow0MaterialPath, Does.StartWith("Assets/Html2VrcGenerated/Shadows/"));
            Assert.That(shadow0Material.GetVector("_ShapeSize"),
                Is.EqualTo(new Vector4(448f, 148f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(shadow0Material.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(32f, 28f, 20f, 12f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(shadow0Material.GetFloat("_Blur"), Is.EqualTo(18f).Within(0.001f));
            Assert.That(insetMaterial.GetVector("_ShapeSize"),
                Is.EqualTo(new Vector4(434f, 134f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetMaterial.GetVector("_CornerRadii"),
                Is.EqualTo(new Vector4(25f, 21f, 13f, 5f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetMaterial.GetVector("_BoxSize"),
                Is.EqualTo(new Vector4(440f, 140f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetMaterial.GetVector("_BoxCornerRadii"),
                Is.EqualTo(new Vector4(28f, 24f, 16f, 8f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetMaterial.GetVector("_Offset"),
                Is.EqualTo(new Vector4(10f, 8f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetMaterial.GetFloat("_Blur"), Is.EqualTo(12f).Within(0.001f));
            Assert.That(insetMaterial.GetFloat("_Inset"), Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                shadow0Image.color,
                Is.EqualTo((Color)new Color32(0x10, 0x20, 0x40, 0x80)).Using(ColorComparer.Instance));

#if UDONSHARP
            var validationClone = Object.Instantiate(layout.gameObject);
            try
            {
                WorldValidation.RemoveIllegalComponents(
                    new List<GameObject> { validationClone },
                    WorldValidation.WhiteListConfiguration.VRCSDK3);
                var images = validationClone.GetComponentsInChildren<Image>(true);
                Assert.That(images.Length, Is.EqualTo(4));
                Assert.That(
                    images.Count(image => image.material.shader.name == UdomShadowAssetUtility.ShaderName),
                    Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(validationClone);
            }
#endif

            panelNode.style.shadowOffsets[0] = 24f;
            panelNode.style.shadowBlurs[0] = 10f;
            panelNode.style.shadowSpreads[0] = 8f;
            panelNode.style.shadowOffsets[4] = -14f;
            panelNode.style.shadowBlurs[2] = 20f;
            panelNode.style.shadowSpreads[2] = -4f;
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(layout, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-layout")));
            Assert.That(shadow0, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-0")));
            Assert.That(shadow1, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-1")));
            Assert.That(insetShadow, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-2")));
            Assert.That(panel, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel")));
            Assert.That(shadow0Rect.anchoredPosition, Is.EqualTo(new Vector2(24f, -12f)));
            Assert.That(shadow0Rect.rect.size, Is.EqualTo(new Vector2(476f, 176f)));
            Assert.That(AssetDatabase.GetAssetPath(shadow0Image.material), Is.EqualTo(shadow0MaterialPath));
            Assert.That(AssetDatabase.AssetPathToGUID(shadow0MaterialPath), Is.EqualTo(shadow0MaterialGuid));
            Assert.That(AssetDatabase.GetAssetPath(insetImage.material), Is.EqualTo(insetMaterialPath));
            Assert.That(AssetDatabase.AssetPathToGUID(insetMaterialPath), Is.EqualTo(insetMaterialGuid));
            Assert.That(shadow0Image.material.GetVector("_ShapeSize"),
                Is.EqualTo(new Vector4(456f, 156f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(shadow0Image.material.GetFloat("_Blur"), Is.EqualTo(10f).Within(0.001f));
            Assert.That(insetImage.material.GetVector("_ShapeSize"),
                Is.EqualTo(new Vector4(448f, 148f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetImage.material.GetVector("_Offset"),
                Is.EqualTo(new Vector4(-14f, 8f, 0f, 0f))
                    .Using(Vector4ComparerWithEqualsOperator.Instance));
            Assert.That(insetImage.material.GetFloat("_Blur"), Is.EqualTo(20f).Within(0.001f));

            panelNode.style.shadowColors[1] = "#00000000";
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(shadow0, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-0")));
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__shadow-1"), Is.Null);
            Assert.That(insetShadow, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-2")));

            panelNode.style.transformOperationTypes = new[] { "rotate" };
            panelNode.style.transformOperationValues = new[] { 15f, 0f };
            panelNode.style.transformOperationValuesArePercent = new[] { false, false };
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            var transformLayout = UdomBuilder.FindNode(root, "shadow-panel::__transform-layout");
            var rotate = UdomBuilder.FindNode(root, "shadow-panel::__transform-operation-0");
            Assert.That(transformLayout, Is.Not.Null);
            Assert.That(rotate, Is.Not.Null);
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__shadow-layout"), Is.Null);
            Assert.That(shadow0, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-0")));
            Assert.That(insetShadow, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel::__shadow-2")));
            Assert.That(panel, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel")));
            Assert.That(shadow0.transform.parent, Is.SameAs(rotate.transform));
            Assert.That(panel.transform.parent, Is.SameAs(rotate.transform));
            Assert.That(insetShadow.transform.parent, Is.SameAs(panel.transform));
            Assert.That(shadow0.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(panel.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(insetShadow.transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(label.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(rotate.GetComponent<RectTransform>().localEulerAngles.z, Is.EqualTo(345f).Within(0.001f));

            panelNode.style.shadowOffsets = System.Array.Empty<float>();
            panelNode.style.shadowBlurs = System.Array.Empty<float>();
            panelNode.style.shadowSpreads = System.Array.Empty<float>();
            panelNode.style.shadowColors = System.Array.Empty<string>();
            panelNode.style.shadowInsets = System.Array.Empty<bool>();
            panelNode.style.transformOperationTypes = System.Array.Empty<string>();
            panelNode.style.transformOperationValues = System.Array.Empty<float>();
            panelNode.style.transformOperationValuesArePercent = System.Array.Empty<bool>();
            UdomBuilder.GenerateOrRegenerate(validation.Document, root, sample);
            Assert.That(panel, Is.SameAs(UdomBuilder.FindNode(root, "shadow-panel")));
            Assert.That(panel.transform.parent, Is.SameAs(documentRoot.transform));
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__shadow-layout"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__transform-layout"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__shadow-0"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "shadow-panel::__shadow-2"), Is.Null);
            Assert.That(panelRect.rect.size, Is.EqualTo(new Vector2(440f, 140f)));
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
        public void HtmlConverter_FlexCssUsesCanonicalSizingAlignmentAndStableHierarchy()
        {
            const string html = @"
              <main id=""css-flex-root"" data-canvas-size=""700 520""
                style=""width: 700px; height: 520px; display: flex; flex-direction: column-reverse; align-items: flex-start; gap: 20px"">
                <section id=""css-flex-row""
                  style=""width: 600px; height: 160px; display: flex; flex-direction: row-reverse; justify-content: flex-end; align-items: center; padding: 20px; gap: 10px"">
                  <div id=""css-flex-a"" style=""width: 80px; height: 40px; flex-grow: 1; flex-shrink: 0.5; flex-basis: 100px; order: 2""></div>
                  <div id=""css-flex-b"" style=""width: 80px; height: 40px; flex-grow: 2; flex-basis: 50%; order: -1; align-self: flex-start""></div>
                  <div id=""css-flex-c"" style=""width: 80px; height: 40px; flex-shrink: 0; order: 2; align-self: stretch""></div>
                </section>
                <section id=""css-space-row""
                  style=""width: 400px; height: 100px; display: flex; justify-content: space-evenly; align-items: center; gap: 10px"">
                  <div id=""css-space-first"" style=""width: 80px; height: 40px""></div>
                  <div id=""css-space-second"" style=""width: 80px; height: 40px""></div>
                </section>
                <section id=""css-absolute-host"" style=""width: 100px; height: 80px; display: block"">
                  <div id=""css-absolute"" style=""width: 50px; height: 30px; position: absolute; left: 15px; top: 25px""></div>
                </section>
                <div id=""css-hidden"" style=""width: 40px; height: 40px; display: none""></div>
              </main>";
            var conversion = HtmlToUdomConverter.Convert(html);

            Assert.That(conversion.IsValid, Is.True, conversion.Format());
            var document = conversion.Document;
            var row = document.root.children[0];
            var spaceRow = document.root.children[1];
            var absolute = document.root.children[2].children[0];
            var hidden = document.root.children[3];
            var a = row.children[0];
            var b = row.children[1];
            var c = row.children[2];
            Assert.That(document.root.style.layout, Is.EqualTo("Vertical"));
            Assert.That(document.root.style.reverseChildren, Is.True);
            Assert.That(document.root.style.childAlignment, Is.EqualTo("LowerLeft"));
            Assert.That(row.style.layout, Is.EqualTo("Horizontal"));
            Assert.That(row.style.reverseChildren, Is.True);
            Assert.That(row.style.justifyContent, Is.EqualTo("End"));
            Assert.That(row.style.childAlignment, Is.EqualTo("MiddleLeft"));
            Assert.That(row.style.spacing, Is.EqualTo(10f).Within(0.001f));
            Assert.That(row.style.useResolvedChildrenHeight, Is.True);
            Assert.That(a.style.flexOrder, Is.EqualTo(2));
            Assert.That(a.style.flexShrink, Is.EqualTo(0.5f));
            Assert.That(a.style.flexBasis, Is.EqualTo(100f));
            Assert.That(a.style.flexBasisIsPercent, Is.False);
            Assert.That(a.style.flexibleWidth, Is.Zero);
            Assert.That(a.style.flexibleHeight, Is.Zero);
            Assert.That(a.style.size[0], Is.EqualTo(126.6667f).Within(0.001f));
            Assert.That(b.style.flexOrder, Is.EqualTo(-1));
            Assert.That(b.style.flexShrink, Is.EqualTo(1f));
            Assert.That(b.style.flexBasis, Is.EqualTo(50f));
            Assert.That(b.style.flexBasisIsPercent, Is.True);
            Assert.That(b.style.alignSelf, Is.EqualTo("Start"));
            Assert.That(b.style.size[0], Is.EqualTo(333.3333f).Within(0.001f));
            Assert.That(b.style.alignSelfMargin, Is.EqualTo(new[] { 0f, 0f, 0f, 80f }));
            Assert.That(c.style.flexShrink, Is.Zero);
            Assert.That(c.style.alignSelf, Is.EqualTo("Stretch"));
            Assert.That(c.style.size, Is.EqualTo(new[] { 80f, 120f }));
            Assert.That(spaceRow.style.layout, Is.EqualTo("Horizontal"));
            Assert.That(spaceRow.style.justifyContent, Is.EqualTo("SpaceEvenly"));
            Assert.That(spaceRow.style.childAlignment, Is.EqualTo("MiddleCenter"));
            Assert.That(spaceRow.style.spacing, Is.EqualTo(86.6667f).Within(0.001f));
            Assert.That(absolute.style.positionAbsolute, Is.True);
            Assert.That(absolute.style.position, Is.EqualTo(new[] { 15f, 25f }));
            Assert.That(hidden.style.displayNone, Is.True);

            var normalized = UdomValidator.Validate(conversion.Json);
            Assert.That(normalized.IsValid, Is.True, normalized.Format());
            Assert.That(normalized.Document.root.children[0].children[1].style.flexBasisIsPercent, Is.True);
            Assert.That(normalized.Document.root.children[0].children[1].style.alignSelf, Is.EqualTo("Start"));

            var build = UdomBuilder.GenerateOrRegenerate(document);
            var root = build.Root;
            var rootRect = UdomBuilder.FindNode(root, "css-flex-root").GetComponent<RectTransform>();
            var rowObject = UdomBuilder.FindNode(root, "css-flex-row").gameObject;
            var spaceRowObject = UdomBuilder.FindNode(root, "css-space-row").gameObject;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowObject.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(spaceRowObject.GetComponent<RectTransform>());

            Assert.That(rowObject.GetComponent<HorizontalLayoutGroup>().childAlignment, Is.EqualTo(TextAnchor.MiddleLeft));
            Assert.That(UdomBuilder.FindNode(root, "css-absolute-host").transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(spaceRowObject.transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(rowObject.transform.GetSiblingIndex(), Is.EqualTo(2));
            Assert.That(UdomBuilder.FindNode(root, "css-flex-c").transform.GetSiblingIndex(), Is.EqualTo(0));
            Assert.That(UdomBuilder.FindNode(root, "css-flex-a").transform.GetSiblingIndex(), Is.EqualTo(1));
            Assert.That(UdomBuilder.FindNode(root, "css-flex-b::__margin").transform.GetSiblingIndex(), Is.EqualTo(2));
            Assert.That(
                UdomBuilder.FindNode(root, "css-flex-a").GetComponent<RectTransform>().localPosition.y,
                Is.Zero.Within(0.001f));
            AssertAlignedWithinMargin(
                root,
                "css-flex-b",
                new Vector2(333.3333f, 40f),
                new Vector2(0f, 40f),
                new Vector2(333.3333f, 120f));
            Assert.That(
                UdomBuilder.FindNode(root, "css-flex-c").GetComponent<RectTransform>().rect.height,
                Is.EqualTo(120f).Within(0.001f));
            Assert.That(
                UdomBuilder.FindNode(root, "css-space-first").GetComponent<RectTransform>().localPosition.x,
                Is.EqualTo(-83.3333f).Within(0.001f));
            Assert.That(
                UdomBuilder.FindNode(root, "css-space-second").GetComponent<RectTransform>().localPosition.x,
                Is.EqualTo(83.3333f).Within(0.001f));
            Assert.That(
                UdomBuilder.FindNode(root, "css-absolute").GetComponent<RectTransform>().anchoredPosition,
                Is.EqualTo(new Vector2(15f, -25f)));
            Assert.That(UdomBuilder.FindNode(root, "css-hidden"), Is.Null);

            var originalB = UdomBuilder.FindNode(root, "css-flex-b").gameObject;
            var changed = HtmlToUdomConverter.Convert(
                html.Replace("align-self: flex-start", "align-self: auto")
                    .Replace("display: none", "display: block"));
            Assert.That(changed.IsValid, Is.True, changed.Format());
            var regenerated = UdomBuilder.GenerateOrRegenerate(changed.Document, root);
            Assert.That(UdomBuilder.FindNode(root, "css-flex-b").gameObject, Is.SameAs(originalB));
            Assert.That(UdomBuilder.FindNode(root, "css-flex-b::__margin"), Is.Null);
            Assert.That(UdomBuilder.FindNode(root, "css-hidden"), Is.Not.Null);
            Assert.That(regenerated.Created, Is.GreaterThan(0));
            Assert.That(regenerated.Removed, Is.GreaterThan(0));
            Object.DestroyImmediate(root.gameObject);
        }

        [Test]
        public void HtmlConverter_RejectsUnsupportedFlexCssValues()
        {
            const string html = @"
              <main id=""invalid-flex""
                style=""display: flex; justify-content: left; align-items: baseline"">
                <div id=""invalid-item""
                  style=""align-self: baseline; flex-shrink: -1; flex-basis: calc(50% - 2px); order: 1.5""></div>
              </main>";

            var conversion = HtmlToUdomConverter.Convert(html);

            Assert.That(conversion.IsValid, Is.False);
            Assert.That(conversion.Format(), Does.Contain("justify-content"));
            Assert.That(conversion.Format(), Does.Contain("align-items"));
            Assert.That(conversion.Format(), Does.Contain("align-self"));
            Assert.That(conversion.Format(), Does.Contain("flex-shrink"));
            Assert.That(conversion.Format(), Does.Contain("flex-basis"));
            Assert.That(conversion.Format(), Does.Contain("order"));
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

        private static void AssertAlignedWithinMargin(
            UdomGeneratedRoot root,
            string nodeId,
            Vector2 expectedContentSize,
            Vector2 expectedContentPosition,
            Vector2 expectedWrapperSize,
            string contentSuffix = "")
        {
            var wrapper = UdomBuilder.FindNode(root, nodeId + "::__margin");
            var content = UdomBuilder.FindNode(root, nodeId + contentSuffix);
            Assert.That(wrapper, Is.Not.Null, nodeId + " alignment wrapper");
            Assert.That(content, Is.Not.Null, nodeId + " aligned content");
            var wrapperRect = wrapper.GetComponent<RectTransform>();
            var contentRect = content.GetComponent<RectTransform>();
            Assert.That(wrapperRect.rect.width, Is.EqualTo(expectedWrapperSize.x).Within(0.001f));
            Assert.That(wrapperRect.rect.height, Is.EqualTo(expectedWrapperSize.y).Within(0.001f));
            Assert.That(contentRect.rect.width, Is.EqualTo(expectedContentSize.x).Within(0.001f));
            Assert.That(contentRect.rect.height, Is.EqualTo(expectedContentSize.y).Within(0.001f));
            Assert.That(
                contentRect.anchoredPosition.x,
                Is.EqualTo(expectedContentPosition.x).Within(0.001f),
                nodeId + " content x");
            Assert.That(
                contentRect.anchoredPosition.y,
                Is.EqualTo(expectedContentPosition.y).Within(0.001f),
                nodeId + " content y");
        }

        private static void AssertResolvedTopLeftRect(
            UdomGeneratedRoot root,
            string nodeId,
            Vector2 expectedPosition,
            Vector2 expectedSize)
        {
            var node = UdomBuilder.FindNode(root, nodeId);
            Assert.That(node, Is.Not.Null, nodeId + " resolved node");
            var rect = node.GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)), nodeId + " anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)), nodeId + " anchorMax");
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(0f, 1f)), nodeId + " pivot");
            Assert.That(
                rect.anchoredPosition,
                Is.EqualTo(new Vector2(expectedPosition.x, -expectedPosition.y)),
                nodeId + " anchored position");
            Assert.That(rect.rect.size, Is.EqualTo(expectedSize), nodeId + " size");
            Assert.That(rect.GetComponent<LayoutElement>().ignoreLayout, Is.True, nodeId + " ignoreLayout");
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
