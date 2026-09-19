using InfinityProject.World.Climate;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    /// <summary>Persistent metadata/settings for Flora V1.1 potential; raster layers live in a derived binary cache.</summary>
    [CreateAssetMenu(menuName = "Infinity/World/Flora Asset")]
    public sealed class FloraAsset : ScriptableObject
    {
        private const int CurrentModelVersion = 2;
        [SerializeField] private FloraGenerationSettings settings = new();
        [SerializeField] private int modelVersion;
        [SerializeField] private GeographyAsset sourceGeography;
        [SerializeField] private HydrologyAsset sourceHydrology;
        [SerializeField] private GroundConditionAsset sourceGround;
        [SerializeField] private ClimateAsset sourceClimate;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int sourceHydrologyRevision;
        [SerializeField] private int sourceGroundRevision;
        [SerializeField] private int sourceClimateRevision;
        [SerializeField] private int generationRevision;
        [SerializeField] private int resolution;
        [SerializeField] private float widthMeters;
        [SerializeField] private float lengthMeters;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private int speciesCount;

        public FloraGenerationSettings Settings => settings;
        public int ModelVersion => modelVersion;
        public int GenerationRevision => generationRevision;
        public int SourceGeographyRevision => sourceGeographyRevision;
        public int Resolution => resolution;
        public float WidthMeters => widthMeters;
        public float LengthMeters => lengthMeters;
        public int SourceGeographySeed => sourceGeographySeed;
        public int SpeciesCount => speciesCount;
        public bool HasMetadata => resolution >= 2 && widthMeters > 0f && lengthMeters > 0f && sourceGeography != null && sourceHydrology != null && sourceGround != null && sourceClimate != null;

        public void StoreMetadata(FloraData data, FloraGenerationSettings generationSettings, GeographyAsset geography, HydrologyAsset hydrology, GroundConditionAsset ground, ClimateAsset climate)
        {
            settings = generationSettings?.Clone() ?? new FloraGenerationSettings(); settings.Validate(); modelVersion = CurrentModelVersion;
            sourceGeography = geography; sourceHydrology = hydrology; sourceGround = ground; sourceClimate = climate;
            sourceGeographyRevision = geography != null ? geography.GenerationRevision : 0; sourceHydrologyRevision = hydrology != null ? hydrology.GenerationRevision : 0;
            sourceGroundRevision = ground != null ? ground.GenerationRevision : 0; sourceClimateRevision = climate != null ? climate.GenerationRevision : 0;
            resolution = data.Resolution; widthMeters = data.WidthMeters; lengthMeters = data.LengthMeters; sourceGeographySeed = data.SourceGeographySeed; speciesCount = data.SpeciesCount; generationRevision++;
        }

        public bool Matches(GeographyAsset geography, HydrologyAsset hydrology, GroundConditionAsset ground, ClimateAsset climate)
        {
            if (!HasMetadata || geography == null || hydrology == null || ground == null || climate == null) return false;
            if (!geography.HasData || !hydrology.HasMetadata || !ground.HasMetadata || !climate.HasMetadata) return false;
            if (sourceGeography != geography || sourceHydrology != hydrology || sourceGround != ground || sourceClimate != climate) return false;
            if (sourceGeographyRevision != geography.GenerationRevision || sourceHydrologyRevision != hydrology.GenerationRevision || sourceGroundRevision != ground.GenerationRevision || sourceClimateRevision != climate.GenerationRevision) return false;
            if (!hydrology.Matches(geography) || !climate.Matches(geography) || !ground.Matches(geography, hydrology, climate)) return false;
            return modelVersion == CurrentModelVersion && resolution == geography.Settings.Resolution && sourceGeographySeed == geography.Settings.Seed && speciesCount == (settings.Species?.Count ?? 0);
        }
    }
}
