using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Workflow.Hybrid
{
    //@TODO - subscenes
    /*
    [UpdateAfter(typeof(MeshConversionRule))]
    public class LightRefConversionRule : IEntityConversionRule
    {
        public System.Type[] ConvertedComponents()
        {
            return new System.Type[] {typeof(LightRef)};
        }

        public bool CanConvert(GameObject go)
        {
            return true;
        }

        public bool Convert(EntityManager entityManager, Entity ent, GameObject go,
            Dictionary<GameObject, List<Entity>> gameObjectEntities,
            ref EntityBounds bounds)
        {
            var lightReference = go.GetComponent<LightRef>().LightReference;

            this.EnsurePosition(entityManager, ent, go);
            this.EnsureRotation(entityManager, ent, go);

            entityManager.AddSharedComponentData(ent, new SharedLight {Value = lightReference});

            return true;
        }
        
        
        public void Postprocess(EntityManager entityManager, ref EntityBounds bounds)
        {
        }
    }
    */
}
