//#define USE_OCCUPANCY_DEBUG
//#define USE_DEBUG_LINES // Enable here and at the top of VehicleMovementJob.cs

using System.Collections.Generic;
using Traffic.Pathing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;

namespace Traffic.Simulation
{
    [AlwaysUpdateSystem]
    public sealed partial class TrafficSystem : JobComponentSystem
    {
        public NativeArray<RoadSection> roadSections;
        bool doneOneTimeInit = false;
        private ComponentGroup m_CarGroup;

        private int numSections = 0;

        public TrafficSettingsData trafficSettings;
        public NativeArray<VehiclePrefabData> vehiclePool;

        // This is not the best way for ECS to store the player, it would be better to have component data for it
        private GameObject _player;
        private Rigidbody _playerRidigbody; // Will only be on the player controlled car

        void OneTimeSetup()
        {
            var allRoads = GetComponentGroup(typeof(RoadSection)).ToComponentDataArray<RoadSection>(Allocator.TempJob);
            var settings = GetComponentGroup(typeof(TrafficSettingsData)).ToComponentDataArray<TrafficSettingsData>(Allocator.TempJob);
            var vehicles = GetComponentGroup(typeof(VehiclePrefabData)).ToComponentDataArray<VehiclePrefabData>(Allocator.TempJob);
            
            
            if (settings.Length == 0 || vehicles.Length == 0 || allRoads.Length == 0)
            {
                allRoads.Dispose();
                vehicles.Dispose();
                settings.Dispose();
                return;
            }

            trafficSettings = settings[0];

            // Copy the vehicle pool for prefabs
            vehiclePool = new NativeArray<VehiclePrefabData>(vehicles.Length, Allocator.Persistent);
            for (int v = 0; v < vehicles.Length; v++)
            {
                if (!EntityManager.HasComponent<VehiclePathing>(vehicles[v].VehiclePrefab))
                {
                    EntityManager.AddComponentData(vehicles[v].VehiclePrefab, new VehiclePathing());
                }

                if (!EntityManager.HasComponent<VehicleTargetPosition>(vehicles[v].VehiclePrefab))
                {
                    EntityManager.AddComponentData(vehicles[v].VehiclePrefab, new VehicleTargetPosition());
                }

                if (!EntityManager.HasComponent<VehiclePhysicsState>(vehicles[v].VehiclePrefab))
                {
                    EntityManager.AddComponentData(vehicles[v].VehiclePrefab, new VehiclePhysicsState());
                }

                vehiclePool[v] = vehicles[v];
            }
            
            // for now just copy everything
            roadSections = new NativeArray<RoadSection>(allRoads.Length, Allocator.Persistent);

            for (int a = 0; a < allRoads.Length; a++)
            {
                roadSections[allRoads[a].sortIndex] = allRoads[a];
            }

            numSections = roadSections.Length;
          
            
#if UNITY_EDITOR && USE_OCCUPANCY_DEBUG

            OccupancyDebug.queueSlots = new NativeArray<Occupation>(numSections * Constants.RoadIndexMultiplier, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            OccupancyDebug.roadSections = new NativeArray<RoadSection>(numSections, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int a = 0; a < roadSections.Length; a++)
            {
                OccupancyDebug.roadSections[a] = roadSections[a];
            }

#endif
            doneOneTimeInit = true;
            allRoads.Dispose();
            vehicles.Dispose();
            settings.Dispose();
        }

        protected override void OnCreateManager()
        {
            base.OnCreateManager();
            
            m_CarGroup = GetComponentGroup(ComponentType.ReadOnly<VehiclePhysicsState>());
            _SpawnBarrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            _DespawnBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();

            // TODO: Should size this dynamically
            _Cells = new NativeMultiHashMap<int, VehicleCell>(30000, Allocator.Persistent);
            _VehicleMap = new NativeMultiHashMap<int, VehicleSlotData>(30000, Allocator.Persistent);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (!doneOneTimeInit)
            {
                OneTimeSetup();

                return inputDeps;
            }
            
            if (vehiclePool.Length == 0 || roadSections.Length == 0)
                return inputDeps;

            #if UNITY_EDITOR && USE_DEBUG_LINES
            var debugLines = _DebugLines.Lines.ToConcurrent();
            #endif

            var queueSlots = new NativeArray<Occupation>(numSections * Constants.RoadIndexMultiplier, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            // Setup job dependencies
            JobHandle clearDeps = new ClearArrayJob<Occupation>
            {
                Data = queueSlots,
            }.Schedule(queueSlots.Length, 512);

            var clearHash2Job = new ClearHashJob<VehicleSlotData> {Hash = _VehicleMap}.Schedule();

            // Move vehicles along path, compute banking
            JobHandle pathingDeps = new VehiclePathUpdate { RoadSections = roadSections, DeltaTimeSeconds = Time.deltaTime * trafficSettings.GlobalSpeedFactor }.Schedule(this, JobHandle.CombineDependencies(clearDeps, inputDeps));
            // Move vehicles that have completed their curve to the next curve (or an off ramp)
            JobHandle pathLinkDeps = new VehiclePathLinkUpdate { RoadSections = roadSections }.Schedule(this, pathingDeps);
            // Move from lane to lane. PERF: Opportunity to not do for every vehicle.
            JobHandle lanePositionDeps = new VehicleLanePosition { RoadSections = roadSections, DeltaTimeSeconds = Time.deltaTime }.Schedule(this, pathLinkDeps);

            float3 playerPosition = default;
            float3 playerVelocity = default;
            if (_player != null)
            {
                playerPosition = _player.transform.position;
                if (_playerRidigbody != null)
                {
                    playerVelocity = _playerRidigbody.velocity;
                }
            }

            // Compute what cells (of the 16 for each road section) is covered by each vehicle
            JobHandle occupationAliasingDeps = new OccupationAliasing {OccupancyToVehicleMap = _VehicleMap.ToConcurrent(), RoadSections = roadSections}.Schedule(this, JobHandle.CombineDependencies(clearHash2Job, clearDeps, lanePositionDeps));
            JobHandle occupationFillDeps = new OccupationFill2 {Occupations = queueSlots}.Schedule(_VehicleMap, 32, occupationAliasingDeps);

            // Back-fill the information:
            // |   A      B     |
            // |AAAABBBBBBB     |
            JobHandle occupationGapDeps = new OccupationGapFill { Occupations = queueSlots }.Schedule(roadSections.Length, 16, occupationFillDeps);
            occupationGapDeps = new OccupationGapAdjustmentJob {Occupations = queueSlots, RoadSections = roadSections}.Schedule(roadSections.Length, 32, occupationGapDeps);
            occupationGapDeps = new OccupationGapFill2 {Occupations = queueSlots}.Schedule(roadSections.Length, 16, occupationGapDeps);

            // Sample occupation ahead of each vehicle and slow down to not run into cars in front
            // Also signal if a lane change is wanted.
            JobHandle moderatorDeps = new VehicleSpeedModerate { Occupancy = queueSlots, RoadSections = roadSections, DeltaTimeSeconds = Time.deltaTime}.Schedule(this, occupationGapDeps);

            // Pick concrete new lanes for cars switching lanes
            JobHandle laneSwitchDeps = new LaneSwitch { Occupancy = queueSlots, RoadSections = roadSections}.Schedule(this, moderatorDeps);

            // Despawn cars that have run out of road
            JobHandle despawnDeps = new VehicleDespawnJob { EntityCommandBuffer = _DespawnBarrier.CreateCommandBuffer().ToConcurrent() }.Schedule(this, laneSwitchDeps);
            _DespawnBarrier.AddJobHandleForProducer(despawnDeps);

            JobHandle spawnDeps;

            var carCount = m_CarGroup.CalculateLength();
            if (carCount < trafficSettings.MaxCars)
            {
                // Spawn new cars
                spawnDeps = new VehicleSpawnJob
                {
                    VehiclePool = vehiclePool,
                    RoadSections = roadSections,
                    Occupation = queueSlots,
                    EntityCommandBuffer = _SpawnBarrier.CreateCommandBuffer().ToConcurrent()
                }.Schedule(this,occupationGapDeps);
                
                _SpawnBarrier.AddJobHandleForProducer(spawnDeps);
            }
            else
            {
                spawnDeps = occupationGapDeps;
            }
            
            

#if UNITY_EDITOR && USE_OCCUPANCY_DEBUG

            spawnDeps.Complete();
            laneSwitchDeps.Complete();

            for (int a = 0; a < queueSlots.Length; a++)
            {
                OccupancyDebug.queueSlots[a] = queueSlots[a];
            }
#endif
            JobHandle finalDeps = default;

            float3 camPos = default;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                camPos = mainCamera.transform.position;
            }

            JobHandle movementDeps = JobHandle.CombineDependencies(spawnDeps, despawnDeps);
            
            int stepsTaken = 0;
            float timeStep = 1.0f / 60.0f;

            _TransformRemain += Time.deltaTime;

            while (_TransformRemain >= timeStep)
            {
                var clearHashJob = new ClearHashJob<VehicleCell> {Hash = _Cells}.Schedule(movementDeps);

                var hashJob = new VehicleHashJob {CellMap = _Cells.ToConcurrent()}.Schedule(this, clearHashJob);

                hashJob = new PlayerHashJob {CellMap = _Cells, Pos = playerPosition, Velocity = playerVelocity}.Schedule(hashJob);

                movementDeps = new VehicleMovementJob
                {
                    TimeStep = timeStep,
                    Cells = _Cells,
#if UNITY_EDITOR && USE_DEBUG_LINES
                    DebugLines = debugLines
#endif
                }.Schedule(this, hashJob);

                _TransformRemain -= timeStep;
                ++stepsTaken;
            }

            JobHandle finalPosition;

            if (stepsTaken > 0)
            {
                JobHandle finalTransform = new VehicleTransformJob {dt = timeStep, CameraPos = camPos}.Schedule(this, movementDeps);

                finalPosition = finalTransform;
            }
            else
            {
                finalPosition = movementDeps;
            }

            finalDeps = finalPosition;


            // Get rid of occupation data
            JobHandle disposeJob = new DisposeArrayJob<Occupation>
            {
                Data = queueSlots
            }.Schedule(JobHandle.CombineDependencies(spawnDeps, laneSwitchDeps));

            return JobHandle.CombineDependencies(disposeJob, finalDeps);
        }

        private float _TransformRemain;
        private NativeMultiHashMap<int, VehicleCell> _Cells;
        private NativeMultiHashMap<int, VehicleSlotData> _VehicleMap;

        private BeginSimulationEntityCommandBufferSystem _SpawnBarrier;
        private EndSimulationEntityCommandBufferSystem _DespawnBarrier;

#if UNITY_EDITOR && USE_DEBUG_LINES
        [Inject] private DebugLineSystem _DebugLines;
#endif

        protected override void OnDestroyManager()
        {
            if (doneOneTimeInit)
            {
                roadSections.Dispose();
                vehiclePool.Dispose();
#if UNITY_EDITOR && USE_OCCUPANCY_DEBUG

                OccupancyDebug.queueSlots.Dispose();
                OccupancyDebug.roadSections.Dispose();

#endif
            }

            _VehicleMap.Dispose();
            _Cells.Dispose();
        }

        public void SetPlayerReference(GameObject player)
        {
            _player = player;
            var rigid = _player.GetComponent<Rigidbody>();
            if (rigid != null)
            {
                _playerRidigbody = rigid;
            }
        }
    }
}
