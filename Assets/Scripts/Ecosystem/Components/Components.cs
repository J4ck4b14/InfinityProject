using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Represents the health and damage capabilities of an entity (both predator and prey).
/// Combines health and damage information into a single component.
/// </summary>
public struct HealthDamage : IComponentData
{
    public float CurrentHealth;  // Current health of the entity (0 = dead, 1 = full health)
    public float MaxHealth;      // Maximum health the entity can have
    public float MinDamage;      // Minimum damage the entity can deal
    public float MaxDamage;      // Maximum damage the entity can deal
}

/// <summary>
/// Represents the lifespan data for an entity, including minimum and maximum age,
/// as well as heroic and legendary status based on age.
/// </summary>
public struct Lifespan : IComponentData
{
    public float MinAge;   // Minimum age the entity can live (e.g., 5 years)
    public float MaxAge;   // Maximum age the entity can live (e.g., 10 years)
    public float HeroAge;  // Age at which the entity becomes a hero (e.g., 12 years)
    public float LegendAge; // Age at which the entity becomes a legend (e.g., 15 years)
    public bool IsLegendary;  // True if the entity is legendary (it can't die of old age)
    public float Age;       // Current age of the entity

    /// <summary>
    /// Randomizes the lifespan based on a Gaussian or normal distribution.
    /// Sets the age, hero age, and legendary age.
    /// </summary>
    /// <param name="random">Random number generator</param>
    public void RandomizeLifespan(Unity.Mathematics.Random random)
    {
        Age = random.NextFloat(MinAge, MaxAge);  // Random age between MinAge and MaxAge
        HeroAge = Age + 3f;   // Heroic age starts a bit after the random lifespan
        LegendAge = Age + 5f; // Legendary age starts even later
    }
}

/// <summary>a
/// Represents the movement data of an entity (both predator and prey).
/// Controls direction and speed of the entity.
/// </summary>
public struct Movement : IComponentData
{
    public float3 Direction;  // The direction the entity is moving towards
    public float Speed;       // The speed at which the entity moves
}

/// <summary>
/// Represents the alert state of an entity (both predator and prey).
/// Tracks whether the entity is alert or not, which affects its behavior towards other entities.
/// </summary>
public struct AlertState : IComponentData
{
    public bool IsAlert;  // Whether the entity is aware of potential danger (predators or prey)
}

/// <summary>
/// Represents the reproduction data of an entity (both predator and prey).
/// Controls the reproduction cycle, fertility window, and reproduction cooldown.
/// </summary>
public struct ReproductionData : IComponentData
{
    public float ReproductionTimer;   // Timer to track the next reproduction event
    public float2 FertilityWindow;   // The time window when the entity can reproduce (e.g., age range)
}

/// <summary>
/// Represents the gender of an entity (both predator and prey).
/// Determines the gender (male/female) for reproduction and group behavior.
/// </summary>
public struct BiologicalGender : IComponentData
{
    public bool IsMale;  // True if the entity is male, false if female
}

/// <summary>
/// Represents the feeding behavior of an entity (prey, predator, or omnivore).
/// Defines whether the entity can eat meat and/or vegetation.
/// </summary>
public struct FeedingBehavior : IComponentData
{
    public bool CanEatMeat;      // True if the entity can eat meat
    public bool CanEatVegetation; // True if the entity can eat vegetation
    public bool IsOmnivore => CanEatMeat && CanEatVegetation; // True if omnivore (eats both)
    public bool IsCarnivore => CanEatMeat && !CanEatVegetation; // True if carnivore (eats meat)
    public bool IsHerbivore => !CanEatMeat && CanEatVegetation; // True if herbivore (eats plants)
}

/// <summary>
/// Represents the fleeing state of a predator. Predators may flee from stronger threats.
/// </summary>
public struct PredatorFleeingState : IComponentData
{
    public bool IsFleeing;  // True if the predator is fleeing from a threat (e.g., humans or other predators)
}

/// <summary>
/// Represents the fleeing state of prey. Prey may flee when they detect a predator nearby.
/// </summary>
public struct PreyFleeingState : IComponentData
{
    public bool IsFleeing;  // True if the prey is fleeing from a predator
}

/// <summary>
/// Represents the state of conflict or fleeing behavior for an entity.
/// This combines both fleeing and conflict states into one component.
/// </summary>
public struct ThreatState : IComponentData
{
    public bool IsFleeing;    // True if the entity is fleeing from danger
    public bool IsInConflict; // True if the entity is in conflict (e.g., combat)
    public Entity Target;     // The target of the conflict (could be a predator or prey)
}

/// <summary>
/// Represents the group behavior of an entity (both predator and prey).
/// The group ID is used to identify which group the entity belongs to (herd, pack, etc.).
/// </summary>
public struct GroupBehavior : IComponentData
{
    public int GroupId;      // The ID of the group the entity belongs to (e.g., herd or pack)
    public int GroupSize;    // The number of entities in the group
    public float CohesionFactor;  // How strongly the group tries to stay together (higher = more cohesive)
}

/// <summary>
/// Represents the size of a predator pack. Used for managing the behavior of predators hunting in groups.
/// </summary>
public struct PackSize : IComponentData
{
    public int Value;  // Number of predators in the pack (relevant for pack-based behavior)
}

/// <summary>
/// Holds the prefab-entity to use when spawning offspring.
/// </summary>
public struct PrefabRef : IComponentData
{
    public Entity Prefab;
}

/// <summary>
/// Tracks how hungry this animal is.
/// HungerRate is expressed in "hunger-units per in-game year" so it scales
/// automatically with the time system (deltaYears).
///
/// Thresholds (normalised 0–1):
///   < 0.3  -> well-fed, mating conditions can be met
///   >= 0.6  -> hungry, overrides Mate goal -> switches to Hunt/Forage
///   >= 0.9  -> starving, movement speed penalty applied
///   >= 1.0  -> dead (handled by DeathSystem)
/// </summary>
public struct Hunger : IComponentData
{
    /// <summary>Current hunger level [0 = full … 1 = starving].</summary>
    public float Level;

    /// <summary>How quickly hunger rises per in-game year (e.g. 0.5 = half-starved in one year).</summary>
    public float HungerRate;

    /// <summary>How much hunger is restored by eating one unit of food.</summary>
    public float FoodRestoreAmount;

    /// <summary>
    /// Passive hunger reduction per in-game second for herbivores/omnivores
    /// when NOT fleeing (simulates background grazing on ambient vegetation).
    /// Set higher than HungerRate to keep deer fed at rest.
    /// </summary>
    public float GrazeRate;
}

/// <summary>
/// Defines the animal's perception field.
/// VisionScanSystem uses this to decide which nearby entities are "visible".
/// </summary>
public struct VisionCone : IComponentData
{
    /// <summary>Radius within which the animal can detect others (world units).</summary>
    public float Range;

    /// <summary>
    /// Half-angle of the forward cone in radians.
    /// Use math.PI for a full 360° scan (e.g. smell-based animals).
    /// Use math.PI * 0.35f (~63°) for a focused forward cone.
    /// </summary>
    public float HalfAngle;
}

/// <summary>
/// Dynamic buffer written every frame by VisionScanSystem.
/// Cleared and repopulated each tick — systems downstream only read it.
/// </summary>
public struct NearbyTarget : IBufferElementData
{
    /// <summary>Entity detected inside this animal's VisionCone.</summary>
    public Entity Entity;

    /// <summary>Squared distance to the target (cheaper than sqrt for comparisons).</summary>
    public float SqDist;

    /// <summary>True if the target is a potential threat (predator that can eat us).</summary>
    public bool IsThreat;

    /// <summary>True if the target is a potential food source (prey or plant).</summary>
    public bool IsFood;

    /// <summary>True if the target is a compatible mate (opposite sex, same species, fertile).</summary>
    public bool IsMate;
}

/// <summary>
/// Integer identifier for the species.  Used by VisionScanSystem to determine
/// predator/prey relationships and by ReproductionSystem for species-matching.
///
/// Convention: assign species IDs in your authoring data and keep a
/// ScriptableObject or static table that maps ID -> "can eat ID[]".
/// </summary>
public struct SpeciesTag : IComponentData
{
    public int SpeciesId;
}

/// <summary>
/// Written by decision systems (Flee/Hunt/Forage/Mate steering).
/// MovementSystem reads this to update Movement.Direction.
///
/// Priority (lower value = higher urgency):
///   0 Flee     — predator nearby, run away
///   1 Hunt     — prey in range, close in and attack
///   2 Forage   — hungry herbivore heading toward food patch
///   3 Mate     — heading toward compatible mate
///   4 Wander   — no pressing goal, random drift
/// </summary>
public struct SteeringGoal : IComponentData
{
    public GoalPriority Priority;

    /// <summary>World-space target position for Move/Flee/Forage/Mate goals.</summary>
    public float3 TargetPosition;

    /// <summary>Entity we are interacting with (attacker, prey, mate). Entity.Null if none.</summary>
    public Entity TargetEntity;

    /// <summary>
    /// Desired speed multiplier (1.0 = base speed).
    /// Fleeing animals use 1.0; wandering animals use ~0.5.
    /// Hunger >= 0.9 applies a starving penalty multiplied here.
    /// </summary>
    public float SpeedMultiplier;
}

public enum GoalPriority : byte
{
    Flee = 0,
    Hunt = 1,
    Forage = 2,
    Mate = 3,
    Wander = 4,
}
