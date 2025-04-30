using System;
using Unity.Entities;
using UnityEngine;

public class AnimalStoreAuthor : MonoBehaviour
{
    [Serializable]
    public struct AnimalSetupInfo
    {
        [Tooltip("The authoring prefab used to spawn this species")]
        public AnimalAuthoring PrefabAuthoring;

        [Tooltip("How many individuals to spawn initially")]
        public int SpawnAmount;
    }

    public AnimalSetupInfo[] animals;
}

// Buffer element definition (recorded by Baker)
public struct AnimalSpawnInfo : IBufferElementData
{
    public Entity EntityPrefab;
    public int Amount;
}


// Baker now only fills a DynamicBuffer<AnimalSpawnInfo>
class Baker : Baker<AnimalStoreAuthor>
{
    public override void Bake(AnimalStoreAuthor authoring)
    {
        // Create an entity for this spawner
        var e = GetEntity(TransformUsageFlags.Dynamic);

        // Add your DynamicBuffer<AnimalSpawnInfo>
        var buf = AddBuffer<AnimalSpawnInfo>(e);

        // For each entry in your inspector array...
        foreach (var setup in authoring.animals)
        {
            // Convert the GameObject into its Entity version
            var prefabEnt = GetEntity(
                setup.PrefabAuthoring.gameObject,
                TransformUsageFlags.Dynamic | TransformUsageFlags.WorldSpace
            );

            // Record “spawn X of this prefab”
            buf.Add(new AnimalSpawnInfo
            {
                EntityPrefab = prefabEnt,
                Amount = setup.SpawnAmount
            });
        }
    }
}


