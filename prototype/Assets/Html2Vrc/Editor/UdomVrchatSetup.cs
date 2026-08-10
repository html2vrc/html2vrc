using System;
using Html2Vrc.VRChat;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Components;

namespace Html2Vrc.Editor
{
    [InitializeOnLoad]
    public static class UdomVrchatSetup
    {
        public const string RuntimeAssemblyPath =
            "Assets/Html2Vrc/VRChat/Runtime/Html2Vrc.VRChat.Runtime.asmdef";

        public const string UdonAssemblyDefinitionPath =
            "Assets/Html2Vrc/VRChat/Runtime/Html2Vrc.VRChat.Runtime.UdonSharp.asset";

        public const string SafeActionScriptPath =
            "Assets/Html2Vrc/VRChat/Runtime/UdomUdonSafeAction.cs";

        public const string SafeActionProgramPath =
            "Assets/Html2Vrc/VRChat/Runtime/UdomUdonSafeAction.asset";

        private static bool isReady;

        static UdomVrchatSetup()
        {
            UdomGeneratedRoot.ExternalReferencesChanged -= RefreshBindingTargets;
            UdomGeneratedRoot.ExternalReferencesChanged += RefreshBindingTargets;
        }

        [MenuItem("Tools/HTML2VRC/VRChat/Prepare UdonSharp Integration")]
        public static void EnsureReadyMenu()
        {
            EnsureReady(true);
            Debug.Log("HTML2VRC: UdonSharp integration is ready.");
        }

        public static void EnsureReadyBatch()
        {
            EnsureReady(true);
            Debug.Log("HTML2VRC_UDONSHARP_SETUP_SUCCESS");
        }

        public static void EnsureReady(bool forceCompile = false)
        {
            if (isReady && !forceCompile)
            {
                return;
            }

            var sourceAssembly =
                AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(RuntimeAssemblyPath);
            if (sourceAssembly == null)
            {
                throw new InvalidOperationException(
                    $"Runtime assembly definition을 찾을 수 없다: {RuntimeAssemblyPath}");
            }

            var assemblyDefinition =
                AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(
                    UdonAssemblyDefinitionPath);
            var assetsChanged = false;
            if (assemblyDefinition == null)
            {
                assemblyDefinition = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                assemblyDefinition.sourceAssembly = sourceAssembly;
                AssetDatabase.CreateAsset(assemblyDefinition, UdonAssemblyDefinitionPath);
                assetsChanged = true;
            }
            else if (assemblyDefinition.sourceAssembly != sourceAssembly)
            {
                assemblyDefinition.sourceAssembly = sourceAssembly;
                EditorUtility.SetDirty(assemblyDefinition);
                assetsChanged = true;
            }

            var sourceScript = AssetDatabase.LoadAssetAtPath<MonoScript>(SafeActionScriptPath);
            if (sourceScript == null || sourceScript.GetClass() != typeof(UdomUdonSafeAction))
            {
                throw new InvalidOperationException(
                    $"UdomUdonSafeAction MonoScript를 찾거나 타입을 확인할 수 없다: {SafeActionScriptPath}");
            }

            var program =
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SafeActionProgramPath);
            if (program == null)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = sourceScript;
                AssetDatabase.CreateAsset(program, SafeActionProgramPath);
                assetsChanged = true;
            }
            else if (program.sourceCsScript != sourceScript)
            {
                program.sourceCsScript = sourceScript;
                EditorUtility.SetDirty(program);
                assetsChanged = true;
            }

            if (assetsChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            if (forceCompile || assetsChanged)
            {
                UdonSharpCompilerV1.CompileSync();
            }

            program = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(UdomUdonSafeAction));
            if (program == null || program.sourceCsScript != sourceScript)
            {
                throw new InvalidOperationException(
                    "UdomUdonSafeAction과 연결된 UdonSharp program asset을 준비하지 못했다.");
            }

            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
            {
                throw new InvalidOperationException(
                    "UdonSharp program 컴파일 오류가 있다. Unity Console을 확인해야 한다.");
            }

            isReady = true;
        }

        public static void RefreshBindingTargets(UdomGeneratedRoot root)
        {
            if (root == null)
            {
                return;
            }

            var actions = root.GetComponentsInChildren<UdomUdonSafeAction>(true);
            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                action.RefreshTarget(root.Resolve(action.TargetSlot));
                EditorUtility.SetDirty(action);

                var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(action);
                if (backingBehaviour != null
                    && UdonSharpProgramAsset.GetProgramAssetForClass(typeof(UdomUdonSafeAction)) != null)
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(action);
                }
            }
        }

        public static void EnsureSceneDescriptor()
        {
            var descriptor = UnityEngine.Object.FindObjectOfType<VRCSceneDescriptor>(true);
            if (descriptor == null)
            {
                var descriptorObject = new GameObject("VRC Scene Descriptor");
                descriptor = descriptorObject.AddComponent<VRCSceneDescriptor>();
            }

            var spawn = descriptor.transform.Find("Player Spawn");
            if (spawn == null)
            {
                var spawnObject = new GameObject("Player Spawn");
                spawn = spawnObject.transform;
                spawn.SetParent(descriptor.transform, false);
                spawn.localPosition = new Vector3(0f, 0f, -2f);
                spawn.localRotation = Quaternion.identity;
            }

            descriptor.spawns = new[] { spawn };
            EditorUtility.SetDirty(descriptor);
        }

        public static bool HasValidSceneDescriptor()
        {
            var descriptor = UnityEngine.Object.FindObjectOfType<VRCSceneDescriptor>(true);
            return descriptor != null
                   && descriptor.spawns != null
                   && descriptor.spawns.Length > 0
                   && descriptor.spawns[0] != null;
        }
    }
}
