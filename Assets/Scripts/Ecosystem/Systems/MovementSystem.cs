using InfinityProject.Time;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Movement uses REAL delta seconds — so animals always move at a visually
/// consistent speed regardless of time scale. Only biological rates (hunger,
/// aging, reproduction) accelerate with the scale.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FleeSteeringSystem))]
public partial class MovementSystem : SystemBase
{
    [BurstCompile]
    partial struct MoveJob : IJobEntity
    {
        public float RealDeltaSeconds;

        private const float TurnRate      = 4f;
        private const float ArrivalRadius = 1.5f;

        public void Execute(
            ref LocalTransform xf,
            ref Movement mov,
            in SteeringGoal goal,
            in Hunger hunger)
        {
            float3 toTarget = goal.TargetPosition - xf.Position;
            toTarget.y = 0f;

            if (math.lengthsq(toTarget) > ArrivalRadius * ArrivalRadius)
            {
                float3 desired = math.normalize(toTarget);
                float3 current = mov.Direction;
                current.y = 0f;
                if (math.lengthsq(current) < 0.001f) current = desired;
                else current = math.normalize(current);

                float maxAngle = TurnRate * RealDeltaSeconds;
                float dot      = math.clamp(math.dot(current, desired), -1f, 1f);
                float angle    = math.acos(dot);
                float t        = angle < 0.0001f ? 1f : math.min(maxAngle / angle, 1f);

                float3 dir = math.normalize(math.lerp(current, desired, t));
                dir.y = 0f;
                mov.Direction = dir;
            }

            float starvePenalty = hunger.Level >= 0.9f ? 0.5f : 1f;
            float speed         = mov.Speed * goal.SpeedMultiplier * starvePenalty;
            xf.Position        += mov.Direction * speed * RealDeltaSeconds;
        }
    }

    protected override void OnUpdate()
    {
        var gt = SystemAPI.GetSingleton<GameTime>();
        float realDelta = (float)(TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime) * TimeConfig.ScaleValues[gt.ScaleIndex]);
        Dependency = new MoveJob { RealDeltaSeconds = realDelta }.ScheduleParallel(Dependency);
    }
}
