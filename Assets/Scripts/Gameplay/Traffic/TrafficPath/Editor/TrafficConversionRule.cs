using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Traffic.Simulation;
using UnityEngine;

namespace Traffic.Pathing
{
    class TrafficPathConversionSystem : GameObjectConversionSystem
    {
        public int RIndex = 0;
        public Dictionary<int,int> PathMap = new Dictionary<int, int>();
        public Dictionary<float3, int> RampMap = new Dictionary<float3, int>();
        public Dictionary<float3, int> MergeMap = new Dictionary<float3, int>();
        public Dictionary<int, Entity> IdxMap = new Dictionary<int, Entity>();
        
        protected override void OnUpdate()
        {
            Entities.ForEach((Path path) =>
            {
                var entity = GetPrimaryEntity(path);
                
                int numPathNodes = path.GetNumNodes();
                PathMap.Add(path.GetInstanceID(), RIndex);
                
                RoadSettings roadSettings = path.gameObject.GetComponentInParent<RoadSettings>();
                
                uint spawnPool = 0;
                if (roadSettings != null)
                    spawnPool = roadSettings.vehicleSelection;
    
                for (int n = 1; n < numPathNodes; n++)
                {
                    var rs = new RoadSection();
                    
                    path.GetSplineSection(n - 1, out rs.p0, out rs.p1, out rs.p2, out rs.p3);
                    rs.arcLength = CatmullRom.ComputeArcLength(rs.p0, rs.p1, rs.p2, rs.p3, 1024);

                    rs.vehicleHalfLen = Constants.VehicleLength / rs.arcLength;
                    rs.vehicleHalfLen /= 2;

                    rs.sortIndex = RIndex;
                    rs.linkNext = RIndex + 1;
                    if (n == numPathNodes - 1)
                    {
                        rs.linkNext = -1;
                        MergeMap[math.round ((path.GetReversibleRawPosition(n)+new float3(path.transform.position)) * Constants.NodePositionRounding)/Constants.NodePositionRounding]=RIndex;
                    }
    
                    rs.linkExtraChance = 0.0f;
                    rs.linkExtra = -1;
    
                    rs.width = path.width;
                    rs.height = path.height;
    
                    rs.minSpeed = path.minSpeed;
                    rs.maxSpeed = path.maxSpeed;
    
                    rs.occupationLimit = math.min(Constants.RoadOccupationSlotsMax, (int)math.round(rs.arcLength / Constants.VehicleLength));

                    var sectionEnt = CreateAdditionalEntity(path);
                    
                    IdxMap[RIndex] = sectionEnt;
                    
                    if (!path.isOnRamp)
                    {
                        RampMap[math.round((path.GetReversibleRawPosition(n - 1) + new float3(path.transform.position))*Constants.NodePositionRounding)/Constants.NodePositionRounding] = RIndex;
                        if (n == 1)
                        {
                            int x = 2; // Only spawn in right lane
                            float t = rs.vehicleHalfLen;
                            float pathTime = (n - 1) + t;

                            var spawner = new Spawner();
                            spawner.Time = math.frac(pathTime);

                            spawner.Direction = math.normalize(path.GetTangent(pathTime));

                            float3 rightPos = (x - 1) * math.mul(spawner.Direction, Vector3.right) * ((rs.width-Constants.VehicleWidth) / 2.0f);
                            spawner.Position = path.GetWorldPosition(pathTime) + rightPos;

                            spawner.RoadIndex = RIndex;
                            
                            spawner.minSpeed = path.minSpeed;
                            spawner.maxSpeed = path.maxSpeed;
                            
                            var speedInverse = 1.0f / spawner.minSpeed;

                            spawner.random = new Unity.Mathematics.Random((uint)RIndex + 1);
                            spawner.delaySpawn = (int) Constants.VehicleLength + spawner.random.NextInt((int) (speedInverse * 60.0f),
                                                     (int) (speedInverse * 120.0f));

                            spawner.LaneIndex = x;
                            spawner.poolSpawn = spawnPool;

                            // Each path will only have one spawner, so use the Primary Entity
                            DstEntityManager.AddComponentData(entity, spawner);
                        }
                    }
                    
                    RIndex++;
                    DstEntityManager.AddComponentData(sectionEnt, rs);
                }
            });
            
            // Loop over the paths again to start building the ramps and merges
            Entities.ForEach((Path path) =>
            {
                int numPathNodes = path.GetNumNodes();
                
                // Handle On Ramp roads
                if(path.isOnRamp)
                {
                    int rsRampIdx = PathMap[path.GetInstanceID()];
                    float3 rampEntry = math.round((path.GetReversibleRawPosition(0)+new float3(path.transform.position))*Constants.NodePositionRounding)/Constants.NodePositionRounding;
                    if (RampMap.ContainsKey(rampEntry))
                    {
                        int rsIndex = RampMap[rampEntry];
                        if (rsIndex > 0)
                        {
                            rsIndex -= 1;
                            if (IdxMap.ContainsKey(rsIndex))
                            {
                                Entity ramp = IdxMap[rsIndex];
                                RoadSection rs = DstEntityManager.GetComponentData<RoadSection>(ramp);
                                if (rs.linkNext == rsIndex + 1)
                                {
                                    rs.linkExtra = rsRampIdx;
                                    rs.linkExtraChance = path.percentageChanceForOnRamp/100.0f;
                                    DstEntityManager.SetComponentData(ramp, rs);
                                }
                            }
                        }
                    }
                }
                
                // Handle merging roads
                {
                    int n = 0;

                    float3 pos =
                        math.round((path.GetReversibleRawPosition(n) + new float3(path.transform.position)) *
                                   Constants.NodePositionRounding) / Constants.NodePositionRounding;
                    if (MergeMap.ContainsKey(pos))
                    {
                        int mergeFromIndex = MergeMap[pos];
                        int mergeToBase = PathMap[path.GetInstanceID()];

                        if (mergeFromIndex > 0 && mergeFromIndex != mergeToBase + n)
                        {
                            Entity merge = IdxMap[mergeFromIndex];
                            RoadSection rs = DstEntityManager.GetComponentData<RoadSection>(merge);
                            if (rs.linkNext == -1)
                            {
                                rs.linkNext = mergeToBase + n;
                                DstEntityManager.SetComponentData(merge,rs);
                            }
                        }
                    }
                }
            });
            
            RIndex = 0;
            PathMap.Clear();
            RampMap.Clear();
            MergeMap.Clear();
            IdxMap.Clear();
        }
    }
}
