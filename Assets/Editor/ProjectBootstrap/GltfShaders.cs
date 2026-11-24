using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // glTFast (the Workspace's 3D models) makes materials at runtime from its own shader graphs, which a
    // build leaves out unless something references them, so models show pink. When the package is
    // installed, this adds its shaders to Graphics Settings > Always Included Shaders before a build.
    // Without the package nothing is found and nothing changes. The hologram's own shader is found by name
    // at run time too, so it is added the same way.
    public class GltfShaders : IPreprocessBuildWithReport
    {
        static readonly string[] k_Names =
        {
            "Shader Graphs/glTF-pbrMetallicRoughness",
            "Shader Graphs/glTF-pbrSpecularGlossiness",
            "Shader Graphs/glTF-unlit",
            "CardboardXR/Hologram", // the assistant's hologram (HologramAssistant.cs), found by name at run time
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null)
                return;
            var serialized = new SerializedObject(settings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            var present = new HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++)
                present.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            bool changed = false;
            foreach (var name in k_Names)
            {
                var shader = Shader.Find(name);
                if (shader == null || present.Contains(shader))
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
                Debug.Log($"[ProjectSetup] glTFast: {name} added to Always Included Shaders");
            }
            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }
    }
}
