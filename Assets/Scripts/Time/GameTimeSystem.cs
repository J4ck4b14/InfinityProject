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
        // Fetch singleton and directly modify it
        var gt = SystemAPI.GetSingletonRW<GameTime>().ValueRW;

        // Accumulate scaled years directly
        gt.TotalYears += TimeConfig.BaseYearsPerSecond * SystemAPI.Time.DeltaTime * TimeConfig.YearScale[gt.ScaleIndex];
    }
}