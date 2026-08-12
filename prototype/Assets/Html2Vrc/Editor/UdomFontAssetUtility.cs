using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Html2Vrc.Editor
{
    public static class UdomFontAssetUtility
    {
        private const string GeneratedRoot = "Assets/Html2VrcGenerated";
        private const string GeneratedFontFolder = GeneratedRoot + "/Fonts";

        public static TMP_FontAsset LoadOrCreate(string sourcePath, TMP_FontAsset fallback)
        {
            var normalizedPath = NormalizeAssetPath(sourcePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return fallback;
            }

            var existingFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(normalizedPath);
            if (existingFontAsset != null)
            {
                return existingFontAsset;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(normalizedPath);
            if (sourceFont == null)
            {
                return fallback;
            }

            var generatedPath = GetGeneratedAssetPath(normalizedPath);
            if (string.IsNullOrWhiteSpace(generatedPath))
            {
                return fallback;
            }

            var generated = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generatedPath);
            if (generated != null)
            {
                return generated;
            }

            EnsureGeneratedFolders();
            generated = TMP_FontAsset.CreateFontAsset(sourceFont);
            if (generated == null)
            {
                throw new InvalidOperationException(
                    $"Could not generate a TMP font asset from Unity font '{normalizedPath}'.");
            }

            var assetName = Path.GetFileNameWithoutExtension(generatedPath);
            generated.name = assetName;
            var atlas = generated.atlasTexture;
            var material = generated.material;
            if (atlas != null)
            {
                atlas.name = assetName + " Atlas";
                atlas.hideFlags = HideFlags.None;
            }

            if (material != null)
            {
                material.name = assetName + " Material";
                material.hideFlags = HideFlags.None;
            }

            AssetDatabase.CreateAsset(generated, generatedPath);
            if (atlas != null)
            {
                AssetDatabase.AddObjectToAsset(atlas, generated);
            }

            if (material != null)
            {
                AssetDatabase.AddObjectToAsset(material, generated);
            }

            EditorUtility.SetDirty(generated);
            if (atlas != null)
            {
                EditorUtility.SetDirty(atlas);
            }

            if (material != null)
            {
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssetIfDirty(generated);
            AssetDatabase.ImportAsset(generatedPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generatedPath) ?? generated;
        }

        public static bool IsValidAssetPath(string sourcePath)
        {
            return !string.IsNullOrWhiteSpace(NormalizeAssetPath(sourcePath));
        }

        public static bool CanLoad(string sourcePath)
        {
            var normalizedPath = NormalizeAssetPath(sourcePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            var extension = Path.GetExtension(normalizedPath);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase)
                ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(normalizedPath) != null
                : AssetDatabase.LoadAssetAtPath<Font>(normalizedPath) != null;
        }

        public static string GetGeneratedAssetPath(string sourcePath)
        {
            var normalizedPath = NormalizeAssetPath(sourcePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(normalizedPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            var sourceName = SanitizeFileName(Path.GetFileNameWithoutExtension(normalizedPath));
            return GeneratedFontFolder + "/" + sourceName + "_" + guid.Substring(0, 12) + "_SDF.asset";
        }

        public static void DeleteGeneratedAsset(string sourcePath)
        {
            var generatedPath = GetGeneratedAssetPath(sourcePath);
            if (!string.IsNullOrWhiteSpace(generatedPath))
            {
                AssetDatabase.DeleteAsset(generatedPath);
            }

            if (AssetDatabase.IsValidFolder(GeneratedFontFolder)
                && AssetDatabase.FindAssets(string.Empty, new[] { GeneratedFontFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedFontFolder);
            }

            if (AssetDatabase.IsValidFolder(GeneratedRoot)
                && AssetDatabase.FindAssets(string.Empty, new[] { GeneratedRoot }).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedRoot);
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            var segments = normalized.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(segments[index])
                    || string.Equals(segments[index], ".", StringComparison.Ordinal)
                    || string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    return null;
                }
            }

            return normalized;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Font";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (Array.IndexOf(invalid, characters[index]) >= 0)
                {
                    characters[index] = '_';
                }
            }

            return new string(characters);
        }

        private static void EnsureGeneratedFolders()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.CreateFolder("Assets", "Html2VrcGenerated");
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFontFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, "Fonts");
            }
        }
    }
}
