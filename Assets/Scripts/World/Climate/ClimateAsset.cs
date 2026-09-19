using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Climate
{
    /// <summary>
    /// Persistent metadata for Climate V0. Heavy raster arrays remain in a reproducible binary editor cache.
    /// </summary>
    [CreateAssetMenu(menuName = "Infinity/World/Climate Asset")]
    public sealed class ClimateAsset : ScriptableObject
    {
        [SerializeField] private ClimateGenerationSettings settings = new();
        [SerializeField] private GeographyAsset sourceGeography;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int generationRevision;
        [SerializeField] private int resolution;
        [SerializeField] private float widthMeters;
        [SerializeField] private float lengthMeters;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private float minimumTemperatureCelsius;
        [SerializeField] private float maximumTemperatureCelsius;
        [SerializeField] private float minimumPrecipitationPotential;
        [SerializeField] private float maximumPrecipitationPotential;

        public ClimateGenerationSettings Settings => settings;
        public GeographyAsset SourceGeography => sourceGeography;
        public int SourceGeographyRevision => sourceGeographyRevision;
        public int GenerationRevision => generationRevision;
        public int Resolution => resolution;
        public float MinimumTemperatureCelsius => minimumTemperatureCelsius;
        public float MaximumTemperatureCelsius => maximumTemperatureCelsius;
        public float MinimumPrecipitationPotential => minimumPrecipitationPotential;
        public float MaximumPrecipitationPotential => maximumPrecipitationPotential;

        public bool HasMetadata =>
            resolution >= 2 && widthMeters > 0f && lengthMeters > 0f && sourceGeography != null;

        public void StoreMetadata(ClimateData data, ClimateGenerationSettings generationSettings, GeographyAsset geographyAsset)
        {
            settings = generationSettings?.Clone() ?? new ClimateGenerationSettings();
            settings.Validate();
            sourceGeography = geographyAsset;
            sourceGeographyRevision = geographyAsset != null ? geographyAsset.GenerationRevision : 0;
            resolution = data.Resolution;
            widthMeters = data.WidthMeters;
            lengthMeters = data.LengthMeters;
            sourceGeographySeed = data.SourceGeographySeed;
            minimumTemperatureCelsius = data.MinimumTemperatureCelsius;
            maximumTemperatureCelsius = data.MaximumTemperatureCelsius;
            minimumPrecipitationPotential = data.MinimumPrecipitationPotential;
            maximumPrecipitationPotential = data.MaximumPrecipitationPotential;
            generationRevision++;
        }

        public bool Matches(GeographyAsset geographyAsset)
        {
            if (!HasMetadata || geographyAsset == null || !geographyAsset.HasData)
                return false;
            if (sourceGeography != geographyAsset || sourceGeographyRevision != geographyAsset.GenerationRevision)
                return false;

            return resolution == geographyAsset.Settings.Resolution &&
                   sourceGeographySeed == geographyAsset.Settings.Seed;
        }
    }
}
