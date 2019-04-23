using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

// For each Occupancy, fill from end to front the speeds if not occupied - maybe not a good idea..

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct OccupationGapAdjustmentJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            [NativeDisableParallelForRestriction]
            public NativeArray<Occupation> Occupations;

            public void Execute(int roadIndex)
            {
                var rs = RoadSections[roadIndex];

                if (rs.linkNext < 0)
                    return;

                for (int lane = 0; lane < Constants.RoadLanes; lane++)
                {
                    int dstSlot = roadIndex * Constants.RoadIndexMultiplier + (rs.occupationLimit - 1) * Constants.RoadLanes + lane;
                    int srcSlot = rs.linkNext * Constants.RoadIndexMultiplier + lane;

                    Occupation src = Occupations[srcSlot];
                    Occupation dst = Occupations[dstSlot];

                    if (dst.occupied == 0)
                    {
                        dst.speed = src.speed;
                        dst.occupied = src.occupied;
                    }
                    else
                    {
                        dst.speed = math.min(src.speed, dst.speed);
                        dst.occupied = math.min(src.occupied, dst.occupied);
                    }

                    Occupations[dstSlot] = dst;
                }
            }

        }

        [BurstCompile]
        public struct OccupationGapFill : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Occupation> Occupations;

            public void Execute(int index)
            {
                for (int lane = 0; lane < Constants.RoadLanes; lane++)
                {
                    int baseSlot = index * Constants.RoadIndexMultiplier + lane;
                    float lastSpeed = float.MaxValue;
                    for (int occ = (Constants.RoadOccupationSlotsMax - 1) * Constants.RoadLanes;
                        occ >= 0;
                        occ -= Constants.RoadLanes)
                    {
                        Occupation occupation = Occupations[baseSlot + occ];
                        if (occupation.occupied != 0)
                        {
                            lastSpeed = occupation.speed;
                        }
                        else
                        {
                            occupation.speed = lastSpeed;
                            lastSpeed += 0.1f;
                            Occupations[baseSlot + occ] = occupation;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        public struct OccupationGapFill2 : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Occupation> Occupations;

            public void Execute(int index)
            {
                for (int lane = 0; lane < Constants.RoadLanes; lane++)
                {
                    int baseSlot = index * Constants.RoadIndexMultiplier + lane;
                    for (int occ = (Constants.RoadOccupationSlotsMax - 2) * Constants.RoadLanes;
                        occ >= 0;
                        occ -= Constants.RoadLanes)
                    {
                        Occupation src = Occupations[baseSlot + occ + 1];
                        Occupation dst = Occupations[baseSlot + occ];
                        dst.speed = math.min(src.speed, dst.speed);
                        Occupations[baseSlot + occ] = dst;
                    }
                }
            }
        }
    }
}
