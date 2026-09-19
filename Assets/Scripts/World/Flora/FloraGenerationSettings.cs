using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    public enum FloraLimitingFactor : byte
    {
        None = 0,
        Temperature = 1,
        WaterAvailability = 2,
        SoilRetention = 3,
        Multiple = 4
    }

    /// <summary>
    /// Species response envelope. Flora V1.1 consumes temperature, generic Water Availability Potential and
    /// substrate-retention potential. It no longer treats precipitation and topographic wetness as two independent gates.
    /// </summary>
    [Serializable]
    public sealed class FloraSpeciesProfile
    {
        public string StableId = "species";
        public string DisplayName = "Species";

        [Header("Temperature response (°C)")]
        public float MinimumTemperatureCelsius = -5f;
        public float OptimalTemperatureCelsius = 16f;
        public float MaximumTemperatureCelsius = 34f;

        [Header("Water-availability niche")]
        [Range(0f, 1f)] public float MinimumWaterAvailabilityPotential = 0.25f;
        [Range(0f, 1f)] public float OptimalWaterAvailabilityPotential = 0.55f;
        [Range(0f, 1f)] public float MaximumWaterAvailabilityPotential = 0.9f;

        [Header("Soil-retention response")]
        [Range(0f, 1f)] public float MinimumSoilRetentionPotential = 0.05f;
        [Range(0f, 1f)] public float FullSoilRetentionPotential = 0.4f;

        [Header("Establishment")]
        [Range(0f, 1f)] public float EstablishmentThreshold = 0.35f;
        [Range(0f, 1f)] public float InitialOccupancyFraction = 0.65f;

        [Header("Living biomass")]
        [Min(0.001f)] public float MaximumBiomassKgPerSquareMeter = 1f;
        [Min(0f)] public float IntrinsicGrowthRatePerDay = 0.025f;
        [Min(0f)] public float SeedBankRecruitmentRatePerDay = 0.0015f;
        [Min(0f)] public float StressMortalityRatePerDay = 0.08f;
        [Range(0f, 1f)] public float CompetitionStrength = 0.2f;

        public FloraSpeciesProfile Clone() => (FloraSpeciesProfile)MemberwiseClone();

        public void Validate(int fallbackIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(StableId)) StableId = $"species_{fallbackIndex}";
            if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = StableId;
            if (OptimalTemperatureCelsius < MinimumTemperatureCelsius) OptimalTemperatureCelsius = MinimumTemperatureCelsius;
            if (MaximumTemperatureCelsius < OptimalTemperatureCelsius) MaximumTemperatureCelsius = OptimalTemperatureCelsius;
            MinimumWaterAvailabilityPotential = Mathf.Clamp01(MinimumWaterAvailabilityPotential);
            OptimalWaterAvailabilityPotential = Mathf.Clamp(OptimalWaterAvailabilityPotential, MinimumWaterAvailabilityPotential, 1f);
            MaximumWaterAvailabilityPotential = Mathf.Clamp(MaximumWaterAvailabilityPotential, OptimalWaterAvailabilityPotential, 1f);
            MinimumSoilRetentionPotential = Mathf.Clamp01(MinimumSoilRetentionPotential);
            FullSoilRetentionPotential = Mathf.Clamp(FullSoilRetentionPotential, MinimumSoilRetentionPotential, 1f);
            EstablishmentThreshold = Mathf.Clamp01(EstablishmentThreshold); InitialOccupancyFraction = Mathf.Clamp01(InitialOccupancyFraction);
            MaximumBiomassKgPerSquareMeter = Mathf.Max(0.001f, MaximumBiomassKgPerSquareMeter);
            IntrinsicGrowthRatePerDay = Mathf.Max(0f, IntrinsicGrowthRatePerDay); SeedBankRecruitmentRatePerDay = Mathf.Max(0f, SeedBankRecruitmentRatePerDay);
            StressMortalityRatePerDay = Mathf.Max(0f, StressMortalityRatePerDay); CompetitionStrength = Mathf.Clamp01(CompetitionStrength);
        }
    }

    [Serializable]
    public sealed class FloraGenerationSettings
    {
        public List<FloraSpeciesProfile> Species = CreateDefaultSpecies();
        public FloraGenerationSettings Clone()
        {
            var clone = new FloraGenerationSettings { Species = new List<FloraSpeciesProfile>() };
            if (Species != null) foreach (FloraSpeciesProfile s in Species) clone.Species.Add(s?.Clone() ?? new FloraSpeciesProfile());
            clone.Validate(); return clone;
        }
        public void Validate()
        {
            Species ??= new List<FloraSpeciesProfile>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Species.Count; i++)
            {
                Species[i] ??= new FloraSpeciesProfile(); Species[i].Validate(i); string baseId = Species[i].StableId, id = baseId; int suffix = 2;
                while (!ids.Add(id)) id = $"{baseId}_{suffix++}"; Species[i].StableId = id;
            }
        }
        public FloraSpeciesProfile FindSpecies(string stableId)
        {
            if (Species == null || string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < Species.Count; i++) if (Species[i] != null && string.Equals(Species[i].StableId, stableId, StringComparison.Ordinal)) return Species[i];
            return null;
        }

        public static List<FloraSpeciesProfile> CreateDefaultSpecies() => new()
        {
            new FloraSpeciesProfile
            {
                StableId = "temperate_grass", DisplayName = "Temperate Grass",
                MinimumTemperatureCelsius = 2f, OptimalTemperatureCelsius = 18f, MaximumTemperatureCelsius = 34f,
                MinimumWaterAvailabilityPotential = 0.24f, OptimalWaterAvailabilityPotential = 0.55f, MaximumWaterAvailabilityPotential = 0.92f,
                MinimumSoilRetentionPotential = 0.04f, FullSoilRetentionPotential = 0.28f,
                EstablishmentThreshold = 0.32f, InitialOccupancyFraction = 0.72f,
                MaximumBiomassKgPerSquareMeter = 0.75f, IntrinsicGrowthRatePerDay = 0.055f, SeedBankRecruitmentRatePerDay = 0.004f,
                StressMortalityRatePerDay = 0.12f, CompetitionStrength = 0.18f
            },
            new FloraSpeciesProfile
            {
                StableId = "riparian_shrub", DisplayName = "Riparian Shrub",
                MinimumTemperatureCelsius = 0f, OptimalTemperatureCelsius = 17f, MaximumTemperatureCelsius = 32f,
                MinimumWaterAvailabilityPotential = 0.55f, OptimalWaterAvailabilityPotential = 0.78f, MaximumWaterAvailabilityPotential = 1f,
                MinimumSoilRetentionPotential = 0.10f, FullSoilRetentionPotential = 0.36f,
                EstablishmentThreshold = 0.40f, InitialOccupancyFraction = 0.62f,
                MaximumBiomassKgPerSquareMeter = 2.2f, IntrinsicGrowthRatePerDay = 0.018f, SeedBankRecruitmentRatePerDay = 0.0012f,
                StressMortalityRatePerDay = 0.07f, CompetitionStrength = 0.28f
            },
            new FloraSpeciesProfile
            {
                StableId = "cold_tolerant_conifer", DisplayName = "Cold-Tolerant Conifer",
                MinimumTemperatureCelsius = -18f, OptimalTemperatureCelsius = 8f, MaximumTemperatureCelsius = 23f,
                MinimumWaterAvailabilityPotential = 0.28f, OptimalWaterAvailabilityPotential = 0.52f, MaximumWaterAvailabilityPotential = 0.86f,
                MinimumSoilRetentionPotential = 0.14f, FullSoilRetentionPotential = 0.46f,
                EstablishmentThreshold = 0.38f, InitialOccupancyFraction = 0.52f,
                MaximumBiomassKgPerSquareMeter = 7.5f, IntrinsicGrowthRatePerDay = 0.0035f, SeedBankRecruitmentRatePerDay = 0.00015f,
                StressMortalityRatePerDay = 0.028f, CompetitionStrength = 0.35f
            }
        };
    }
}
