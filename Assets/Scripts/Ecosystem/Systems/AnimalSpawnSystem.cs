using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct AnimalSpawnInfo : IBufferElementData
{
    public Entity EntityPrefab;
    public int    Amount;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimalSpawnSystem : SystemBase
{
    private bool _ran = false;

    protected override void OnUpdate()
    {
        if (_ran) return;

        var query = GetEntityQuery(ComponentType.ReadOnly<AnimalSpawnInfo>());
        if (query.CalculateEntityCount() == 0) return;

        _ran = true;

        var terrain = Terrain.activeTerrain;
        var td      = terrain?.terrainData;
        float tx = 0, tz = 0, ty = 1, tox = 0, toz = 0;
        int   hmRes = 0;
        NativeArray<float> hmap = default;

        if (td != null)
        {
            tx = td.size.x; tz = td.size.z; ty = td.size.y;
            hmRes = td.heightmapResolution;
            var op = terrain.transform.position;
            tox = op.x; toz = op.z;
            var raw = td.GetHeights(0, 0, hmRes, hmRes);
            hmap = new NativeArray<float>(hmRes * hmRes, Allocator.Temp);
            for (int y = 0; y < hmRes; y++)
            for (int x = 0; x < hmRes; x++)
                hmap[y * hmRes + x] = raw[y, x];
        }

        using var stores = query.ToEntityArray(Allocator.Temp);
        int spawned = 0;

        foreach (var store in stores)
        {
            var buf = EntityManager.GetBuffer<AnimalSpawnInfo>(store);
            for (int i = 0; i < buf.Length; i++)
            {
                var info = buf[i];
                if (info.EntityPrefab == Entity.Null || info.Amount <= 0) continue;
                var rng = new Unity.Mathematics.Random((uint)(i * 1000003u + 7u));

                for (int j = 0; j < info.Amount; j++)
                {
                    float wx = td != null ? rng.NextFloat(tox + 5f, tox + tx - 5f) : 0f;
                    float wz = td != null ? rng.NextFloat(toz + 5f, toz + tz - 5f) : 0f;
                    float wy = 0f;

                    if (td != null && hmRes > 0)
                    {
                        float u = math.saturate((wx - tox) / tx);
                        float v = math.saturate((wz - toz) / tz);
                        float fx = u * (hmRes - 1), fz = v * (hmRes - 1);
                        int ix = (int)fx, iz = (int)fz;
                        int ix2 = math.min(ix+1, hmRes-1), iz2 = math.min(iz+1, hmRes-1);
                        float h = math.lerp(
                            math.lerp(hmap[iz*hmRes+ix],  hmap[iz*hmRes+ix2],  fx-ix),
                            math.lerp(hmap[iz2*hmRes+ix], hmap[iz2*hmRes+ix2], fx-ix), fz-iz);
                        wy = h * ty;
                    }

                    var inst = EntityManager.Instantiate(info.EntityPrefab);
                    var rot  = quaternion.RotateY(rng.NextFloat(0f, math.PI * 2f));
                    EntityManager.SetComponentData(inst,
                        LocalTransform.FromPositionRotationScale(new float3(wx, wy, wz), rot, 1f));
                    spawned++;
                }
            }
            EntityManager.RemoveComponent<AnimalSpawnInfo>(store);
        }

        if (hmap.IsCreated) hmap.Dispose();
        Debug.Log($"[AnimalSpawn] Spawned {spawned} animals.");
    }
}
