using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AnimalAuthoring : MonoBehaviour
{
    public enum DietType
    {
        Carnivore,
        Herbivore,
        Omnivore
    }

    [Header("Life Settings")]
    [Tooltip("Health of the animal (starting health).")]
    public float health = 100f;
    [Tooltip("Maximum damage the animal can deal (relevant for predators).")]
    public float maxDamage = 5f;
    [Tooltip("Minimum damage the animal can deal (relevant for predators).")]
    public float minDamage = 1f;
    [Tooltip("Whether the animal is fertile and able to reproduce.")]
    public bool fertile = true;
    [Tooltip("Gender of the animal (true = male, false = female).")]
    public bool male = true;
    [Tooltip("Age window where the species is fertile")]
    public float2 fertilityWindow = new();
    [Tooltip("Time (in seconds) before reproduction can happen again.")]
    public int timeForReproduction = 10;
    [Tooltip("Diet type of the animal (Carnivore, Herbivore, Omnivore).")]
    public DietType diet = DietType.Herbivore;

    [Header("Age Settings")]
    [Tooltip("Maximum age the animal can live.")]
    public float maxAge = 10f;
    [Tooltip("Age at which the animal begins to die from old age.")]
    public float dieAtAge = 7f;

    [Header("Lifespan Settings")]
    [Tooltip("Whether the animal has achieved legendary status (cannot die of old age).")]
    public bool isLegendary = false;

    class Baker : Baker<AnimalAuthoring>
    {
        public override void Bake(AnimalAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new HealthDamage
            {
                CurrentHealth = authoring.health,
                MaxHealth = authoring.health,
                MinDamage = authoring.minDamage,
                MaxDamage = authoring.maxDamage
            });

            AddComponent(entity, new Lifespan
            {
                MinAge = authoring.fertilityWindow.x,
                MaxAge = authoring.maxAge,
                IsLegendary = authoring.isLegendary,
                Age = 0f
            });

            AddComponent(entity, new Movement
            {
                Direction = UnityEngine.Random.onUnitSphere,
                Speed = 2f
            });

            AddComponent(entity, new ReproductionData
            {
                ReproductionTimer = authoring.timeForReproduction,
                FertilityWindow = authoring.fertilityWindow
            });

            AddComponent(entity, new BiologicalGender
            {
                IsMale = authoring.male
            });

            AddComponent(entity, new FeedingBehavior
            {
                CanEatMeat = authoring.diet != DietType.Herbivore,
                CanEatVegetation = authoring.diet != DietType.Carnivore
            });

            AddComponent(entity, new PreyFleeingState
            {
                IsFleeing = false
            });

            AddComponent(entity, new GroupBehavior
            {
                GroupId = 1,
                GroupSize = 1,
                CohesionFactor = 1.0f
            });

            AddComponent(entity, new PackSize
            {
                Value = 1
            });

            AddComponent(entity, new ThreatState
            {
                IsFleeing = false,
                IsInConflict = false,
                Target = Entity.Null
            });

            AddComponent(entity, new PrefabRef
            {
                Prefab = entity
            });
        }
    }
}
