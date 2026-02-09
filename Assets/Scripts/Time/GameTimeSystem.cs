using InfinityProject.Time;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class GameTimeSystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();
        if (!SystemAPI.HasSingleton<GameTime>())
        {
            var e = EntityManager.CreateEntity(typeof(GameTime));
            EntityManager.SetComponentData(e, new GameTime
            {
                TotalYears = 0.0,
                ScaleIndex = 1 // Start at normal
            });
        }
    }

    protected override void OnUpdate()
    {
        // Get a writable reference to the singleton so changes persist automatically.
        ref var gt = ref SystemAPI.GetSingletonRW<GameTime>().ValueRW;

        // Ensure ScaleIndex is within the configured bounds to avoid IndexOutOfRange
        if (gt.ScaleIndex >= TimeConfig.YearScale.Length)
        {
            gt.ScaleIndex = (byte)(TimeConfig.YearScale.Length - 1);
        }

        // Accumulate scaled years directly. Use double precision for accumulation.
        double deltaYears = TimeConfig.BaseYearsPerSecond * (double)SystemAPI.Time.DeltaTime * TimeConfig.YearScale[gt.ScaleIndex];
        gt.TotalYears += deltaYears;
    }
}