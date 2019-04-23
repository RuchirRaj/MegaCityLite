using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.Workflow.Hybrid
{
    [Serializable]
    public struct SharedLight : ISharedComponentData
    {
        public GameObject Value;
    }

    class SharedLightComponent : SharedComponentDataProxy<SharedLight>
    {
    }
}
