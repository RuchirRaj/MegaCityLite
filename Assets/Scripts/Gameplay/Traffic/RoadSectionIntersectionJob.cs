using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Traffic.Simulation
{
    partial class TrafficSystem
    {
        [BurstCompile]
        struct RoadSectionIntersectionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;
            [WriteOnly] public NativeArray<int> RoadSectionsToCheck;
            [ReadOnly] public float4 IntersectionPositionRadiusSq;

            float SqDistance(float3 sphere, float3 minBounds, float3 maxBounds)
            {
                bool3 lessMin = sphere < minBounds;
                bool3 grtMax = sphere > maxBounds;

                float3 lessSqDist = (minBounds - sphere) * (minBounds - sphere);
                float3 grtSqDist = (sphere - maxBounds) * (sphere - maxBounds);

                float3 sqDist = math.select(new float3(0,0,0), lessSqDist, lessMin);
                sqDist = math.select(sqDist, grtSqDist, grtMax);

                return math.csum(sqDist);
            }

            public void Execute(int index)
            {
                RoadSection rs = RoadSections[index];

                // Compute RoadSection Bounds
                float3 minBounds = math.min(rs.p1, rs.p2);
                float3 maxBounds = math.max(rs.p1, rs.p2);

                // Enqueue road sections that overlap
                float sqDistance = SqDistance(IntersectionPositionRadiusSq.xyz, minBounds, maxBounds);

                bool overlap = (sqDistance <= IntersectionPositionRadiusSq.w);

                RoadSectionsToCheck[index] = overlap ? index : -1;
            }
        }
    }
}
