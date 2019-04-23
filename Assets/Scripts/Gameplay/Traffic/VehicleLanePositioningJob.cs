using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct VehicleLanePosition : IJobProcessComponentData<VehiclePathing, VehicleTargetPosition>
        {
            public float DeltaTimeSeconds;
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            public float3 GetOffsetFromLaneIndex(int laneIndex, quaternion direction, float w)
            {
                float3 right = math.mul(direction, new float3(1, 0, 0));

                return right * (laneIndex - 1f) * ((w-Constants.VehicleWidth) / 2.0f);
            }

            public void Execute(ref VehiclePathing p, ref VehicleTargetPosition pos)
            {
                RoadSection rs = RoadSections[p.RoadIndex];

                quaternion direction = CatmullRom.GetOrientation(rs.p0, rs.p1, rs.p2, rs.p3, p.curvePos);

                float3 currentLane = GetOffsetFromLaneIndex(p.LaneIndex, direction, rs.width);
                float3 destinationLane = GetOffsetFromLaneIndex(p.WantedLaneIndex, direction, rs.width);

                pos.IdealPosition += math.lerp(currentLane, destinationLane, math.smoothstep(0, 1, p.LaneTween));

                if (p.LaneIndex != p.WantedLaneIndex)
                {
                    p.LaneTween += DeltaTimeSeconds * p.speed * 0.1f;
                    if (p.LaneTween >= 1.0f)
                    {
                        p.LaneIndex = p.WantedLaneIndex;
                        p.LaneTween = 0.0f;
                    }
                }
            }
        }
    }
}

