using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Html2Vrc.Editor
{
    public static class UdomRoundedCornerAssetUtility
    {
        public const string ShaderName = "HTML2VRC/UI Rounded Corners";

        private const string GeneratedRoot = "Assets/Html2VrcGenerated";
        private const string GeneratedFolder = GeneratedRoot + "/RoundedCorners";

        public static bool HasRadius(UdomStyle style)
        {
            if (style == null)
            {
                return false;
            }

            for (var index = 0; index < 4; index++)
            {
                if ((style.cornerRadius != null
                     && index < style.cornerRadius.Length
                     && style.cornerRadius[index] > 0f)
                    || (style.cornerRadiusPercent != null
                        && index < style.cornerRadiusPercent.Length
                        && style.cornerRadiusPercent[index] > 0f))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Configure(
            Image image,
            UdomStyle style,
            Vector2 boxSize,
            TextAsset sourceAsset,
            string stableId)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required rounded-corner shader '{ShaderName}' was not found.");
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
                    material = new Material(shader) { name = assetStem + " Rounded Corners" };
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

            ApplyProperties(material, style, boxSize);
            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            image.material = material;
        }

        public static void ApplyProperties(Material material, UdomStyle style, Vector2 boxSize)
        {
            var size = new Vector2(Mathf.Max(0.0001f, boxSize.x), Mathf.Max(0.0001f, boxSize.y));
            var radii = ResolveAndNormalizeRadii(style, size);
            material.SetVector("_RectSize", new Vector4(size.x, size.y, 0f, 0f));
            material.SetVector("_CornerRadii", radii);
        }

        public static Vector4 NormalizeRadii(float[] values, Vector2 boxSize)
        {
            var radii = values != null && values.Length >= 4
                ? new Vector4(
                    Mathf.Max(0f, values[0]),
                    Mathf.Max(0f, values[1]),
                    Mathf.Max(0f, values[2]),
                    Mathf.Max(0f, values[3]))
                : Vector4.zero;
            var width = Mathf.Max(0f, boxSize.x);
            var height = Mathf.Max(0f, boxSize.y);
            var scale = 1f;
            scale = LimitScale(scale, width, radii.x + radii.y);
            scale = LimitScale(scale, width, radii.w + radii.z);
            scale = LimitScale(scale, height, radii.x + radii.w);
            scale = LimitScale(scale, height, radii.y + radii.z);
            return radii * scale;
        }

        public static Vector4 ResolveAndNormalizeRadii(UdomStyle style, Vector2 boxSize)
        {
            var values = style != null && style.cornerRadius != null && style.cornerRadius.Length >= 4
                ? (float[])style.cornerRadius.Clone()
                : new[] { 0f, 0f, 0f, 0f };
            var percentages = style != null ? style.cornerRadiusPercent : null;
            var referenceLength = Mathf.Min(Mathf.Max(0f, boxSize.x), Mathf.Max(0f, boxSize.y));
            if (percentages != null && percentages.Length >= 4)
            {
                for (var index = 0; index < values.Length; index++)
                {
                    if (percentages[index] >= 0f)
                    {
                        values[index] = referenceLength * percentages[index] / 100f;
                    }
                }
            }

            return NormalizeRadii(values, boxSize);
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

        public static void Clear(Image image)
        {
            if (image == null || image.material == null || image.material.shader == null
                || !string.Equals(image.material.shader.name, ShaderName, StringComparison.Ordinal))
            {
                return;
            }

            var material = image.material;
            image.material = null;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static float LimitScale(float current, float available, float requested)
        {
            return requested > 0f ? Mathf.Min(current, available / requested) : current;
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
                name = image.name + " Rounded Corners",
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
            var source = string.IsNullOrWhiteSpace(value) ? "rounded" : value;
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

            var stableKey = string.IsNullOrWhiteSpace(stableId) ? "rounded" : stableId;
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
