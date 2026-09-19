using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Fauna
{
    /// <summary>Persistent fauna metadata and settings. Mobile agent state lives in a binary cache.</summary>
    [CreateAssetMenu(menuName = "Infinity/World/Fauna Asset")]
    public sealed class FaunaAsset : ScriptableObject
    {
        [SerializeField] private FaunaGenerationSettings settings = new();
        [SerializeField] private GeographyAsset sourceGeography;
        [SerializeField] private FloraAsset sourceFlora;
        [SerializeField] private int sourceGeographyRevision;
        [SerializeField] private int sourceFloraRevision;
        [SerializeField] private int simulationRevision;
        [SerializeField] private int sourceGeographySeed;
        [SerializeField] private int agentCount;
        [SerializeField] private int aliveCount;
        [SerializeField] private double simulationTimeDays;

        public FaunaGenerationSettings Settings => settings;
        public int SimulationRevision => simulationRevision;
        public int AgentCount => agentCount;
        public int AliveCount => aliveCount;
        public double SimulationTimeDays => simulationTimeDays;
        public bool HasMetadata => sourceGeography != null && sourceFlora != null && agentCount > 0;

        public void StoreMetadata(FaunaSimulationData data, FaunaGenerationSettings generationSettings, GeographyAsset geography, FloraAsset flora)
        {
            settings = generationSettings?.Clone() ?? new FaunaGenerationSettings();
            settings.Validate();
            sourceGeography = geography;
            sourceFlora = flora;
            sourceGeographyRevision = geography != null ? geography.GenerationRevision : 0;
            sourceFloraRevision = flora != null ? flora.GenerationRevision : 0;
            sourceGeographySeed = data.SourceGeographySeed;
            agentCount = data.AgentCount;
            aliveCount = data.AliveCount;
            simulationTimeDays = data.SimulationTimeDays;
            simulationRevision++;
        }

        public bool Matches(GeographyAsset geography, FloraAsset flora)
            => HasMetadata && geography != null && flora != null && geography.HasData && flora.HasMetadata &&
               sourceGeography == geography && sourceFlora == flora &&
               sourceGeographyRevision == geography.GenerationRevision && sourceFloraRevision == flora.GenerationRevision &&
               sourceGeographySeed == geography.Settings.Seed;

        public bool CanRebind(GeographyAsset geography, FloraAsset flora)
            => HasMetadata && geography != null && flora != null && geography.HasData && flora.HasMetadata &&
               sourceGeography == geography && sourceFlora == flora &&
               sourceGeographyRevision == geography.GenerationRevision && sourceGeographySeed == geography.Settings.Seed;
    }
}
