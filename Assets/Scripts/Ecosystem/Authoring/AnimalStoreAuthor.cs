using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

#region Buffer Element Definitions

/// <summary>
/// Holds a reference to an animal-prefab entity.
/// Used by the runtime spawn system to know which prefab to instantiate.
/// </summary>
[InternalBufferCapacity(8)]
public struct AnimalPrefabBuffer : IBufferElementData
{
    public Entity Prefab;
}

/// <summary>
/// Holds the spawn count corresponding to each prefab in AnimalPrefabBuffer.
/// Ensures 1:1 indexing with the prefab buffer.
/// </summary>
[InternalBufferCapacity(8)]
public struct AnimalSpawnCountBuffer : IBufferElementData
{
    public int Count;
}

#endregion

/// <summary>
/// Authoring MonoBehaviour for "storing" which animal prefabs to spawn, and how many.
/// During conversion it writes two ECS buffers (prefab refs & counts) onto one "store" entity.
/// A dedicated runtime system will later read those buffers, perform actual instantiation,
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
            // Create a single entity that holds our two parallel buffers.
            var storeEntity = GetEntity(TransformUsageFlags.None);

            // Add and populate the prefab buffer.
            var prefabBuffer = AddBuffer<AnimalPrefabBuffer>(storeEntity);
            // Add and populate the spawn count buffer.
            var countBuffer = AddBuffer<AnimalSpawnCountBuffer>(storeEntity);

            foreach (var entry in authoring.animals)
            {
                // Convert each GameObject prefab into its converted-entity form.
                var prefabEntity = GetEntity(entry.prefab, TransformUsageFlags.Dynamic);

                prefabBuffer.Add(new AnimalPrefabBuffer { Prefab = prefabEntity });
                countBuffer.Add(new AnimalSpawnCountBuffer { Count = entry.spawnCount });
            }

            // NOTE: No EntityManager.Instantiate() calls here! 
            // This is purely data collection for runtime use.
        }
    }
}
