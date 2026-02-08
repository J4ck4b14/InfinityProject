using System;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring MonoBehaviour for "storing" which animal prefabs to spawn, and how many.
/// During conversion it writes a single ECS buffer (prefab refs + counts) onto one "store" entity.
/// A dedicated runtime system will later read that buffer, perform actual instantiation,
/// then destroy the store so it only runs once.
/// </summary>
public class AnimalStoreAuthor : MonoBehaviour
{
    [Serializable]
    public struct AnimalEntry
    {
        [Tooltip("GameObject prefab must have your AnimalAuthoring Baker on it.")]
        public GameObject prefab;
        [Tooltip("How many instances of this prefab to spawn at startup.")]
        public int spawnCount;
    }

    [Tooltip("Configure each animal type and its spawn amount here.")]
    public AnimalEntry[] animals;

    // Baker runs at convert-time (in the editor or in build) to fill ECS buffers.
    class Baker : Baker<AnimalStoreAuthor>
    {
        public override void Bake(AnimalStoreAuthor authoring)
        {
            // Create a single entity that holds our spawn buffer.
            var storeEntity = GetEntity(TransformUsageFlags.None);

            // Add and populate the runtime spawn buffer expected by AnimalSpawnSystem:
            // AnimalSpawnInfo { EntityPrefab, Amount }
            var spawnBuffer = AddBuffer<AnimalSpawnInfo>(storeEntity);

            foreach (var entry in authoring.animals)
            {
                // Convert each GameObject prefab into its converted-entity form.
                var prefabEntity = GetEntity(entry.prefab, TransformUsageFlags.Dynamic);

                spawnBuffer.Add(new AnimalSpawnInfo
                {
                    EntityPrefab = prefabEntity,
                    Amount = entry.spawnCount
                });
            }

            // NOTE: No EntityManager.Instantiate() calls here!
            // This is purely data collection for runtime use.
        }
    }
}
