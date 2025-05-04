using InfinityProject.Time;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// 1. Reference the ECB System so we can playback safely at end of frame
[UpdateInGroup(typeof(SimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]           // Ensures we only run when there *are* reproducing animals
public partial class AnimalReproductionSystem : SystemBase
{
    // We’ll grab the ECB system once on Create
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        // 2. Compute scaled years per frame exactly once
        var gt = SystemAPI.GetSingleton<GameTime>();
        double scale = TimeConfig.YearScale[gt.ScaleIndex];
        double baseY = TimeConfig.BaseYearsPerSecond;
        float deltaYears = (float)(baseY * SystemAPI.Time.DeltaTime * scale);

        // 3. Create a parallel ECB
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();

        // 4. Capture the ECB and deltaYears into our job
        Entities
            .WithName("AnimalReproduction")
            .ForEach((
                Entity parentEntity,
                int entityInQueryIndex,
                ref ReproductionData rep,
                in HealthDamage health,
                in Lifespan life,
                in global::PrefabRef prefab,
                in LocalTransform xf
            ) =>
            {
                // decrement the timer
                rep.ReproductionTimer -= deltaYears;

                bool healthy = health.CurrentHealth > health.MaxHealth * 0.5f;
                bool fertile = life.Age >= rep.FertilityWindow.x
                             && life.Age <= rep.FertilityWindow.y;

                if (rep.ReproductionTimer <= 0f && healthy && fertile)
                {
                    // 5. Instantiate a baby via the ECB (no more 'Unity.Entities.Prefab' confusion)
                    Entity baby = ecb.Instantiate(entityInQueryIndex, prefab.Prefab);

                    // 6. Position it near the parent
                    float3 offset = new float3(
                        UnityEngine.Random.Range(-1f, 1f),
                        0f,
                        UnityEngine.Random.Range(-1f, 1f)
                    );

                    // We use SetComponent because LocalTransform is a chunk component
                    ecb.SetComponent(entityInQueryIndex, baby, new LocalTransform
                    {
                        Position = xf.Position + offset,
                        Rotation = xf.Rotation,
                        Scale = xf.Scale
                    });

                    // 7. Reset the timer (assumes original interval stored elsewhere — you could cache it)
                    rep.ReproductionTimer += math.max(rep.FertilityWindow.y - rep.FertilityWindow.x, 1f);
                }
            })
            .ScheduleParallel();

        // 8. Tell the ECB system when we’re done scheduling
        _ecbSystem.AddJobHandleForProducer(Dependency);
    }
}