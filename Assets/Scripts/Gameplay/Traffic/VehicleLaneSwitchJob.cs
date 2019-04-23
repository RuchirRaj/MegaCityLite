using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct LaneSwitch : IJobProcessComponentData<VehiclePathing>
        {
            [ReadOnly] public NativeArray<Occupation> Occupancy;
            [ReadOnly] public NativeArray<RoadSection> RoadSections;

            bool LaneChangeNoLongerSafe(int start, int end, int rI, int lI,int vid)
            {
                int baseOffset = rI * Constants.RoadIndexMultiplier + lI;
                start *= Constants.RoadLanes;
                end *= Constants.RoadLanes;

                for (int a = start; a <= end; a += Constants.RoadLanes)
                {
                    if (Occupancy[baseOffset + a].occupied != 0 && Occupancy[baseOffset+a].occupied!=vid)
                        return true;
                }

                return false;
            }
            
            bool LaneChangeSafe(int start, int end, int rI, int lI)
            {
                int baseOffset = rI * Constants.RoadIndexMultiplier + lI;
                start *= Constants.RoadLanes;
                end *= Constants.RoadLanes;

                for (int a = start; a <= end; a += Constants.RoadLanes)
                {
                    if (Occupancy[baseOffset + a].occupied != 0)
                        return false;
                }

                return true;
            }

            float LaneChangeSpeed(int start, int rI, int lI)
            {
                int baseOffset = rI * Constants.RoadIndexMultiplier + lI;
                start *= Constants.RoadLanes;

                float speed = Occupancy[baseOffset + start].speed;
                if (speed <= 0.0f)
                    return Single.MaxValue;
                return speed;
            }
            
            public void Execute(ref VehiclePathing vehicle)
            {
                int rI = vehicle.RoadIndex;
                int lI = vehicle.LaneIndex;
                RoadSection rs = RoadSections[rI];

                int occupationIndexStart = math.max(0, (int)(math.floor(vehicle.curvePos * rs.occupationLimit))-1);
                int occupationIndexEnd = math.min(rs.occupationLimit - 1, (int)(math.floor(vehicle.curvePos * rs.occupationLimit))+1);
                
                if (vehicle.LaneIndex != vehicle.WantedLaneIndex)
                {
                    if (vehicle.LaneSwitchDelay > 0)
                        vehicle.LaneSwitchDelay--;
                    else
                    {
                        if (LaneChangeNoLongerSafe(occupationIndexStart, occupationIndexEnd, rI, vehicle.WantedLaneIndex, vehicle.vehicleId))
                        {
                            vehicle.LaneTween = 1 - vehicle.LaneTween;
                            byte tempIdx = vehicle.LaneIndex;
                            vehicle.LaneIndex = vehicle.WantedLaneIndex;
                            vehicle.WantedLaneIndex = tempIdx;
                            vehicle.LaneSwitchDelay = Constants.LaneSwitchDelay;
                        }
                    }
                }
                else if (vehicle.WantNewLane != 0)
                {
                    int4 laneOptions;
                    switch (lI)
                    {
                        default:
                            laneOptions = new int4(lI, lI, lI, lI);
                            break;
                        case 0:
                            laneOptions = new int4(lI, lI + 1, lI, lI);
                            break;
                        case 1:
                            laneOptions = new int4(lI - 1, lI + 1, lI, lI);
                            break;
                        case 2:
                            laneOptions = new int4(lI - 1, lI, lI, lI);
                            break;
                    }

                    float4 neighbourSpeeds = new float4(
                        LaneChangeSpeed(occupationIndexStart, rI, laneOptions.x),
                        LaneChangeSpeed(occupationIndexStart, rI, laneOptions.y),
                        LaneChangeSpeed(occupationIndexStart, rI, laneOptions.z),
                        LaneChangeSpeed(occupationIndexStart, rI, laneOptions.w));
                    bool4 unoccupied = new bool4(
                        LaneChangeSafe(occupationIndexStart, occupationIndexEnd, rI, laneOptions.x),
                        LaneChangeSafe(occupationIndexStart, occupationIndexEnd, rI, laneOptions.y),
                        LaneChangeSafe(occupationIndexStart, occupationIndexEnd, rI, laneOptions.z),
                        LaneChangeSafe(occupationIndexStart, occupationIndexEnd, rI, laneOptions.w));
                    bool4 mask = neighbourSpeeds > vehicle.speed;
                    mask = mask & unoccupied;

                    if (mask.x)
                    {
                        vehicle.WantedLaneIndex = (byte)(laneOptions.x);
                        vehicle.LaneTween = 0.0f;
                    }
                    else if (mask.y)
                    {
                        vehicle.WantedLaneIndex = (byte)(laneOptions.y);
                        vehicle.LaneTween = 0.0f;
                    }
                }
            }
        }
    }
}
