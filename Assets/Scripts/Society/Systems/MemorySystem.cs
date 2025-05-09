using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Applies dynamic exponential decay to each MemoryEvent’s EmotionalWeight,
/// driven by event magnitude, agent age, buffer fullness, and trauma count,
/// then prunes negligible or excess entries.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class MemorySystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        Entities
            .WithName("DecayAndPruneMemories")
            .ForEach((ref DynamicBuffer<MemoryEvent> memBuf, in Identity id) =>
            {
                // Compute how full the buffer is [0–1]
                float fullness = memBuf.Length / (float)MemoryConfig.MaxEvents;

                // Count high‐magnitude “trauma” events
                int traumaCount = 0;
                for (int i = 0; i < memBuf.Length; i++)
                    if (memBuf[i].Magnitude >= 0.7f)
                        traumaCount++;

                // Decay each event’s emotional weight
                for (int i = memBuf.Length - 1; i >= 0; i--)
                {
                    var ev = memBuf[i];

                    // λ_event = Λ_base * (1 + Age*w_age + Fullness*w_full)
                    //                  / (1 + Magnitude*w_mag + TraumaCount*w_trauma)
                    float num = MemoryConfig.LambdaBase
                              * (1
                                 + id.Age * MemoryConfig.WeightAge
                                 + fullness * MemoryConfig.WeightFullness);
                    float den = 1
                              + ev.Magnitude * MemoryConfig.WeightMagnitude
                              + traumaCount * MemoryConfig.WeightTrauma;
                    float lambdaEvent = num / den;

                    ev.EmotionalWeight *= math.exp(-lambdaEvent * deltaTime);
                    memBuf[i] = ev;
                }

                // Remove entries with negligible weight
                for (int i = memBuf.Length - 1; i >= 0; i--)
                    if (memBuf[i].EmotionalWeight < 0.01f)
                        memBuf.RemoveAt(i);

                // Enforce max buffer capacity by removing oldest
                while (memBuf.Length > MemoryConfig.MaxEvents)
                    memBuf.RemoveAt(0);

            })
            .ScheduleParallel();
    }
}