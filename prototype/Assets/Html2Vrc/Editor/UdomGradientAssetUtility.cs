using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Html2Vrc.Editor
{
    public static class UdomGradientAssetUtility
    {
        public const string ShaderName = "HTML2VRC/UI Linear Gradient";
        public const string RadialShaderName = "HTML2VRC/UI Radial Gradient";
        public const string ConicShaderName = "HTML2VRC/UI Conic Gradient";
        public const int LutWidth = 1025;

        private const string GeneratedRoot = "Assets/Html2VrcGenerated";
        private const string GeneratedGradientFolder = GeneratedRoot + "/Gradients";

        public static void Configure(
            Image image,
            UdomStyle style,
            Vector2 boxSize,
            TextAsset sourceAsset,
            string stableId)
        {
            var shaderName = GetShaderName(style != null ? style.backgroundType : null);
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required gradient shader '{shaderName}' was not found.");
            }

            var sourcePath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null;
            Material material;
            Texture2D texture;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                EnsureGeneratedFolders();
                var assetStem = GetAssetStem(sourcePath, stableId);
                var materialPath = GeneratedGradientFolder + "/" + assetStem + ".mat";
                var texturePath = GeneratedGradientFolder + "/" + assetStem + "_Lut.asset";
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = assetStem + " Gradient" };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                }

                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null)
                {
                    texture = CreateTexture(assetStem + " Gradient LUT");
                    AssetDatabase.CreateAsset(texture, texturePath);
                }
                else if (texture.width != LutWidth || texture.height != 1)
                {
                    texture.Reinitialize(LutWidth, 1, TextureFormat.RGBA32, false);
                }
            }
            else
            {
                material = GetTransientMaterial(image, shader);
                texture = material.GetTexture("_GradientTex") as Texture2D;
                if (texture == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                {
                    texture = CreateTexture((stableId ?? "UDOM") + " Gradient LUT");
                    texture.hideFlags = HideFlags.DontSave;
                }
            }

            UpdateTexture(texture, style.backgroundGradientPositions, style.backgroundGradientColors);
            material.SetTexture("_GradientTex", texture);
            ApplyLayoutProperties(material, style, boxSize);
            EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(material);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                AssetDatabase.SaveAssetIfDirty(texture);
                AssetDatabase.SaveAssetIfDirty(material);
            }

            image.color = Color.white;
            image.material = material;
        }

        public static void ApplyLayoutProperties(Material material, UdomStyle style, Vector2 boxSize)
        {
            var size = new Vector2(Mathf.Max(0.0001f, boxSize.x), Mathf.Max(0.0001f, boxSize.y));
            if (string.Equals(
                    style.backgroundType,
                    "radial-gradient",
                    StringComparison.OrdinalIgnoreCase))
            {
                var center = ResolveVector(
                    style.backgroundGradientCenter,
                    style.backgroundGradientCenterIsPercent,
                    size,
                    new Vector2(50f, 50f));
                var radius = ResolveVector(
                    style.backgroundGradientRadius,
                    style.backgroundGradientRadiusIsPercent,
                    size,
                    new Vector2(50f, 50f));
                material.SetVector(
                    "_GradientCenter",
                    new Vector4(-size.x * 0.5f + center.x, size.y * 0.5f - center.y, 0f, 0f));
                material.SetVector(
                    "_GradientRadius",
                    new Vector4(Mathf.Max(0f, radius.x), Mathf.Max(0f, radius.y), 0f, 0f));
            }
            else if (string.Equals(
                         style.backgroundType,
                         "conic-gradient",
                         StringComparison.OrdinalIgnoreCase))
            {
                var center = ResolveVector(
                    style.backgroundGradientCenter,
                    style.backgroundGradientCenterIsPercent,
                    size,
                    new Vector2(50f, 50f));
                material.SetVector(
                    "_GradientCenter",
                    new Vector4(-size.x * 0.5f + center.x, size.y * 0.5f - center.y, 0f, 0f));
                material.SetFloat("_GradientStart", style.backgroundGradientAngle / 360f);
            }
            else
            {
                var radians = style.backgroundGradientAngle * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Sin(radians) * size.x, Mathf.Cos(radians) * size.y);
                material.SetVector("_GradientAxis", new Vector4(axis.x, axis.y, 0f, 0f));
            }

            UdomRoundedCornerAssetUtility.ApplyProperties(material, style, size);
        }

        public static bool IsGradientType(string backgroundType)
        {
            return string.Equals(backgroundType, "linear-gradient", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(backgroundType, "radial-gradient", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(backgroundType, "conic-gradient", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsShader(string shaderName)
        {
            return string.Equals(shaderName, ShaderName, StringComparison.Ordinal)
                   || string.Equals(shaderName, RadialShaderName, StringComparison.Ordinal)
                   || string.Equals(shaderName, ConicShaderName, StringComparison.Ordinal);
        }

        public static void DeleteGeneratedAssets(TextAsset sourceAsset, string stableId)
        {
            var sourcePath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : null;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var assetStem = GetAssetStem(sourcePath, stableId);
            AssetDatabase.DeleteAsset(GeneratedGradientFolder + "/" + assetStem + ".mat");
            AssetDatabase.DeleteAsset(GeneratedGradientFolder + "/" + assetStem + "_Lut.asset");
            if (AssetDatabase.IsValidFolder(GeneratedGradientFolder)
                && AssetDatabase.FindAssets(string.Empty, new[] { GeneratedGradientFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedGradientFolder);
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
                || !IsShader(image.material.shader.name))
            {
                return;
            }

            var material = image.material;
            var texture = material.GetTexture("_GradientTex") as Texture2D;
            image.material = null;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                if (texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static Color Evaluate(float position, float[] positions, string[] colors)
        {
            if (positions == null || colors == null || positions.Length == 0 || positions.Length != colors.Length)
            {
                return Color.clear;
            }

            var value = Mathf.Clamp01(position);
            if (value <= positions[0])
            {
                return UdomBuilderUtility.ParseColor(colors[0], Color.clear);
            }

            for (var index = 1; index < positions.Length; index++)
            {
                if (value > positions[index])
                {
                    continue;
                }

                var start = UdomBuilderUtility.ParseColor(colors[index - 1], Color.clear);
                var end = UdomBuilderUtility.ParseColor(colors[index], Color.clear);
                var distance = positions[index] - positions[index - 1];
                return distance <= 0.00001f
                    ? end
                    : Color.LerpUnclamped(start, end, (value - positions[index - 1]) / distance);
            }

            return UdomBuilderUtility.ParseColor(colors[colors.Length - 1], Color.clear);
        }

        public static float EvaluateConicPosition(
            Vector2 localPosition,
            Vector2 localCenter,
            float startAngle)
        {
            var delta = localPosition - localCenter;
            var turns = Mathf.Atan2(delta.x, delta.y) / (Mathf.PI * 2f);
            return Mathf.Repeat(turns - startAngle / 360f, 1f);
        }

        private static Material GetTransientMaterial(Image image, Shader shader)
        {
            var current = image.material;
            if (current != null
                && current.shader != null
                && IsShader(current.shader.name)
                && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(current)))
            {
                if (current.shader != shader)
                {
                    current.shader = shader;
                }

                return current;
            }

            return new Material(shader)
            {
                name = image.name + " Gradient",
                hideFlags = HideFlags.DontSave
            };
        }

        private static string GetShaderName(string backgroundType)
        {
            if (string.Equals(
                    backgroundType,
                    "radial-gradient",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RadialShaderName;
            }

            return string.Equals(
                backgroundType,
                "conic-gradient",
                StringComparison.OrdinalIgnoreCase)
                ? ConicShaderName
                : ShaderName;
        }

        private static Vector2 ResolveVector(
            float[] values,
            bool[] isPercent,
            Vector2 boxSize,
            Vector2 fallbackPercent)
        {
            var x = values != null && values.Length >= 1 ? values[0] : fallbackPercent.x;
            var y = values != null && values.Length >= 2 ? values[1] : fallbackPercent.y;
            var xIsPercent = isPercent == null || isPercent.Length < 1 || isPercent[0];
            var yIsPercent = isPercent == null || isPercent.Length < 2 || isPercent[1];
            return new Vector2(
                xIsPercent ? boxSize.x * x / 100f : x,
                yIsPercent ? boxSize.y * y / 100f : y);
        }

        private static Texture2D CreateTexture(string name)
        {
            return new Texture2D(LutWidth, 1, TextureFormat.RGBA32, false, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
        }

        private static void UpdateTexture(Texture2D texture, float[] positions, string[] colors)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 0;
            var pixels = new Color[LutWidth];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = Evaluate(index / (float)(pixels.Length - 1), positions, colors);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        private static void EnsureGeneratedFolders()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(GeneratedRoot));
            }

            if (!AssetDatabase.IsValidFolder(GeneratedGradientFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, Path.GetFileName(GeneratedGradientFolder));
            }
        }

        private static string Sanitize(string value)
        {
            var source = string.IsNullOrWhiteSpace(value) ? "gradient" : value;
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

            var stableKey = string.IsNullOrWhiteSpace(stableId) ? "gradient" : stableId;
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
