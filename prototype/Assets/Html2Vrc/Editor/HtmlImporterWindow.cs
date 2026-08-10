using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Html2Vrc.Editor
{
    public sealed class HtmlImporterWindow : EditorWindow
    {
        private TextAsset source;
        private HtmlToUdomResult lastConversion;
        private Vector2 issueScroll;
        private Vector2 previewScroll;
        private string status;

        [MenuItem("Tools/HTML2VRC/HTML Importer (Preview)")]
        public static void Open()
        {
            var window = GetWindow<HtmlImporterWindow>("HTML2VRC HTML");
            window.minSize = new Vector2(620f, 520f);
            window.TryUseSelection();
        }

        private void OnSelectionChange()
        {
            TryUseSelection();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("제한형 HTML → UDOM → Unity UI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "일반 웹 브라우저가 아니다. 지원 목록 안의 정적 HTML과 안전한 data-action만 UDOM으로 번역한다. script, onclick, 외부 CSS는 오류로 막는다.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            source = (TextAsset)EditorGUILayout.ObjectField("HTML 파일", source, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                lastConversion = null;
                status = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button("HTML 검증 / 변환", GUILayout.Height(30f)))
                {
                    ConvertSource();
                }

                if (GUILayout.Button("UDOM JSON 저장", GUILayout.Height(30f)))
                {
                    SaveUdom();
                }

                if (GUILayout.Button("Generate / Regenerate", GUILayout.Height(30f)))
                {
                    GenerateSource();
                }
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    status,
                    lastConversion != null && lastConversion.IsValid ? MessageType.Info : MessageType.Error);
            }

            if (lastConversion == null)
            {
                return;
            }

            if (lastConversion.Issues.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("검증 결과", EditorStyles.boldLabel);
                issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.MaxHeight(150f));
                for (var index = 0; index < lastConversion.Issues.Count; index++)
                {
                    var issue = lastConversion.Issues[index];
                    EditorGUILayout.HelpBox(
                        $"{issue.Path}\n{issue.Message}",
                        issue.Severity == UdomIssueSeverity.Error
                            ? MessageType.Error
                            : MessageType.Warning);
                }

                EditorGUILayout.EndScrollView();
            }

            if (string.IsNullOrWhiteSpace(lastConversion.Json))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("생성될 UDOM 미리보기", EditorStyles.boldLabel);
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll);
            EditorGUILayout.TextArea(lastConversion.Json, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void TryUseSelection()
        {
            if (!(Selection.activeObject is TextAsset selected))
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            source = selected;
            lastConversion = null;
            status = null;
        }

        private bool ConvertSource()
        {
            lastConversion = HtmlToUdomConverter.Convert(source != null ? source.text : string.Empty);
            status = lastConversion.IsValid
                ? $"변환 성공 — 문서 ID: {lastConversion.Document.id}, 문제 {lastConversion.Issues.Count}개"
                : $"변환 실패\n{lastConversion.Format()}";
            return lastConversion.IsValid;
        }

        private void SaveUdom()
        {
            if (!ConvertSource())
            {
                return;
            }

            var sourcePath = AssetDatabase.GetAssetPath(source);
            var destination = Path.ChangeExtension(sourcePath, null) + ".generated.udom.json";
            if (File.Exists(destination)
                && !EditorUtility.DisplayDialog(
                    "UDOM 파일 덮어쓰기",
                    $"{destination} 파일을 새 변환 결과로 덮어쓸까?",
                    "덮어쓰기",
                    "취소"))
            {
                return;
            }

            File.WriteAllText(destination, lastConversion.Json);
            AssetDatabase.ImportAsset(destination);
            status = $"UDOM 저장 완료: {destination}";
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(destination);
        }

        private void GenerateSource()
        {
            if (!ConvertSource())
            {
                return;
            }

            try
            {
                var document = lastConversion.Document;
                var existing = UdomBuilder.FindGeneratedRoot(source, document.id);
                var build = UdomBuilder.GenerateOrRegenerate(document, existing, source);
                EditorSceneManager.MarkSceneDirty(build.Root.gameObject.scene);
                Selection.activeGameObject = build.Root.gameObject;
                status = $"HTML 생성 완료 — 새 노드 {build.Created}, 갱신 {build.Updated}, 제거 {build.Removed}";
            }
            catch (Exception exception)
            {
                status = $"생성 실패: {exception.Message}";
                Debug.LogException(exception);
            }
        }
    }
}
