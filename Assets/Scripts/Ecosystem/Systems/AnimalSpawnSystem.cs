using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
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
///1. Reads every DynamicBuffer<AnimalSpawnInfo> on all "store" entities.
///2. Instantiates each prefab N times, placing them on the active Terrain.
///3. Destroys the buffer so it never runs again.
/// 
/// This version uses a Burst job to sample terrain heights and normals in parallel
/// then applies rotations that align the spawned animals to the terrain slope.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[RequireMatchingQueriesForUpdate] // Only runs if any entity has AnimalSpawnInfo buffer
public partial class AnimalSpawnSystem : SystemBase
{
 [BurstCompile]
 struct SampleHeightAndNormalJob : IJobParallelFor
 {
 [ReadOnly] public NativeArray<float2> PositionsXZ; // world-space X,Z
 [ReadOnly] public int HeightmapResolution;
 [ReadOnly] public float TerrainSizeX;
 [ReadOnly] public float TerrainSizeZ;
 [ReadOnly] public float TerrainSizeY; // height scale
 [ReadOnly] public float TerrainOriginX;
 [ReadOnly] public float TerrainOriginZ;
 [ReadOnly] public NativeArray<float> Heightmap; // normalized [0..1] heights, row-major (y * res + x)

 [WriteOnly] public NativeArray<float3> OutPositions; // world-space positions with Y filled
 [WriteOnly] public NativeArray<quaternion> OutRotations; // rotation aligned to normal

 public void Execute(int index)
 {
 float2 pos = PositionsXZ[index];
 // Convert world XZ into terrain local coordinates by subtracting origin
 float localX = pos.x - TerrainOriginX;
 float localZ = pos.y - TerrainOriginZ;

 // Normalized uv inside [0,1]
 float u = math.clamp(localX / TerrainSizeX,0f,1f);
 float v = math.clamp(localZ / TerrainSizeZ,0f,1f);

 float fx = u * (HeightmapResolution -1);
 float fy = v * (HeightmapResolution -1);

 int ix = (int)math.floor(fx);
 int iy = (int)math.floor(fy);
 float tx = fx - ix;
 float ty = fy - iy;

 // Bilinear interpolation for height (normalized)
 int sx, sy;

 sx = math.clamp(ix,0, HeightmapResolution -1);
 sy = math.clamp(iy,0, HeightmapResolution -1);
 float h00 = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix +1,0, HeightmapResolution -1);
 sy = math.clamp(iy,0, HeightmapResolution -1);
 float h10 = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix,0, HeightmapResolution -1);
 sy = math.clamp(iy +1,0, HeightmapResolution -1);
 float h01 = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix +1,0, HeightmapResolution -1);
 sy = math.clamp(iy +1,0, HeightmapResolution -1);
 float h11 = Heightmap[sy * HeightmapResolution + sx];

 float hLerpX0 = math.lerp(h00, h10, tx);
 float hLerpX1 = math.lerp(h01, h11, tx);
 float hNorm = math.lerp(hLerpX0, hLerpX1, ty);
 float worldY = hNorm * TerrainSizeY;

 // For normal compute central differences in heightmap-space (one texel step)
 sx = math.clamp(ix -1,0, HeightmapResolution -1);
 sy = math.clamp(iy,0, HeightmapResolution -1);
 float left = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix +1,0, HeightmapResolution -1);
 sy = math.clamp(iy,0, HeightmapResolution -1);
 float right = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix,0, HeightmapResolution -1);
 sy = math.clamp(iy -1,0, HeightmapResolution -1);
 float down = Heightmap[sy * HeightmapResolution + sx];

 sx = math.clamp(ix,0, HeightmapResolution -1);
 sy = math.clamp(iy +1,0, HeightmapResolution -1);
 float up = Heightmap[sy * HeightmapResolution + sx];

 // Build tangent vectors in world-space then cross to get normal
 float stepX = TerrainSizeX / (HeightmapResolution -1);
 float stepZ = TerrainSizeZ / (HeightmapResolution -1);

 float3 vx = new (stepX, (right - left) * TerrainSizeY,0f);
 float3 vz = new (0f, (up - down) * TerrainSizeY, stepZ);
 float3 normal = math.normalize(math.cross(vz, vx));
 if (math.any(math.isnan(normal))) normal = new float3(0f,1f,0f);

 // Create a random yaw per-instance from a simple hash on index so results are deterministic-ish
 uint seed = (uint)(index *747796405u +2891336453u);
 float rnd = math.frac(math.sin((float)seed) *43758.5453f);
 float yaw = rnd * math.PI *2f;

 // Compute forward vector from yaw on tangent plane
 float3 forward = new (math.sin(yaw),0f, math.cos(yaw));
 forward -= normal * math.dot(forward, normal); // project onto tangent
 if (math.lengthsq(forward) <=0f)
 forward = math.normalize(new float3(1f,0f,0f) - normal * math.dot(new float3(1f,0f,0f), normal));
 else
 forward = math.normalize(forward);

 quaternion rot = quaternion.LookRotationSafe(forward, normal);

 OutPositions[index] = new(pos.x, worldY, pos.y);
 OutRotations[index] = rot;
 }
 }

 protected override void OnUpdate()
 {
 // Cache references for terrain sampling
 var terrain = Terrain.activeTerrain;
 var tData = terrain != null ? terrain.terrainData : null;

 // Query all entities that have the AnimalSpawnInfo buffer
 var query = GetEntityQuery(ComponentType.ReadOnly<AnimalSpawnInfo>());
 using var storeEntities = query.ToEntityArray(Allocator.Temp);
 {
 foreach (var storeEntity in storeEntities)
 {
 var buf = EntityManager.GetBuffer<AnimalSpawnInfo>(storeEntity);

 // Count total instances to spawn so we can batch-sample heights
 int totalToSpawn =0;
 for (int i =0; i < buf.Length; i++) totalToSpawn += math.max(0, buf[i].Amount);
 if (totalToSpawn ==0)
 {
 // Remove buffer and continue
 EntityManager.RemoveComponent<AnimalSpawnInfo>(storeEntity);
 continue;
 }

 NativeArray<float2> positionsXZ = default;
 NativeArray<float3> outPositions = default;
 NativeArray<quaternion> outRotations = default;
 NativeArray<float> heightmapNative = default;

 try
 {
 positionsXZ = new(totalToSpawn, Allocator.TempJob);
 outPositions = new(totalToSpawn, Allocator.TempJob);
 outRotations = new(totalToSpawn, Allocator.TempJob);

 // Fill positions with random X,Z in terrain bounds (or zero if no terrain)
 Unity.Mathematics.Random rng = new((uint)UnityEngine.Random.Range(1, int.MaxValue));

 float terrainSizeX =0f, terrainSizeZ =0f, terrainSizeY =1f;
 int hmRes =0;
 float terrainOriginX =0f, terrainOriginZ =0f;

 if (tData != null)
 {
 terrainSizeX = tData.size.x;
 terrainSizeZ = tData.size.z;
 terrainSizeY = tData.size.y;
 hmRes = tData.heightmapResolution;

 // Store terrain origin for converting world -> local coordinates inside job
 var terrainWorldPos = terrain.GetPosition();
 terrainOriginX = terrainWorldPos.x;
 terrainOriginZ = terrainWorldPos.z;

 // Copy heightmap into a NativeArray<float> (row-major y*res + x)
 float[,] hm = tData.GetHeights(0,0, hmRes, hmRes);
 heightmapNative = new(hmRes * hmRes, Allocator.TempJob);
 for (int y =0; y < hmRes; y++)
 for (int x =0; x < hmRes; x++)
 heightmapNative[y * hmRes + x] = hm[y, x];
 }

 int idx =0;
 for (int i =0; i < buf.Length; i++)
 {
 var info = buf[i];
 for (int j =0; j < info.Amount; j++)
 {
 float x = (tData != null) ? rng.NextFloat(0f, terrainSizeX) + terrainOriginX :0f;
 float z = (tData != null) ? rng.NextFloat(0f, terrainSizeZ) + terrainOriginZ :0f;
 positionsXZ[idx++] = new(x, z);
 }
 }

 JobHandle handle = default;
 if (tData != null && hmRes >0)
 {
 // Schedule job to sample heights and compute normals/rotations
 var job = new SampleHeightAndNormalJob
 {
 PositionsXZ = positionsXZ,
 HeightmapResolution = hmRes,
 TerrainSizeX = terrainSizeX,
 TerrainSizeZ = terrainSizeZ,
 TerrainSizeY = terrainSizeY,
 TerrainOriginX = terrainOriginX,
 TerrainOriginZ = terrainOriginZ,
 Heightmap = heightmapNative,
 OutPositions = outPositions,
 OutRotations = outRotations
 };

 handle = job.Schedule(totalToSpawn,64);
 handle.Complete();
 }
 else
 {
 // No terrain: place at y=0 and identity rotation
 for (int i =0; i < totalToSpawn; i++)
 {
 outPositions[i] = new(positionsXZ[i].x,0f, positionsXZ[i].y);
 outRotations[i] = quaternion.identity;
 }
 }

 // Finally instantiate and assign LocalTransform using computed positions & rotations
 int outIdx =0;
 for (int i =0; i < buf.Length; i++)
 {
 var info = buf[i];
 for (int j =0; j < info.Amount; j++)
 {
 var instance = EntityManager.Instantiate(info.EntityPrefab);

 var pos = outPositions[outIdx];
 var rot = outRotations[outIdx];
 outIdx++;

 EntityManager.SetComponentData(instance, new LocalTransform
 {
 Position = pos,
 Rotation = rot,
 Scale =1f
 });
 }
 }

 // Remove the buffer to prevent re-spawning on next frames
 EntityManager.RemoveComponent<AnimalSpawnInfo>(storeEntity);

 // Ensure scheduled job is completed and dependencies are handled (we completed above)
 if (handle.IsCompleted)
 handle.Complete();
 }
 finally
 {
 if (positionsXZ.IsCreated) positionsXZ.Dispose();
 if (outPositions.IsCreated) outPositions.Dispose();
 if (outRotations.IsCreated) outRotations.Dispose();
 if (heightmapNative.IsCreated) heightmapNative.Dispose();
 }
 }
 }
 }
}