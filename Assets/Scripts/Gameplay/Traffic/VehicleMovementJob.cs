//#define USE_DEBUG_LINES // Enable here and at the top of TrafficSystem.cs

using Traffic.Pathing;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Traffic.Simulation
{
    public partial class TrafficSystem
    {
        [BurstCompile]
        public struct VehicleMovementJob : IJobProcessComponentData<VehicleTargetPosition, VehiclePhysicsState>
        {
            public float TimeStep;

            public const float kSlowingDistanceMeters = 45.0f;
            public const float kMaxSpeedMetersPerSecond = 10.0f;
            public const float kMaxAccelMetersPerSecondSq = 40.0f;

#if UNITY_EDITOR && USE_DEBUG_LINES
            // These Debug Lines can be turned on to see the vectors coming from the vehicles
            // Using them in a smaller scene is recommended if many are added
            public NativeQueue<DebugLineSystem.LineData3D>.Concurrent DebugLines;
#endif

            [ReadOnly] public NativeMultiHashMap<int, VehicleCell> Cells;

            private float3 CellAvoidance(int key, float3 pos, float3 velocity, float radius)
            {
                VehicleCell cell;
                NativeMultiHashMapIterator<int> iter;

                if (length(velocity) < 0.001f || !Cells.TryGetFirstValue(key, out cell, out iter))
                    return default(float3);

                float3 vnorm = normalize(velocity);

                float3 right = cross(vnorm, float3(0.0f, 1.0f, 0.0f));
                float3 up = normalize(cross(right, vnorm));

                float3 ownAnticipated = pos + velocity * TimeStep;

                float maxScanRangeMeters = 64.0f;

                float closestDist = float.MaxValue;
                float xa = 0.0f;
                float ya = 0.0f;
                float mag = 0.0f;

                do
                {
                    // For the vehicle in the cell, calculate its anticipated position
                    float3 anticipated = cell.Position + cell.Velocity * TimeStep;

                    float3 currDelta = pos - cell.Position;
                    float3 delta = anticipated - ownAnticipated;

                    // Don't avoid self
                    if (lengthsq(currDelta) < 0.3f)
                        continue;

                    float dz = dot(delta, vnorm);

                    // Ignore this vehicle if it's behind or too far away
                    if (dz < 0.0f || dz > maxScanRangeMeters)
                        continue;

                    float lsqDelta = lengthsq(delta);

                    // Only update if the distance between anticipated positions is less than the current closest and radii
                    if (lsqDelta < closestDist && lsqDelta < (cell.Radius + radius) * (cell.Radius + radius))
                    {
                        float dx = dot(delta, right);
                        float dy = dot(delta, up);

                        closestDist = lsqDelta;

                        xa = dx;
                        ya = dy;
                        mag = cell.Radius + radius;
                    }

                } while (Cells.TryGetNextValue(out cell, ref iter));

                float3 result = default(float3);

                if (xa < 0.0f)
                    result += (mag + xa) * right;
                else if (xa > 0.0f)
                    result -= (mag - xa) * right;

                if (ya < 0.0f)
                    result += (mag + ya) * up;
                else if (ya > 0.0f)
                    result -= (mag - ya) * up;

#if UNITY_EDITOR && USE_DEBUG_LINES
                var projectedVelHash = GridHash.Hash(pos + vnorm * 32.0f, VehicleHashJob.kCellSize);
                DebugLines.Enqueue(new DebugLineSystem.LineData3D() {A = pos, B = pos + up * 5.0f, Color = (uint) key});
                DebugLines.Enqueue(new DebugLineSystem.LineData3D()
                    {A = pos, B = pos + velocity * 2.0f, Color = (uint) projectedVelHash});
#endif

                result *= maxScanRangeMeters - sqrt(closestDist) / maxScanRangeMeters;
                return result;
            }

            private float3 Avoidance(float3 pos, float3 velocity, float radius)
            {
                float cellSize = VehicleHashJob.kCellSize;
                float3 steering = default(float3);

                int hash = GridHash.Hash(pos, cellSize);

                // Check to see if the vehicle will be in another cell (in a few frames)
                int projectedHash = GridHash.Hash(pos + velocity * 2.0f, cellSize);

                steering += CellAvoidance(hash, pos, velocity, radius);

                // The vehicle is projected to be in the same cell, so just return steering
                if (hash == projectedHash)
                {
                    return steering;
                }

                // The vehicle is projected to be in another, so calculate Avoidance on things in that cell as well
                steering += CellAvoidance(projectedHash, pos, velocity, radius);
                return steering;
            }

            private float3 Seek(float3 target, float3 curr, float3 velocity, float speedMult)
            {
                float3 targetOffset = target - curr;
                float distance = length(targetOffset);
                float rampedSpeed = kMaxSpeedMetersPerSecond * speedMult * (distance / kSlowingDistanceMeters);
                float clippedSpeed = min(rampedSpeed, kMaxSpeedMetersPerSecond * speedMult);

                // Compute velocity based on target position
                float3 desiredVelocity = targetOffset * (clippedSpeed / distance);

                float3 steering = desiredVelocity - velocity;

                if (lengthsq(steering) < 0.5f)
                    return default(float3);

                return steering;
            }

            public void Execute([ReadOnly] ref VehicleTargetPosition targetPos, ref VehiclePhysicsState state)
            {
                // 2 steering vectors, seeking the road and avoidance
                float3 seekSteering = Seek(targetPos.IdealPosition, state.Position, state.Velocity, state.SpeedMult);
                float3 avoidSteering = Avoidance(state.Position, state.Velocity, Constants.AvoidanceRadius);

                // If there is any avoidSteering value, select that vector otherwise just seek the curve of the road
                float3 steering = lengthsq(avoidSteering) > 0.01f ? avoidSteering : seekSteering;

#if UNITY_EDITOR && USE_DEBUG_LINES
                DebugLines.Enqueue(new DebugLineSystem.LineData3D()
                    {A = state.Position, B = state.Position + steering, Color = 0xffff00});
                DebugLines.Enqueue(new DebugLineSystem.LineData3D()
                    {A = state.Position, B = state.Position + avoidSteering, Color = 0xff00ff});
                DebugLines.Enqueue(new DebugLineSystem.LineData3D()
                    {A = state.Position, B = targetPos.IdealPosition, Color = 0xff3434});
                DebugLines.Enqueue(new DebugLineSystem.LineData3D()
                    {A = state.Position, B = state.Position + state.Heading * 10.0f, Color = 0x4060c0});
#endif

                var maxSpeed = kMaxSpeedMetersPerSecond * state.SpeedMult;
                //var speed = length(steering);
                //speed = speed < maxSpeed ? speed : maxSpeed; // Clamp the speed

                // Update the VehiclePhysicsState
                float3 targetHeading = normalizesafe(steering);
                state.Heading = state.Heading + targetHeading * TimeStep;
                state.Heading = normalizesafe(state.Heading);
                state.Velocity = state.Heading * targetPos.IdealSpeed;
                state.Position = state.Position + state.Velocity * TimeStep;

                if (dot(targetHeading, state.Velocity) > 0)
                {
                    float bankAmount = 0.0f;
                    float kBankMaxRadians = (float) (PI / 16.0f) * state.SpeedMult; // 11.25 degrees * speed multiplier

                    float speedRatio = 100.0f * kBankMaxRadians / maxSpeed;

                    bankAmount = -dot(targetHeading.zx * float2(-1.0f, 1.0f), state.Velocity.xz);
                    bankAmount = clamp(bankAmount * speedRatio, -kBankMaxRadians, kBankMaxRadians);

                    var bankDifference = state.BankRadians - bankAmount;

                    state.BankRadians = (bankAmount > .001f || bankAmount < -.001f)
                        ? state.BankRadians - bankDifference * TimeStep
                        : 0.0f;
                }
            }
        }
    }
}
