using InfinityProject.Time;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// ── COMBAT ────────────────────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystem))]
public partial class CombatSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    partial struct CombatJob : IJobEntity
    {
        public float RealDeltaSeconds;  // real seconds — damage is scale-independent
        public EntityCommandBuffer.ParallelWriter Ecb;

        [Unity.Collections.ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [Unity.Collections.ReadOnly] public ComponentLookup<HealthDamage>   HealthLookup;

        private const float AttackRange = 2.0f;

        public void Execute(
            Entity self,
            [EntityIndexInQuery] int idx,
            ref SteeringGoal goal,
            ref Hunger hunger,
            in HealthDamage myDamage,
            in FeedingBehavior diet,
            in LocalTransform xf)
        {
            if (goal.Priority != GoalPriority.Hunt) return;
            if (goal.TargetEntity == Entity.Null)    return;
            if (!diet.CanEatMeat)                    return;

            float3 toTarget = TransformLookup[goal.TargetEntity].Position - xf.Position;
            if (math.lengthsq(toTarget) > AttackRange * AttackRange) return;

            uint  seed = (uint)(idx * 1664525u + 1013904223u);
            var   rng  = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            // Damage uses REAL delta seconds — a wolf biting a deer is a physical
            // event that should not accelerate just because time is sped up.
            float dmg  = rng.NextFloat(myDamage.MinDamage, myDamage.MaxDamage) * RealDeltaSeconds;

            var preyHealth = HealthLookup[goal.TargetEntity];
            preyHealth.CurrentHealth -= dmg;

            if (preyHealth.CurrentHealth <= 0f)
            {
                Ecb.DestroyEntity(idx, goal.TargetEntity);
                hunger.Level = math.max(0f, hunger.Level - hunger.FoodRestoreAmount);
                goal = new SteeringGoal { Priority = GoalPriority.Wander, SpeedMultiplier = 0.4f };
            }
            else
            {
                Ecb.SetComponent(idx, goal.TargetEntity, preyHealth);
            }
        }
    }

    protected override void OnUpdate()
    {
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();

        float realDelta = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);

        Dependency = new CombatJob
        {
            RealDeltaSeconds = realDelta,
            Ecb              = ecb,
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true),
            HealthLookup    = GetComponentLookup<HealthDamage>(isReadOnly: true),
        }.ScheduleParallel(Dependency);

        _ecbSystem.AddJobHandleForProducer(Dependency);
    }
}

// ── FEED ──────────────────────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CombatSystem))]
public partial class FeedSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    partial struct FeedJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        [Unity.Collections.ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        private const float EatRange = 1.5f;

        public void Execute(
            [EntityIndexInQuery] int idx,
            ref SteeringGoal goal,
            ref Hunger hunger,
            in FeedingBehavior diet,
            in LocalTransform xf)
        {
            if (goal.Priority != GoalPriority.Forage) return;
            if (!diet.CanEatVegetation)               return;
            if (goal.TargetEntity == Entity.Null)      return;

            float3 toFood = TransformLookup[goal.TargetEntity].Position - xf.Position;
            if (math.lengthsq(toFood) > EatRange * EatRange) return;

            Ecb.DestroyEntity(idx, goal.TargetEntity);
            hunger.Level = math.max(0f, hunger.Level - hunger.FoodRestoreAmount);
            goal = new SteeringGoal { Priority = GoalPriority.Wander, SpeedMultiplier = 0.4f };
        }
    }

    protected override void OnUpdate()
    {
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();

        Dependency = new FeedJob
        {
            Ecb             = ecb,
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true),
        }.ScheduleParallel(Dependency);

        _ecbSystem.AddJobHandleForProducer(Dependency);
    }
}

// ── DEATH ─────────────────────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FeedSystem))]
public partial class DeathSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem _ecbSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    partial struct DeathJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(
            Entity e,
            [EntityIndexInQuery] int idx,
            in HealthDamage health,
            in Hunger hunger,
            in Lifespan life)
        {
            bool dead = health.CurrentHealth <= 0f
                     || hunger.Level >= 1.0f
                     || (!life.IsLegendary && life.Age >= life.MaxAge);

            if (dead) Ecb.DestroyEntity(idx, e);
        }
    }

    protected override void OnUpdate()
    {
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();
        Dependency = new DeathJob { Ecb = ecb }.ScheduleParallel(Dependency);
        _ecbSystem.AddJobHandleForProducer(Dependency);
    }
}
