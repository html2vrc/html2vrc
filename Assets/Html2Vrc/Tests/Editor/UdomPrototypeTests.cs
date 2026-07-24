using System.Collections;
using System.Collections.Generic;
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
