using System;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    [Serializable]
    public sealed class FloraSpeciesData
    {
        public string StableId { get; }
        public string DisplayName { get; }
        public float[] EstablishmentSuitability { get; }
        public float[] InitialBiomass { get; }
        public float[] CarryingCapacityKgPerSquareMeter { get; }
        public float[] InitialBiomassKgPerSquareMeter { get; }
        public byte[] LimitingFactor { get; }

        public FloraSpeciesData(
            string stableId,
            string displayName,
            float[] suitability,
            float[] initialBiomass,
            float[] carryingCapacityKgPerSquareMeter,
            float[] initialBiomassKgPerSquareMeter,
            byte[] limitingFactor)
        {
            if (suitability == null) throw new ArgumentNullException(nameof(suitability));
            if (initialBiomass == null || initialBiomass.Length != suitability.Length)
                throw new ArgumentException("Biomass array must match suitability length.", nameof(initialBiomass));
            if (carryingCapacityKgPerSquareMeter == null || carryingCapacityKgPerSquareMeter.Length != suitability.Length)
                throw new ArgumentException("Carrying-capacity array must match suitability length.", nameof(carryingCapacityKgPerSquareMeter));
            if (initialBiomassKgPerSquareMeter == null || initialBiomassKgPerSquareMeter.Length != suitability.Length)
                throw new ArgumentException("Initial biomass-density array must match suitability length.", nameof(initialBiomassKgPerSquareMeter));
            if (limitingFactor == null || limitingFactor.Length != suitability.Length)
                throw new ArgumentException("Limiting-factor array must match suitability length.", nameof(limitingFactor));

            StableId = stableId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? StableId : displayName;
            EstablishmentSuitability = suitability;
            InitialBiomass = initialBiomass;
            CarryingCapacityKgPerSquareMeter = carryingCapacityKgPerSquareMeter;
            InitialBiomassKgPerSquareMeter = initialBiomassKgPerSquareMeter;
            LimitingFactor = limitingFactor;
        }
    }

    [Serializable]
    public sealed class FloraData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public int SourceGeographySeed { get; }
        public FloraSpeciesData[] Species { get; }

        public FloraData(int resolution, float widthMeters, float lengthMeters, int sourceGeographySeed, FloraSpeciesData[] species)
        {
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f || lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            Species = species ?? throw new ArgumentNullException(nameof(species));
            int expected = resolution * resolution;
            foreach (FloraSpeciesData layer in Species)
            {
                if (layer == null || layer.EstablishmentSuitability.Length != expected)
                    throw new ArgumentException("Every flora layer must contain resolution² samples.", nameof(species));
            }
            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            SourceGeographySeed = sourceGeographySeed;
        }

        public int SpeciesCount => Species.Length;
        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);
        public float ApproximateCellAreaSquareMeters => SampleSpacingX * SampleSpacingZ;

        public FloraSpeciesData GetSpecies(int index)
        {
            if (Species.Length == 0) return null;
            return Species[Mathf.Clamp(index, 0, Species.Length - 1)];
        }

        public int FindSpeciesIndex(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return -1;
            for (int i = 0; i < Species.Length; i++)
                if (string.Equals(Species[i].StableId, stableId, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
