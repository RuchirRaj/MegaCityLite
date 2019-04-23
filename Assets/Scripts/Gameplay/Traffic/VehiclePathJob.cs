using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Mathematics.math;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        public const float kMaxTether = 50.0f;
        public const float kMaxTetherSquared = kMaxTether * kMaxTether;

        [BurstCompile]
        public struct VehiclePathUpdate : IJobProcessComponentData<VehiclePathing, VehicleTargetPosition, VehiclePhysicsState>
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;
            public float DeltaTimeSeconds;

            public void Execute(ref VehiclePathing p, ref VehicleTargetPosition pos, [ReadOnly] ref VehiclePhysicsState physicsState)
            {
                var rs = RoadSections[p.RoadIndex];

                float3 c0 = CatmullRom.GetPosition(rs.p0, rs.p1, rs.p2, rs.p3, p.curvePos);
                float3 c1 = CatmullRom.GetTangent(rs.p0, rs.p1, rs.p2, rs.p3, p.curvePos);
                float3 c2 = CatmullRom.GetConcavity(rs.p0, rs.p1, rs.p2, rs.p3, p.curvePos);

                float curveSpeed = length(c1);

                pos.IdealPosition = c0;
                pos.IdealSpeed = p.speed;

                if (lengthsq(physicsState.Position - c0) < kMaxTetherSquared)
                {
                    p.curvePos += Constants.VehicleSpeedFudge / rs.arcLength * p.speed / curveSpeed * DeltaTimeSeconds;
                }
            }
        }
    }
}
