using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.SceneManagement;
using FrustumPlanes = Unity.Rendering.FrustumPlanes;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Workflow.Hybrid
{
    struct LightPoolIndex : IComponentData
    {
        public int PrefabIndex;
        public int InstanceIndex;
        public Entity SourceEntity;
    }

    struct LightPoolCreatedTag : IComponentData
    {
    }

    [ExecuteAlways]
    public class LightPoolSystem : JobComponentSystem
    {
        private const int PoolSize = 128;
        private float FadeDurationInSeconds = 0.5f;

        static ProfilerMarker ProfileUpdateA = new ProfilerMarker("LightPoolSystem.OnUpdateA");
        static ProfilerMarker ProfileUpdateB = new ProfilerMarker("LightPoolSystem.OnUpdateB");
        static ProfilerMarker ProfileUpdateC = new ProfilerMarker("LightPoolSystem.OnUpdateC");
        static ProfilerMarker ProfileUpdateD = new ProfilerMarker("LightPoolSystem.OnUpdateD");
        static ProfilerMarker ProfileUpdateE = new ProfilerMarker("LightPoolSystem.OnUpdateE");

        struct AssignedLight
        {
            public GameObject Instance;
            public Entity ProxyEntity;
            public bool Active;
            public float Dimmer;
            public Light[] Components;
        }

        struct LightPoolStruct
        {
            public List<GameObject> Prefabs;
            public List<Stack<GameObject>> AvailableStacks;
        }

        private LightPoolStruct LightPool;
        private readonly AssignedLight[] AssignedLights = new AssignedLight[PoolSize];

        private void BuildPool(GameObject prefab, int count)
        {
            LightPool.Prefabs.Add(prefab);
            var lights = new Stack<GameObject>(count);
            LightPool.AvailableStacks.Add(lights);

            for (int i = 0; i < count; ++i)
            {
                GameObject instance;
#if UNITY_EDITOR
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    instance = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                }
                else
#endif
                {
                    instance = Object.Instantiate(prefab);
                }

                SceneManager.MoveGameObjectToScene(instance, AdditiveScene);
                instance.SetActive(false);
                instance.hideFlags = HideFlags.DontSave;
                lights.Push(instance);
            }
        }

        private int EnsurePool(GameObject prefab)
        {
            var index = LightPool.Prefabs.IndexOf(prefab);
            if (index >= 0)
                return index;

            BuildPool(prefab, PoolSize);
            return LightPool.Prefabs.Count - 1;
        }

        struct EntityDist
        {
            public float DistanceSq;
            public Entity ProxyEntity;
        }

        [BurstCompile]
        struct FindClosestLightsJob : IJobProcessComponentDataWithEntity<Translation>
        {
            public NativeArray<EntityDist> ClosestLights;
            public float3 CameraPos;
            [ReadOnly] public NativeArray<float4> CullingPlanes;

            public void Execute(Entity entity, int index, [ReadOnly] ref Translation translation)
            {
                if (FrustumPlanes.Intersect(CullingPlanes, translation.Value, 20) == FrustumPlanes.IntersectResult.Out)
                    return;

                var d = math.lengthsq(CameraPos - translation.Value);

                int j = ClosestLights.Length - 1;

                if (d >= ClosestLights[j].DistanceSq)
                    return;

                while (j > 0 && d < ClosestLights[j - 1].DistanceSq)
                {
                    ClosestLights[j] = ClosestLights[j - 1];
                    j -= 1;
                }

                ClosestLights[j] = new EntityDist
                {
                    DistanceSq = d,
                    ProxyEntity = entity
                };
            }
        }

        private ComponentGroup m_NewSharedLights;
        private ComponentGroup m_ActiveLightProxies;

        private EntityCommandBufferSystem m_CommandBufferSystem;

        private NativeArray<EntityDist> ClosestLights;
        private Scene AdditiveScene;
        private Plane[] ManagedCullingPlanes = new Plane[6];
        private NativeArray<float4> CullingPlanes;

        protected override void OnCreateManager()
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                AdditiveScene = SceneManager.GetSceneByName("AdditiveLightPoolScene");
            }
            else
#endif
            {
                AdditiveScene = SceneManager.CreateScene("AdditiveLightPoolScenePlaymode");
            }

            LightPool.Prefabs = new List<GameObject>();
            LightPool.AvailableStacks = new List<Stack<GameObject>>();

            ClosestLights = new NativeArray<EntityDist>(PoolSize, Allocator.Persistent);
            CullingPlanes = new NativeArray<float4>(6, Allocator.Persistent);

            m_NewSharedLights = GetComponentGroup
            (
                ComponentType.ReadOnly<SharedLight>(),
                ComponentType.Exclude<LightPoolCreatedTag>()
            );

            m_ActiveLightProxies = GetComponentGroup
            (
                ComponentType.ReadWrite<LightPoolIndex>(),
                ComponentType.ReadOnly<Translation>()
            );

            m_CommandBufferSystem = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
        }

        protected override void OnDestroyManager()
        {
            foreach (var stack in LightPool.AvailableStacks)
            {
                foreach (var light in stack)
                {
                    Object.DestroyImmediate(light);
                }
            }

            foreach (var light in AssignedLights)
            {
                Object.DestroyImmediate(light.Instance);
            }

            ClosestLights.Dispose();
            CullingPlanes.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle handle)
        {
            if (Camera.main == null || !AdditiveScene.isLoaded)
                return handle;

            #region Setup new lights
            ProfileUpdateA.Begin();
            using (var chunks = m_NewSharedLights.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                if (chunks.Length > 0)
                {
                    var lightProxyArchetype = EntityManager.CreateArchetype
                    (
                        ComponentType.ReadWrite<LightPoolIndex>(),
                        ComponentType.ReadWrite<Translation>(),
                        ComponentType.ReadWrite<Rotation>()
                    );

                    var lightProxies = new NativeArray<Entity>(m_NewSharedLights.CalculateLength(), Allocator.Temp);
                    EntityManager.CreateEntity(lightProxyArchetype, lightProxies);
                    int entityIndex = 0;

                    var archetypeSharedLightType = GetArchetypeChunkSharedComponentType<SharedLight>();
                    var archetypeLightEntityType = GetArchetypeChunkEntityType();
                    var archetypeLocalToWorldType = GetArchetypeChunkComponentType<LocalToWorld>(true);

                    foreach (var chunk in chunks)
                    {
                        var sharedLight = chunk.GetSharedComponentData(archetypeSharedLightType, EntityManager);
                        var lightEntity = chunk.GetNativeArray(archetypeLightEntityType);
                        var poolIndex = EnsurePool(sharedLight.Value);

                        var localToWorldArray = chunk.GetNativeArray(archetypeLocalToWorldType);
                        for (int i = 0; i < chunk.Count; ++i)
                        {
                            var position = localToWorldArray[i].Position;
                            var rotation = new quaternion(localToWorldArray[i].Value);
                            var proxy = lightProxies[entityIndex];
                            EntityManager.SetComponentData(proxy, new LightPoolIndex
                            {
                                SourceEntity = lightEntity[i],
                                PrefabIndex = poolIndex,
                                InstanceIndex = -1
                            });
                            EntityManager.SetComponentData(proxy, new Translation {Value = position});
                            EntityManager.SetComponentData(proxy, new Rotation() {Value = rotation});
                            entityIndex += 1;
                        }
                    }

                    EntityManager.AddComponent(m_NewSharedLights, ComponentType.ReadOnly<LightPoolCreatedTag>());
                }
            }
            ProfileUpdateA.End();
            #endregion

            #region Find closest lights
            ProfileUpdateB.Begin();
            {
                GeometryUtility.CalculateFrustumPlanes(Camera.main, ManagedCullingPlanes);
                CullingPlanes[0] = new float4(ManagedCullingPlanes[0].normal.x, ManagedCullingPlanes[0].normal.y,
                    ManagedCullingPlanes[0].normal.z, ManagedCullingPlanes[0].distance);
                CullingPlanes[1] = new float4(ManagedCullingPlanes[1].normal.x, ManagedCullingPlanes[1].normal.y,
                    ManagedCullingPlanes[1].normal.z, ManagedCullingPlanes[1].distance);
                CullingPlanes[2] = new float4(ManagedCullingPlanes[2].normal.x, ManagedCullingPlanes[2].normal.y,
                    ManagedCullingPlanes[2].normal.z, ManagedCullingPlanes[2].distance);
                CullingPlanes[3] = new float4(ManagedCullingPlanes[3].normal.x, ManagedCullingPlanes[3].normal.y,
                    ManagedCullingPlanes[3].normal.z, ManagedCullingPlanes[3].distance);
                CullingPlanes[4] = new float4(ManagedCullingPlanes[4].normal.x, ManagedCullingPlanes[4].normal.y,
                    ManagedCullingPlanes[4].normal.z, ManagedCullingPlanes[4].distance);
                CullingPlanes[5] = new float4(ManagedCullingPlanes[5].normal.x, ManagedCullingPlanes[5].normal.y,
                    ManagedCullingPlanes[5].normal.z, ManagedCullingPlanes[5].distance);
            }

            for (int i = 0; i < ClosestLights.Length; ++i)
            {
                ClosestLights[i] = new EntityDist {DistanceSq = float.MaxValue};
            }

            new FindClosestLightsJob
            {
                ClosestLights = ClosestLights,
                CameraPos = Camera.main.transform.position,
                CullingPlanes = CullingPlanes
            }.ScheduleGroupSingle(m_ActiveLightProxies).Complete();
            ProfileUpdateB.End();
            #endregion

            #region Assign instances
            ProfileUpdateC.Begin();
            for(int i = 0; i < AssignedLights.Length; ++i)
            {
                AssignedLights[i].Active = false;
            }

            var lightPoolIndexFromEntity = GetComponentDataFromEntity<LightPoolIndex>();
            {
                var translationFromEntity = GetComponentDataFromEntity<Translation>(true);
                var rotationFromEntity = GetComponentDataFromEntity<Rotation>(true);
                var searchForAvailableAt = 0;
                for (int i = 0; i < ClosestLights.Length; ++i)
                {
                    if (!EntityManager.Exists(ClosestLights[i].ProxyEntity))
                        break;

                    var indices = lightPoolIndexFromEntity[ClosestLights[i].ProxyEntity];

                    if (!EntityManager.Exists(indices.SourceEntity))
                        continue;

                    if (indices.InstanceIndex >= 0)
                    {
                        AssignedLights[indices.InstanceIndex].Active = true;
                        continue;
                    }

                    while (true)
                    {
                        if (searchForAvailableAt == AssignedLights.Length)
                            break;

                        if (AssignedLights[searchForAvailableAt].Instance == null)
                        {
                            var entity = ClosestLights[i].ProxyEntity;
                            var idx = searchForAvailableAt;
                            indices.InstanceIndex = idx;
                            lightPoolIndexFromEntity[entity] = indices;

                            var instance = LightPool.AvailableStacks[indices.PrefabIndex].Pop();
                            AssignedLights[idx].Instance = instance;
                            AssignedLights[idx].Active = true;
                            AssignedLights[idx].ProxyEntity = entity;

                            var translation = translationFromEntity[entity].Value;
                            var rotation = rotationFromEntity[entity].Value;

                            instance.SetActive(true);
                            instance.transform.position = translation;
                            instance.transform.rotation = rotation;

                            AssignedLights[searchForAvailableAt].Components = instance.GetComponentsInChildren<Light>();

                            break;
                        }

                        searchForAvailableAt += 1;
                    }
                }
            }
            ProfileUpdateC.End();
            #endregion

            #region Update light intensity
            ProfileUpdateD.Begin();
            var commandBuffer = m_CommandBufferSystem.CreateCommandBuffer();
            float dimDeltaAbs = FadeDurationInSeconds > 0f ? Time.deltaTime / FadeDurationInSeconds : 1f;
            for (int i = 0; i < AssignedLights.Length; ++i)
            {
                if (AssignedLights[i].Instance == null)
                    continue;

                var dimDelta = math.select(-dimDeltaAbs, +dimDeltaAbs, AssignedLights[i].Active);
                var newDim = math.saturate(AssignedLights[i].Dimmer + dimDelta);

                if (AssignedLights[i].Dimmer != newDim)
                {
                    foreach (var light in AssignedLights[i].Components)
                    {
                        HDAdditionalLightData ald = light.GetComponent<HDAdditionalLightData>();
                        ald.lightDimmer = AssignedLights[i].Dimmer;
                    }

                    AssignedLights[i].Dimmer = newDim;
                }

                if (AssignedLights[i].Dimmer == 0f)
                {
                    var indices = lightPoolIndexFromEntity[AssignedLights[i].ProxyEntity];
                    indices.InstanceIndex = -1;
                    lightPoolIndexFromEntity[AssignedLights[i].ProxyEntity] = indices;

                    if(!EntityManager.Exists(indices.SourceEntity))
                        commandBuffer.DestroyEntity(AssignedLights[i].ProxyEntity);

                    LightPool.AvailableStacks[indices.PrefabIndex].Push(AssignedLights[i].Instance);
                    AssignedLights[i].Instance.SetActive(false);
                    AssignedLights[i].Instance = null;
                    AssignedLights[i].ProxyEntity = Entity.Null;
                }
            }
            ProfileUpdateD.End();
            #endregion

            return handle;
        }
    }
}
