using System;
using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Hydrology;
using UnityEngine;

namespace InfinityProject.World.Ground
{
    public static class GroundConditionGenerator
    {
        public static GroundConditionData Generate(
            GeographyData geography, HydrologyData hydrology, ClimateData climate,
            GroundConditionGenerationSettings settings = null)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (hydrology == null) throw new ArgumentNullException(nameof(hydrology));
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            ValidateCompatible(geography, hydrology, climate);
            settings ??= new GroundConditionGenerationSettings(); settings = settings.Clone(); settings.Validate();

            int count = geography.Resolution * geography.Resolution;
            float[] groundSlope = BuildSlopeDegreesAtScale(geography, settings.SlopeAnalysisRadiusMeters);
            var twi = new float[count]; var runOn = new float[count]; var water = new float[count]; var retention = new float[count];
            float contourWidth = settings.SlopeAnalysisRadiusMeters > 0.001f
                ? Mathf.Max(settings.SlopeAnalysisRadiusMeters * 2f, 1f)
                : Mathf.Max(Mathf.Sqrt(geography.SampleSpacingX * geography.SampleSpacingZ), 0.0001f);
            float minSlopeRad = settings.MinimumSlopeDegrees * Mathf.Deg2Rad;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                float contributingArea = Mathf.Max(hydrology.FlowAccumulationSquareMeters[i], 0.0001f);
                float specificCatchmentArea = contributingArea / contourWidth;
                float slopeRadians = Mathf.Max(groundSlope[i] * Mathf.Deg2Rad, minSlopeRad);
                float twiValue = Mathf.Log(Mathf.Max(specificCatchmentArea / Mathf.Max(Mathf.Tan(slopeRadians), 0.000001f), 0.000001f));
                if (!IsFinite(twiValue)) throw new InvalidOperationException($"Ground wetness became non-finite at sample {i}.");

                float runOnValue = RunOnPotentialFromArea(contributingArea, settings);
                float precipitation = Mathf.Clamp01(climate.PrecipitationPotential[i]);
                float waterValue = WaterAvailabilityFromSupply(precipitation, runOnValue, settings);
                float retentionT = Mathf.InverseLerp(settings.FullRetentionSlopeDegrees, settings.NoRetentionSlopeDegrees, groundSlope[i]);
                retentionT = retentionT * retentionT * (3f - 2f * retentionT);

                twi[i] = twiValue;
                runOn[i] = runOnValue;
                water[i] = waterValue;
                retention[i] = 1f - retentionT;
                min = Mathf.Min(min, twiValue); max = Mathf.Max(max, twiValue);
            }

            var sorted = (float[])twi.Clone(); Array.Sort(sorted);
            return new GroundConditionData(
                geography.Resolution, geography.WidthMeters, geography.LengthMeters, geography.Seed, hydrology.SourceHeightHash,
                twi, runOn, water, retention,
                min, max, Percentile(sorted, 0.05f), Percentile(sorted, 0.95f));
        }

        /// <summary>
        /// Fits a local elevation plane over a fixed physical neighbourhood and returns that plane's slope.
        /// The neighbourhood is expressed in metres, so denser rasters refine the same landform question
        /// instead of exposing progressively smaller corrugations to ecology.
        /// </summary>
        public static float[] BuildSlopeDegreesAtScale(GeographyData geography, float radiusMeters)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            int n = geography.Resolution, count = n * n;
            if (radiusMeters <= 0.0001f) return (float[])geography.SlopeDegrees.Clone();

            int rx = Mathf.Max(1, Mathf.RoundToInt(radiusMeters / geography.SampleSpacingX));
            int rz = Mathf.Max(1, Mathf.RoundToInt(radiusMeters / geography.SampleSpacingZ));
            int pitch = n + 1;

            // Integral moments let every local least-squares plane be solved in O(1) after one O(n²) pass.
            // This avoids an expensive neighbourhood walk for every sample at 1024² and above.
            var integralZ = new double[pitch * pitch];
            var integralXZ = new double[pitch * pitch];
            var integralYZ = new double[pitch * pitch];
            for (int y = 0; y < n; y++)
            {
                double rowZ = 0d, rowXZ = 0d, rowYZ = 0d;
                int dstRow = (y + 1) * pitch;
                int prevRow = y * pitch;
                for (int x = 0; x < n; x++)
                {
                    double elevation = geography.GetElevationMeters(x, y);
                    rowZ += elevation;
                    rowXZ += x * elevation;
                    rowYZ += y * elevation;
                    integralZ[dstRow + x + 1] = integralZ[prevRow + x + 1] + rowZ;
                    integralXZ[dstRow + x + 1] = integralXZ[prevRow + x + 1] + rowXZ;
                    integralYZ[dstRow + x + 1] = integralYZ[prevRow + x + 1] + rowYZ;
                }
            }

            var slope = new float[count];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int x0 = Mathf.Max(0, x - rx), x1 = Mathf.Min(n - 1, x + rx);
                int y0 = Mathf.Max(0, y - rz), y1 = Mathf.Min(n - 1, y + rz);
                int nx = x1 - x0 + 1, ny = y1 - y0 + 1;
                double sampleCount = nx * (double)ny;

                double sumZ = RectangleSum(integralZ, pitch, x0, y0, x1, y1);
                double sumXZ = RectangleSum(integralXZ, pitch, x0, y0, x1, y1);
                double sumYZ = RectangleSum(integralYZ, pitch, x0, y0, x1, y1);

                double sumX1D = SumIntegers(x0, x1);
                double sumY1D = SumIntegers(y0, y1);
                double sumX = sumX1D * ny;
                double sumY = sumY1D * nx;
                double sumX2 = SumIntegerSquares(x0, x1) * ny;
                double sumY2 = SumIntegerSquares(y0, y1) * nx;

                double varX = sumX2 - sumX * sumX / sampleCount;
                double varY = sumY2 - sumY * sumY / sampleCount;
                double covXZ = sumXZ - sumX * sumZ / sampleCount;
                double covYZ = sumYZ - sumY * sumZ / sampleCount;

                double dhdx = varX > 1e-12 ? (covXZ / varX) / geography.SampleSpacingX : 0d;
                double dhdz = varY > 1e-12 ? (covYZ / varY) / geography.SampleSpacingZ : 0d;
                slope[y * n + x] = Mathf.Atan((float)Math.Sqrt(dhdx * dhdx + dhdz * dhdz)) * Mathf.Rad2Deg;
            }
            return slope;
        }

        private static double RectangleSum(double[] integral, int pitch, int x0, int y0, int x1, int y1)
        {
            return integral[(y1 + 1) * pitch + (x1 + 1)]
                 - integral[y0 * pitch + (x1 + 1)]
                 - integral[(y1 + 1) * pitch + x0]
                 + integral[y0 * pitch + x0];
        }

        private static double SumIntegers(int first, int last)
        {
            if (last < first) return 0d;
            return 0.5d * (first + (double)last) * (last - first + 1d);
        }

        private static double SumIntegerSquares(int first, int last)
        {
            return SumIntegerSquaresTo(last) - SumIntegerSquaresTo(first - 1);
        }

        private static double SumIntegerSquaresTo(int n)
        {
            if (n <= 0) return 0d;
            return n * (n + 1d) * (2d * n + 1d) / 6d;
        }

        public static float SampleSlopeDegreesAtScale(GeographyData geography, int x, int y, float radiusMeters)
        {
            float[] field = BuildSlopeDegreesAtScale(geography, radiusMeters);
            x = Mathf.Clamp(x, 0, geography.Resolution - 1); y = Mathf.Clamp(y, 0, geography.Resolution - 1);
            return field[y * geography.Resolution + x];
        }

        public static float RunOnPotentialFromArea(float contributingAreaSquareMeters, GroundConditionGenerationSettings settings)
        {
            float excess = Mathf.Max(0f, contributingAreaSquareMeters - settings.RunOnLocalSupportAreaSquareMeters);
            return Mathf.Clamp01(1f - Mathf.Exp(-excess / Mathf.Max(settings.RunOnResponseAreaSquareMeters, 1f)));
        }

        public static float WaterAvailabilityFromSupply(float precipitationPotential, float runOnPotential, GroundConditionGenerationSettings settings)
        {
            float direct = Mathf.Clamp01(precipitationPotential);
            float boost = Mathf.Clamp01(runOnPotential) * Mathf.Clamp01(settings.RunOnWaterBoostStrength);
            return Mathf.Clamp01(direct + (1f - direct) * boost);
        }

        private static void ValidateCompatible(GeographyData geography, HydrologyData hydrology, ClimateData climate)
        {
            if (geography.Resolution != hydrology.Resolution || geography.Resolution != climate.Resolution)
                throw new ArgumentException("Geography, Hydrology and Climate must use the same resolution.");
            if (Mathf.Abs(geography.WidthMeters - hydrology.WidthMeters) > 0.001f || Mathf.Abs(geography.LengthMeters - hydrology.LengthMeters) > 0.001f ||
                Mathf.Abs(geography.WidthMeters - climate.WidthMeters) > 0.001f || Mathf.Abs(geography.LengthMeters - climate.LengthMeters) > 0.001f)
                throw new ArgumentException("Geography, Hydrology and Climate must describe the same physical world extent.");
            if (geography.Seed != hydrology.SourceGeographySeed || geography.Seed != climate.SourceGeographySeed)
                throw new ArgumentException("Hydrology or Climate does not belong to this Geography seed.");
        }

        private static float Percentile(float[] sorted, float p)
        {
            if (sorted == null || sorted.Length == 0) return 0f;
            if (sorted.Length == 1) return sorted[0];
            float pos = Mathf.Clamp01(p) * (sorted.Length - 1); int lo = Mathf.FloorToInt(pos), hi = Mathf.Min(lo + 1, sorted.Length - 1);
            return Mathf.Lerp(sorted[lo], sorted[hi], pos - lo);
        }
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
