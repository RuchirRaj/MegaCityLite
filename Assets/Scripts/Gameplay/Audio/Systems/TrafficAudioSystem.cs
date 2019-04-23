using System;
using System.Collections.Generic;
using Traffic.Simulation;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Random = Unity.Mathematics.Random;

namespace Unity.Audio.Megacity
{
    [Serializable]
    public struct TrafficAudioParameters
    {
        [Range(0, 0.9f)]
        public float FlyByPole /* = 0.002f*/;
        [Range(0.5f, 4f)]
        public float FalloffCurve /* = 1.3f*/;
        [Range(0, 3)]
        public float Volume /* 0.4f*/;
    }

    public class TrafficAudioBarrier : EntityCommandBufferSystem { }

    [UpdateInGroup(typeof(AudioFrame))]
    public class TrafficAudioFieldSystem : JobComponentSystem
    {
        // This is how much the flyby should multiplicatively decay by each second
        const float k_FlyByPole = 0.002f;

        struct AdditiveState : IComponentData {}

        struct FlyByState : IComponentData
        {
            public Entity Target;
        }

        struct VehicleEmitter : ISystemStateComponentData
        {
            public float Angle;
            public float Left, Right;
            public float Volume;
            public Entity Sample;
        }

        FlyByInputBarrier m_FlyByBarrier;

        TrafficAudioBarrier m_Barrier;

        ComponentGroup m_NewVehiclesGroup;
        ComponentGroup m_AliveVehiclesGroup;
        ComponentGroup m_SamplesGroup;
        ComponentGroup m_DeadVehiclesGroup;
        ComponentGroup m_FlyByGroup;

        EntityArchetype m_FlyByArchetype;

        AudioManagerSystem m_AudioManager;
        FlyBySystem m_FlyBySystem;

        Transform m_ListenerTransform;

        NativeList<Entity> m_SampleEntities;

        NativeQueue<Entity> m_FlyByCandidates;
        NativeList<Entity> m_FlyByEntityBuffer;
        NativeArray<Random> m_Randoms;

        SoundCollection m_FlyBySoundCollection;

        TrafficAudioParameters m_Parameters;

        [BurstCompile]
        struct CalculateMixContributions : IJobChunk
        {
            [WriteOnly] public NativeQueue<Entity>.Concurrent FlyByCandidates;

            public float3 ListenerPosition;
            public float4x4 ListenerWorldToLocal;
            public float Falloff;

            public float DtPole;

            [ReadOnly] public ArchetypeChunkEntityType EntityType;
            [ReadOnly] public ArchetypeChunkComponentType<Translation> PositionType;
            public ArchetypeChunkComponentType<VehicleEmitter> EmitterType;
            public ArchetypeChunkComponentType<PositionalFlyByData> FlyByPositionType;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var positions = chunk.GetNativeArray(PositionType);
                var emitters = chunk.GetNativeArray(EmitterType);
                var flybyPositions = chunk.GetNativeArray(FlyByPositionType);

                for (int i = 0; i < chunk.Count; ++i)
                {
                    var emitter = emitters[i];

                    var aroundOrigin = positions[i].Value - ListenerPosition;
                    var rotated = math.transform(ListenerWorldToLocal, positions[i].Value);

                    emitter.Angle = (float)math.PI - math.atan2(rotated.z, rotated.x);

                    var cosine = math.cos(emitter.Angle);

                    emitter.Left = 0.5f + 0.5f * cosine;
                    emitter.Right = 0.5f + 0.5f * (1 - cosine);

                    var distance = math.length(aroundOrigin);

                    var newVolume = 1.0f / (0.4f + math.pow(distance - 3, Falloff));
                    newVolume = math.select(newVolume, Falloff, distance < 3.0f) / Falloff;
                    emitter.Volume = math.select(emitter.Volume * DtPole, newVolume, newVolume > emitter.Volume);

                    var positional = flybyPositions[i];

                    positional.Left = emitter.Left;
                    positional.Right = emitter.Right;
                    positional.PrevDist = positional.CurrentDist;
                    positional.CurrentDist = distance;

                    flybyPositions[i] = positional;

                    emitters[i] = emitter;

                    if (distance < 15f)
                        FlyByCandidates.Enqueue(entities[i]);
                }
                
            }
        }

        struct DistanceComparitor : IComparer<Entity>
        {
            public ComponentDataFromEntity<PositionalFlyByData> EmitterFromEntity;

            public int Compare(Entity x, Entity y)
            {
                var a = EmitterFromEntity[x];
                var b = EmitterFromEntity[y];

                return (int)(a.CurrentDist - b.CurrentDist);
            }
        }

        struct SelectFlyByEmitters : IJob
        {
            [ReadOnly] public ComponentDataFromEntity<PositionalFlyByData> EmitterFromEntity;

            public NativeQueue<Entity> FlyByCandidates;
            public NativeList<Entity> OutputFlyBys;

            [ReadOnly] public NativeArray<Entity> ActiveFlybyEntities;

            public ComponentDataFromEntity<FlyByState> FlyByStateFromEntity;

            public int MaxFlybys;

            public void Execute()
            {
                var currentCandidateCapacity = MaxFlybys - ActiveFlybyEntities.Length;

                if (currentCandidateCapacity == 0)
                {
                    FlyByCandidates.Clear();
                    return;

                }

                var candidates = FlyByCandidates.Count;

                NativeArray<Entity> sortedCandidates = new NativeArray<Entity>(candidates, Allocator.Temp);

                for (int i = 0; i < candidates; ++i)
                {
                    // TODO: Cull things that already exist
                    sortedCandidates[i] = FlyByCandidates.Dequeue();
                }

                var comparitor = new DistanceComparitor { EmitterFromEntity = EmitterFromEntity };

                sortedCandidates.Sort(comparitor);

                var maxEmittersToConsider = math.min(sortedCandidates.Length, currentCandidateCapacity);

                for (int i = 0, c = 0; i < sortedCandidates.Length; ++i)
                {
                    if (c >= maxEmittersToConsider)
                        break;
                    var emitter = sortedCandidates[i];

                    bool alreadyExists = false;

                    for (int e = 0; e < ActiveFlybyEntities.Length; ++e)
                    {
                        if (emitter == ActiveFlybyEntities[e])
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (alreadyExists)
                        continue;

                    c++;
                    OutputFlyBys.Add(emitter);
                }

                FlyByCandidates.Clear();
                sortedCandidates.Dispose();
            }
        }

        [BurstCompile]
        struct UpdatePanningForFlyBys : IJob
        {
            public ComponentDataFromEntity<PositionalFlyByData> PositionalFromEntity;

            // This array is also used by SelectFlyBy - keep in mind this job has to implicitly
            // take a dependency on that job.
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Entity> ActiveFlybys;

            public ComponentDataFromEntity<FlyByState> FlyByStateFromEntity;

            public void Execute()
            {
                for (int i = 0; i < ActiveFlybys.Length; ++i)
                {
                    var flyBySelf = ActiveFlybys[i];
                    var emitter = FlyByStateFromEntity[flyBySelf].Target;

                    if (PositionalFromEntity.Exists(emitter) && PositionalFromEntity.Exists(flyBySelf))
                    {
                        var positional = PositionalFromEntity[emitter];
                        PositionalFromEntity[flyBySelf] = positional;
                    }
                }
            }
        }

        //[BurstCompile]
        struct SetupNewFlybys : IJob
        {
            public NativeList<Entity> InputFlyBysToCreate;
            public ComponentDataFromEntity<PositionalFlyByData> PositionalFromEntity;

            public EntityCommandBuffer ECB;
            public SoundCollection SoundGroup;
            public EntityArchetype FlyByArcheType;

            public void Execute()
            {
                for (int i = 0; i < InputFlyBysToCreate.Length; ++i)
                {
                    var entity = ECB.CreateEntity(FlyByArcheType);
                    ECB.SetComponent(entity, new FlyBy { Group = SoundGroup });
                    ECB.SetComponent(entity, new FlyByState { Target = InputFlyBysToCreate[i] });
                    ECB.SetComponent(entity, PositionalFromEntity[InputFlyBysToCreate[i]]);

                }
                InputFlyBysToCreate.Clear();
            }
        }


        [BurstCompile]
        struct FoldEmitterFieldsToSamples : IJobProcessComponentDataWithEntity<VehicleEmitter>
        {
            public ComponentDataFromEntity<SamplePlayback> SamplePlaybackFromAliveEntities;
            public EntityCommandBuffer Ecb;
            public ComponentType EmitterType;
            public ComponentType PositionalType;

            public float GeneralVolume;

            public void Execute(Entity entity, int index, ref VehicleEmitter emitter)
            {
                if (SamplePlaybackFromAliveEntities.Exists(emitter.Sample))
                {
                    var playback = SamplePlaybackFromAliveEntities[emitter.Sample];
                    playback.Left += emitter.Left * emitter.Volume;
                    playback.Right += emitter.Right * emitter.Volume;
                    playback.Volume = GeneralVolume;
                    SamplePlaybackFromAliveEntities[emitter.Sample] = playback;
                }
                else
                {
                    // Prune it.
                    Ecb.RemoveComponent(entity, EmitterType);
                    Ecb.RemoveComponent(entity, PositionalType);
                }
            }
        }

        [BurstCompile]
        struct ClearPlaybackAdditiveStates : IJobProcessComponentData<SamplePlayback>
        {
            public void Execute(ref SamplePlayback playback)
            {
                playback.Left = 0;
                playback.Right = 0;
                playback.Volume = 0;
                playback.Pitch = 1;
            }
        }

        // TODO: Enable burst when it's supported for adding Components to entities
        //[BurstCompile]
        struct AttachToNewVehicles : IJobChunk
        {
            [ReadOnly] public NativeArray<Entity> SamplePlaybackEntities;

#pragma warning disable 0649
            [NativeSetThreadIndex]
            int m_ThreadIndex;
#pragma warning restore 0649

            [NativeDisableContainerSafetyRestriction]
            public NativeArray<Mathematics.Random> Randoms;

            public EntityCommandBuffer.Concurrent Ecb;

            [ReadOnly] public ArchetypeChunkEntityType EntityType;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                if (SamplePlaybackEntities.Length == 0)
                    return;

                var entities = chunk.GetNativeArray(EntityType);
                var random = Randoms[m_ThreadIndex];

                for (int i = 0; i < chunk.Count; ++i)
                {
                    var newVehicle = new VehicleEmitter
                    {
                        Sample = SamplePlaybackEntities[random.NextInt(0, SamplePlaybackEntities.Length)]
                    };

                    Ecb.AddComponent(chunkIndex, entities[i], newVehicle);
                    Ecb.AddComponent(chunkIndex, entities[i], new PositionalFlyByData());
                }

                Randoms[m_ThreadIndex] = random;
            }
        }

        // TODO: Need to run this job on destruction as well.
        [BurstCompile]
        struct DetachDeadVehicles : IJobProcessComponentDataWithEntity<VehicleEmitter>
        {
            public EntityCommandBuffer Ecb;
            public ComponentType EmitterType;

            public void Execute(Entity entity, int index, [ReadOnly] ref VehicleEmitter c0)
            {
                Ecb.RemoveComponent(entity, EmitterType);
            }
        }

        void AddClip(AudioClip clip)
        {
            var playbackSystem = World.Active.GetOrCreateManager<SamplePlaybackSystem>();
            playbackSystem.AddClip(clip);
        }

        public void AddDistributedSamplePlayback(AudioClip clip)
        {
            var sample = EntityManager.CreateEntity();
            AddClip(clip);
            EntityManager.AddComponentData(sample, new AdditiveState());
            EntityManager.AddComponentData(sample, new SamplePlayback { Volume = 1, Loop = 1, Pitch = 1 });
            EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
            m_SampleEntities.Add(sample);
        }

        public void ClearSamplePlaybacks()
        {
            for(int i = 0; i < m_SampleEntities.Length; ++i)
                EntityManager.DestroyEntity(m_SampleEntities[i]);

            m_SampleEntities.Clear();
        }

        public void DetachFromAllVehicles()
        {
            using (var trafficEntities = m_AliveVehiclesGroup.ToEntityArray(Allocator.TempJob))
            {
                for (int i = 0; i < trafficEntities.Length; ++i)
                {
                    EntityManager.RemoveComponent<VehicleEmitter>(trafficEntities[i]);
                    EntityManager.RemoveComponent<PositionalFlyByData>(trafficEntities[i]);
                }
            }

            using (var flyBys = m_FlyByGroup.ToEntityArray(Allocator.TempJob))
            {
                for (int i = 0; i < flyBys.Length; ++i)
                {
                    EntityManager.DestroyEntity(flyBys[i]);
                }
            }
        }

        public void SetParameters(TrafficAudioParameters parameters)
        {
            m_Parameters = parameters;
        }

        public void SetFlyBySoundGroup(SoundCollection group)
        {
            m_FlyBySoundCollection = group;
        }

        protected override void OnDestroyManager()
        {
            m_FlyByEntityBuffer.Dispose();
            m_SampleEntities.Dispose();
            m_FlyByCandidates.Dispose();
            m_Randoms.Dispose();
        }

        protected override void OnCreateManager()
        {
            m_FlyByBarrier = World.GetOrCreateManager<FlyByInputBarrier>();
            m_Barrier = World.GetOrCreateManager<TrafficAudioBarrier>();
            m_AudioManager = World.GetOrCreateManager<AudioManagerSystem>();
            m_FlyBySystem = World.GetOrCreateManager<FlyBySystem>();

            m_FlyByArchetype = EntityManager.CreateArchetype(typeof(FlyBy), typeof(FlyByState), typeof(PositionalFlyByData));

            m_SampleEntities = new NativeList<Entity>(Allocator.Persistent);
            m_FlyByCandidates = new NativeQueue<Entity>(Allocator.Persistent);
            m_FlyByEntityBuffer = new NativeList<Entity>(Allocator.Persistent);
            m_Randoms = new NativeArray<Random>(JobsUtility.MaxJobThreadCount, Allocator.Persistent);

            var seedRandom = new Random((uint)(2 + Time.time * 0xFFFF));

            for(int i = 0; i < m_Randoms.Length; ++i)
                m_Randoms[i] = new Random(seedRandom.NextUInt());

            m_NewVehiclesGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(VehiclePathing), typeof(Translation) },
                    None = new ComponentType[] { typeof(VehicleEmitter), typeof(PositionalFlyByData) },
                    Any = Array.Empty<ComponentType>(),
                });

            m_AliveVehiclesGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(VehiclePathing), typeof(Translation), typeof(VehicleEmitter), typeof(PositionalFlyByData) },
                    None = Array.Empty<ComponentType>(),
                    Any = Array.Empty<ComponentType>(),
                });

            m_SamplesGroup = GetComponentGroup(typeof(SamplePlayback), typeof(AdditiveState));

            m_DeadVehiclesGroup = GetComponentGroup(typeof(VehicleEmitter), ComponentType.Exclude<VehiclePathing>());

            m_FlyByGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(FlyBy), typeof(FlyByState) },
                    None = Array.Empty<ComponentType>(),
                    Any = Array.Empty<ComponentType>(),
                });
        }

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            if (!m_AudioManager.AudioEnabled || m_SampleEntities.Length == 0)
                return deps;

            var deadSetup = new DetachDeadVehicles
            {
                Ecb = m_Barrier.CreateCommandBuffer(),
                EmitterType = ComponentType.ReadWrite<VehicleEmitter>()
            };

            var newSetup = new AttachToNewVehicles
            {
                EntityType = GetArchetypeChunkEntityType(),
                SamplePlaybackEntities = m_SampleEntities,
                Ecb = m_Barrier.CreateCommandBuffer().ToConcurrent(),
                Randoms = m_Randoms
            };

            deps = JobHandle.CombineDependencies(
                deadSetup.ScheduleGroupSingle(m_DeadVehiclesGroup, deps),
                newSetup.Schedule(m_NewVehiclesGroup, deps)
            );

            var listener = m_AudioManager.ListenerTransform;

            var mixContribution = new CalculateMixContributions
            {
                EntityType = GetArchetypeChunkEntityType(),
                ListenerPosition = listener.position,
                ListenerWorldToLocal = listener.worldToLocalMatrix,
                Falloff = m_Parameters.FalloffCurve,
                DtPole = Mathf.Pow(m_Parameters.FlyByPole, Time.deltaTime),
                FlyByCandidates = m_FlyByCandidates.ToConcurrent(),
                EmitterType = GetArchetypeChunkComponentType<VehicleEmitter>(),
                FlyByPositionType = GetArchetypeChunkComponentType<PositionalFlyByData>(),
                PositionType = GetArchetypeChunkComponentType<Translation>()
            };

            var flyByEntities = m_FlyByGroup.ToEntityArray(Allocator.TempJob, out var flyByEntitiesJobHandle);

            var flyByStateFromEntity = GetComponentDataFromEntity<FlyByState>();
            var positionalFromEntity = GetComponentDataFromEntity<PositionalFlyByData>();

            var flyBySelection = new SelectFlyByEmitters
            {
                FlyByCandidates = m_FlyByCandidates,
                EmitterFromEntity = positionalFromEntity,
                OutputFlyBys = m_FlyByEntityBuffer,
                ActiveFlybyEntities = flyByEntities,
                FlyByStateFromEntity = flyByStateFromEntity,
                MaxFlybys = m_FlyBySystem.MaxVoices
            };

            var flyByCreations = new SetupNewFlybys
            {
                InputFlyBysToCreate = m_FlyByEntityBuffer,
                ECB = m_FlyByBarrier.CreateCommandBuffer(),
                SoundGroup = m_FlyBySoundCollection,
                PositionalFromEntity = positionalFromEntity,
                FlyByArcheType = m_FlyByArchetype
            };

            var flyByPannings = new UpdatePanningForFlyBys
            {
                ActiveFlybys = flyByEntities,
                FlyByStateFromEntity = flyByStateFromEntity,
                PositionalFromEntity = positionalFromEntity
            };

            var samplePlaybackFromEntity = GetComponentDataFromEntity<SamplePlayback>();

            var foldingMix = new FoldEmitterFieldsToSamples
            {
                SamplePlaybackFromAliveEntities = samplePlaybackFromEntity,
                Ecb = m_Barrier.CreateCommandBuffer(),
                EmitterType = ComponentType.ReadWrite<VehicleEmitter>(),
                PositionalType = ComponentType.ReadWrite<PositionalFlyByData>(),
                GeneralVolume = m_Parameters.Volume
            };

            var mixDeps = mixContribution.Schedule(m_AliveVehiclesGroup, deps);
            var flyByCalcDeps = flyBySelection.Schedule(JobHandle.CombineDependencies(mixDeps, flyByEntitiesJobHandle));
            var flyByDeps = flyByCreations.Schedule(flyByCalcDeps);

            m_FlyByBarrier.AddJobHandleForProducer(flyByDeps);

            flyByDeps = flyByPannings.Schedule(flyByDeps);

            deps = new ClearPlaybackAdditiveStates().ScheduleGroup(m_SamplesGroup, mixDeps);

            deps = foldingMix.ScheduleSingle(this, deps);

            m_Barrier.AddJobHandleForProducer(deps);

            return JobHandle.CombineDependencies(deps, flyByDeps);
        }
    }
}
