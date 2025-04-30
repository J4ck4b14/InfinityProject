using InfinityProject.Time;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimalReproductionSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // Grab the global time scale
        var gt = SystemAPI.GetSingleton<GameTime>();
        double scale = TimeConfig.YearScale[gt.ScaleIndex];
        double baseYearsPerSec = TimeConfig.BaseYearsPerSecond;

        // Compute delta years this frame
        double dtYears = baseYearsPerSec * SystemAPI.Time.DeltaTime * scale;
        float fYears = (float)dtYears;

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        Entities
        .WithName("AnimalReproduction")
        .ForEach((Entity entity,
                  ref ReproductionData rep,
                  in HealthDamage health,
                  in Lifespan life,
                  in PrefabRef prefab,
                  in LocalTransform xf) =>
        {
            // Decrement by scaled years
            rep.ReproductionTimer -= fYears;

            bool healthy = health.CurrentHealth > health.MaxHealth * 0.5f;
            bool fertile = life.Age >= rep.FertilityWindow.x
                          && life.Age <= rep.FertilityWindow.y;

            if (rep.ReproductionTimer <= 0f && healthy && fertile)
            {
                var baby = ecb.Instantiate(prefab.Prefab);

                // position near parent
                float3 offset = new float3(
                    UnityEngine.Random.Range(-1f, 1f),
                    0f,
                    UnityEngine.Random.Range(-1f, 1f)
                );
                ecb.SetComponent(baby, new LocalTransform
                {
                    Position = xf.Position + offset,
                    Rotation = quaternion.identity,
                    Scale = 1f
                });

                // reset timer (e.g. add original interval back)
                rep.ReproductionTimer += rep.ReproductionTimer == 0f
                    ? 1f
                    : rep.ReproductionTimer;
            }
        }).Run();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}