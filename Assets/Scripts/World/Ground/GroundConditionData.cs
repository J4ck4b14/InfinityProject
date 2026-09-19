using System;
using UnityEngine;

namespace InfinityProject.World.Ground
{
    /// <summary>
    /// Ground V0.4 state. Raw TWI remains a diagnostic descriptor; RunOnPotential and
    /// WaterAvailabilityPotential form the stable ecology-facing water interface.
    /// </summary>
    [Serializable]
    public sealed class GroundConditionData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public int SourceGeographySeed { get; }
        public long SourceHeightHash { get; }
        public float[] HydrologicalWetnessIndex { get; }
        public float[] RunOnPotential { get; }
        public float[] WaterAvailabilityPotential { get; }
        public float[] SoilRetentionPotential { get; }
        public float MinimumWetnessIndex { get; }
        public float MaximumWetnessIndex { get; }
        public float Percentile05WetnessIndex { get; }
        public float Percentile95WetnessIndex { get; }
        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);

        public GroundConditionData(
            int resolution, float widthMeters, float lengthMeters, int sourceGeographySeed, long sourceHeightHash,
            float[] hydrologicalWetnessIndex, float[] runOnPotential, float[] waterAvailabilityPotential, float[] soilRetentionPotential,
            float minimumWetnessIndex, float maximumWetnessIndex, float percentile05WetnessIndex, float percentile95WetnessIndex)
        {
            int expected = resolution * resolution;
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f || lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            ValidateArray(hydrologicalWetnessIndex, expected, nameof(hydrologicalWetnessIndex));
            ValidateArray(runOnPotential, expected, nameof(runOnPotential));
            ValidateArray(waterAvailabilityPotential, expected, nameof(waterAvailabilityPotential));
            ValidateArray(soilRetentionPotential, expected, nameof(soilRetentionPotential));
            Resolution = resolution; WidthMeters = widthMeters; LengthMeters = lengthMeters; SourceGeographySeed = sourceGeographySeed; SourceHeightHash = sourceHeightHash;
            HydrologicalWetnessIndex = hydrologicalWetnessIndex; RunOnPotential = runOnPotential; WaterAvailabilityPotential = waterAvailabilityPotential; SoilRetentionPotential = soilRetentionPotential;
            MinimumWetnessIndex = minimumWetnessIndex; MaximumWetnessIndex = maximumWetnessIndex; Percentile05WetnessIndex = percentile05WetnessIndex; Percentile95WetnessIndex = percentile95WetnessIndex;
        }

        /// <summary>
        /// Compatibility constructor for legacy deterministic fixtures. The former wetness-potential argument is
        /// interpreted as generic water availability. Production V0.4 generation always supplies RunOn separately.
        /// </summary>
        public GroundConditionData(
            int resolution, float widthMeters, float lengthMeters, int sourceGeographySeed, long sourceHeightHash,
            float[] hydrologicalWetnessIndex, float[] legacyWaterAvailability, float[] soilRetentionPotential,
            float minimumWetnessIndex, float maximumWetnessIndex, float percentile05WetnessIndex, float percentile95WetnessIndex)
            : this(resolution, widthMeters, lengthMeters, sourceGeographySeed, sourceHeightHash,
                hydrologicalWetnessIndex, new float[resolution * resolution], (float[])legacyWaterAvailability.Clone(), soilRetentionPotential,
                minimumWetnessIndex, maximumWetnessIndex, percentile05WetnessIndex, percentile95WetnessIndex)
        { }

        private static void ValidateArray(float[] values, int expected, string name)
        {
            if (values == null || values.Length != expected) throw new ArgumentException($"{name} array size must equal resolution².", name);
        }

        public int Index(int x, int y) { x = Mathf.Clamp(x, 0, Resolution - 1); y = Mathf.Clamp(y, 0, Resolution - 1); return y * Resolution + x; }
        public float GetRelativeWetness01(int index)
        {
            index = Mathf.Clamp(index, 0, HydrologicalWetnessIndex.Length - 1); float range = Percentile95WetnessIndex - Percentile05WetnessIndex;
            return range <= 0.000001f ? 0.5f : Mathf.Clamp01((HydrologicalWetnessIndex[index] - Percentile05WetnessIndex) / range);
        }
    }
}
