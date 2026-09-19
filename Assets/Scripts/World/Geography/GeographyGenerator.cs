using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Deterministic first-pass landform generator.
    /// V0 generates only geography: no sea level, rivers, erosion or biome logic.
    /// </summary>
    public static class GeographyGenerator
    {
        public static GeographyData Generate(GeographyGenerationSettings sourceSettings)
        {
            var settings = sourceSettings?.Clone() ?? new GeographyGenerationSettings();
            settings.Validate();

            int count = settings.Resolution * settings.Resolution;
            var heights = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                var job = new GenerateHeightJob
                {
                    Resolution = settings.Resolution,
                    WidthMeters = settings.WidthMeters,
                    LengthMeters = settings.LengthMeters,
                    Seed = settings.Seed,

                    BaseElevation = settings.BaseElevation,
                    MacroScaleMeters = settings.MacroScaleMeters,
                    MacroAmplitude = settings.MacroAmplitude,

                    MountainScaleMeters = settings.MountainScaleMeters,
                    MountainAmplitude = settings.MountainAmplitude,
                    MountainThreshold = settings.MountainThreshold,
                    MountainBlend = settings.MountainBlend,

                    WarpScaleMeters = settings.WarpScaleMeters,
                    WarpStrengthMeters = settings.WarpStrengthMeters,

                    DetailScaleMeters = settings.DetailScaleMeters,
                    DetailAmplitude = settings.DetailAmplitude,

                    Octaves = settings.Octaves,
                    Persistence = settings.Persistence,
                    Lacunarity = settings.Lacunarity,

                    Heights = heights
                };

                job.Schedule(count, 64).Complete();

                var managedHeights = new float[count];
                NativeArray<float>.Copy(heights, managedHeights, count);

                return new GeographyData(
                    settings.Resolution,
                    settings.WidthMeters,
                    settings.LengthMeters,
                    settings.MaxElevationMeters,
                    settings.Seed,
                    managedHeights);
            }
            finally
            {
                if (heights.IsCreated)
                    heights.Dispose();
            }
        }

        [BurstCompile]
        private struct GenerateHeightJob : IJobParallelFor
        {
            public int Resolution;
            public float WidthMeters;
            public float LengthMeters;
            public int Seed;

            public float BaseElevation;
            public float MacroScaleMeters;
            public float MacroAmplitude;

            public float MountainScaleMeters;
            public float MountainAmplitude;
            public float MountainThreshold;
            public float MountainBlend;

            public float WarpScaleMeters;
            public float WarpStrengthMeters;

            public float DetailScaleMeters;
            public float DetailAmplitude;

            public int Octaves;
            public float Persistence;
            public float Lacunarity;

            [WriteOnly] public NativeArray<float> Heights;

            public void Execute(int index)
            {
                int x = index % Resolution;
                int y = index / Resolution;

                float worldX = ((float)x / (Resolution - 1)) * WidthMeters;
                float worldZ = ((float)y / (Resolution - 1)) * LengthMeters;
                float2 world = new float2(worldX, worldZ);

                float2 warp = DomainWarp(world);
                float2 warpedWorld = world + warp * WarpStrengthMeters;

                float macro = Fbm01(warpedWorld, MacroScaleMeters, SeedOffset(0xA341316Cu));
                float macroSigned = (macro - 0.5f) * 2f;
                float baseHeight = BaseElevation + macroSigned * MacroAmplitude;

                float mountainMask = SmoothStep(
                    MountainThreshold - MountainBlend,
                    MountainThreshold + MountainBlend,
                    macro);

                float ridges = RidgedFbm01(warpedWorld, MountainScaleMeters, SeedOffset(0xC8013EA4u));
                float mountainHeight = mountainMask * ridges * MountainAmplitude;

                float detail = Fbm01(warpedWorld, DetailScaleMeters, SeedOffset(0xAD90777Du));
                float detailSigned = (detail - 0.5f) * 2f * DetailAmplitude;

                Heights[index] = math.saturate(baseHeight + mountainHeight + detailSigned);
            }

            private float2 DomainWarp(float2 world)
            {
                if (WarpStrengthMeters <= 0f)
                    return float2.zero;

                float2 p = world / math.max(WarpScaleMeters, 1f);
                float2 a = SeedOffset(0x7E95761Eu);
                float2 b = SeedOffset(0x9E3779B9u);

                float wx = noise.snoise(p + a);
                float wz = noise.snoise(p + b);
                return new float2(wx, wz);
            }

            private float Fbm01(float2 world, float scaleMeters, float2 offset)
            {
                float2 p = world / math.max(scaleMeters, 1f) + offset;
                float amplitude = 1f;
                float frequency = 1f;
                float total = 0f;
                float amplitudeSum = 0f;

                for (int octave = 0; octave < Octaves; octave++)
                {
                    total += noise.snoise(p * frequency) * amplitude;
                    amplitudeSum += amplitude;
                    amplitude *= Persistence;
                    frequency *= Lacunarity;
                }

                float normalized = total / math.max(amplitudeSum, 0.0001f);
                return math.saturate(normalized * 0.5f + 0.5f);
            }

            private float RidgedFbm01(float2 world, float scaleMeters, float2 offset)
            {
                float2 p = world / math.max(scaleMeters, 1f) + offset;
                float amplitude = 1f;
                float frequency = 1f;
                float total = 0f;
                float amplitudeSum = 0f;

                for (int octave = 0; octave < Octaves; octave++)
                {
                    float n = noise.snoise(p * frequency);
                    float ridge = 1f - math.abs(n);
                    ridge *= ridge;

                    total += ridge * amplitude;
                    amplitudeSum += amplitude;
                    amplitude *= Persistence;
                    frequency *= Lacunarity;
                }

                return math.saturate(total / math.max(amplitudeSum, 0.0001f));
            }

            private float2 SeedOffset(uint salt)
            {
                uint h1 = Hash((uint)Seed ^ salt);
                uint h2 = Hash(h1 ^ 0x85EBCA6Bu);

                // Large coordinate offsets decorrelate fields without changing world-space scale.
                float ox = (h1 & 0x00FFFFFFu) / 16777215f * 4096f;
                float oz = (h2 & 0x00FFFFFFu) / 16777215f * 4096f;
                return new float2(ox, oz);
            }

            private static uint Hash(uint value)
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }

            private static float SmoothStep(float edge0, float edge1, float value)
            {
                float t = math.saturate((value - edge0) / math.max(edge1 - edge0, 0.0001f));
                return t * t * (3f - 2f * t);
            }
        }
    }
}
