using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Html2Vrc.Editor
{
    public sealed class UdomImporterWindow : EditorWindow
    {
        private TextAsset source;
        private Vector2 scroll;
        private UdomValidationResult lastValidation;
        private string status;
        private bool overrideTargetCanvasSize;
        private Vector2 targetCanvasSize = new Vector2(1200f, 800f);

        [MenuItem("Tools/HTML2VRC/UDOM Importer")]
        public static void Open()
        {
            var window = GetWindow<UdomImporterWindow>("HTML2VRC UDOM");
            window.minSize = new Vector2(520f, 360f);
            window.TryUseSelection();
        }

        [MenuItem("Assets/HTML2VRC/Validate UDOM", true)]
        private static bool ValidateSelectedAssetMenu()
        {
            return Selection.activeObject is TextAsset;
        }

        [MenuItem("Assets/HTML2VRC/Validate UDOM")]
        private static void ValidateSelectedAsset()
        {
            var selected = Selection.activeObject as TextAsset;
            var validation = UdomValidator.Validate(
                selected != null ? selected.text : string.Empty,
                selected != null ? AssetDatabase.GetAssetPath(selected) : null);
            if (validation.IsValid)
            {
                Debug.Log($"HTML2VRC: '{selected.name}' validation passed.\n{validation.Format()}", selected);
            }
            else
            {
                Debug.LogError($"HTML2VRC: '{selected.name}' validation failed.\n{validation.Format()}", selected);
            }
        }

        private void OnSelectionChange()
        {
            TryUseSelection();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UDOM → Unity Native UI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "JSON을 검증한 뒤 안정 ID로 기존 오브젝트를 갱신한다. 외부 슬롯 참조와 생성 마커가 없는 사용자 오브젝트는 유지한다.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            source = (TextAsset)EditorGUILayout.ObjectField("UDOM JSON", source, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                lastValidation = null;
                status = null;
            }

            overrideTargetCanvasSize = EditorGUILayout.Toggle(
                "Override Target Canvas",
                overrideTargetCanvasSize);
            using (new EditorGUI.DisabledScope(!overrideTargetCanvasSize))
            {
                targetCanvasSize = EditorGUILayout.Vector2Field("Target Canvas Size", targetCanvasSize);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("Validate", GUILayout.Height(30f)))
                    {
                        ValidateSource();
                    }

                    if (GUILayout.Button("Generate / Regenerate", GUILayout.Height(30f)))
                    {
                        GenerateSource();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, lastValidation != null && lastValidation.IsValid
                    ? MessageType.Info
                    : MessageType.Error);
            }

            if (lastValidation == null || lastValidation.Issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation 결과", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (var index = 0; index < lastValidation.Issues.Count; index++)
            {
                var issue = lastValidation.Issues[index];
                EditorGUILayout.HelpBox(
                    $"{issue.Path}\n{issue.Message}",
                    issue.Severity == UdomIssueSeverity.Error ? MessageType.Error : MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void TryUseSelection()
        {
            if (Selection.activeObject is TextAsset selected && selected != source)
            {
                source = selected;
                lastValidation = null;
                status = null;
            }
        }

        private void ValidateSource()
        {
            lastValidation = UdomValidator.Validate(
                source != null ? source.text : string.Empty,
                source != null ? AssetDatabase.GetAssetPath(source) : null);
            status = lastValidation.IsValid
                ? $"검증 성공: {lastValidation.Issues.Count}개 경고/정보"
                : $"검증 실패\n{lastValidation.Format()}";
        }

        private void GenerateSource()
        {
            ValidateSource();
            if (lastValidation == null || !lastValidation.IsValid)
            {
                return;
            }

            try
            {
                if (overrideTargetCanvasSize
                    && (targetCanvasSize.x <= 0f || targetCanvasSize.y <= 0f))
                {
                    status = "생성 실패: Target Canvas Size는 양수여야 한다.";
                    return;
                }

                var document = lastValidation.Document;
                var existing = UdomBuilder.FindGeneratedRoot(source, document.id);
                var build = UdomBuilder.GenerateOrRegenerate(
                    document,
                    existing,
                    source,
                    overrideTargetCanvasSize ? targetCanvasSize : Vector2.zero);
                EditorSceneManager.MarkSceneDirty(build.Root.gameObject.scene);
                Selection.activeGameObject = build.Root.gameObject;
                status = $"생성 완료 — 새 노드 {build.Created}, 갱신 {build.Updated}, 제거 {build.Removed}";
            }
            catch (Exception exception)
            {
                status = $"생성 실패: {exception.Message}";
                Debug.LogException(exception);
            }
        }
    }
}
