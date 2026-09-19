using System;
using UnityEngine;

namespace InfinityProject.World.Climate
{
    /// <summary>
    /// Designer-facing settings for Climate V0. Values describe a static climate field rather than
    /// moment-to-moment weather. Precipitation is a dimensionless potential until Infinity owns an
    /// atmospheric water budget.
    /// </summary>
    [Serializable]
    public sealed class ClimateGenerationSettings
    {
        [Header("Temperature")]
        [Tooltip("Mean air temperature at zero elevation and the north/south midpoint of the world.")]
        public float BaseTemperatureCelsius = 16f;

        [Tooltip("Temperature decrease per kilometre of elevation.")]
        [Range(0f, 15f)]
        public float LapseRateCelsiusPerKilometre = 6.5f;

        [Tooltip("Total temperature change from the south edge to the north edge. Negative values make north colder.")]
        [Range(-20f, 20f)]
        public float SouthToNorthTemperatureDeltaCelsius = -1.5f;

        [Header("Precipitation potential")]
        [Tooltip("Background moisture potential before terrain exposure is applied.")]
        [Range(0f, 1f)]
        public float BackgroundPrecipitationPotential = 0.48f;

        [Tooltip("Bearing the moisture-bearing wind comes FROM, clockwise from world +Z/north. 270° means from west.")]
        [Range(0f, 360f)]
        public float MoistureWindFromDegrees = 270f;

        [Tooltip("Strength of local windward-slope enhancement.")]
        [Range(0f, 1f)]
        public float WindwardBoost = 0.28f;

        [Tooltip("Strength of broad uplift when terrain rises above its upwind approach.")]
        [Range(0f, 1f)]
        public float BroadOrographicBoost = 0.18f;

        [Tooltip("Strength of drying behind higher upwind terrain.")]
        [Range(0f, 1f)]
        public float RainShadowStrength = 0.34f;

        [Tooltip("How far upwind the static orographic diagnostic samples terrain.")]
        [Min(1f)]
        public float UpwindSampleRangeMeters = 700f;

        [Tooltip("Number of terrain samples taken between a cell and its upwind range.")]
        [Range(2, 32)]
        public int UpwindSampleCount = 10;

        [Tooltip("Vertical relief required for broad orographic terms to approach full strength.")]
        [Min(1f)]
        public float OrographicReliefScaleMeters = 220f;

        public ClimateGenerationSettings Clone()
            => (ClimateGenerationSettings)MemberwiseClone();

        public void Validate()
        {
            LapseRateCelsiusPerKilometre = Mathf.Clamp(LapseRateCelsiusPerKilometre, 0f, 15f);
            SouthToNorthTemperatureDeltaCelsius = Mathf.Clamp(SouthToNorthTemperatureDeltaCelsius, -20f, 20f);
            BackgroundPrecipitationPotential = Mathf.Clamp01(BackgroundPrecipitationPotential);
            MoistureWindFromDegrees = Mathf.Repeat(MoistureWindFromDegrees, 360f);
            WindwardBoost = Mathf.Clamp01(WindwardBoost);
            BroadOrographicBoost = Mathf.Clamp01(BroadOrographicBoost);
            RainShadowStrength = Mathf.Clamp01(RainShadowStrength);
            UpwindSampleRangeMeters = Mathf.Max(1f, UpwindSampleRangeMeters);
            UpwindSampleCount = Mathf.Clamp(UpwindSampleCount, 2, 32);
            OrographicReliefScaleMeters = Mathf.Max(1f, OrographicReliefScaleMeters);
        }
    }
}
