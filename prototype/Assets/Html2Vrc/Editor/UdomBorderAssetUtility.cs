using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Html2Vrc.Editor
{
    public static class UdomBorderAssetUtility
    {
        public const string ShaderName = "HTML2VRC/UI Box Border";

        private const string GeneratedRoot = "Assets/Html2VrcGenerated";
        private const string GeneratedFolder = GeneratedRoot + "/Borders";

        public static bool HasVisibleBorder(UdomStyle style)
        {
            var widths = GetWidths(style);
            var colors = GetColors(style);
            return (widths.x > 0f && colors[0].a > 0f)
                   || (widths.y > 0f && colors[1].a > 0f)
                   || (widths.z > 0f && colors[2].a > 0f)
                   || (widths.w > 0f && colors[3].a > 0f);
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
                throw new InvalidOperationException($"Required border shader '{ShaderName}' was not found.");
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
                    material = new Material(shader) { name = assetStem + " Border" };
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
            Save(material);

            image.color = Color.white;
            image.sprite = null;
            image.material = material;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }

        public static void ApplyProperties(Material material, UdomStyle style, Vector2 boxSize)
        {
            var size = new Vector2(
                Mathf.Max(0.0001f, boxSize.x),
                Mathf.Max(0.0001f, boxSize.y));
            var widths = GetWidths(style);
            var outerRadii = UdomRoundedCornerAssetUtility.ResolveAndNormalizeRadii(style, size);
            var innerWidth = size.x - widths.x - widths.z;
            var innerHeight = size.y - widths.y - widths.w;
            var hasInner = innerWidth > 0.0001f && innerHeight > 0.0001f;
            var innerSize = new Vector2(
                Mathf.Max(0.0001f, innerWidth),
                Mathf.Max(0.0001f, innerHeight));
            var innerCenter = new Vector2(
                (widths.x - widths.z) * 0.5f,
                (widths.w - widths.y) * 0.5f);
            ResolveInnerRadii(
                outerRadii,
                widths,
                innerSize,
                out var innerRadiiX,
                out var innerRadiiY);
            var colors = GetColors(style);

            material.SetVector("_RectSize", new Vector4(size.x, size.y, 0f, 0f));
            material.SetVector("_OuterRadii", outerRadii);
            material.SetVector("_InnerSize", new Vector4(innerSize.x, innerSize.y, 0f, 0f));
            material.SetVector("_InnerCenter", new Vector4(innerCenter.x, innerCenter.y, 0f, 0f));
            material.SetVector("_InnerRadiiX", innerRadiiX);
            material.SetVector("_InnerRadiiY", innerRadiiY);
            material.SetVector("_BorderWidths", widths);
            material.SetFloat("_HasInner", hasInner ? 1f : 0f);
            material.SetColor("_BorderLeftColor", colors[0]);
            material.SetColor("_BorderTopColor", colors[1]);
            material.SetColor("_BorderRightColor", colors[2]);
            material.SetColor("_BorderBottomColor", colors[3]);
        }

        public static Vector4 GetWidths(UdomStyle style)
        {
            var values = style != null ? style.borderWidth : null;
            return values != null && values.Length >= 4
                ? new Vector4(
                    Mathf.Max(0f, values[0]),
                    Mathf.Max(0f, values[1]),
                    Mathf.Max(0f, values[2]),
                    Mathf.Max(0f, values[3]))
                : Vector4.zero;
        }

        public static Color[] GetColors(UdomStyle style)
        {
            var result = new Color[4];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = style != null
                                && style.borderColor != null
                                && index < style.borderColor.Length
                    ? UdomBuilderUtility.ParseColor(style.borderColor[index], Color.clear)
                    : Color.clear;
            }

            return result;
        }

        public static void ResolveInnerRadii(
            Vector4 outerRadii,
            Vector4 widths,
            Vector2 innerSize,
            out Vector4 radiiX,
            out Vector4 radiiY)
        {
            radiiX = new Vector4(
                Mathf.Max(0f, outerRadii.x - widths.x),
                Mathf.Max(0f, outerRadii.y - widths.z),
                Mathf.Max(0f, outerRadii.z - widths.z),
                Mathf.Max(0f, outerRadii.w - widths.x));
            radiiY = new Vector4(
                Mathf.Max(0f, outerRadii.x - widths.y),
                Mathf.Max(0f, outerRadii.y - widths.y),
                Mathf.Max(0f, outerRadii.z - widths.w),
                Mathf.Max(0f, outerRadii.w - widths.w));

            var scale = 1f;
            scale = LimitScale(scale, innerSize.x, radiiX.x + radiiX.y);
            scale = LimitScale(scale, innerSize.x, radiiX.w + radiiX.z);
            scale = LimitScale(scale, innerSize.y, radiiY.x + radiiY.w);
            scale = LimitScale(scale, innerSize.y, radiiY.y + radiiY.z);
            radiiX *= scale;
            radiiY *= scale;
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

        private static float LimitScale(float current, float available, float requested)
        {
            return requested > 0f ? Mathf.Min(current, Mathf.Max(0f, available) / requested) : current;
        }

        private static void Save(Material material)
        {
            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }
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
                name = image.name + " Border",
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
            var source = string.IsNullOrWhiteSpace(value) ? "border" : value;
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

            var stableKey = string.IsNullOrWhiteSpace(stableId) ? "border" : stableId;
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
