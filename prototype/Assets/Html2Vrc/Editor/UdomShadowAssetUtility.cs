using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Html2Vrc.Editor
{
    public static class UdomShadowAssetUtility
    {
        public const string ShaderName = "HTML2VRC/UI Box Shadow";

        private const string GeneratedRoot = "Assets/Html2VrcGenerated";
        private const string GeneratedFolder = GeneratedRoot + "/Shadows";

        public static int GetCount(UdomStyle style)
        {
            return style != null && style.shadowColors != null ? style.shadowColors.Length : 0;
        }

        public static bool IsRenderable(UdomStyle style, int index)
        {
            if (style == null || index < 0 || index >= GetCount(style))
            {
                return false;
            }

            return UdomBuilderUtility.ParseColor(style.shadowColors[index], Color.clear).a > 0f;
        }

        public static bool IsInset(UdomStyle style, int index)
        {
            return style != null
                   && style.shadowInsets != null
                   && index >= 0
                   && index < style.shadowInsets.Length
                   && style.shadowInsets[index];
        }

        public static bool IsOuterRenderable(UdomStyle style, int index)
        {
            return IsRenderable(style, index) && !IsInset(style, index);
        }

        public static bool IsInsetRenderable(UdomStyle style, int index)
        {
            return IsRenderable(style, index) && IsInset(style, index);
        }

        public static Vector2 GetOffset(UdomStyle style, int index)
        {
            var valueIndex = index * 2;
            return new Vector2(
                GetValue(style != null ? style.shadowOffsets : null, valueIndex),
                GetValue(style != null ? style.shadowOffsets : null, valueIndex + 1));
        }

        public static float GetBlur(UdomStyle style, int index)
        {
            return Mathf.Max(0f, GetValue(style != null ? style.shadowBlurs : null, index));
        }

        public static float GetSpread(UdomStyle style, int index)
        {
            return GetValue(style != null ? style.shadowSpreads : null, index);
        }

        public static Vector2 GetGeometrySize(UdomStyle style, int index, Vector2 boxSize)
        {
            if (IsInset(style, index))
            {
                return new Vector2(
                    Mathf.Max(0.0001f, boxSize.x),
                    Mathf.Max(0.0001f, boxSize.y));
            }

            var shapeSize = GetShapeSize(style, index, boxSize);
            var blur = GetBlur(style, index);
            return shapeSize + Vector2.one * (blur * 2f);
        }

        public static void Configure(
            Image image,
            UdomStyle style,
            int index,
            Vector2 boxSize,
            TextAsset sourceAsset,
            string stableId)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required shadow shader '{ShaderName}' was not found.");
            }

            var sourcePath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null;
            Material material;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                EnsureGeneratedFolders();
                var assetStem = GetAssetStem(sourcePath, stableId);
                var materialPath = GeneratedFolder + "/" + assetStem + ".mat";
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = assetStem + " Shadow" };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                }
            }
            else
            {
                material = GetTransientMaterial(image, shader);
            }

            ApplyProperties(material, style, index, boxSize);
            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            image.color = UdomBuilderUtility.ParseColor(style.shadowColors[index], Color.clear);
            image.material = material;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        public static void ApplyProperties(
            Material material,
            UdomStyle style,
            int index,
            Vector2 boxSize)
        {
            var safeBoxSize = new Vector2(
                Mathf.Max(0.0001f, boxSize.x),
                Mathf.Max(0.0001f, boxSize.y));
            var inset = IsInset(style, index);
            var spread = GetSpread(style, index);
            var shapeSize = GetShapeSize(style, index, safeBoxSize);
            var baseRadii = UdomRoundedCornerAssetUtility.ResolveAndNormalizeRadii(style, safeBoxSize);
            var radiusDelta = inset ? -spread : spread;
            var shapeRadii = new[]
            {
                Mathf.Max(0f, baseRadii.x + radiusDelta),
                Mathf.Max(0f, baseRadii.y + radiusDelta),
                Mathf.Max(0f, baseRadii.z + radiusDelta),
                Mathf.Max(0f, baseRadii.w + radiusDelta)
            };
            var radii = UdomRoundedCornerAssetUtility.NormalizeRadii(shapeRadii, shapeSize);
            var offset = GetOffset(style, index);
            material.SetVector("_ShapeSize", new Vector4(shapeSize.x, shapeSize.y, 0f, 0f));
            material.SetVector("_CornerRadii", radii);
            material.SetVector("_BoxSize", new Vector4(safeBoxSize.x, safeBoxSize.y, 0f, 0f));
            material.SetVector("_BoxCornerRadii", baseRadii);
            material.SetVector(
                "_Offset",
                inset ? new Vector4(offset.x, -offset.y, 0f, 0f) : Vector4.zero);
            material.SetFloat("_Blur", GetBlur(style, index));
            material.SetFloat("_Inset", inset ? 1f : 0f);
        }

        public static bool IsShader(string shaderName)
        {
            return string.Equals(shaderName, ShaderName, StringComparison.Ordinal);
        }

        public static void DeleteGeneratedAssets(TextAsset sourceAsset, string stableId)
        {
            var sourcePath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var assetStem = GetAssetStem(sourcePath, stableId);
            AssetDatabase.DeleteAsset(GeneratedFolder + "/" + assetStem + ".mat");
            if (AssetDatabase.IsValidFolder(GeneratedFolder)
                && AssetDatabase.FindAssets(string.Empty, new[] { GeneratedFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedFolder);
            }

            if (AssetDatabase.IsValidFolder(GeneratedRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { GeneratedRoot }).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedRoot);
            }
        }

        private static Vector2 GetShapeSize(UdomStyle style, int index, Vector2 boxSize)
        {
            var spread = GetSpread(style, index);
            var spreadScale = IsInset(style, index) ? -2f : 2f;
            return new Vector2(
                Mathf.Max(0.0001f, boxSize.x + spread * spreadScale),
                Mathf.Max(0.0001f, boxSize.y + spread * spreadScale));
        }

        private static float GetValue(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }

        private static Material GetTransientMaterial(Image image, Shader shader)
        {
            var current = image.material;
            if (current != null
                && current.shader == shader
                && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(current)))
            {
                return current;
            }

            return new Material(shader)
            {
                name = image.name + " Shadow",
                hideFlags = HideFlags.DontSave
            };
        }

        private static void EnsureGeneratedFolders()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(GeneratedRoot));
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, Path.GetFileName(GeneratedFolder));
            }
        }

        private static string Sanitize(string value)
        {
            var source = string.IsNullOrWhiteSpace(value) ? "shadow" : value;
            var characters = source.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (!char.IsLetterOrDigit(characters[index]) && characters[index] != '-' && characters[index] != '_')
                {
                    characters[index] = '_';
                }
            }

            return new string(characters);
        }

        private static string GetAssetStem(string sourcePath, string stableId)
        {
            var sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(sourceGuid))
            {
                throw new InvalidOperationException($"Could not resolve an asset GUID for '{sourcePath}'.");
            }

            var stableKey = string.IsNullOrWhiteSpace(stableId) ? "shadow" : stableId;
            var readableId = Sanitize(stableKey);
            if (readableId.Length > 32)
            {
                readableId = readableId.Substring(0, 32);
            }

            return sourceGuid
                   + "_"
                   + readableId
                   + "_"
                   + Hash128.Compute(stableKey);
        }
    }
}
