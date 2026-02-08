using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AnimalAuthoring : MonoBehaviour
{
    // Enum for diet type: Carnivore, Herbivore, Omnivore
    public enum DietType
    {
        Carnivore,
        Herbivore,
        Omnivore
    }

    [Header("Life Settings")]
    [Tooltip("Health of the animal (starting health).")]
    public float health =100f; // Health of the animal
    [Tooltip("Maximum damage the animal can deal (relevant for predators).")]
    public float maxDamage =5f; // Maximum damage the animal can deal (not used by prey)
    [Tooltip("Minimum damage the animal can deal (relevant for predators).")]
    public float minDamage =1f; // Minimum damage the animal can deal (not used by prey)
    [Tooltip("Whether the animal is fertile and able to reproduce.")]
    public bool fertile = true; // Is the animal fertile (able to reproduce)?
    [Tooltip("Gender of the animal (true = male, false = female).")]
    public bool male = true; // Gender of the animal (true = male, false = female)
    [Tooltip("Age window where the species is fertile")]
    public Vector2 fertilityWindow = new(0.5f,5f); // Use UnityEngine.Vector2 for inspector
    [Tooltip("Time (in seconds) before reproduction can happen again.")]
    public int timeForReproduction =10;
    [Tooltip("Diet type of the animal (Carnivore, Herbivore, Omnivore).")]
    public DietType diet = DietType.Herbivore;

    [Header("Age Settings")]
    [Tooltip("Maximum age the animal can live.")]
    public float maxAge =10f;
    [Tooltip("Age at which the animal begins to die from old age.")]
    public float dieAtAge =7f;

    [Header("Lifespan Settings")]
    [Tooltip("Whether the animal has achieved legendary status (cannot die of old age).")]
    public bool isLegendary = false;

    // Baker class to bake the GameObject into an Entity
    class Baker : Baker<AnimalAuthoring>
    {
        public override void Bake(AnimalAuthoring authoring)
        {
            // Get the Entity for this GameObject
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PrefabRef { Prefab = entity });

            // Add core components for the animal entity
            AddComponent(entity, new HealthDamage
            {
                CurrentHealth = authoring.health,
                MaxHealth = authoring.health, // Max health same as starting health for now
                MinDamage = authoring.minDamage,
                MaxDamage = authoring.maxDamage
            });

            // Add Lifespan component (with randomized lifespan logic)
            // Use a variable seed so entities don't all get the exact same lifespan.
            var rnd = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, int.MaxValue));
            var lifespan = new Lifespan { MinAge =5f, MaxAge = authoring.maxAge, IsLegendary = authoring.isLegendary };
            lifespan.RandomizeLifespan(rnd); // Randomize the lifespan

            AddComponent(entity, lifespan);

            // Add movement component
            // Ensure horizontal movement (y =0) and normalized direction to avoid vertical drift.
            var dir = UnityEngine.Random.onUnitSphere;
            dir.y =0f;
            var horiz = new float3(dir.x,0f, dir.z);
            if (math.lengthsq(horiz) <=0f)
                horiz = new float3(1f,0f,0f);
            horiz = math.normalize(horiz);

            AddComponent(entity, new Movement { Direction = horiz, Speed =2f });

            // Add reproduction data (use inspector Vector2 -> float2 conversion)
            AddComponent(entity, new ReproductionData
            {
                ReproductionTimer =0f,
                FertilityWindow = new float2(authoring.fertilityWindow.x, authoring.fertilityWindow.y)
            });

            // Add biological gender component
            AddComponent(entity, new BiologicalGender { IsMale = authoring.male });

            // Add feeding behavior component (animals will vary based on diet)
            AddComponent(entity, new FeedingBehavior
            {
                CanEatMeat = (authoring.diet == DietType.Carnivore || authoring.diet == DietType.Omnivore),
                CanEatVegetation = (authoring.diet != DietType.Carnivore)
            });

            // Add fleeing state components (for prey and predator behavior)
            AddComponent(entity, new PreyFleeingState { IsFleeing = false });

            // Add the group behavior (Herd behavior for prey or pack behavior for predators)
            AddComponent(entity, new GroupBehavior { GroupId =1, GroupSize =1, CohesionFactor =1.0f });

            // Add the PackSize component (for pack behavior in predators, not used for prey)
            AddComponent(entity, new PackSize { Value =1 });

            // Add threat state (used by prey to detect predators)
            AddComponent(entity, new ThreatState { IsFleeing = false, IsInConflict = false, Target = Entity.Null });
        }
    }
}