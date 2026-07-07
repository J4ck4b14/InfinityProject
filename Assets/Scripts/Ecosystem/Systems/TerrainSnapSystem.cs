using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Runs on the main thread after MovementSystem.
/// Samples Unity's active Terrain height at each animal's XZ position and
/// snaps the Y coordinate to the surface, preventing animals from sinking
/// into or floating above the terrain.
///
/// This must be a plain (non-Burst, non-job) system because Terrain.SampleHeight
/// is a managed Unity API that cannot be called from a Burst job.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystem))]
public partial class TerrainSnapSystem : SystemBase
{
    private Terrain _terrain;

    protected override void OnUpdate()
    {
        // Cache terrain reference — find it once, reuse until it changes
        if (_terrain == null)
        {
            _terrain = Terrain.activeTerrain;
            if (_terrain == null) return;   // no terrain in scene yet
        }

        // Iterate every entity that has a LocalTransform and a Movement component.
        // We use foreach directly on the main thread — no job, no Burst.
        foreach (var xf in SystemAPI.Query<RefRW<LocalTransform>>()
                                    .WithAll<Movement>())
        {
            float3 pos    = xf.ValueRO.Position;
            float  ground = _terrain.SampleHeight(new Vector3(pos.x, 0f, pos.z))
                          + _terrain.transform.position.y;

            // Only snap downward if below ground, or upward if above by more
            // than a small threshold — avoids jitter on perfectly flat areas.
            if (math.abs(pos.y - ground) > 0.01f)
            {
                xf.ValueRW.Position = new float3(pos.x, ground, pos.z);
            }
        }
    }
}
