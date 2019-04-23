using System;
using Unity.Mathematics;
using UnityEngine;

namespace Traffic.Pathing
{
    [ExecuteInEditMode]
    public class PathNode : MonoBehaviour
    {
        void OnDestroy()
        {
            var parent = transform.parent.GetComponent<Path>();
            if (parent != null)
            {
                parent.NodeDeleted();
            }
        }
    }
    
	[Serializable]
	public struct RoadWaypoint
	{
		public float3 position;
	}
    
}
