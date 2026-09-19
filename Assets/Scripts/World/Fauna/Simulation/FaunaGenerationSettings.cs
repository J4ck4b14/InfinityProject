using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityProject.World.Fauna
{
    [Serializable]
    public sealed class FaunaDietPreference
    {
        public string FloraSpeciesStableId = "temperate_grass";
        [Range(0f, 1f)] public float Palatability = 1f;

        public FaunaDietPreference Clone()
            => (FaunaDietPreference)MemberwiseClone();

        public void Validate()
        {
            FloraSpeciesStableId ??= string.Empty;
            Palatability = Mathf.Clamp01(Palatability);
        }
    }

    /// <summary>
    /// Configuration for the current deer consumer model. Values use physical units where practical;
    /// the model is an ecological baseline rather than a full zoological simulation.
    /// </summary>
    [Serializable]
    public sealed class FaunaSpeciesProfile
    {
        public string StableId = "deer";
        public string DisplayName = "Deer";

        [Header("Population")]
        [Min(1)] public int AgentCount = 30;
        [Min(1)] public int HerdSize = 5;

        [Header("Locomotion / perception")]
        [Min(1f)] public float MoveSpeedMetersPerDay = 1800f;
        [Min(1f)] public float ForagePerceptionRadiusMeters = 110f;
        [Min(0.5f)] public float BrowseRadiusMeters = 9f;
        [Range(1f, 89f)] public float MaxTraversableSlopeDegrees = 34f;
        [Range(0f, 89f)] public float ComfortableSlopeDegrees = 22f;
        [Range(0f, 2f)] public float ForageSteeringWeight = 1.2f;
        [Range(0f, 2f)] public float HerdCohesionWeight = 0.38f;
        [Min(1f)] public float HerdCohesionRadiusMeters = 75f;
        [Range(0f, 1f)] public float HeadingPersistenceWeight = 0.2f;
        [Range(0f, 0.5f)] public float ExplorationWeight = 0.08f;

        [Header("Metabolism")]
        [Min(0.01f)] public float DailyFoodRequirementKg = 4.5f;
        [Min(0f)] public float HungerIncreasePerDay = 0.32f;
        [Min(0f)] public float EnergyUsePerDay = 0.10f;
        [Min(0f)] public float EnergyRecoveryPerFullRationPerDay = 0.22f;
        [Min(0f)] public float StarvationHealthLossPerDay = 0.35f;
        [Min(0f)] public float HealthRecoveryPerDay = 0.025f;

        [Header("Diet")]
        public List<FaunaDietPreference> Diet = CreateDefaultDiet();

        public FaunaSpeciesProfile Clone()
        {
            var clone = (FaunaSpeciesProfile)MemberwiseClone();
            clone.Diet = new List<FaunaDietPreference>();
            if (Diet != null)
                foreach (FaunaDietPreference item in Diet) clone.Diet.Add(item?.Clone() ?? new FaunaDietPreference());
            clone.Validate();
            return clone;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(StableId)) StableId = "deer";
            if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = StableId;
            AgentCount = Mathf.Clamp(AgentCount, 1, 10000);
            HerdSize = Mathf.Clamp(HerdSize, 1, AgentCount);
            MoveSpeedMetersPerDay = Mathf.Max(1f, MoveSpeedMetersPerDay);
            ForagePerceptionRadiusMeters = Mathf.Max(1f, ForagePerceptionRadiusMeters);
            BrowseRadiusMeters = Mathf.Max(0.5f, BrowseRadiusMeters);
            MaxTraversableSlopeDegrees = Mathf.Clamp(MaxTraversableSlopeDegrees, 1f, 89f);
            ComfortableSlopeDegrees = Mathf.Clamp(ComfortableSlopeDegrees, 0f, MaxTraversableSlopeDegrees);
            ForageSteeringWeight = Mathf.Clamp(ForageSteeringWeight, 0f, 2f);
            HerdCohesionWeight = Mathf.Clamp(HerdCohesionWeight, 0f, 2f);
            HerdCohesionRadiusMeters = Mathf.Max(1f, HerdCohesionRadiusMeters);
            HeadingPersistenceWeight = Mathf.Clamp01(HeadingPersistenceWeight);
            ExplorationWeight = Mathf.Clamp(ExplorationWeight, 0f, 0.5f);
            DailyFoodRequirementKg = Mathf.Max(0.01f, DailyFoodRequirementKg);
            HungerIncreasePerDay = Mathf.Max(0f, HungerIncreasePerDay);
            EnergyUsePerDay = Mathf.Max(0f, EnergyUsePerDay);
            EnergyRecoveryPerFullRationPerDay = Mathf.Max(0f, EnergyRecoveryPerFullRationPerDay);
            StarvationHealthLossPerDay = Mathf.Max(0f, StarvationHealthLossPerDay);
            HealthRecoveryPerDay = Mathf.Max(0f, HealthRecoveryPerDay);
            Diet ??= new List<FaunaDietPreference>();
            for (int i = 0; i < Diet.Count; i++)
            {
                Diet[i] ??= new FaunaDietPreference();
                Diet[i].Validate();
            }
        }

        public static List<FaunaDietPreference> CreateDefaultDiet()
            => new()
            {
                new FaunaDietPreference { FloraSpeciesStableId = "temperate_grass", Palatability = 1f },
                new FaunaDietPreference { FloraSpeciesStableId = "riparian_shrub", Palatability = 0.55f },
                new FaunaDietPreference { FloraSpeciesStableId = "cold_tolerant_conifer", Palatability = 0.08f }
            };
    }

    [Serializable]
    public sealed class FaunaGenerationSettings
    {
        public int Seed = 7331;
        [Range(0.002f, 0.25f)] public float MaximumMovementSubstepDays = 0.02f;
        [Range(4, 32)] public int ForageDirectionSamples = 12;
        public FaunaSpeciesProfile Species = new();

        public FaunaGenerationSettings Clone()
        {
            var clone = (FaunaGenerationSettings)MemberwiseClone();
            clone.Species = Species?.Clone() ?? new FaunaSpeciesProfile();
            clone.Validate();
            return clone;
        }

        public void Validate()
        {
            MaximumMovementSubstepDays = Mathf.Clamp(MaximumMovementSubstepDays, 0.002f, 0.25f);
            ForageDirectionSamples = Mathf.Clamp(ForageDirectionSamples, 4, 32);
            Species ??= new FaunaSpeciesProfile();
            Species.Validate();
        }
    }
}
