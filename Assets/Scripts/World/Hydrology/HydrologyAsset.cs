using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Hydrology
{
    /// <summary>
    /// Persistent metadata for hydrology derived from one geography state.
    /// Heavy per-sample arrays live in an editor cache instead of Unity YAML serialization.
    /// </summary>
    [CreateAssetMenu(menuName = "Infinity/World/Hydrology Asset")]
    public sealed class HydrologyAsset : ScriptableObject
    {
        [SerializeField] private HydrologyGenerationSettings settings = new();
        [SerializeField] private GeographyAsset sourceGeography;
        [SerializeField] private int resolution;
        [SerializeField] private float widthMeters;
        [SerializeField] private float lengthMeters;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private long sourceHeightHash;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int generationRevision;
        [SerializeField] private int basinCount;
        [SerializeField] private int rawSinkCount;
        [SerializeField] private int outletCount;
        [SerializeField] private int depressionSampleCount;
        [SerializeField] private float maxDepressionDepthMeters;

        public HydrologyGenerationSettings Settings => settings;
        public GeographyAsset SourceGeography => sourceGeography;
        public int Resolution => resolution;
        public int SourceGeographySeed => sourceGeographySeed;
        public long SourceHeightHash => sourceHeightHash;
        public int SourceGeographyRevision => sourceGeographyRevision;
        public int GenerationRevision => generationRevision;
        public int BasinCount => basinCount;
        public int RawSinkCount => rawSinkCount;
        public int OutletCount => outletCount;
        public int DepressionSampleCount => depressionSampleCount;
        public float MaxDepressionDepthMeters => maxDepressionDepthMeters;

        public bool HasMetadata =>
            resolution >= 2 &&
            widthMeters > 0f &&
            lengthMeters > 0f &&
            sourceGeography != null;

        public void StoreMetadata(
            HydrologyData data,
            HydrologyGenerationSettings generationSettings,
            GeographyAsset geographyAsset)
        {
            settings = generationSettings?.Clone() ?? new HydrologyGenerationSettings();
            settings.Validate();
            sourceGeography = geographyAsset;
            resolution = data.Resolution;
            widthMeters = data.WidthMeters;
            lengthMeters = data.LengthMeters;
            sourceGeographySeed = data.SourceGeographySeed;
            sourceHeightHash = data.SourceHeightHash;
            sourceGeographyRevision = geographyAsset != null ? geographyAsset.GenerationRevision : 0;
            basinCount = data.BasinCount;
            rawSinkCount = data.RawSinkCount;
            outletCount = data.OutletCount;
            depressionSampleCount = data.DepressionSampleCount;
            maxDepressionDepthMeters = data.MaxDepressionDepthMeters;
            generationRevision++;
        }

        public bool Matches(GeographyAsset geographyAsset)
        {
            if (!HasMetadata || geographyAsset == null || !geographyAsset.HasData)
                return false;
            if (sourceGeography != geographyAsset)
                return false;
            if (sourceGeographyRevision != geographyAsset.GenerationRevision)
                return false;

            GeographyGenerationSettings geoSettings = geographyAsset.Settings;
            return resolution == geoSettings.Resolution &&
                   sourceGeographySeed == geoSettings.Seed;
        }
    }
}
