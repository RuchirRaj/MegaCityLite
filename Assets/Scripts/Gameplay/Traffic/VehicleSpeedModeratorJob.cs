using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct VehicleSpeedModerate : IJobProcessComponentData<VehiclePathing>
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;
            [ReadOnly] public NativeArray<Occupation> Occupancy;
            public float DeltaTimeSeconds;

            public void Execute(ref VehiclePathing vehicle)
            {
                int rI = vehicle.RoadIndex;
                int lI = vehicle.LaneIndex;
                RoadSection rs = RoadSections[rI];

                float frontOfVehiclePos = vehicle.curvePos + rs.vehicleHalfLen;

                // Look ahead one slot
                int slot = (int)(math.floor(frontOfVehiclePos * rs.occupationLimit)) + 1;

                if (slot >= rs.occupationLimit)
                {
                    if (rs.linkNext != -1)
                    {
                        rI = rs.linkNext;
                        rs = RoadSections[rI];
                        slot = 0;
                    }
                    else
                    {
                        --slot;
                    }
                }

                int sampleIndex = rI * Constants.RoadIndexMultiplier + slot * Constants.RoadLanes + lI;

                float wantedSpeed = vehicle.speedMult * math.lerp(rs.minSpeed, rs.maxSpeed,vehicle.speedRangeSelected);

                vehicle.WantNewLane = 0;

                vehicle.targetSpeed = math.min(wantedSpeed, Occupancy[sampleIndex].speed);

                var lerpAmount = DeltaTimeSeconds < 1.0f ? DeltaTimeSeconds : 1.0f;
                vehicle.speed = math.lerp(vehicle.speed, vehicle.targetSpeed, lerpAmount);

                if (math.abs(vehicle.targetSpeed - wantedSpeed) > 0.10f * wantedSpeed)
                {
                    vehicle.WantNewLane = 1;
                }
            }
        }
    }
}
