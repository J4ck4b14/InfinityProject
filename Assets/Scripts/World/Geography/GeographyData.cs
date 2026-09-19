using System;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Authoritative geography data produced by Infinity.
    /// Unity Terrain is only a presentation/collision target for this data.
    /// </summary>
    [Serializable]
    public sealed class GeographyData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public float MaxElevationMeters { get; }
        public int Seed { get; }

        /// <summary>Normalized elevation in [0,1], flattened as y * Resolution + x.</summary>
        public float[] NormalizedHeight { get; }

        /// <summary>Slope angle in degrees [0,90].</summary>
        public float[] SlopeDegrees { get; }

        /// <summary>
        /// Downslope bearing in degrees clockwise from world/local +Z (north).
        /// Flat samples use 0 by convention.
        /// </summary>
        public float[] AspectDegrees { get; }

        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);

        public GeographyData(
            int resolution,
            float widthMeters,
            float lengthMeters,
            float maxElevationMeters,
            int seed,
            float[] normalizedHeight)
        {
            if (resolution < 2)
                throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be at least 2.");
            if (widthMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (lengthMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(lengthMeters));
            if (maxElevationMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxElevationMeters));
            if (normalizedHeight == null)
                throw new ArgumentNullException(nameof(normalizedHeight));
            if (normalizedHeight.Length != resolution * resolution)
                throw new ArgumentException("Height array size must equal resolution².", nameof(normalizedHeight));

            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            MaxElevationMeters = maxElevationMeters;
            Seed = seed;
            NormalizedHeight = normalizedHeight;

            SlopeDegrees = new float[normalizedHeight.Length];
            AspectDegrees = new float[normalizedHeight.Length];
            DeriveSurfaceMetrics();
        }

        public int Index(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return y * Resolution + x;
        }

        public float GetNormalizedHeight(int x, int y)
            => NormalizedHeight[Index(x, y)];

        public float GetElevationMeters(int x, int y)
            => GetNormalizedHeight(x, y) * MaxElevationMeters;

        public float GetSlopeDegrees(int x, int y)
            => SlopeDegrees[Index(x, y)];

        public float GetAspectDegrees(int x, int y)
            => AspectDegrees[Index(x, y)];

        /// <summary>
        /// Converts a local XZ position in metres into continuous grid coordinates.
        /// X maps west→east and Y maps south→north in the data grid.
        /// </summary>
        public float2 LocalMetersToGrid(float2 localXZ)
        {
            float gx = math.saturate(localXZ.x / WidthMeters) * (Resolution - 1);
            float gy = math.saturate(localXZ.y / LengthMeters) * (Resolution - 1);
            return new float2(gx, gy);
        }

        public float2 GridToLocalMeters(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return new float2(x * SampleSpacingX, y * SampleSpacingZ);
        }

        public float SampleNormalizedHeight(float2 localXZ)
        {
            float2 grid = LocalMetersToGrid(localXZ);
            int x0 = Mathf.FloorToInt(grid.x);
            int y0 = Mathf.FloorToInt(grid.y);
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);
            float tx = grid.x - x0;
            float ty = grid.y - y0;

            float a = Mathf.Lerp(GetNormalizedHeight(x0, y0), GetNormalizedHeight(x1, y0), tx);
            float b = Mathf.Lerp(GetNormalizedHeight(x0, y1), GetNormalizedHeight(x1, y1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        public float SampleElevationMeters(float2 localXZ)
            => SampleNormalizedHeight(localXZ) * MaxElevationMeters;

        private void DeriveSurfaceMetrics()
        {
            float dx = SampleSpacingX;
            float dz = SampleSpacingZ;

            for (int y = 0; y < Resolution; y++)
            {
                int y0 = Mathf.Max(0, y - 1);
                int y1 = Mathf.Min(Resolution - 1, y + 1);
                float zDistance = Mathf.Max((y1 - y0) * dz, 0.0001f);

                for (int x = 0; x < Resolution; x++)
                {
                    int x0 = Mathf.Max(0, x - 1);
                    int x1 = Mathf.Min(Resolution - 1, x + 1);
                    float xDistance = Mathf.Max((x1 - x0) * dx, 0.0001f);

                    float west = GetElevationMeters(x0, y);
                    float east = GetElevationMeters(x1, y);
                    float south = GetElevationMeters(x, y0);
                    float north = GetElevationMeters(x, y1);

                    float dHdx = (east - west) / xDistance;
                    float dHdz = (north - south) / zDistance;
                    float gradient = Mathf.Sqrt(dHdx * dHdx + dHdz * dHdz);

                    int index = Index(x, y);
                    SlopeDegrees[index] = Mathf.Atan(gradient) * Mathf.Rad2Deg;

                    if (gradient < 0.000001f)
                    {
                        AspectDegrees[index] = 0f;
                    }
                    else
                    {
                        // Downslope vector = negative height gradient.
                        float bearing = Mathf.Atan2(-dHdx, -dHdz) * Mathf.Rad2Deg;
                        AspectDegrees[index] = Mathf.Repeat(bearing + 360f, 360f);
                    }
                }
            }
        }
    }
}
