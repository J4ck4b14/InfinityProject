using InfinityProject.Time;
using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimalAgingSystem : SystemBase
{
    [BurstCompile]
    partial struct AgingJob : IJobEntity
    {
        public float DeltaYears;

        public void Execute(ref Lifespan life)
        {
            if (!life.IsLegendary)
                life.Age += DeltaYears;
        }
    }

    protected override void OnUpdate()
    {
        // Fetch current scale
        var gt = SystemAPI.GetSingleton<GameTime>();
        double scale = TimeConfig.YearScale[gt.ScaleIndex];
        double baseYearsPerSec = TimeConfig.BaseYearsPerSecond;

        // Compute scaled years this frame using test shim to allow deterministic test delta
        double deltaSeconds = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);
        double dtYears = baseYearsPerSec * deltaSeconds * scale;
        float fYears = (float)dtYears;

        var job = new AgingJob { DeltaYears = fYears };
        var handle = job.ScheduleParallel(Dependency);
        Dependency = handle;
    }
}