using System;
using InfinityProject.World.Geography;

namespace InfinityProject.World.Hydrology
{
    /// <summary>
    /// Lightweight read-only elevation view for systems that need geography height without rebuilding
    /// unrelated derived metrics such as slope or aspect.
    /// </summary>
    public sealed class HydrologyElevationInput
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public float MaxElevationMeters { get; }
        public int Seed { get; }
        public float[] NormalizedHeight { get; }

        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);

        public HydrologyElevationInput(
            int resolution,
            float widthMeters,
            float lengthMeters,
            float maxElevationMeters,
            int seed,
            float[] normalizedHeight)
        {
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            if (lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(lengthMeters));
            if (maxElevationMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(maxElevationMeters));
            if (normalizedHeight == null) throw new ArgumentNullException(nameof(normalizedHeight));
            if (normalizedHeight.Length != resolution * resolution)
                throw new ArgumentException("Height array size must equal resolution².", nameof(normalizedHeight));

            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            MaxElevationMeters = maxElevationMeters;
            Seed = seed;
            NormalizedHeight = normalizedHeight;
        }

        public HydrologyElevationInput(GeographyData geography)
            : this(
                geography.Resolution,
                geography.WidthMeters,
                geography.LengthMeters,
                geography.MaxElevationMeters,
                geography.Seed,
                geography.NormalizedHeight)
        {
        }
    }
}
