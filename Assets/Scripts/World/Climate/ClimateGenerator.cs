using System;
using InfinityProject.World.Geography;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityProject.World.Climate
{
    /// <summary>
    /// Climate V0 derives deterministic, static environmental fields from Geography.
    /// The precipitation model is deliberately interpretable rather than meteorologically complete.
    /// </summary>
    public static class ClimateGenerator
    {
        public static ClimateData Generate(GeographyData geography, ClimateGenerationSettings settings = null)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            settings ??= new ClimateGenerationSettings();
            settings = settings.Clone();
            settings.Validate();

            int resolution = geography.Resolution;
            int count = resolution * resolution;
            var temperature = new float[count];
            var precipitation = new float[count];

            float minTemperature = float.PositiveInfinity;
            float maxTemperature = float.NegativeInfinity;
            float minPrecipitation = float.PositiveInfinity;
            float maxPrecipitation = float.NegativeInfinity;

            Vector2 upwindDirection = BearingToVector(settings.MoistureWindFromDegrees);
            float reliefScale = Mathf.Max(settings.OrographicReliefScaleMeters, 1f);

            for (int y = 0; y < resolution; y++)
            {
                float north01 = y / (float)(resolution - 1);
                float regionalOffset = (north01 - 0.5f) * settings.SouthToNorthTemperatureDeltaCelsius;

                for (int x = 0; x < resolution; x++)
                {
                    int index = y * resolution + x;
                    float elevation = geography.NormalizedHeight[index] * geography.MaxElevationMeters;
                    float temp = settings.BaseTemperatureCelsius + regionalOffset -
                                 settings.LapseRateCelsiusPerKilometre * (elevation / 1000f);

                    if (!IsFinite(temp))
                        throw new InvalidOperationException($"Climate temperature became non-finite at sample {index}.");

                    temperature[index] = temp;
                    minTemperature = Mathf.Min(minTemperature, temp);
                    maxTemperature = Mathf.Max(maxTemperature, temp);

                    float slopeRadians = geography.SlopeDegrees[index] * Mathf.Deg2Rad;
                    Vector2 downslopeDirection = BearingToVector(geography.AspectDegrees[index]);
                    float facingUpwind = Mathf.Max(0f, Vector2.Dot(downslopeDirection, upwindDirection));
                    float localWindward = Mathf.Sin(slopeRadians) * facingUpwind;

                    SampleUpwindRelief(
                        geography,
                        x,
                        y,
                        elevation,
                        upwindDirection,
                        settings.UpwindSampleRangeMeters,
                        settings.UpwindSampleCount,
                        out float meanUpwindElevation,
                        out float maximumUpwindElevation);

                    float broadRise = Mathf.Clamp01((elevation - meanUpwindElevation) / reliefScale);
                    float barrier = Mathf.Clamp01((maximumUpwindElevation - elevation) / reliefScale);

                    float precip = settings.BackgroundPrecipitationPotential +
                                   settings.WindwardBoost * localWindward +
                                   settings.BroadOrographicBoost * broadRise -
                                   settings.RainShadowStrength * barrier;
                    precip = Mathf.Clamp01(precip);

                    if (!IsFinite(precip))
                        throw new InvalidOperationException($"Climate precipitation potential became non-finite at sample {index}.");

                    precipitation[index] = precip;
                    minPrecipitation = Mathf.Min(minPrecipitation, precip);
                    maxPrecipitation = Mathf.Max(maxPrecipitation, precip);
                }
            }

            return new ClimateData(
                resolution,
                geography.WidthMeters,
                geography.LengthMeters,
                geography.Seed,
                temperature,
                precipitation,
                minTemperature,
                maxTemperature,
                minPrecipitation,
                maxPrecipitation);
        }

        private static void SampleUpwindRelief(
            GeographyData geography,
            int x,
            int y,
            float currentElevation,
            Vector2 upwindDirection,
            float rangeMeters,
            int sampleCount,
            out float meanElevation,
            out float maximumElevation)
        {
            float localX = x * geography.SampleSpacingX;
            float localZ = y * geography.SampleSpacingZ;
            float sum = 0f;
            int validSamples = 0;
            maximumElevation = currentElevation;

            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float distance = rangeMeters * sample / sampleCount;
                float sx = localX + upwindDirection.x * distance;
                float sz = localZ + upwindDirection.y * distance;

                if (sx < 0f || sx > geography.WidthMeters || sz < 0f || sz > geography.LengthMeters)
                    continue;

                float elevation = geography.SampleElevationMeters(new float2(sx, sz));
                sum += elevation;
                validSamples++;
                maximumElevation = Mathf.Max(maximumElevation, elevation);
            }

            meanElevation = validSamples > 0 ? sum / validSamples : currentElevation;
        }

        /// <summary>
        /// Converts a bearing clockwise from north (+Z) into an X/Z-plane direction.
        /// For a wind-from bearing, this vector points from the sample toward the moisture source/upwind.
        /// </summary>
        private static Vector2 BearingToVector(float bearingDegrees)
        {
            float radians = bearingDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
