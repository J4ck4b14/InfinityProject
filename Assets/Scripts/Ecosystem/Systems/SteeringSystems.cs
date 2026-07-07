using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// ── WANDER (lowest priority, resets goal every frame as fallback) ─────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VisionScanSystem))]
public partial class WanderSystem : SystemBase
{
    [BurstCompile]
    partial struct WanderJob : IJobEntity
    {
        public float TotalSeconds;

        public void Execute(Entity e, ref SteeringGoal goal, in LocalTransform xf)
        {
            uint seed = (uint)(e.Index * 2654435761u + (uint)(TotalSeconds * 100f));
            var  rng  = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);

            float3 wanderDir = rng.NextFloat3Direction();
            wanderDir.y = 0f;
            if (math.lengthsq(wanderDir) < 0.001f) wanderDir = new float3(1, 0, 0);
            wanderDir = math.normalize(wanderDir);

            goal = new SteeringGoal
            {
                Priority        = GoalPriority.Wander,
                TargetPosition  = xf.Position + wanderDir * 5f,
                TargetEntity    = Entity.Null,
                SpeedMultiplier = 0.4f,
            };
        }
    }

    protected override void OnUpdate()
    {
        Dependency = new WanderJob { TotalSeconds = (float)SystemAPI.Time.ElapsedTime }
            .ScheduleParallel(Dependency);
    }
}

// ── MATE SEEK (priority 3) ────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(WanderSystem))]
public partial class MateSeekSystem : SystemBase
{
    [BurstCompile]
    partial struct MateSeekJob : IJobEntity
    {
        [Unity.Collections.ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(
            ref SteeringGoal goal,
            in DynamicBuffer<NearbyTarget> targets,
            in LocalTransform xf)
        {
            if ((int)goal.Priority < (int)GoalPriority.Mate) return;

            Entity bestMate   = Entity.Null;
            float  bestSqDist = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (!t.IsMate || t.SqDist >= bestSqDist) continue;
                bestSqDist = t.SqDist;
                bestMate   = t.Entity;
            }

            if (bestMate == Entity.Null) return;

            goal = new SteeringGoal
            {
                Priority        = GoalPriority.Mate,
                TargetPosition  = TransformLookup[bestMate].Position,
                TargetEntity    = bestMate,
                SpeedMultiplier = 0.8f,
            };
        }
    }

    protected override void OnUpdate()
    {
        Dependency = new MateSeekJob
        {
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true)
        }.ScheduleParallel(Dependency);
    }
}

// ── FORAGE (priority 2) ───────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MateSeekSystem))]
public partial class ForageSteeringSystem : SystemBase
{
    [BurstCompile]
    partial struct ForageJob : IJobEntity
    {
        [Unity.Collections.ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(
            ref SteeringGoal goal,
            in DynamicBuffer<NearbyTarget> targets,
            in FeedingBehavior diet,
            in Hunger hunger)
        {
            if ((int)goal.Priority < (int)GoalPriority.Forage) return;
            if (!diet.CanEatVegetation || hunger.Level < 0.3f)  return;

            Entity bestFood   = Entity.Null;
            float  bestSqDist = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (!t.IsFood || t.IsThreat || t.SqDist >= bestSqDist) continue;
                bestSqDist = t.SqDist;
                bestFood   = t.Entity;
            }

            if (bestFood == Entity.Null) return;

            goal = new SteeringGoal
            {
                Priority        = GoalPriority.Forage,
                TargetPosition  = TransformLookup[bestFood].Position,
                TargetEntity    = bestFood,
                SpeedMultiplier = 0.9f,
            };
        }
    }

    protected override void OnUpdate()
    {
        Dependency = new ForageJob
        {
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true)
        }.ScheduleParallel(Dependency);
    }
}

// ── HUNT (priority 1) ─────────────────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ForageSteeringSystem))]
public partial class HuntSteeringSystem : SystemBase
{
    [BurstCompile]
    partial struct HuntJob : IJobEntity
    {
        [Unity.Collections.ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(
            ref SteeringGoal goal,
            in DynamicBuffer<NearbyTarget> targets,
            in FeedingBehavior diet,
            in Hunger hunger)
        {
            if ((int)goal.Priority < (int)GoalPriority.Hunt) return;
            if (!diet.CanEatMeat || hunger.Level < 0.3f)      return;

            Entity bestPrey   = Entity.Null;
            float  bestSqDist = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (!t.IsFood || t.IsThreat || t.SqDist >= bestSqDist) continue;
                bestSqDist = t.SqDist;
                bestPrey   = t.Entity;
            }

            if (bestPrey == Entity.Null) return;

            goal = new SteeringGoal
            {
                Priority        = GoalPriority.Hunt,
                TargetPosition  = TransformLookup[bestPrey].Position,
                TargetEntity    = bestPrey,
                SpeedMultiplier = 1.2f,
            };
        }
    }

    protected override void OnUpdate()
    {
        Dependency = new HuntJob
        {
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true)
        }.ScheduleParallel(Dependency);
    }
}

// ── FLEE (priority 0 – highest) ───────────────────────────────────────────────

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HuntSteeringSystem))]
public partial class FleeSteeringSystem : SystemBase
{
    [BurstCompile]
    partial struct FleeJob : IJobEntity
    {
        [Unity.Collections.ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(
            Entity self,
            ref SteeringGoal goal,
            in DynamicBuffer<NearbyTarget> targets,
            in LocalTransform xf)
        {
            float3 fleeDir     = float3.zero;
            int    threatCount = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (!t.IsThreat) continue;

                float3 toThreat = TransformLookup[t.Entity].Position - xf.Position;
                float  weight   = 1f / math.max(math.sqrt(t.SqDist), 0.1f);
                fleeDir -= toThreat * weight;
                threatCount++;
            }

            if (threatCount == 0) return;

            fleeDir.y = 0f;
            if (math.lengthsq(fleeDir) < 0.001f) fleeDir = new float3(1, 0, 0);
            fleeDir = math.normalize(fleeDir);

            goal = new SteeringGoal
            {
                Priority        = GoalPriority.Flee,
                TargetPosition  = xf.Position + fleeDir * 20f,
                TargetEntity    = Entity.Null,
                SpeedMultiplier = 1.4f,
            };
        }
    }

    protected override void OnUpdate()
    {
        Dependency = new FleeJob
        {
            TransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true)
        }.ScheduleParallel(Dependency);
    }
}
