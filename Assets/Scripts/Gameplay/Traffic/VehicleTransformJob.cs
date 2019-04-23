using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static Unity.Mathematics.math;
using quaternion = Unity.Mathematics.quaternion;

namespace Traffic.Simulation
{
    partial class TrafficSystem
    {
        [BurstCompile]
        struct VehicleTransformJob : IJobProcessComponentData<VehiclePhysicsState, Translation, Rotation>
        {
            public float3 CameraPos;
            public float dt;

            static float3 ClampMagnitude(float3 vector, float max)
            {
                float sqrMag = math.lengthsq(vector);
                if (sqrMag > (max * max))
                    return math.normalize(vector) * max;
                return vector;
            }

            public void Execute(
                [ReadOnly] ref VehiclePhysicsState physicsState,
                ref Translation outputPos,
                ref Rotation outputRot)
            {
                outputPos.Value = physicsState.Position;

                var orient = quaternion.LookRotation(physicsState.Heading, float3(0.0f, 1.0f, 0.0f));
                var bankQuat = FastBankQuat(physicsState.BankRadians);
                outputRot.Value = mul(orient, bankQuat);
            }
        }

        // Uses unexpanded Taylor root expression (x, 1 respectively) for sin(), cos().
        // This looks good where x is within -1 to 1.
        private static quaternion FastBankQuat(float radians)
        {
            // Create a unit quaternion
            // Length of quat = Sqrt ( w^2 + x^2 + y^2 + z^2)
            var length = sqrt(1.0f + (radians * 0.5f) * (radians * 0.5f));
            return new quaternion(0, 0, (radians * 0.5f) / length, 1.0f / length);
        }
    }
}
