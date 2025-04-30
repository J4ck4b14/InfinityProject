using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class AnimalSpawnSystem : SystemBase  // ← note the 'partial'
{
    protected override void OnUpdate()
    {
        // Cache the active terrain
        Terrain terrain = Terrain.activeTerrain;
        TerrainData tData = terrain != null ? terrain.terrainData : null;

        Entities
            .WithName("InitialAnimalSpawn")
            .WithStructuralChanges()      // allow Instantiate & RemoveComponent
            .ForEach((Entity spawner, DynamicBuffer<AnimalSpawnInfo> buf) =>
            {
                if (buf.Length == 0) return;

                for (int i = 0; i < buf.Length; i++)
                {
                    var info = buf[i];
                    for (int j = 0; j < info.Amount; j++)
                    {
                        // Instantiate the prefab entity
                        Entity baby = EntityManager.Instantiate(info.EntityPrefab);

                        if (tData != null)
                        {
                            float x = UnityEngine.Random.Range(0f, tData.size.x);
                            float z = UnityEngine.Random.Range(0f, tData.size.z);
                            float y = terrain.SampleHeight(new Vector3(x, 0f, z));

                            // Assign its transform
                            EntityManager.SetComponentData(baby, new LocalTransform
                            {
                                Position = new float3(x, y, z),
                                Rotation = quaternion.identity,
                                Scale = 1f
                            });
                        }
                    }
                }

                // Remove our buffer so we only spawn once
                EntityManager.RemoveComponent<AnimalSpawnInfo>(spawner);
            })
            .Run();  // must terminate in Run() or Schedule…
    }
}
