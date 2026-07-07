using InfinityProject.Time;
using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimalAgingSystem : SystemBase
{
    [BurstCompile]
    partial struct AgingJob : IJobEntity
    {
        public float DeltaSeconds;  // in-game seconds this frame

        public void Execute(ref Lifespan life)
        {
            if (!life.IsLegendary)
                life.Age += DeltaSeconds;
        }
    }

    protected override void OnUpdate()
    {
        var   gt          = SystemAPI.GetSingleton<GameTime>();
        float realDelta   = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);
        float deltaGameS  = (float)(realDelta * TimeConfig.ScaleValues[gt.ScaleIndex]);

        Dependency = new AgingJob { DeltaSeconds = deltaGameS }.ScheduleParallel(Dependency);
    }
}
