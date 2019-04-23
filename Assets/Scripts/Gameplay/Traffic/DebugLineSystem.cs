using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
[UpdateInGroup(typeof(PresentationSystemGroup))]
public class DebugLineSystem : EntityCommandBufferSystem
{
    public struct LineData3D
    {
        public float3 A, B;
        public uint Color;
    }

    public NativeQueue<LineData3D> Lines;

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        Lines = new NativeQueue<LineData3D>(Allocator.TempJob);
    }

    protected override void OnDestroyManager()
    {
        Lines.Dispose();
        base.OnDestroyManager();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        LineData3D line;
        float recip = 1.0f / 255.0f;

        while (Lines.TryDequeue(out line))
        {
            float r = ((line.Color & 0xff0000) >> 16) * recip;
            float g = ((line.Color & 0x00ff00) >>  8) * recip;
            float b = ((line.Color & 0x0000ff)      ) * recip;
            Debug.DrawLine(line.A, line.B, new Color(r, g, b));
        }

        Lines.Dispose();
        Lines = new NativeQueue<LineData3D>(Allocator.TempJob);
    }
}
#endif
