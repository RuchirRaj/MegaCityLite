using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Traffic.Simulation
{
    public struct VehicleDespawnJob : IJobProcessComponentDataWithEntity<VehiclePathing>
    {
        public EntityCommandBuffer.Concurrent EntityCommandBuffer;

        public void Execute(Entity entity, int index, [ReadOnly] ref VehiclePathing vehicle)
        {
            if (vehicle.curvePos >= 1.0f)
            {
                EntityCommandBuffer.DestroyEntity(index, entity);
            }
        }
    }
}
