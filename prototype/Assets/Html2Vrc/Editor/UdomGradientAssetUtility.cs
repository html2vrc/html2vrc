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
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required gradient shader '{ShaderName}' was not found.");
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
            var radians = style.backgroundGradientAngle * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Sin(radians) * size.x, Mathf.Cos(radians) * size.y);
            material.SetVector("_GradientAxis", new Vector4(axis.x, axis.y, 0f, 0f));
            UdomRoundedCornerAssetUtility.ApplyProperties(material, style, size);
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
                || !string.Equals(image.material.shader.name, ShaderName, StringComparison.Ordinal))
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
                name = image.name + " Gradient",
                hideFlags = HideFlags.DontSave
            };
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
