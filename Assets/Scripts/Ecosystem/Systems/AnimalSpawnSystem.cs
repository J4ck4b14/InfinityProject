using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

#region Buffer Element (shared with Baker)

/// <summary>
/// Must match the buffer element from AnimalStoreAuthor.Baker.
/// </summary>
public struct AnimalSpawnInfo : IBufferElementData
{
    public Entity EntityPrefab;
    public int Amount;
}

#endregion

/// <summary>
/// Runs once at startup (InitializationSystemGroup):
/// 1. Reads every DynamicBuffer<AnimalSpawnInfo> on all "store" entities.
/// 2. Instantiates each prefab N times, placing them on the active Terrain.
/// 3. Destroys the buffer so it never runs again.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[RequireMatchingQueriesForUpdate]  // Only runs if any entity has AnimalSpawnInfo buffer
public partial class AnimalSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // Cache references for terrain sampling
        var terrain = Terrain.activeTerrain;
        var tData = terrain != null ? terrain.terrainData : null;

        Entities
            .WithName("InitialAnimalSpawn")
            // We need structural changes to call Instantiate() & RemoveComponent()
            .WithStructuralChanges()
            .ForEach((Entity storeEntity, DynamicBuffer<AnimalSpawnInfo> buf) =>
            {
                // Spawn each species entry in the buffer
                for (int i = 0; i < buf.Length; i++)
                {
                    var info = buf[i];
                    for (int j = 0; j < info.Amount; j++)
                    {
                        // 1) Instantiate the prefab entity
                        var instance = EntityManager.Instantiate(info.EntityPrefab);

                        // 2) Choose a random X,Z within terrain bounds
                        float x = tData != null
                            ? UnityEngine.Random.Range(0f, tData.size.x)
                            : 0f;
                        float z = tData != null
                            ? UnityEngine.Random.Range(0f, tData.size.z)
                            : 0f;
                        // 3) Sample height at that point
                        float y = terrain != null
                            ? terrain.SampleHeight(new Vector3(x, 0f, z))
                            : 0f;

                        // 4) Assign its LocalTransform so it appears on the ground
                        EntityManager.SetComponentData(instance, new LocalTransform
                        {
                            Position = new float3(x, y, z),
                            Rotation = quaternion.identity,
                            Scale = 1f
                        });
                    }
                }

                // Remove the buffer to prevent re-spawning on next frames
                EntityManager.RemoveComponent<AnimalSpawnInfo>(storeEntity);

            })
            .Run();  // Must end in Run() when using structural changes
    }
}