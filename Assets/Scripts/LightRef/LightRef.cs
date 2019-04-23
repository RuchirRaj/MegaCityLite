using Unity.Entities;
using Unity.Transforms;
using Unity.Workflow.Hybrid;
using UnityEngine;

public class LightRef : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject LightReference;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddSharedComponentData(entity, new SharedLight {Value = LightReference});
    }
}
