using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.Graphing;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PrefabUtilities
{
    [MenuItem("Prefabs/Revert Selection")]
    static void RevertSelection()
    {
        var gameObjectSelection = Selection.gameObjects;
        Undo.RegisterCompleteObjectUndo(gameObjectSelection, "revert selection");
        foreach (var go in gameObjectSelection)
        {
            if (go != null && PrefabUtility.IsPartOfNonAssetPrefabInstance(go) && PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            }
        }
    }

    [MenuItem("Prefabs/Revert Selection (preserve scale)")]
    static void RevertSelectionPreserveScale()
    {
        var gameObjectSelection = Selection.gameObjects;
        Undo.RegisterCompleteObjectUndo(gameObjectSelection, "revert selection");
        foreach (var go in gameObjectSelection)
        {
            if (go != null && PrefabUtility.IsPartOfNonAssetPrefabInstance(go) && PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                var t = EditorJsonUtility.ToJson(go.transform);
                PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
                EditorJsonUtility.FromJsonOverwrite(t, go.transform);
            }
        }
    }
}