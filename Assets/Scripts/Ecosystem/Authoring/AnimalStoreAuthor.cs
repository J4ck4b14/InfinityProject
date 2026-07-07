using System;
using Unity.Entities;
using UnityEngine;

public class AnimalStoreAuthor : MonoBehaviour
{
    [Serializable]
    public struct AnimalEntry
    {
        [Tooltip("GameObject prefab with AnimalAuthoring on it.")]
        public GameObject prefab;
        [Tooltip("How many to spawn at startup.")]
        public int spawnCount;
    }

    public AnimalEntry[] animals;

    class Baker : Baker<AnimalStoreAuthor>
    {
        public override void Bake(AnimalStoreAuthor authoring)
        {
            var storeEntity = GetEntity(TransformUsageFlags.None);
            var spawnBuffer = AddBuffer<AnimalSpawnInfo>(storeEntity);

            foreach (var entry in authoring.animals)
            {
                if (entry.prefab == null || entry.spawnCount <= 0) continue;

                // DependsOn ensures the baker re-runs if the prefab changes,
                // and registers the prefab so GetEntity can resolve it correctly.
                DependsOn(entry.prefab);

                var prefabEntity = GetEntity(entry.prefab, TransformUsageFlags.Dynamic);

                spawnBuffer.Add(new AnimalSpawnInfo
                {
                    EntityPrefab = prefabEntity,
                    Amount       = entry.spawnCount
                });
            }
        }
    }
}
