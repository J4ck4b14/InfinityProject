using Unity.Burst;
using InfinityProject.Time;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[RequireMatchingQueriesForUpdate]
public partial class AnimalReproductionSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    partial struct ReproductionJob : IJobEntity
    {
        public float DeltaSeconds;  // in-game seconds
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(
            Entity _,
            [EntityIndexInQuery] int entityInQueryIndex,
            ref ReproductionData rep,
            in HealthDamage health,
            in Lifespan life,
            in PrefabRef prefab,
            in LocalTransform xf)
        {
            rep.ReproductionTimer -= DeltaSeconds;

            bool healthy = health.CurrentHealth > health.MaxHealth * 0.5f;
            bool fertile = life.Age >= rep.FertilityWindow.x
                        && life.Age <= rep.FertilityWindow.y;

            if (rep.ReproductionTimer <= 0f && healthy && fertile)
            {
                Entity baby = Ecb.Instantiate(entityInQueryIndex, prefab.Prefab);

                uint   seed = (uint)(entityInQueryIndex * 747796405u + 2891336453u);
                float  rx   = math.frac(math.sin((float)(seed + 1)) * 43758.5453f) * 2f - 1f;
                float  rz   = math.frac(math.sin((float)(seed + 2)) * 43758.5453f) * 2f - 1f;

                Ecb.SetComponent(entityInQueryIndex, baby, new LocalTransform
                {
                    Position = xf.Position + new float3(rx, 0f, rz),
                    Rotation = xf.Rotation,
                    Scale    = xf.Scale
                });

                // Reset cooldown — expressed in seconds now
                rep.ReproductionTimer += math.max(rep.FertilityWindow.y - rep.FertilityWindow.x, 1f);
            }
        }
    }

    protected override void OnUpdate()
    {
        var   gt         = SystemAPI.GetSingleton<GameTime>();
        float realDelta  = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);
        float deltaGameS = (float)(realDelta * TimeConfig.ScaleValues[gt.ScaleIndex]);

        var ecb    = _ecbSystem.CreateCommandBuffer().AsParallelWriter();
        var handle = new ReproductionJob { DeltaSeconds = deltaGameS, Ecb = ecb }
                        .ScheduleParallel(Dependency);

        _ecbSystem.AddJobHandleForProducer(handle);
        Dependency = handle;
    }
}
