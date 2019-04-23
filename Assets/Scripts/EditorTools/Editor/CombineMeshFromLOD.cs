﻿using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using Traffic.Pathing;
using Unity.Rendering;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.PlayerLoop;

public class CombineMeshFromLOD
{
	public static void UpdateBounds(HLOD hlod)
	{
		var renderers = hlod.GetComponentsInChildren<Renderer>();
		Bounds bounds = renderers[0].bounds;
		for (int i = 0; i != renderers.Length; i++)
			bounds.Encapsulate(renderers[i].bounds);

		var lodgroup = hlod.GetComponent<LODGroup>();
		lodgroup.size = bounds.size.magnitude;
		lodgroup.localReferencePoint = lodgroup.transform.InverseTransformPoint(bounds.center);
	}

    [MenuItem("HLOD/UpdateCombinedMesh")]
    public static void GenerateCombinedMesh()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() == null ||
            PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot == null)
        {
            Debug.LogWarning("UpdateCombinedMesh can only be used while prefab editing.");
            return;
        }

        var root = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot;
        var hlod = root.GetComponent<HLOD>();

        if (hlod == null)
        {
            Debug.LogWarning("UpdateCombinedMesh requires a correctly configured HLOD setup");
            return;
        }

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);

        var hlodTransforms = hlod.LODParentTransforms;

        var lodCount = hlod.GetComponent<LODGroup>().lodCount;
        while (lodCount < hlodTransforms.Length)
        {
            if (hlodTransforms[hlodTransforms.Length - 1])
                Object.DestroyImmediate(hlodTransforms[hlodTransforms.Length - 1].gameObject);
            ArrayUtility.RemoveAt(ref hlodTransforms, hlodTransforms.Length - 1);
        }

        System.Array.Resize(ref hlodTransforms, lodCount);
        var generatedMeshes = new List<Mesh>();

        for (int i = 1; i < lodCount; i++)
        {
            if (hlodTransforms[i] == null)
            {
                hlodTransforms[i] = CreateLowLod();   
                hlodTransforms[i].SetParent(root.transform, false);
            }
            
            GenerateCombinedMesh(hlod, hlod.LODParentTransforms[0], hlodTransforms[i], i, generatedMeshes);    
        }

        hlod.LODParentTransforms = hlodTransforms;

        WriteMeshAsset(generatedMeshes);
        
        HLOD.InvalidateHLODCache();       
    }
	
	public static void GenerateCombinedMesh(HLOD hlod, Transform sourceLOD, Transform generatedLOD, int lodIndex, List<Mesh> generatedMeshes)
	{
		const float fieldOfView = 60.0F;
		const float distanceBias = 0.0F;
		
		var instances = new Dictionary<Material, List<CombineInstance>>();
		var lodGroups = sourceLOD.GetComponentsInChildren<LODGroup>();
		var hlodSwitchDistance = LODGroupExtensions.CalculateLODSwitchDistance(fieldOfView, hlod.GetComponent<LODGroup>(), lodIndex-1);
		
		foreach (var group in lodGroups)
		{
			float cullingDistance = LODGroupExtensions.CalculateLODSwitchDistance(fieldOfView, group, group.lodCount-1);
			
			if (cullingDistance + distanceBias < hlodSwitchDistance)
				continue;
			
			var renderers = group.GetLODs()[group.lodCount - 1].renderers;

			foreach (var renderer in renderers)
			{
				var meshRenderer = renderer as MeshRenderer;
				if (meshRenderer == null)
					continue;
				
				var instance = new CombineInstance();
				instance.transform = generatedLOD.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

				var materials = meshRenderer.sharedMaterials;
				for (int m = 0; m != materials.Length; m++)
				{
					if (!instances.ContainsKey(materials[m]))
						instances[materials[m]] = new List<CombineInstance>();
					instance.mesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
					instance.subMeshIndex = m;
					instances[materials[m]].Add(instance);
				}
			}
		}

		while (generatedLOD.childCount != 0)
			Object.DestroyImmediate(generatedLOD.GetChild(0).gameObject);

		var generatedRenderers = new List<Renderer>();
		
		foreach (var instance in instances)
		{
			var mesh = new Mesh();
			mesh.name = "CombinedLowLOD";
			mesh.CombineMeshes(instance.Value.ToArray(), true, true, false);
			var go = new GameObject("CombinedLowLOD", typeof(MeshRenderer), typeof(MeshFilter));
			go.GetComponent<MeshFilter>().sharedMesh = mesh;
			go.GetComponent<MeshRenderer>().sharedMaterial= instance.Key;
			
			generatedRenderers.Add(go.GetComponent<MeshRenderer>());
			generatedMeshes.Add(mesh);
			
			go.transform.SetParent(generatedLOD, false);
		}
		
		var lodGroup = generatedLOD.GetComponent<LODGroup>();
		var lods = lodGroup.GetLODs();
		lods[0].renderers = generatedRenderers.ToArray();
		lodGroup.SetLODs(lods);
	}

    public static void WriteMeshAsset(List<Mesh> generatedMeshes)
    {
        var path = PrefabStageUtility.GetCurrentPrefabStage().prefabAssetPath;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("Not connected to prefab");
            return;
        }

        path = System.IO.Path.ChangeExtension(path, "asset");
        if (generatedMeshes.Count != 0)
        {
            AssetDatabase.CreateAsset(generatedMeshes[0], path);
            for (int i = 1; i != generatedMeshes.Count;i++)
                AssetDatabase.AddObjectToAsset(generatedMeshes[i], path);
            
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.DeleteAsset(path);
        }
    }

    public static Transform CreateLowLod()
    {
        var lowLOD = new GameObject("Low LOD").transform;
        var lowLODGroup = lowLOD.gameObject.AddComponent<LODGroup>();
        var lowLODS = lowLODGroup.GetLODs();
        ArrayUtility.RemoveAt(ref lowLODS, 1);
        ArrayUtility.RemoveAt(ref lowLODS, 1);
        lowLODS[0].screenRelativeTransitionHeight = 0.02F;
        lowLODGroup.SetLODs(lowLODS);
        return lowLOD.transform;
    }


    [MenuItem("HLOD/Setup HLOD")]
	public static void CreateBuildingHLOD()
	{
		if (PrefabStageUtility.GetCurrentPrefabStage() == null ||
		    PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot == null)
		{
			Debug.LogWarning("Setup HLOD can only be used while prefab editing.");
			return;
		}

		var parent = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot.transform;
		if (parent.GetComponent<LODGroup>() != null || parent.GetComponent<HLOD>() != null)
		{
			Debug.LogWarning("HLOD is already setup on this hierarchy");
			return;
		}

		EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);

	    var lowLOD = CreateLowLod();
		
		
		var highLOD = new GameObject("High LOD").transform;

		while (parent.childCount != 0)
		{
			parent.GetChild(0).SetParent(highLOD, false);
		}
			
		lowLOD.SetParent(parent, false);
		highLOD.SetParent(parent, false);
		
		parent.gameObject.AddComponent(typeof(HLOD));
		var hlod = parent.GetComponent<HLOD>();
		Transform[] transforms = {highLOD, lowLOD};
		hlod.LODParentTransforms = transforms;


		var lodgroup = parent.GetComponent<LODGroup>();

		var lods = lodgroup.GetLODs();
		lods[0].screenRelativeTransitionHeight = 0.3F;
		lods[1].screenRelativeTransitionHeight = 0.02F;
		ArrayUtility.RemoveAt(ref lods, 2);
		lodgroup.SetLODs(lods);

		UpdateBounds(hlod);
	    var generatedMeshes = new List<Mesh>();

		GenerateCombinedMesh(hlod, highLOD, lowLOD, 1, generatedMeshes);
	    WriteMeshAsset(generatedMeshes);
	        
	    HLOD.InvalidateHLODCache();
	}
}