using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Audio.Megacity
{
    [UpdateInGroup(typeof(AudioFrame))]
    class FlyByInputBarrier : EntityCommandBufferSystem {}

#pragma warning disable 0649
    struct FlyBy : IComponentData
    {
        public float Delay;

        public float PitchVariance;
        public SoundCollection Group;
    }
#pragma warning restore 0649

    struct PositionalFlyByData : IComponentData
    {
        public float PrevDist;
        public float CurrentDist;
        public float Left;
        public float Right;
    }

    public struct SoundCollection
    {
#pragma warning disable 0649
        internal int ID;
#pragma warning restore 0649
    }

    [Serializable]
    public struct FlyByParameters
    {
        [Range(0, 50)]
        public float MinSpeed/* = 28*/;

        [Range(50, 200)]
        public float MaxSpeed/* = 100*/;

        [Range(40, 100)]
        public float Fast/* = 85*/;

        [Range(0.1f, 1)]
        public float FastPitchMul/* = 0.8f*/;

        [Range(0, 1)]
        public float Volume/* = 0.6f*/;

        [Range(0, 1)]
        public float LowLayer /* = 1f*/;

        [Range(0, 1)]
        public float HighLayer /* = 1f*/;

    }

    [UpdateBefore(typeof(AudioFrame))]
    class FlyByOutputBarrier : EntityCommandBufferSystem {}

    [UpdateAfter(typeof(FlyByInputBarrier)), UpdateInGroup(typeof(AudioFrame))]
    class FlyBySystem : JobComponentSystem
    {
        struct SampleInfo
        {
            public Entity PlayerEntity;
            public float Length;
            public int Intensity;
        }

        struct FlyByState : ISystemStateComponentData
        {
            public float Timeout;
            public int SampleIndex;
        }

        public int MaxVoices => m_SampleFlyByEntities.Length;

        NativeList<SampleInfo> m_SampleFlyByEntities;
        NativeList<int> m_SampleHighFreeList;
        NativeList<int> m_SampleLowFreeList;

        ComponentGroup m_NewFlyByGroup;
        ComponentGroup m_AliveFlyByGroup;
        ComponentGroup m_DeadFlyByGroup;

        FlyByOutputBarrier m_Outputs;

        FlyByParameters m_Parameters = new FlyByParameters();

        public void AddClip(AudioClip clip)
        {
            var playbackSystem = World.GetOrCreateManager<SamplePlaybackSystem>();
            playbackSystem.AddClip(clip);
        }

        public void AddHighFlyBySound(SoundCollection collection, AudioClip clip)
        {
            var sample = EntityManager.CreateEntity();
            AddClip(clip);
            EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
            m_SampleFlyByEntities.Add(new SampleInfo {Length = clip.length, PlayerEntity = sample, Intensity = 1});
            m_SampleHighFreeList.Add(m_SampleFlyByEntities.Length - 1);
        }

        public void AddLowFlyBySound(SoundCollection collection, AudioClip clip)
        {
            var sample = EntityManager.CreateEntity();
            AddClip(clip);
            EntityManager.AddComponentData(sample, new SharedAudioClip { ClipInstanceID = clip.GetInstanceID() });
            m_SampleFlyByEntities.Add(new SampleInfo { Length = clip.length, PlayerEntity = sample, Intensity = 0});
            m_SampleLowFreeList.Add(m_SampleFlyByEntities.Length - 1);
        }

        public void ClearCollections()
        {
            for(int i = 0; i < m_SampleFlyByEntities.Length; ++i)
                EntityManager.DestroyEntity(m_SampleFlyByEntities[i].PlayerEntity);

            m_SampleFlyByEntities.Clear();
            m_SampleHighFreeList.Clear();
            m_SampleLowFreeList.Clear();
        }

        public void SetParameters(FlyByParameters parameters)
        {
            m_Parameters = parameters;
        }

        public SoundCollection CreateCollection()
        {
            return new SoundCollection();
        }

        protected override void OnDestroyManager()
        {
            m_SampleFlyByEntities.Dispose();
            m_SampleHighFreeList.Dispose();
            m_SampleLowFreeList.Dispose();

            m_NewFlyBysEnumerable.Dispose();
            m_DeadFlyBysEnumerable.Dispose();
            m_AliveFlyBysEnumerable.Dispose();
        }

        protected override void OnCreateManager()
        {
            m_NewFlyByGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(FlyBy), ComponentType.ReadOnly(typeof(PositionalFlyByData)) },
                    None = new ComponentType[] { typeof(FlyByState) },
                    Any = Array.Empty<ComponentType>(),
                });

            m_AliveFlyByGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(FlyBy), ComponentType.ReadOnly(typeof(PositionalFlyByData)), typeof(FlyByState) },
                    None = Array.Empty<ComponentType>(),
                    Any = Array.Empty<ComponentType>(),
                });

            m_DeadFlyByGroup = GetComponentGroup(
                new EntityArchetypeQuery
                {
                    All = new ComponentType[] { typeof(FlyByState) },
                    None = new ComponentType[] { typeof(FlyBy), ComponentType.ReadOnly(typeof(PositionalFlyByData)) },
                    Any = Array.Empty<ComponentType>(),
                });

            m_Outputs = World.Active.GetOrCreateManager<FlyByOutputBarrier>();

            m_SampleFlyByEntities = new NativeList<SampleInfo>(Allocator.Persistent);
            m_SampleHighFreeList = new NativeList<int>(Allocator.Persistent);
            m_SampleLowFreeList = new NativeList<int>(Allocator.Persistent);

            m_NewFlyBysEnumerable = new ChunkEntityEnumerable();
            m_DeadFlyBysEnumerable = new ChunkEntityEnumerable();
            m_AliveFlyBysEnumerable = new ChunkEntityEnumerable();
        }

        ChunkEntityEnumerable m_NewFlyBysEnumerable;
        ChunkEntityEnumerable m_DeadFlyBysEnumerable;
        ChunkEntityEnumerable m_AliveFlyBysEnumerable;

        protected override JobHandle OnUpdate(JobHandle deps)
        {
            var buffer = m_Outputs.CreateCommandBuffer();

            m_NewFlyBysEnumerable.Setup(EntityManager, m_NewFlyByGroup, Allocator.Persistent);
            m_DeadFlyBysEnumerable.Setup(EntityManager, m_DeadFlyByGroup, Allocator.Persistent);
            m_AliveFlyBysEnumerable.Setup(EntityManager, m_AliveFlyByGroup, Allocator.Persistent);

            var maintenance = new SetupNewAndOldFlybys
            {
                CurrentSamples = m_SampleFlyByEntities,
                Dead = m_DeadFlyBysEnumerable,
                ECB = buffer,
                New = m_NewFlyBysEnumerable,
                Rand = new Mathematics.Random((uint)(2 + Time.time * 0xFFFF)),
                HighFreeList = m_SampleHighFreeList,
                LowFreeList = m_SampleLowFreeList,
                Dt = Time.deltaTime,
                Params = m_Parameters,
                PositionalFromEntity = GetComponentDataFromEntity<PositionalFlyByData>(true)
            };

            var playbackFromEntity = GetComponentDataFromEntity<SamplePlayback>(false);

            var update = new TickCurrents
            {
                CurrentSamples = m_SampleFlyByEntities,
                Alive = m_AliveFlyBysEnumerable,
                ECB = buffer,
                HighFreeList = m_SampleHighFreeList,
                LowFreeList = m_SampleLowFreeList,
                Dt = Time.deltaTime,
                SamplePlaybackType = ComponentType.ReadWrite<SamplePlayback>(),
                PlaybackFromEntity = playbackFromEntity,
                StateFromEntity = GetComponentDataFromEntity<FlyByState>(false),
                PositionalFromEntity = GetComponentDataFromEntity<PositionalFlyByData>(true)
            };

            deps = update.Schedule(deps);
            deps = maintenance.Schedule(deps);
            m_Outputs.AddJobHandleForProducer(deps);

            return deps;
        }

        /* Trigger speeds:
         *  > 90 max fly by sound
         *  < 20 no sound
         *  <
         */

        //[BurstCompile]
        struct SetupNewAndOldFlybys : IJob
        {
            [ReadOnly] public ChunkEntityEnumerable New;
            [ReadOnly] public ChunkEntityEnumerable Dead;

            [ReadOnly] public ComponentDataFromEntity<PositionalFlyByData> PositionalFromEntity;

            [ReadOnly] public NativeList<SampleInfo> CurrentSamples;

            public NativeList<int> HighFreeList;
            public NativeList<int> LowFreeList;

            public EntityCommandBuffer ECB;

            public Mathematics.Random Rand;

            public float Dt;

            public FlyByParameters Params;

            public void Execute()
            {
                foreach (var newFlyby in New)
                {
                    var positional = PositionalFromEntity[newFlyby];

                    var speed = positional.PrevDist - positional.CurrentDist;
                    speed /= Dt;

                    // Discard slow fly bys.
                    if (speed < Params.MinSpeed)
                    {
                        ECB.DestroyEntity(newFlyby);
                        continue;
                    }

                    bool isFast = speed > Params.Fast;

                    var intensity = (speed - Params.MinSpeed) / (Params.MaxSpeed - Params.MinSpeed);
                    var pitch = 0.5f + intensity * 0.5f;
                    var layerSpecificVolume = isFast ? Params.HighLayer : Params.LowLayer;

                    pitch *= (isFast ? Params.FastPitchMul : 1);


                    // allocate player.

                    var freeList = isFast ? HighFreeList : LowFreeList;
                    var otherList = !isFast ? HighFreeList : LowFreeList;

                    if (freeList.Length == 0)
                    {
                        // steal from other queue.
                        freeList = otherList;

                        if (freeList.Length == 0)
                            break;
                    }


                    int freeIndex = Rand.NextInt(freeList.Length - 1);

                    int playerIndex = freeList[freeIndex];

                    var sample = CurrentSamples[playerIndex];

                    var distanceAttenuation = Params.Volume * math.min(0.5f, 1 / math.sqrt(math.max(1, positional.CurrentDist - 3)));

                    //Debug.Log($"[{playerIndex}] Creating ({isFast}) player with speed {speed}, intensity {intensity}, distance {positional.CurrentDist}, attenuation {distanceAttenuation}, pitch {pitch}");

                    ECB.AddComponent(newFlyby,
                        new FlyByState
                        {
                            Timeout = sample.Length / pitch,
                            SampleIndex = playerIndex
                        }
                    );

                    //Debug.Log("Final volume: " + math.sqrt(intensity) * distanceAttenuation);

                    ECB.AddComponent(sample.PlayerEntity,
                        new SamplePlayback
                        {
                            Left = positional.Left,
                            Right = positional.Right,
                            Pitch = pitch,
                            Volume = layerSpecificVolume * math.min(1, math.sqrt(intensity)) * distanceAttenuation
                        }
                    );

                    freeList.RemoveAtSwapBack(freeIndex);
                }

                foreach (var deadFlyby in Dead)
                {
                    //Debug.Log($"[{Dead.States[i].SampleIndex}] Dead player");

                    ECB.RemoveComponent(deadFlyby, ComponentType.ReadWrite<FlyByState>());
                }
            }
        }

        [BurstCompile]
        struct TickCurrents : IJob
        {
            [ReadOnly] public ChunkEntityEnumerable Alive;

            [ReadOnly] public ComponentDataFromEntity<PositionalFlyByData> PositionalFromEntity;

            [ReadOnly] public NativeList<SampleInfo> CurrentSamples;

            public ComponentDataFromEntity<FlyByState> StateFromEntity;
            public ComponentDataFromEntity<SamplePlayback> PlaybackFromEntity;

            public NativeList<int> HighFreeList;
            public NativeList<int> LowFreeList;

            public EntityCommandBuffer ECB;

            public float Dt;

            public ComponentType SamplePlaybackType;

            public void Execute()
            {
                foreach (var aliveFlyby in Alive)
                {
                    var flyby = StateFromEntity[aliveFlyby];

                    flyby.Timeout -= Dt;

                    if (flyby.Timeout > 0)
                    {
                        var playerEntity = CurrentSamples[flyby.SampleIndex].PlayerEntity;

                        // update sample playback parameters
                        var playback = PlaybackFromEntity[playerEntity];
                        var positional = PositionalFromEntity[aliveFlyby];
                        playback.Left = positional.Left;
                        playback.Right = positional.Right;
                        PlaybackFromEntity[playerEntity] = playback;
                    }
                    else
                    {
                        ECB.DestroyEntity(aliveFlyby);
                        ECB.RemoveComponent(CurrentSamples[flyby.SampleIndex].PlayerEntity, SamplePlaybackType);

                        if(CurrentSamples[flyby.SampleIndex].Intensity > 0)
                            HighFreeList.Add(flyby.SampleIndex);
                        else
                            LowFreeList.Add(flyby.SampleIndex);
                    }

                    StateFromEntity[aliveFlyby] = flyby;
                }
            }
        }
    }
}
