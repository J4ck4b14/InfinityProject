using System;
using UnityEngine;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Designer-facing inputs for deterministic geography generation.
    /// Every distance is expressed in metres; resolution only controls sampling density.
    /// </summary>
    [Serializable]
    public class GeographyGenerationSettings
    {
        [Header("World")]
        [Min(16)] public int Resolution = 512;
        [Min(1f)] public float WidthMeters = 2000f;
        [Min(1f)] public float LengthMeters = 2000f;
        [Min(1f)] public float MaxElevationMeters = 450f;
        public int Seed = 12345;

        [Header("Macro Landform")]
        [Range(0f, 1f)] public float BaseElevation = 0.30f;
        [Min(1f)] public float MacroScaleMeters = 1800f;
        [Range(0f, 1f)] public float MacroAmplitude = 0.28f;

        [Header("Mountain Structure")]
        [Min(1f)] public float MountainScaleMeters = 850f;
        [Range(0f, 1f)] public float MountainAmplitude = 0.42f;
        [Range(0f, 1f)] public float MountainThreshold = 0.58f;
        [Range(0.001f, 0.5f)] public float MountainBlend = 0.16f;

        [Header("Domain Warp")]
        [Min(1f)] public float WarpScaleMeters = 1300f;
        [Min(0f)] public float WarpStrengthMeters = 180f;

        [Header("Local Detail")]
        [Min(1f)] public float DetailScaleMeters = 180f;
        [Range(0f, 0.5f)] public float DetailAmplitude = 0.055f;

        [Header("Fractal Noise")]
        [Range(1, 8)] public int Octaves = 5;
        [Range(0.1f, 0.9f)] public float Persistence = 0.5f;
        [Range(1.1f, 4f)] public float Lacunarity = 2f;

        public GeographyGenerationSettings Clone()
            => (GeographyGenerationSettings)MemberwiseClone();

        /// <summary>
        /// Sanitises values that may have come from code rather than an Inspector.
        /// </summary>
        public void Validate()
        {
            Resolution = Mathf.Max(16, Resolution);
            WidthMeters = Mathf.Max(1f, WidthMeters);
            LengthMeters = Mathf.Max(1f, LengthMeters);
            MaxElevationMeters = Mathf.Max(1f, MaxElevationMeters);

            BaseElevation = Mathf.Clamp01(BaseElevation);
            MacroScaleMeters = Mathf.Max(1f, MacroScaleMeters);
            MacroAmplitude = Mathf.Clamp01(MacroAmplitude);

            MountainScaleMeters = Mathf.Max(1f, MountainScaleMeters);
            MountainAmplitude = Mathf.Clamp01(MountainAmplitude);
            MountainThreshold = Mathf.Clamp01(MountainThreshold);
            MountainBlend = Mathf.Clamp(MountainBlend, 0.001f, 0.5f);

            WarpScaleMeters = Mathf.Max(1f, WarpScaleMeters);
            WarpStrengthMeters = Mathf.Max(0f, WarpStrengthMeters);

            DetailScaleMeters = Mathf.Max(1f, DetailScaleMeters);
            DetailAmplitude = Mathf.Clamp(DetailAmplitude, 0f, 0.5f);

            Octaves = Mathf.Clamp(Octaves, 1, 8);
            Persistence = Mathf.Clamp(Persistence, 0.1f, 0.9f);
            Lacunarity = Mathf.Clamp(Lacunarity, 1.1f, 4f);
        }
    }
}
