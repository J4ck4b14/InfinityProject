using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Applies dynamic exponential decay to each MemoryEvent’s EmotionalWeight,
/// driven by event magnitude, agent age, buffer fullness, and trauma count,
/// then prunes negligible or excess entries.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class MemorySystem : SystemBase
{
    [BurstCompile]
    partial struct MemoryDecayJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(ref DynamicBuffer<MemoryEvent> memBuf, in Identity id)
        {
            if (memBuf.Length == 0)
                return;

            float fullness = memBuf.Length / (float)MemoryConfig.MaxEvents;

            // Count trauma events
            int traumaCount = 0;
            for (int i = 0; i < memBuf.Length; i++)
                if (memBuf[i].Magnitude >= 0.7f)
                    traumaCount++;

            float numBase = MemoryConfig.LambdaBase * (1f + id.Age * MemoryConfig.WeightAge + fullness * MemoryConfig.WeightFullness);
            float traumaDenBase = 1f + traumaCount * MemoryConfig.WeightTrauma; // includes the leading1

            // Decay and compact in one pass
            int write = 0;
            for (int i = 0; i < memBuf.Length; i++)
            {
                var ev = memBuf[i];
                float den = traumaDenBase + ev.Magnitude * MemoryConfig.WeightMagnitude; //1 + mag*w_mag + traumaCount*w_trauma
                float lambdaEvent = numBase / den;
                ev.EmotionalWeight *= math.exp(-lambdaEvent * DeltaTime);

                if (ev.EmotionalWeight >= 0.01f)
                {
                    memBuf[write++] = ev;
                }
            }

            if (write < memBuf.Length)
            {
                memBuf.ResizeUninitialized(write);
            }

            // Enforce max capacity: remove oldest entries if necessary by shifting left
            int excess = memBuf.Length - MemoryConfig.MaxEvents;
            if (excess > 0)
            {
                int remaining = memBuf.Length - excess;
                for (int k = 0; k < remaining; k++)
                {
                    memBuf[k] = memBuf[k + excess];
                }
                memBuf.ResizeUninitialized(remaining);
            }
        }
    }

    protected override void OnUpdate()
    {
        var job = new MemoryDecayJob { DeltaTime = SystemAPI.Time.DeltaTime };
        var handle = job.ScheduleParallel(Dependency);
        Dependency = handle;
    }
}