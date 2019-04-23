using System;
using Traffic.Pathing;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel;
using Unity.Jobs;

namespace Traffic.Simulation
{
    [BurstCompile]
    public struct ClearArrayJob<T> : IJobParallelFor where T: struct
    {
        [WriteOnly]
        public NativeArray<T> Data;

        public void Execute(int index)
        {
            Data[index] = default;
        }
    }

    [BurstCompile]
    public struct ClearHashJob<T> : IJob where T: struct
    {
        [WriteOnly]
        public NativeMultiHashMap<int, T> Hash;

        public void Execute()
        {
            Hash.Clear();
        }
    }

    [BurstCompile]
    public struct DisposeArrayJob<T> : IJob where T: struct
    {
        [WriteOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<T> Data;

        public void Execute()
        {
        }
    }
}
