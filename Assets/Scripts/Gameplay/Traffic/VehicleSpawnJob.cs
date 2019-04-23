using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        public struct VehicleSpawnJob : IJobProcessComponentDataWithEntity<Spawner>
        {
            public EntityCommandBuffer.Concurrent EntityCommandBuffer;
            [ReadOnly] public NativeArray<RoadSection> RoadSections;
            [ReadOnly] public NativeArray<Occupation> Occupation;
            [ReadOnly] public NativeArray<VehiclePrefabData> VehiclePool;

            public static int vehicleUID = 0;

            bool Occupied(int start, int end, int rI, int lI)
            {
                int baseOffset = rI * Constants.RoadIndexMultiplier + lI;
                start *= Constants.RoadLanes;
                end *= Constants.RoadLanes;

                for (int a = start; a <= end; a += Constants.RoadLanes)
                {
                    if (Occupation[baseOffset + a].occupied != 0)
                        return true;
                }

                return false;
            }

            public int GetSpawnVehicleIndex(ref Unity.Mathematics.Random random, uint poolSpawn)
            {
                if (poolSpawn==0)
                    return random.NextInt(0, VehiclePool.Length);

                // Otherwise we need to figure out which vehicle to assign
                // Todo: could bake the num set bits out!
                uint pool = poolSpawn;
                uint numSetBits = poolSpawn - ((poolSpawn >> 1) & 0x55555555);
                numSetBits = (numSetBits & 0x33333333) + ((numSetBits >> 2) & 0x33333333);
                numSetBits = ((numSetBits + (numSetBits >> 4) & 0x0F0F0F0F) * 0x01010101) >> 24;

                // we now have a number between 0 & 32,
                int chosenBitIdx = random.NextInt(0, (int)numSetBits)+1;
                uint poolTemp = poolSpawn;
                uint lsb = poolTemp;
                //TODO: make the below better?
                while(chosenBitIdx>0)
                {
                    lsb = poolTemp;
                    poolTemp &= poolTemp - 1;    // clear least significant set bit
                    lsb ^= poolTemp;             // lsb contains the index (1<<index) of the pool for this position
                    chosenBitIdx--;
                }

                float fidx = math.log2(lsb);

                return (int) (fidx);
            }

            public void Execute(Entity entity, int index, ref Spawner thisSpawner)
            {
                if (thisSpawner.delaySpawn > 0)
                    thisSpawner.delaySpawn--;
                else
                {
                    RoadSection rs = RoadSections[thisSpawner.RoadIndex];
                    Interlocked.Increment(ref vehicleUID);

                    float backOfVehiclePos = thisSpawner.Time - rs.vehicleHalfLen;
                    float frontOfVehiclePos = thisSpawner.Time + rs.vehicleHalfLen;

                    int occupationIndexStart = math.max(0,
                        (int) (math.floor(backOfVehiclePos * rs.occupationLimit)));
                    int occupationIndexEnd = math.min(rs.occupationLimit - 1,
                        (int) (math.floor(frontOfVehiclePos * rs.occupationLimit)));

                    if (!Occupied(occupationIndexStart, occupationIndexEnd, thisSpawner.RoadIndex,
                        thisSpawner.LaneIndex))
                    {
                        int vehiclePoolIndex = GetSpawnVehicleIndex(ref thisSpawner.random,thisSpawner.poolSpawn);
                        float speedMult = VehiclePool[vehiclePoolIndex].VehicleSpeed;
                        float speedRangeSelected = thisSpawner.random.NextFloat(0.0f, 1.0f);
                        float initialSpeed = 0.0f;

                        var vehicleEntity = EntityCommandBuffer.Instantiate(index, VehiclePool[vehiclePoolIndex].VehiclePrefab);
                        EntityCommandBuffer.SetComponent(index, vehicleEntity, new VehiclePathing
                        {
                            vehicleId = vehicleUID,
                            RoadIndex = thisSpawner.RoadIndex, LaneIndex = (byte) thisSpawner.LaneIndex,
                            WantedLaneIndex = (byte) thisSpawner.LaneIndex, speed = initialSpeed,
                            speedRangeSelected = speedRangeSelected, speedMult = speedMult,
                            targetSpeed = initialSpeed, curvePos = thisSpawner.Time,
                            random = new Unity.Mathematics.Random(thisSpawner.random.NextUInt(1, uint.MaxValue))
                        }); 
                        var heading = CatmullRom.GetTangent(rs.p0, rs.p1, rs.p2, rs.p3, 0.0f);

                        EntityCommandBuffer.SetComponent(index, vehicleEntity, new VehicleTargetPosition { IdealPosition = thisSpawner.Position });
                        EntityCommandBuffer.SetComponent(index, vehicleEntity, new VehiclePhysicsState { Position = thisSpawner.Position, Heading = heading, SpeedMult = speedMult});
                    }

                    var speedInverse = 1.0f / thisSpawner.minSpeed;

                    thisSpawner.delaySpawn = (int) Constants.VehicleLength + thisSpawner.random.NextInt((int)(speedInverse * 10.0f), (int)(speedInverse * 120.0f));
                }
            }
        }
    }
}

