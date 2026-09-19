using System;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    /// <summary>
    /// Mutable, authoritative biomass state for one flora species. Environmental suitability and carrying
    /// capacity remain derived in FloraData; this class answers how much living biomass exists now.
    /// </summary>
    [Serializable]
    public sealed class FloraSimulationSpeciesState
    {
        public string StableId { get; }
        public string DisplayName { get; }
        public float[] CurrentBiomassKgPerSquareMeter { get; }
        public float[] CumulativeRemovedKgPerSquareMeter { get; }

        public FloraSimulationSpeciesState(
            string stableId,
            string displayName,
            float[] currentBiomassKgPerSquareMeter,
            float[] cumulativeRemovedKgPerSquareMeter)
        {
            if (currentBiomassKgPerSquareMeter == null)
                throw new ArgumentNullException(nameof(currentBiomassKgPerSquareMeter));
            if (cumulativeRemovedKgPerSquareMeter == null || cumulativeRemovedKgPerSquareMeter.Length != currentBiomassKgPerSquareMeter.Length)
                throw new ArgumentException("Removal history must match biomass length.", nameof(cumulativeRemovedKgPerSquareMeter));

            StableId = stableId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? StableId : displayName;
            CurrentBiomassKgPerSquareMeter = currentBiomassKgPerSquareMeter;
            CumulativeRemovedKgPerSquareMeter = cumulativeRemovedKgPerSquareMeter;
        }
    }

    [Serializable]
    public sealed class FloraSimulationData
    {
        public int Resolution { get; }
        public float WidthMeters { get; }
        public float LengthMeters { get; }
        public int SourceGeographySeed { get; }
        public double SimulationTimeDays { get; private set; }
        public FloraSimulationSpeciesState[] Species { get; }

        public int SpeciesCount => Species?.Length ?? 0;
        public float SampleSpacingX => WidthMeters / (Resolution - 1);
        public float SampleSpacingZ => LengthMeters / (Resolution - 1);
        public float ApproximateCellAreaSquareMeters => SampleSpacingX * SampleSpacingZ;

        /// <summary>
        /// Trapezoidal integration area represented by one raster sample. Edge samples own half a cell in the
        /// clipped dimension and corners own a quarter, so a uniform density integrates to exactly Width×Length.
        /// </summary>
        public float SampleAreaSquareMeters(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            float xWeight = x == 0 || x == Resolution - 1 ? 0.5f : 1f;
            float yWeight = y == 0 || y == Resolution - 1 ? 0.5f : 1f;
            return SampleSpacingX * SampleSpacingZ * xWeight * yWeight;
        }

        public FloraSimulationData(
            int resolution,
            float widthMeters,
            float lengthMeters,
            int sourceGeographySeed,
            double simulationTimeDays,
            FloraSimulationSpeciesState[] species)
        {
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (widthMeters <= 0f || lengthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(widthMeters));
            Species = species ?? throw new ArgumentNullException(nameof(species));
            int expected = resolution * resolution;
            for (int i = 0; i < Species.Length; i++)
            {
                if (Species[i] == null || Species[i].CurrentBiomassKgPerSquareMeter.Length != expected)
                    throw new ArgumentException("Every living flora layer must contain resolution² samples.", nameof(species));
            }

            Resolution = resolution;
            WidthMeters = widthMeters;
            LengthMeters = lengthMeters;
            SourceGeographySeed = sourceGeographySeed;
            SimulationTimeDays = Math.Max(0d, simulationTimeDays);
        }

        public static FloraSimulationData CreateInitial(FloraData potential)
        {
            if (potential == null) throw new ArgumentNullException(nameof(potential));
            var layers = new FloraSimulationSpeciesState[potential.SpeciesCount];
            for (int i = 0; i < layers.Length; i++)
            {
                FloraSpeciesData source = potential.Species[i];
                float[] biomass = (float[])source.InitialBiomassKgPerSquareMeter.Clone();
                layers[i] = new FloraSimulationSpeciesState(
                    source.StableId,
                    source.DisplayName,
                    biomass,
                    new float[biomass.Length]);
            }
            return new FloraSimulationData(
                potential.Resolution,
                potential.WidthMeters,
                potential.LengthMeters,
                potential.SourceGeographySeed,
                0d,
                layers);
        }

        /// <summary>
        /// Rebinds living biomass to a newly generated potential field by stable species ID. Existing biomass
        /// and removal history are preserved whenever the physical raster is compatible; new species start from
        /// their generated initial biomass. This is what allows climate changes to cause gradual dieback/recovery
        /// instead of resetting ecological history.
        /// </summary>
        public static FloraSimulationData RebindPreservingBiomass(FloraSimulationData previous, FloraData potential)
        {
            if (potential == null) throw new ArgumentNullException(nameof(potential));
            if (previous == null) return CreateInitial(potential);
            if (previous.Resolution != potential.Resolution ||
                Mathf.Abs(previous.WidthMeters - potential.WidthMeters) > 0.001f ||
                Mathf.Abs(previous.LengthMeters - potential.LengthMeters) > 0.001f ||
                previous.SourceGeographySeed != potential.SourceGeographySeed)
                return CreateInitial(potential);

            var layers = new FloraSimulationSpeciesState[potential.SpeciesCount];
            for (int i = 0; i < layers.Length; i++)
            {
                FloraSpeciesData target = potential.Species[i];
                int previousIndex = previous.FindSpeciesIndex(target.StableId);
                if (previousIndex >= 0)
                {
                    FloraSimulationSpeciesState old = previous.Species[previousIndex];
                    layers[i] = new FloraSimulationSpeciesState(
                        target.StableId,
                        target.DisplayName,
                        (float[])old.CurrentBiomassKgPerSquareMeter.Clone(),
                        (float[])old.CumulativeRemovedKgPerSquareMeter.Clone());
                }
                else
                {
                    float[] biomass = (float[])target.InitialBiomassKgPerSquareMeter.Clone();
                    layers[i] = new FloraSimulationSpeciesState(
                        target.StableId,
                        target.DisplayName,
                        biomass,
                        new float[biomass.Length]);
                }
            }

            return new FloraSimulationData(
                potential.Resolution,
                potential.WidthMeters,
                potential.LengthMeters,
                potential.SourceGeographySeed,
                previous.SimulationTimeDays,
                layers);
        }

        public int FindSpeciesIndex(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return -1;
            if (Species == null) return -1;
            for (int i = 0; i < Species.Length; i++)
                if (Species[i] != null && string.Equals(Species[i].StableId, stableId, StringComparison.Ordinal)) return i;
            return -1;
        }

        public int Index(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return y * Resolution + x;
        }

        public int WorldToNearestIndex(Vector2 localXZ)
        {
            int x = Mathf.RoundToInt(Mathf.Clamp01(localXZ.x / WidthMeters) * (Resolution - 1));
            int y = Mathf.RoundToInt(Mathf.Clamp01(localXZ.y / LengthMeters) * (Resolution - 1));
            return Index(x, y);
        }

        public float SampleBiomassDensity(int speciesIndex, Vector2 localXZ)
        {
            if (Species == null || speciesIndex < 0 || speciesIndex >= Species.Length || Species[speciesIndex] == null) return 0f;
            return Species[speciesIndex].CurrentBiomassKgPerSquareMeter[WorldToNearestIndex(localXZ)];
        }

        public double TotalBiomassKg(int speciesIndex)
        {
            if (Species == null || speciesIndex < 0 || speciesIndex >= Species.Length || Species[speciesIndex] == null) return 0d;
            return IntegrateDensityKg(Species[speciesIndex].CurrentBiomassKgPerSquareMeter);
        }

        public double IntegrateDensityKg(float[] densityKgPerSquareMeter)
        {
            if (densityKgPerSquareMeter == null || densityKgPerSquareMeter.Length != Resolution * Resolution)
                throw new ArgumentException("Density raster must contain resolution² samples.", nameof(densityKgPerSquareMeter));

            double total = 0d;
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
                total += densityKgPerSquareMeter[Index(x, y)] * SampleAreaSquareMeters(x, y);
            return total;
        }

        internal void SetSimulationTime(double absoluteDays)
            => SimulationTimeDays = Math.Max(0d, absoluteDays);
    }
}
