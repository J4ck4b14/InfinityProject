using Unity.Burst;
using InfinityProject.Time;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

//1. Reference the ECB System so we can playback safely at end of frame
[UpdateInGroup(typeof(SimulationSystemGroup))]
[RequireMatchingQueriesForUpdate] // Ensures we only run when there *are* reproducing animals
public partial class AnimalReproductionSystem : SystemBase
{
    // We’ll grab the ECB system once on Create
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    partial struct ReproductionJob : IJobEntity
    {
        public float DeltaYears;
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(Entity _, [EntityIndexInQuery] int entityInQueryIndex, ref ReproductionData rep, in HealthDamage health, in Lifespan life, in PrefabRef prefab, in LocalTransform xf)
        {
            // decrement the timer
            rep.ReproductionTimer -= DeltaYears;

            bool healthy = health.CurrentHealth > health.MaxHealth * 0.5f;
            bool fertile = life.Age >= rep.FertilityWindow.x
            && life.Age <= rep.FertilityWindow.y;

            if (rep.ReproductionTimer <= 0f && healthy && fertile)
            {
                // Instantiate a baby via the ECB
                Entity baby = Ecb.Instantiate(entityInQueryIndex, prefab.Prefab);

                // Deterministic per-entity pseudo-random offset (job-compatible)
                uint seed = (uint)(entityInQueryIndex * 747796405u + 2891336453u);
                float rx = math.frac(math.sin((float)(seed + 1)) * 43758.5453f) * 2f - 1f;
                float rz = math.frac(math.sin((float)(seed + 2)) * 43758.5453f) * 2f - 1f;
                float3 offset = new (rx, 0f, rz);

                // Set LocalTransform on the spawned baby
                Ecb.SetComponent(entityInQueryIndex, baby, new LocalTransform
                {
                    Position = xf.Position + offset,
                    Rotation = xf.Rotation,
                    Scale = xf.Scale
                });

                // Reset the timer
                rep.ReproductionTimer += math.max(rep.FertilityWindow.y - rep.FertilityWindow.x, 1f);
            }
        }
    }

    protected override void OnUpdate()
    {
        //2. Compute scaled years per frame
        var gt = SystemAPI.GetSingleton<GameTime>();
        double scale = TimeConfig.YearScale[gt.ScaleIndex];
        double baseY = TimeConfig.BaseYearsPerSecond;

        // Use test shim to allow deterministic delta in unit tests
        double deltaSeconds = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);
        float deltaYears = (float)(baseY * deltaSeconds * scale);

        //3. Create a parallel ECB
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();

        //4. Schedule the IJobEntity
        var job = new ReproductionJob
        {
            DeltaYears = deltaYears,
            Ecb = ecb
        };

        var handle = job.ScheduleParallel(Dependency);

        //8. Tell the ECB system when we’re done scheduling
        _ecbSystem.AddJobHandleForProducer(handle);
        Dependency = handle;
    }
}