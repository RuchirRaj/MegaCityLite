using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct VehiclePathLinkUpdate : IJobProcessComponentData<VehiclePathing>
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            public void Execute(ref VehiclePathing p)
            {
                var rs = RoadSections[p.RoadIndex];
                if (p.curvePos >= 1.0f)
                {
                    float chanceForExtra = p.random.NextFloat(0.0f, 1.0f);
                    if (rs.linkExtra >= 0 && chanceForExtra < rs.linkExtraChance)
                    {
                        p.RoadIndex = rs.linkExtra;
                        p.curvePos = 0.0f;
                    }
                    else
                    {
                        if (rs.linkNext >= 0)
                        {
                            p.RoadIndex = rs.linkNext;
                            p.curvePos = 0.0f;
                        }
                    }
                }
            }
        }
    }
}