using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;

namespace Html2Vrc.Editor
{
    public static class UdomVrchatBuildVerification
    {
        [MenuItem("Tools/HTML2VRC/VRChat/Build Sample World Bundle")]
        public static async void BuildSampleWorldBundle()
        {
            try
            {
                var builder = PrepareBuilder(
                    UdomSampleSceneBuilder.BuildSampleScene,
                    UdomSampleSceneBuilder.SampleScenePath);
                var bundlePath = await builder.Build();
                Debug.Log($"HTML2VRC_VRCHAT_BUILD_SUCCESS: {bundlePath}");
                ExitBatchMode(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"HTML2VRC_VRCHAT_BUILD_FAILED: {exception.Message}");
                ExitBatchMode(1);
            }
        }

        [MenuItem("Tools/HTML2VRC/VRChat/Build & Test Sample World")]
        public static async void BuildAndTestSampleWorld()
        {
            try
            {
                var builder = PrepareBuilder(
                    UdomSampleSceneBuilder.BuildSampleScene,
                    UdomSampleSceneBuilder.SampleScenePath);
                await builder.BuildAndTest();
                Debug.Log("HTML2VRC_VRCHAT_BUILD_AND_TEST_LAUNCHED");
                ExitBatchMode(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"HTML2VRC_VRCHAT_BUILD_AND_TEST_FAILED: {exception.Message}");
                ExitBatchMode(1);
            }
        }

        [MenuItem("Tools/HTML2VRC/VRChat/Build HTML Sample World Bundle")]
        public static async void BuildHtmlSampleWorldBundle()
        {
            try
            {
                var builder = PrepareBuilder(
                    UdomHtmlSampleSceneBuilder.BuildSampleScene,
                    UdomHtmlSampleSceneBuilder.SampleScenePath);
                var bundlePath = await builder.Build();
                Debug.Log($"HTML2VRC_HTML_VRCHAT_BUILD_SUCCESS: {bundlePath}");
                ExitBatchMode(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"HTML2VRC_HTML_VRCHAT_BUILD_FAILED: {exception.Message}");
                ExitBatchMode(1);
            }
        }

        [MenuItem("Tools/HTML2VRC/VRChat/Build & Test HTML Sample World")]
        public static async void BuildAndTestHtmlSampleWorld()
        {
            try
            {
                var builder = PrepareBuilder(
                    UdomHtmlSampleSceneBuilder.BuildSampleScene,
                    UdomHtmlSampleSceneBuilder.SampleScenePath);
                await builder.BuildAndTest();
                Debug.Log("HTML2VRC_HTML_VRCHAT_BUILD_AND_TEST_LAUNCHED");
                ExitBatchMode(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"HTML2VRC_HTML_VRCHAT_BUILD_AND_TEST_FAILED: {exception.Message}");
                ExitBatchMode(1);
            }
        }

        private static IVRCSdkWorldBuilderApi PrepareBuilder(
            Func<UdomGeneratedRoot> buildScene,
            string scenePath)
        {
            buildScene();
            EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single);

            if (!UpdateLayers.AreLayersSetup())
            {
                UpdateLayers.SetupEditorLayers();
            }

            if (!UpdateLayers.IsCollisionLayerMatrixSetup())
            {
                UpdateLayers.SetupCollisionLayerMatrix();
            }

            EditorWindow.GetWindow(typeof(VRCSdkControlPanel));
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder))
            {
                throw new InvalidOperationException(
                    "VRChat Worlds SDK builder를 준비하지 못했다.");
            }

            if (!builder.IsValidBuilder(out var validationMessage))
            {
                throw new InvalidOperationException(
                    $"VRChat Worlds SDK builder가 샘플 씬을 월드로 인식하지 못했다: {validationMessage}");
            }

            return builder;
        }

        private static void ExitBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
