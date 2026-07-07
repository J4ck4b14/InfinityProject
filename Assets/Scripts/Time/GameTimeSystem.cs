using InfinityProject.Time;
using Unity.Entities;

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
                TotalSeconds = 0.0,
                ScaleIndex   = 1    // start at 1:1
            });
        }
    }

    protected override void OnUpdate()
    {
        ref var gt = ref SystemAPI.GetSingletonRW<GameTime>().ValueRW;

        if (gt.ScaleIndex >= TimeConfig.ScaleValues.Length)
            gt.ScaleIndex = (byte)(TimeConfig.ScaleValues.Length - 1);

        double deltaGameSeconds = SystemAPI.Time.DeltaTime * TimeConfig.ScaleValues[gt.ScaleIndex];
        gt.TotalSeconds += deltaGameSeconds;
    }
}
