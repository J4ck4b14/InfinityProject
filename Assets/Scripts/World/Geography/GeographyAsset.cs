using UnityEngine;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Persistent serialized source for generated geography.
    /// Stores the generated elevation field and the settings that produced it.
    /// Derived metrics such as slope/aspect are rebuilt from the elevation field.
    /// </summary>
    [CreateAssetMenu(menuName = "Infinity/World/Geography Asset")]
    public sealed class GeographyAsset : ScriptableObject
    {
        [SerializeField] private GeographyGenerationSettings settings = new();
        [SerializeField] private float[] normalizedHeight;
        [SerializeField] private int generationRevision;

        public GeographyGenerationSettings Settings => settings;
        public float[] NormalizedHeight => normalizedHeight;
        public int GenerationRevision => generationRevision;
        public bool HasData => normalizedHeight != null && normalizedHeight.Length == settings.Resolution * settings.Resolution;

        public void Store(GeographyData data, GeographyGenerationSettings generationSettings)
        {
            settings = generationSettings != null ? generationSettings.Clone() : new GeographyGenerationSettings();
            settings.Resolution = data.Resolution;
            settings.WidthMeters = data.WidthMeters;
            settings.LengthMeters = data.LengthMeters;
            settings.MaxElevationMeters = data.MaxElevationMeters;
            settings.Seed = data.Seed;
            settings.Validate();

            normalizedHeight = (float[])data.NormalizedHeight.Clone();
            generationRevision++;
        }

        public GeographyData ToData()
        {
            if (!HasData)
                return null;

            return new GeographyData(
                settings.Resolution,
                settings.WidthMeters,
                settings.LengthMeters,
                settings.MaxElevationMeters,
                settings.Seed,
                (float[])normalizedHeight.Clone());
        }
    }
}
