using UnityEngine;

namespace InfinityProject.World.Flora
{
    /// <summary>
    /// Persistent metadata for authoritative living Flora state. Large mutable rasters live in a binary cache.
    /// </summary>
    [CreateAssetMenu(menuName = "Infinity/World/Flora State Asset")]
    public sealed class FloraStateAsset : ScriptableObject
    {
        [SerializeField] private FloraSimulationSettings settings = new();
        [SerializeField] private FloraAsset sourceFlora;
        [SerializeField] private int sourceFloraRevision;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int simulationRevision;
        [SerializeField] private int resolution;
        [SerializeField] private float widthMeters;
        [SerializeField] private float lengthMeters;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private int speciesCount;
        [SerializeField] private double simulationTimeDays;

        public FloraSimulationSettings Settings => settings;
        public FloraAsset SourceFlora => sourceFlora;
        public int SourceFloraRevision => sourceFloraRevision;
        public int SimulationRevision => simulationRevision;
        public int Resolution => resolution;
        public int SpeciesCount => speciesCount;
        public double SimulationTimeDays => simulationTimeDays;
        public bool HasMetadata => sourceFlora != null && resolution >= 2 && widthMeters > 0f && lengthMeters > 0f;

        public void StoreMetadata(FloraSimulationData data, FloraSimulationSettings simulationSettings, FloraAsset flora)
        {
            settings = simulationSettings?.Clone() ?? new FloraSimulationSettings();
            settings.Validate();
            sourceFlora = flora;
            sourceFloraRevision = flora != null ? flora.GenerationRevision : 0;
            sourceGeographyRevision = flora != null ? flora.SourceGeographyRevision : 0;
            resolution = data.Resolution;
            widthMeters = data.WidthMeters;
            lengthMeters = data.LengthMeters;
            sourceGeographySeed = data.SourceGeographySeed;
            speciesCount = data.SpeciesCount;
            simulationTimeDays = data.SimulationTimeDays;
            simulationRevision++;
        }

        public bool Matches(FloraAsset flora)
            => HasMetadata && flora != null && flora.HasMetadata &&
               sourceFlora == flora && sourceFloraRevision == flora.GenerationRevision &&
               resolution == flora.Resolution && sourceGeographySeed == flora.SourceGeographySeed &&
               speciesCount == flora.SpeciesCount;

        public bool CanRebind(FloraAsset flora)
            => HasMetadata && flora != null && flora.HasMetadata && sourceFlora == flora &&
               resolution == flora.Resolution && sourceGeographySeed == flora.SourceGeographySeed &&
               sourceGeographyRevision == flora.SourceGeographyRevision;
    }
}
