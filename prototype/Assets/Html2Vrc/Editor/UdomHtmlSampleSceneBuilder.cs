using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Html2Vrc.Editor
{
    /// <summary>
    /// WorldSettings.html을 직접 변환해 독립된 VRChat 검증 씬을 만든다.
    /// 기존 UDOM 샘플 씬과 경로를 분리해 HTML 입력 경로를 명확히 검증한다.
    /// </summary>
    public static class UdomHtmlSampleSceneBuilder
    {
        public const string SampleHtmlPath = "Assets/Html2Vrc/Samples/WorldSettings.html";
        public const string SampleScenePath =
            "Assets/Html2Vrc/Samples/WorldSettingsHtmlSample.unity";

        [MenuItem("Tools/HTML2VRC/Build HTML World Settings Scene")]
        public static void BuildSampleSceneMenu()
        {
            var root = BuildSampleScene();
            Selection.activeGameObject = root.gameObject;
            EditorGUIUtility.PingObject(root.gameObject);
            Debug.Log(
                $"HTML2VRC_HTML_SCENE_SUCCESS: {SampleHtmlPath} -> {SampleScenePath}",
                root);
        }

        public static UdomGeneratedRoot BuildSampleScene()
        {
            // 다른 씬에 사용자가 남긴 수정사항을 버리지 않는다.
            if (!EditorSceneManager.SaveOpenScenes())
            {
                throw new InvalidOperationException("열려 있는 Unity 씬을 저장하지 못했다.");
            }

            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleHtmlPath);
            if (source == null)
            {
                throw new FileNotFoundException(
                    "샘플 HTML을 TextAsset으로 불러오지 못했다.",
                    SampleHtmlPath);
            }

            var conversion = HtmlToUdomConverter.Convert(source.text);
            if (!conversion.IsValid)
            {
                throw new InvalidOperationException(
                    $"샘플 HTML 변환에 실패했다.{Environment.NewLine}{conversion.Format()}");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = CreateCamera();
            CreateDirectionalLight();
            var externalLight = CreateExternalLight();
            CreateTestFloor();
#if UDONSHARP
            UdomVrchatSetup.EnsureSceneDescriptor();
#endif

            var build = UdomBuilder.GenerateOrRegenerate(
                conversion.Document,
                null,
                source);
            build.Root.transform.position = Vector3.zero;
            build.Root.transform.rotation = Quaternion.identity;
            build.Root.SetExternalReference("world-light", externalLight.gameObject);

            var canvas = build.Root.GetComponent<Canvas>();
            canvas.worldCamera = camera;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SampleScenePath))
            {
                throw new InvalidOperationException(
                    $"HTML 샘플 씬 저장에 실패했다: {SampleScenePath}");
            }

            AssetDatabase.SaveAssets();
            return build.Root;
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject(
                "Main Camera",
                typeof(Camera),
                typeof(AudioListener));
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
            var lightObject = new GameObject(
                "World Light (HTML External User Object)",
                typeof(Light));
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
            floor.name = "HTML Test Floor (User-Owned Collider)";
            // 7.2m 높이 패널의 아래쪽보다 0.2m 낮게 바닥 윗면을 둔다.
            floor.transform.position = new Vector3(0f, -3.85f, 0f);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
        }
    }
}
