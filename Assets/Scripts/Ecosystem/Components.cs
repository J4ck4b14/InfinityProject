using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

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
