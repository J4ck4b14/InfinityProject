using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


// Runtime
[InternalBufferCapacity(64)]
struct AnimalPrefab : IBufferElementData
{
    public Entity EntityPrefab;
}

public class AnimalStoreAuthor : MonoBehaviour
{
    [Serializable]
    public struct AnimalSetupInfo
    {
        public AnimalAuthoring animalPrefab; // The prefab for deer or wolf
        [Tooltip("How many, beauty?")]
        public int spawnAmmount;
    }

    [Serializable]
    public struct SpawnInfo
    {
        public AnimalSetupInfo animal;
    }

    public SpawnInfo[] animals;

    // Baker class to bake the GameObject into an Entity
    class Baker : Baker<AnimalStoreAuthor>
    {
        public override void Bake(AnimalStoreAuthor authoring)
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            // Get the Entity for this GameObject
            var entity = GetEntity(TransformUsageFlags.None);
            var animalPrefabs = AddBuffer<AnimalPrefab>(entity);

            foreach (var spawn in authoring.animals)
            {

                // Log to check if we are iterating over all the spawn entries correctly
                Debug.Log($"Processing {spawn.animal} animal types in this spawn entry.");
                var animalEntity = GetEntity(spawn.animal.animalPrefab.gameObject, TransformUsageFlags.Dynamic | TransformUsageFlags.WorldSpace);

                // Add animal prefab to buffer
                animalPrefabs.Add(new AnimalPrefab { EntityPrefab = animalEntity });

                for (int j = 0; j < spawn.animal.spawnAmmount; j++)
                {
                    // Instantiate the animal prefab
                    Entity newAnimal = entityManager.Instantiate(animalEntity);

                    Terrain terrain = Terrain.activeTerrain;

                    if (terrain != null)
                    {
                        // Get the terrain's size (width and length)
                        float terrainWidth = terrain.terrainData.size.x;
                        float terrainLength = terrain.terrainData.size.z;

                        // Random position for spawning animals (within the terrain's x, z bounds)
                        float x = UnityEngine.Random.Range(0f, terrainWidth);  // Random x position on the terrain
                        float z = UnityEngine.Random.Range(0f, terrainLength); // Random z position on the terrain

                        // Get the height of the terrain at the random x, z coordinates
                        float y = terrain.SampleHeight(new Vector3(x, 0f, z));

                        // Set the spawn position for the new animal (on the terrain surface)
                        var spawnPosition = new float3(x, y, z);

                        // Set the spawn position for the new animal
                        entityManager.SetComponentData(newAnimal, new LocalTransform { Position = spawnPosition });

                        // Add additional components as needed (e.g., Health, Movement, Lifespan, etc.)
                        // Initialize health (half of the max health for babies)
                        entityManager.AddComponentData(newAnimal, new HealthDamage { CurrentHealth = 50f, MaxHealth = 100f, MinDamage = 1f, MaxDamage = 5f });

                        // Initialize movement (random direction, default speed)
                        entityManager.AddComponentData(newAnimal, new Movement { Direction = UnityEngine.Random.onUnitSphere, Speed = 2f });

                        // Add the lifespan (set to 0 age initially for new animals)
                        entityManager.AddComponentData(newAnimal, new Lifespan { Age = 0f, MaxAge = 10f, HeroAge = 12f, LegendAge = 15f });

                        // Set fertility state (initially non-fertile for babies)
                        entityManager.AddComponentData(newAnimal, new ReproductionData { ReproductionTimer = 0f, FertilityWindow = new float2(0.5f, 5f) });

                        // Optionally, assign the animal to a group (e.g., group 1 for a herd or pack)
                        entityManager.AddComponentData(newAnimal, new GroupBehavior { GroupId = 1, GroupSize = 1, CohesionFactor = 1f });
                    }
                    else
                    {
                        Debug.LogError("NO TERRAIN DETECTED.");
                    }

                }

            }
        }
    }
}