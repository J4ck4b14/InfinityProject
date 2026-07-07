using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateBefore(typeof(AnimalSpawnSystem))]
public partial class AnimalSpawnDiagnostic : SystemBase
{
    private bool _ran   = false;
    private int  _frame = 0;

    protected override void OnUpdate()
    {
        _frame++;
        if (_frame < 5) return;
        if (_ran) return;
        _ran = true;

        // Check store entities
        var query = GetEntityQuery(ComponentType.ReadOnly<AnimalSpawnInfo>());
        int storeCount = query.CalculateEntityCount();

        if (storeCount == 0)
        {
            Debug.LogWarning("[AnimalSpawn] No AnimalSpawnInfo entities found after 5 frames.");
            return;
        }

        int total = 0;
        using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var e in entities)
        {
            var buf = EntityManager.GetBuffer<AnimalSpawnInfo>(e);
            for (int i = 0; i < buf.Length; i++)
            {
                int amt = buf[i].Amount;
                bool prefabValid = buf[i].EntityPrefab != Entity.Null;
                bool prefabExists = EntityManager.Exists(buf[i].EntityPrefab);
                Debug.Log($"[AnimalSpawn] Entry {i}: amount={amt}, prefab valid={prefabValid}, prefab exists={prefabExists}");
                total += amt;
            }
        }

        // Check terrain
        var terrain = Terrain.activeTerrain;
        Debug.Log($"[AnimalSpawn] Terrain.activeTerrain = {(terrain != null ? terrain.name : "NULL")}");
        Debug.Log($"[AnimalSpawn] Total to spawn: {total}");

        // Check how many Movement entities exist (= how many animals were actually spawned)
        var movQuery = GetEntityQuery(ComponentType.ReadOnly<Movement>());
        int spawned = movQuery.CalculateEntityCount();
        Debug.Log($"[AnimalSpawn] Entities with Movement component currently: {spawned}");
    }
}
