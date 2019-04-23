using Unity.Rendering;
using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using Unity.Collections;

namespace Traffic.Simulation
{
    public class TrafficSettings : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
    {
        public float pathSegments=100;
        public float globalSpeedFactor = 1.0f;
        public int maxCars = 2000;

        public float[] speedMultipliers;

        public List<GameObject> vehiclePrefabs;


        public void DeclareReferencedPrefabs(List<GameObject> gameObjects)
        {
            for (int i = 0; i < vehiclePrefabs.Count; i++)
            {
                gameObjects.Add(vehiclePrefabs[i]);
            }
        }

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            for (int j = 0; j < vehiclePrefabs.Count; j++)
            {
                // A primary entity needs to be called before additional entities can be used
                Entity vehiclePrefab = conversionSystem.CreateAdditionalEntity(this);
                var prefabData = new VehiclePrefabData
                {
                    VehiclePrefab = conversionSystem.GetPrimaryEntity(vehiclePrefabs[j]),
                    VehicleSpeed = j < speedMultipliers.Length ? speedMultipliers[j] : 3.0f
                };
                dstManager.AddComponentData(vehiclePrefab, prefabData);
            }
            
            var trafficSettings = new TrafficSettingsData
            {
                GlobalSpeedFactor = globalSpeedFactor,
                PathSegments = pathSegments,
                MaxCars = maxCars
            };

            dstManager.AddComponentData(entity, trafficSettings);
        }
    }
}
