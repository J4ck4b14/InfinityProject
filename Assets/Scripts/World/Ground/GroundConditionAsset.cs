using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Hydrology;
using UnityEngine;

namespace InfinityProject.World.Ground
{
    /// <summary>Persistent metadata for Ground Conditions derived from Geography + Hydrology + Climate.</summary>
    [CreateAssetMenu(menuName = "Infinity/World/Ground Conditions Asset")]
    public sealed class GroundConditionAsset : ScriptableObject
    {
        private const int CurrentModelVersion = 4;

        [SerializeField] private GroundConditionGenerationSettings settings = new();
        [SerializeField] private int modelVersion;
        [SerializeField] private GeographyAsset sourceGeography;
        [SerializeField] private HydrologyAsset sourceHydrology;
        [SerializeField] private ClimateAsset sourceClimate;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int sourceHydrologyRevision;
        [SerializeField] private int sourceClimateRevision;
        [SerializeField] private int generationRevision;
        [SerializeField] private int resolution;
        [SerializeField] private float widthMeters;
        [SerializeField] private float lengthMeters;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private long sourceHeightHash;
        [SerializeField] private float minimumWetnessIndex;
        [SerializeField] private float maximumWetnessIndex;
        [SerializeField] private float percentile05WetnessIndex;
        [SerializeField] private float percentile95WetnessIndex;

        public GroundConditionGenerationSettings Settings => settings;
        public int ModelVersion => modelVersion;
        public GeographyAsset SourceGeography => sourceGeography;
        public HydrologyAsset SourceHydrology => sourceHydrology;
        public ClimateAsset SourceClimate => sourceClimate;
        public int GenerationRevision => generationRevision;
        public int Resolution => resolution;
        public float MinimumWetnessIndex => minimumWetnessIndex;
        public float MaximumWetnessIndex => maximumWetnessIndex;
        public float Percentile05WetnessIndex => percentile05WetnessIndex;
        public float Percentile95WetnessIndex => percentile95WetnessIndex;

        public bool HasMetadata => resolution >= 2 && widthMeters > 0f && lengthMeters > 0f &&
                                   sourceGeography != null && sourceHydrology != null && sourceClimate != null;

        public void StoreMetadata(GroundConditionData data, GroundConditionGenerationSettings generationSettings,
            GeographyAsset geographyAsset, HydrologyAsset hydrologyAsset, ClimateAsset climateAsset)
        {
            settings = generationSettings?.Clone() ?? new GroundConditionGenerationSettings(); settings.Validate();
            modelVersion = CurrentModelVersion;
            sourceGeography = geographyAsset; sourceHydrology = hydrologyAsset; sourceClimate = climateAsset;
            sourceGeographyRevision = geographyAsset != null ? geographyAsset.GenerationRevision : 0;
            sourceHydrologyRevision = hydrologyAsset != null ? hydrologyAsset.GenerationRevision : 0;
            sourceClimateRevision = climateAsset != null ? climateAsset.GenerationRevision : 0;
            resolution = data.Resolution; widthMeters = data.WidthMeters; lengthMeters = data.LengthMeters;
            sourceGeographySeed = data.SourceGeographySeed; sourceHeightHash = data.SourceHeightHash;
            minimumWetnessIndex = data.MinimumWetnessIndex; maximumWetnessIndex = data.MaximumWetnessIndex;
            percentile05WetnessIndex = data.Percentile05WetnessIndex; percentile95WetnessIndex = data.Percentile95WetnessIndex;
            generationRevision++;
        }

        public bool Matches(GeographyAsset geographyAsset, HydrologyAsset hydrologyAsset, ClimateAsset climateAsset)
        {
            if (!HasMetadata || geographyAsset == null || hydrologyAsset == null || climateAsset == null) return false;
            if (!geographyAsset.HasData || !hydrologyAsset.HasMetadata || !climateAsset.HasMetadata) return false;
            if (sourceGeography != geographyAsset || sourceHydrology != hydrologyAsset || sourceClimate != climateAsset) return false;
            if (sourceGeographyRevision != geographyAsset.GenerationRevision || sourceHydrologyRevision != hydrologyAsset.GenerationRevision ||
                sourceClimateRevision != climateAsset.GenerationRevision) return false;
            if (!hydrologyAsset.Matches(geographyAsset) || !climateAsset.Matches(geographyAsset)) return false;
            return modelVersion == CurrentModelVersion && resolution == geographyAsset.Settings.Resolution && sourceGeographySeed == geographyAsset.Settings.Seed;
        }
    }
}
