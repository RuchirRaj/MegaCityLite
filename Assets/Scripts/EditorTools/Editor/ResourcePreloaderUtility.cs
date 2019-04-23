﻿using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityObject = UnityEngine.Object;

static class ResourcePreloaderUtility
{
    [PostProcessScene]
    [MenuItem("GameObject/Create Resource Preloader")]
    static void CreateResourcePreloader()
    {
        if (UnityObject.FindObjectOfType<ResourcePreloader>() != null)
        {
            Debug.Log($"{typeof(ResourcePreloader)} already exists in this scene.");
            return;
        }

        var resources = AssetDatabase.FindAssets("t:Texture")
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .Where(path => path.StartsWith("Assets/Textures/"))
            .Select(p => AssetDatabase.LoadAssetAtPath<Texture>(p))
            .Where(t => t.hideFlags == HideFlags.None)
            .ToList();

        new GameObject("Resource Preloader", typeof(ResourcePreloader))
            .GetComponent<ResourcePreloader>()
            .SetResources(resources);
    }
}
