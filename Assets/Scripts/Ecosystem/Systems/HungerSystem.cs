using InfinityProject.Time;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Increases hunger over time for all animals.
/// Herbivores and omnivores also passively graze — their hunger is offset by
/// a GrazeRate when they are NOT fleeing, simulating background feeding on
/// ambient vegetation. This keeps herbivores alive until proper plant entities
/// are implemented.
///
/// Net hunger per second for a herbivore at rest:
///   HungerRate - GrazeRate   (should be slightly negative so they maintain condition)
/// Net hunger per second while fleeing:
///   HungerRate only          (burning energy, can't eat)
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimalAgingSystem))]
public partial class HungerSystem : SystemBase
{
    [BurstCompile]
    partial struct HungerJob : IJobEntity
    {
        public float DeltaSeconds;

        public void Execute(
            ref Hunger hunger,
            in FeedingBehavior diet,
            in SteeringGoal goal)
        {
            float net = hunger.HungerRate * DeltaSeconds;

            // Herbivores / omnivores passively graze when not fleeing
            if (diet.CanEatVegetation && goal.Priority != GoalPriority.Flee)
                net -= hunger.GrazeRate * DeltaSeconds;

            hunger.Level = math.clamp(hunger.Level + net, 0f, 1f);
        }
    }

    protected override void OnUpdate()
    {
        var   gt         = SystemAPI.GetSingleton<GameTime>();
        float realDelta  = TimeTestShim.EffectiveDeltaSeconds(SystemAPI.Time.DeltaTime);
        float deltaGameS = (float)(realDelta * TimeConfig.ScaleValues[gt.ScaleIndex]);

        Dependency = new HungerJob { DeltaSeconds = deltaGameS }.ScheduleParallel(Dependency);
    }
}
