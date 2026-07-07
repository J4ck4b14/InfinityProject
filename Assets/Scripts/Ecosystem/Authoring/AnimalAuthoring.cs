using InfinityProject.Time;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Inspector values for durations are in IN-GAME YEARS for readability.
/// The Baker converts them to seconds automatically before storing on the entity.
/// </summary>
public class AnimalAuthoringV2 : MonoBehaviour
{
    public enum DietType { Carnivore, Herbivore, Omnivore }

    [Header("Life  (years)")]
    public float health    = 100f;
    public float minDamage = 1f;
    public float maxDamage = 5f;

    [Tooltip("Max lifespan in in-game YEARS.")]
    public float maxAgeYears = 10f;

    public bool isLegendary = false;

    [Header("Diet & Species")]
    public DietType diet = DietType.Herbivore;
    public int speciesId = 1;

    [Header("Gender")]
    public bool isMale = true;

    [Header("Hunger")]
    [Range(0f, 0.3f)]
    [Tooltip("Starting hunger level [0 = full, 1 = starving].")]
    public float initialHunger = 0.05f;

    [Tooltip("Hunger added per in-game second. " +
             "Default 1e-7: at 5min/year scale the animal starves in ~5 real minutes.")]
    public float hungerRate = 1e-7f;

    [Tooltip("Hunger restored by one meal [0–1].")]
    public float foodRestoreAmount = 0.5f;

    [Tooltip("Passive hunger reduction per in-game second for herbivores/omnivores when not fleeing. " +
             "Should be slightly greater than hungerRate so resting deer slowly recover. " +
             "Carnivores ignore this — they rely solely on kills.")]
    public float grazeRate = 1.1e-7f;

    [Header("Fertility  (years)")]
    [Tooltip("Earliest age at which the animal can reproduce, in in-game YEARS.")]
    public float fertilityMinYears = 1f;

    [Tooltip("Latest age at which the animal can reproduce, in in-game YEARS.")]
    public float fertilityMaxYears = 5f;

    [Tooltip("Minimum time between births, in in-game YEARS.")]
    public float reproductionCooldownYears = 1f;

    [Header("Vision")]
    public float visionRange        = 12f;
    [Range(10f, 180f)]
    public float visionHalfAngleDeg = 90f;

    [Header("Movement  (scale-independent, real seconds)")]
    public float speed = 3f;

    // ── Baker ─────────────────────────────────────────────────────────────────

    class Baker : Baker<AnimalAuthoringV2>
    {
        public override void Bake(AnimalAuthoringV2 a)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Convert all year-based inspector values to seconds once, here.
            const float SPY = (float)TimeConfig.SecondsPerYear;
            float maxAgeSec      = a.maxAgeYears              * SPY;
            float fertilityMinS  = a.fertilityMinYears        * SPY;
            float fertilityMaxS  = a.fertilityMaxYears        * SPY;
            float cooldownSec    = a.reproductionCooldownYears * SPY;

            AddComponent(entity, new PrefabRef { Prefab = entity });

            AddComponent(entity, new HealthDamage
            {
                CurrentHealth = a.health,
                MaxHealth     = a.health,
                MinDamage     = a.minDamage,
                MaxDamage     = a.maxDamage,
            });

            // Starting age: random in [0, maxAge * 0.5] so nobody spawns near death
            var   rng      = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, int.MaxValue));
            float startAge = rng.NextFloat(0f, maxAgeSec * 0.5f);

            AddComponent(entity, new Lifespan
            {
                MinAge      = 0f,
                MaxAge      = maxAgeSec,
                Age         = startAge,
                IsLegendary = a.isLegendary,
                HeroAge     = maxAgeSec * 0.75f,
                LegendAge   = maxAgeSec * 0.95f,
            });

            AddComponent(entity, new Hunger
            {
                Level             = a.initialHunger,
                HungerRate        = a.hungerRate,
                FoodRestoreAmount = a.foodRestoreAmount,
                GrazeRate         = a.grazeRate,
            });

            AddComponent(entity, new SpeciesTag { SpeciesId = a.speciesId });

            AddComponent(entity, new FeedingBehavior
            {
                CanEatMeat       = a.diet is DietType.Carnivore or DietType.Omnivore,
                CanEatVegetation = a.diet is DietType.Herbivore or DietType.Omnivore,
            });

            AddComponent(entity, new BiologicalGender { IsMale = a.isMale });

            AddComponent(entity, new ReproductionData
            {
                ReproductionTimer = cooldownSec,
                FertilityWindow   = new float2(fertilityMinS, fertilityMaxS),
            });

            AddComponent(entity, new VisionCone
            {
                Range     = a.visionRange,
                HalfAngle = math.radians(a.visionHalfAngleDeg),
            });

            AddBuffer<NearbyTarget>(entity);

            var dir   = UnityEngine.Random.onUnitSphere;
            dir.y     = 0f;
            var horiz = new float3(dir.x, 0f, dir.z);
            if (math.lengthsq(horiz) <= 0f) horiz = new float3(1f, 0f, 0f);

            AddComponent(entity, new Movement
            {
                Direction = math.normalize(horiz),
                Speed     = a.speed,
            });

            AddComponent(entity, new SteeringGoal
            {
                Priority        = GoalPriority.Wander,
                TargetPosition  = float3.zero,
                TargetEntity    = Entity.Null,
                SpeedMultiplier = 0.4f,
            });

            AddComponent(entity, new AlertState   { IsAlert = false });
            AddComponent(entity, new ThreatState  { IsFleeing = false, IsInConflict = false, Target = Entity.Null });
            AddComponent(entity, new GroupBehavior { GroupId = a.speciesId, GroupSize = 1, CohesionFactor = 1f });
            AddComponent(entity, new PackSize     { Value = 1 });
        }
    }
}
