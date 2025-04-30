using Unity.Entities;
using InfinityProject.Time;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimalAgingSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // Fetch current scale
        var gt = SystemAPI.GetSingleton<GameTime>();
        double scale = TimeConfig.YearScale[gt.ScaleIndex];
        double baseYearsPerSec = TimeConfig.BaseYearsPerSecond;

        // Compute scaled years this frame
        double dtYears = baseYearsPerSec * SystemAPI.Time.DeltaTime * scale;
        float fYears = (float)dtYears;

        Entities
            .ForEach((ref Lifespan life) =>
            {
                if (!life.IsLegendary)
                    life.Age += fYears;
            })
            .Run();
    }
}