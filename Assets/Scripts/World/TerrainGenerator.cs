using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class TerrainGenerator
{
    public static float[] Generate(int res, float seed, float scale,
        float waterLevel = 0.3f, float seabedDepth = 0.15f,
        int oceanEdgesMask = 0, int beachFadeMetres = 80, float terrainWidth = 1000f)
    {
        int total = res * res;
        var heights = new NativeArray<float>(total, Allocator.TempJob);
        var blurred = new NativeArray<float>(total, Allocator.TempJob);
        var final   = new NativeArray<float>(total, Allocator.TempJob);

        try
        {
            new PerlinJob { res=res, seed=seed, scale=scale, heights=heights }
                .Schedule(total, 64).Complete();

            new BlurJob { res=res, src=heights, dst=blurred }
                .Schedule(total, 64).Complete();

            int fadeSamples = terrainWidth > 0f ? Mathf.RoundToInt(beachFadeMetres / terrainWidth * res) : 0;
            fadeSamples = Mathf.Max(fadeSamples, 1);

            new SeaJob
            {
                res          = res,
                waterLevel   = waterLevel,
                seabedDepth  = seabedDepth,
                oceanMask    = oceanEdgesMask,
                fadeSamples  = fadeSamples,
                src          = blurred,
                dst          = final
            }.Schedule(total, 64).Complete();

            var result = new float[total];
            NativeArray<float>.Copy(final, result, total);
            return result;
        }
        finally
        {
            heights.Dispose();
            blurred.Dispose();
            final.Dispose();
        }
    }

    public static void ApplyToTerrain(Terrain terrain, float[] heights, int res,
        float width, float height, float length)
    {
        var td = terrain.terrainData;
        td.heightmapResolution = res + 1;
        td.size = new Vector3(width, height, length);

        int hmRes = res + 1;
        var arr = new float[hmRes, hmRes];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
                arr[y, x] = heights[y * res + x];
            arr[y, res] = arr[y, res - 1];
        }
        for (int x = 0; x < hmRes; x++)
            arr[res, x] = arr[res - 1, x];

        td.SetHeights(0, 0, arr);
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────

    [BurstCompile]
    struct PerlinJob : IJobParallelFor
    {
        public int   res;
        public float seed, scale;
        [WriteOnly] public NativeArray<float> heights;

        public void Execute(int idx)
        {
            int   x  = idx % res;
            int   y  = idx / res;

            // World-space UV so scale is in metres regardless of resolution
            float u  = (float)x / (res - 1);
            float v  = (float)y / (res - 1);

            // scale = metres per noise tile on a 1000m terrain
            float sf = math.max(scale / 1000f, 0.001f);
            float px = (u + seed * 0.127f) / sf;
            float py = (v + seed * 0.311f) / sf;

            // 5-octave fBm with proper normalization
            float sum=0, amp=1, freq=1, wsum=0;
            for (int o = 0; o < 5; o++)
            {
                sum  += GradNoise(px*freq, py*freq) * amp;
                wsum += amp;
                amp  *= 0.5f;
                freq *= 2.0f;
            }

            heights[idx] = math.clamp(sum / wsum, 0f, 1f);
        }

        // Gradient noise — quintic fade, 8 directions, output [-0.707, 0.707]
        float GradNoise(float x, float y)
        {
            int   ix = (int)math.floor(x), iy = (int)math.floor(y);
            float fx = x - ix,             fy = y - iy;
            float ux = Fade(fx),           uy = Fade(fy);

            float g00 = Grad(Hash(ix,   iy  ), fx,   fy  );
            float g10 = Grad(Hash(ix+1, iy  ), fx-1, fy  );
            float g01 = Grad(Hash(ix,   iy+1), fx,   fy-1);
            float g11 = Grad(Hash(ix+1, iy+1), fx-1, fy-1);

            // Remap [-0.707,0.707] -> [0,1]
            float v2 = math.lerp(math.lerp(g00,g10,ux), math.lerp(g01,g11,ux), uy);
            return v2 / 0.7071068f * 0.5f + 0.5f;
        }

        float Fade(float t) => t*t*t*(t*(t*6f-15f)+10f);

        float Grad(uint h, float x, float y)
        {
            switch (h & 7u)
            {
                case 0: return  x;
                case 1: return -x;
                case 2: return  y;
                case 3: return -y;
                case 4: return  (x+y)*0.7071068f;
                case 5: return  (-x+y)*0.7071068f;
                case 6: return  (x-y)*0.7071068f;
                default:return  (-x-y)*0.7071068f;
            }
        }

        uint Hash(int x, int y)
        {
            uint h = (uint)(x*1619 + y*31337 + 1013904223);
            h ^= h >> 16; h *= 0x45d9f3b7u; h ^= h >> 16;
            return h;
        }
    }

    [BurstCompile]
    struct BlurJob : IJobParallelFor
    {
        public int res;
        [ReadOnly]  public NativeArray<float> src;
        [WriteOnly] public NativeArray<float> dst;

        public void Execute(int idx)
        {
            int x = idx % res, y = idx / res;
            float sum = 0f, wsum = 0f;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = math.clamp(x+dx, 0, res-1);
                int ny = math.clamp(y+dy, 0, res-1);
                float w = math.exp(-(dx*dx + dy*dy) * 0.5f);
                sum  += src[ny*res+nx] * w;
                wsum += w;
            }
            dst[idx] = sum / wsum;
        }
    }

    [BurstCompile]
    struct SeaJob : IJobParallelFor
    {
        public int   res, oceanMask, fadeSamples;
        public float waterLevel, seabedDepth;
        [ReadOnly]  public NativeArray<float> src;
        [WriteOnly] public NativeArray<float> dst;

        public void Execute(int idx)
        {
            int   x = idx % res, y = idx / res;
            float h = src[idx];

            // ── Ocean edge fade ───────────────────────────────────────────────
            // Each flagged edge pulls height down to a shallow sea level
            float edgeFade = 1f;
            if ((oceanMask & 1) != 0) edgeFade = math.min(edgeFade, math.saturate((float)(res-1-y) / fadeSamples));
            if ((oceanMask & 2) != 0) edgeFade = math.min(edgeFade, math.saturate((float)y         / fadeSamples));
            if ((oceanMask & 4) != 0) edgeFade = math.min(edgeFade, math.saturate((float)(res-1-x) / fadeSamples));
            if ((oceanMask & 8) != 0) edgeFade = math.min(edgeFade, math.saturate((float)x         / fadeSamples));
            edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade); // smoothstep
            h = math.lerp(waterLevel * 0.1f, h, edgeFade);

            // ── Seabed shaping ────────────────────────────────────────────────
            // Below water: keep noise shape but remap into [waterLevel-seabedDepth, waterLevel]
            // so underwater terrain is varied, not flat, and never pokes above sea level.
            if (h < waterLevel)
            {
                float t = 1f - h / math.max(waterLevel, 0.001f); // 0=surface, 1=deepest
                h = waterLevel - t * seabedDepth;
            }

            dst[idx] = math.clamp(h, 0f, 1f);
        }
    }
}
