using System;
using UnityEngine;

namespace InfinityProject.World.Climate
{
    /// <summary>
    /// Static climate fields derived from one Geography state.
    /// Temperature is physical-ish °C; precipitation remains a dimensionless environmental potential.
    /// </summary>
    [Serializable]
    public sealed class ClimateData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public int SourceGeographySeed { get; }
        public float[] TemperatureCelsius { get; }
        public float[] PrecipitationPotential { get; }
        public float MinimumTemperatureCelsius { get; }
        public float MaximumTemperatureCelsius { get; }
        public float MinimumPrecipitationPotential { get; }
        public float MaximumPrecipitationPotential { get; }

        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);

        public ClimateData(
            int resolution,
            float widthMeters,
            float lengthMeters,
            int sourceGeographySeed,
            float[] temperatureCelsius,
            float[] precipitationPotential,
            float minimumTemperatureCelsius,
            float maximumTemperatureCelsius,
            float minimumPrecipitationPotential,
            float maximumPrecipitationPotential)
        {
            int expected = resolution * resolution;
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f || lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (temperatureCelsius == null || temperatureCelsius.Length != expected)
                throw new ArgumentException("Temperature array size must equal resolution².", nameof(temperatureCelsius));
            if (precipitationPotential == null || precipitationPotential.Length != expected)
                throw new ArgumentException("Precipitation array size must equal resolution².", nameof(precipitationPotential));

            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            SourceGeographySeed = sourceGeographySeed;
            TemperatureCelsius = temperatureCelsius;
            PrecipitationPotential = precipitationPotential;
            MinimumTemperatureCelsius = minimumTemperatureCelsius;
            MaximumTemperatureCelsius = maximumTemperatureCelsius;
            MinimumPrecipitationPotential = minimumPrecipitationPotential;
            MaximumPrecipitationPotential = maximumPrecipitationPotential;
        }

        public int Index(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return y * Resolution + x;
        }

        public float GetTemperatureCelsius(int x, int y) => TemperatureCelsius[Index(x, y)];
        public float GetPrecipitationPotential(int x, int y) => PrecipitationPotential[Index(x, y)];
    }
}
