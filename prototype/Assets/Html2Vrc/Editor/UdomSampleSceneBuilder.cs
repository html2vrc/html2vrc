using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Html2Vrc.Editor
{
    public static class UdomSampleSceneBuilder
    {
        public const string SampleUdomPath = "Assets/Html2Vrc/Samples/WorldSettings.udom.json";
        public const string SampleScenePath = "Assets/Html2Vrc/Samples/WorldSettingsSample.unity";
        public const string ScreenshotRelativePath = "Artifacts/WorldSettingsSample.png";

        [MenuItem("Tools/HTML2VRC/Build Sample World Settings Scene")]
        public static void BuildSampleSceneMenu()
        {
            var root = BuildSampleScene();
            Selection.activeGameObject = root.gameObject;
            EditorGUIUtility.PingObject(root.gameObject);
            Debug.Log($"HTML2VRC: Sample scene generated at {SampleScenePath}.", root);
        }

        public static void BuildSampleSceneBatch()
        {
            BuildSampleScene();
            CaptureSampleScreenshot();
            AssetDatabase.SaveAssets();
            Debug.Log("HTML2VRC_BATCH_SUCCESS: sample scene generated and screenshot captured.");
        }

        public static UdomGeneratedRoot BuildSampleScene()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleUdomPath);
            if (source == null)
            {
                throw new FileNotFoundException("Sample UDOM was not imported as TextAsset.", SampleUdomPath);
            }

            var validation = UdomValidator.Validate(source.text);
            if (!validation.IsValid)
            {
                throw new UdomBuildException(validation);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = CreateCamera();
            CreateDirectionalLight();
            var externalLight = CreateExternalLight();
            CreateTestFloor();
#if UDONSHARP
            UdomVrchatSetup.EnsureSceneDescriptor();
#endif

            var build = UdomBuilder.GenerateOrRegenerate(validation.Document, null, source);
            build.Root.transform.position = Vector3.zero;
            build.Root.transform.rotation = Quaternion.identity;
            build.Root.SetExternalReference("world-light", externalLight.gameObject);
            var canvas = build.Root.GetComponent<Canvas>();
            canvas.worldCamera = camera;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SampleScenePath))
            {
                throw new InvalidOperationException($"Failed to save sample scene: {SampleScenePath}");
            }

            AssetDatabase.SaveAssets();
            return build.Root;
        }

        public static string CaptureSampleScreenshot()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                throw new InvalidOperationException("Sample scene has no Main Camera.");
            }

            const int width = 1600;
            const int height = 900;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                ForceRebuildAllCanvasLayouts();
                camera.Render();
                ForceRebuildAllCanvasLayouts();
                camera.Render();
                RenderTexture.active = renderTexture;

                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();

                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                  ?? throw new InvalidOperationException("Could not determine project root.");
                var absolutePath = Path.Combine(projectRoot, ScreenshotRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)
                                          ?? throw new InvalidOperationException("Could not determine artifact directory."));
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                Debug.Log($"HTML2VRC_SCREENSHOT: {absolutePath}");
                return absolutePath;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static void ForceRebuildAllCanvasLayouts()
        {
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            for (var index = 0; index < canvases.Length; index++)
            {
                var rect = canvases[index].GetComponent<RectTransform>();
                if (rect != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                }
            }

            Canvas.ForceUpdateCanvases();
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.075f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            return camera;
        }

        private static void CreateDirectionalLight()
        {
            var lightObject = new GameObject("Directional Light", typeof(Light));
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.2f;
        }

        private static Light CreateExternalLight()
        {
            var lightObject = new GameObject("World Light (External User Object)", typeof(Light));
            lightObject.transform.position = new Vector3(0f, 2f, -2f);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 6f;
            light.color = new Color(0.35f, 0.78f, 1f);
            return light;
        }

        private static void CreateTestFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Test Floor (User-Owned Collider)";
            // The sample panel is 720 px high at a 0.01 world scale (7.2 m).
            // Keep the floor top 0.2 m below its -3.6 m lower edge.
            floor.transform.position = new Vector3(0f, -3.85f, 0f);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
        }
    }
}
