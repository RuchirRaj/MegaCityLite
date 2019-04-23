using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Traffic.Simulation
{

    public partial class TrafficSystem
    {
        public struct VehicleSlotData
        {
            public float Speed;
            public int Id;
        }

        [BurstCompile]
        public struct OccupationAliasing : IJobProcessComponentData<VehiclePathing>
        {
            public NativeMultiHashMap<int, VehicleSlotData>.Concurrent OccupancyToVehicleMap;
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            private int CurvePositionToOccupancyIndex(int roadIndex, int laneIndex, float curvePos)
            {
                var rs = RoadSections[roadIndex];

                if (curvePos >= 1.0)
                {
                    if (rs.linkNext != -1)
                    {
                        roadIndex = rs.linkNext;
                        rs = RoadSections[roadIndex];
                        curvePos = 0.0f;
                    }
                }

                // This is unfortunate. Would need linkPrev.
                int slot = math.min(math.max(0, (int) (math.floor(curvePos * rs.occupationLimit))), rs.occupationLimit - 1);
                return Constants.RoadIndexMultiplier * roadIndex + slot * Constants.RoadLanes + laneIndex;
            }

            private void OccupyLane(ref VehiclePathing vehicle, ref RoadSection rs, int laneIndex)
            {
                int i0 = CurvePositionToOccupancyIndex(vehicle.RoadIndex, laneIndex, vehicle.curvePos - rs.vehicleHalfLen);
                int i1 = CurvePositionToOccupancyIndex(vehicle.RoadIndex, laneIndex, vehicle.curvePos + rs.vehicleHalfLen);

                var d = new VehicleSlotData {Speed = vehicle.speed, Id = vehicle.vehicleId};

                OccupancyToVehicleMap.Add(i0, d);
                if (i0 != i1)
                    OccupancyToVehicleMap.Add(i1, d);
            }

            public void Execute(ref VehiclePathing vehicle)
            {
                RoadSection rs = RoadSections[vehicle.RoadIndex];

                OccupyLane(ref vehicle, ref rs, vehicle.LaneIndex);
                if (vehicle.LaneIndex != vehicle.WantedLaneIndex)
                    OccupyLane(ref vehicle, ref rs, vehicle.WantedLaneIndex);
            }
        }

        [BurstCompile]
        public struct OccupationFill2 : IJobNativeMultiHashMapVisitKeyValue<int, VehicleSlotData>
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<Occupation> Occupations;

            public void ExecuteNext(int i, VehicleSlotData d)
            {
                var o = Occupations[i];

                if (o.occupied != 0)
                {
                    o.occupied = math.min(o.occupied, d.Id);
                    o.speed = math.min(o.speed, d.Speed);
                }
                else
                {
                    o.occupied = d.Id;
                    o.speed = d.Speed;
                }

                Occupations[i] = o;
            }
        }

        [BurstCompile]
        public struct OccupationFill : IJobProcessComponentData<VehiclePathing>
        {
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Occupation> Occupations;

            void FillOccupation(int startIndex, int endIndex, int rI, int lI, float speed, int vid, int linkNext,int occLimit)
            {
                int baseSlot = rI * Constants.RoadIndexMultiplier + lI;
                startIndex *= Constants.RoadLanes;
                if (linkNext>=0 && endIndex >= occLimit)
                {
                    // Fill correct occupation slot in potentially distant road
                    int nStart = 0;
                    int nBase = linkNext * Constants.RoadIndexMultiplier + lI;
                    int end = endIndex - occLimit;
                    end *= Constants.RoadLanes;
                    for (int a = nStart; a <= end; a += Constants.RoadLanes)
                    {
                        Occupations[nBase + a] = new Occupation {occupied = vid, speed = speed};
                    }
                    endIndex = RoadSections[linkNext].occupationLimit-1;
                }
                endIndex *= Constants.RoadLanes;
                for (int a = startIndex; a <= endIndex; a += Constants.RoadLanes)
                {
                    Occupations[baseSlot + a] = new Occupation {occupied = vid, speed = speed};
                }
            }

            public void Execute([ReadOnly] ref VehiclePathing vehicle)
            {
                if (vehicle.curvePos < 1.0f)
                {
                    int rI = vehicle.RoadIndex;
                    int lI = vehicle.LaneIndex;
                    RoadSection rs = RoadSections[rI];

                    float backOfVehiclePos = vehicle.curvePos - rs.vehicleHalfLen;
                    float frontOfVehiclePos = vehicle.curvePos + rs.vehicleHalfLen;

                    int OccupationIndexStart = math.max(0, (int) (math.floor(backOfVehiclePos * rs.occupationLimit)));
                    int OccupationIndexEnd;

                    // It is possible now that the next road link is not next to the current one in memory (e.g. merging)
                    if (rs.linkNext != -1)
                        OccupationIndexEnd = (int)(math.floor(frontOfVehiclePos * rs.occupationLimit));
                    else
                        OccupationIndexEnd = math.min(rs.occupationLimit - 1,
                            (int) (math.floor(frontOfVehiclePos * rs.occupationLimit)));

                    FillOccupation(OccupationIndexStart, OccupationIndexEnd, rI, lI, vehicle.speed,
                        vehicle.vehicleId, rs.linkNext, rs.occupationLimit);
                    if (vehicle.LaneIndex != vehicle.WantedLaneIndex)
                    {
                        FillOccupation(OccupationIndexStart, OccupationIndexEnd, rI, vehicle.WantedLaneIndex,
                            vehicle.speed, vehicle.vehicleId,rs.linkNext,rs.occupationLimit);
                    }
                }
            }
        }
    }
}
