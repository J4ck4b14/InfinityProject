using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    /// <summary>
    /// Deterministic Flora V1 biomass process. Static FloraData supplies environmental carrying capacity;
    /// FloraSimulationData owns current biomass and removal history.
    /// </summary>
    public static class FloraSimulator
    {
        private const float Epsilon = 0.000001f;

        public static void Step(
            FloraData potential,
            FloraGenerationSettings generationSettings,
            FloraSimulationData state,
            FloraSimulationSettings simulationSettings,
            float deltaDays)
        {
            if (potential == null) throw new ArgumentNullException(nameof(potential));
            if (generationSettings == null) throw new ArgumentNullException(nameof(generationSettings));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (deltaDays < 0f || float.IsNaN(deltaDays) || float.IsInfinity(deltaDays))
                throw new ArgumentOutOfRangeException(nameof(deltaDays));
            if (deltaDays <= 0f) return;

            ValidateCompatible(potential, state);
            generationSettings = generationSettings.Clone();
            generationSettings.Validate();
            simulationSettings = simulationSettings?.Clone() ?? new FloraSimulationSettings();
            simulationSettings.Validate();

            int speciesCount = potential.SpeciesCount;
            if (speciesCount != state.SpeciesCount)
                throw new ArgumentException("Flora potential/state species counts differ. Rebind living Flora first.");

            FloraSpeciesProfile[] profiles = new FloraSpeciesProfile[speciesCount];
            for (int s = 0; s < speciesCount; s++)
            {
                string stableId = potential.Species[s].StableId;
                if (!string.Equals(stableId, state.Species[s].StableId, StringComparison.Ordinal))
                    throw new ArgumentException("Flora potential/state species ordering differs. Rebind living Flora first.");
                profiles[s] = generationSettings.FindSpecies(stableId)
                    ?? throw new ArgumentException($"Missing Flora profile '{stableId}'.");
            }

            // Substeps are an integration detail, not authoritative time. Compute the requested target once
            // and snap the state to it after integration so floating-point substep accumulation cannot drift.
            double targetTimeDays = state.SimulationTimeDays + (double)deltaDays;
            float remaining = deltaDays;
            while (remaining > Epsilon)
            {
                float dt = Mathf.Min(remaining, simulationSettings.MaximumSubstepDays);
                StepSubstep(potential, state, profiles, simulationSettings, dt);
                remaining -= dt;
            }
            state.SetSimulationTime(targetTimeDays);
        }

        private static void StepSubstep(
            FloraData potential,
            FloraSimulationData state,
            FloraSpeciesProfile[] profiles,
            FloraSimulationSettings settings,
            float dt)
        {
            int speciesCount = potential.SpeciesCount;
            int sampleCount = potential.Resolution * potential.Resolution;
            var occupancy = new float[speciesCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float occupancySum = 0f;
                for (int s = 0; s < speciesCount; s++)
                {
                    float baseK = potential.Species[s].CarryingCapacityKgPerSquareMeter[i];
                    float biomass = state.Species[s].CurrentBiomassKgPerSquareMeter[i];
                    float ratio = baseK > Epsilon ? Mathf.Clamp01(biomass / baseK) : 0f;
                    occupancy[s] = ratio;
                    occupancySum += ratio;
                }

                for (int s = 0; s < speciesCount; s++)
                {
                    FloraSpeciesData potentialSpecies = potential.Species[s];
                    FloraSimulationSpeciesState livingSpecies = state.Species[s];
                    FloraSpeciesProfile profile = profiles[s];

                    float baseK = Mathf.Max(0f, potentialSpecies.CarryingCapacityKgPerSquareMeter[i]);
                    float biomass = Mathf.Max(0f, livingSpecies.CurrentBiomassKgPerSquareMeter[i]);
                    float otherOccupancy = speciesCount > 1
                        ? Mathf.Max(0f, occupancySum - occupancy[s]) / (speciesCount - 1)
                        : 0f;
                    float competitionFraction = Mathf.Clamp(
                        1f - profile.CompetitionStrength * otherOccupancy,
                        settings.MinimumCompetitionCapacityFraction,
                        1f);
                    float effectiveK = baseK * competitionFraction;

                    float delta;
                    if (effectiveK <= Epsilon)
                    {
                        delta = -profile.StressMortalityRatePerDay * biomass * dt;
                    }
                    else
                    {
                        float logistic = profile.IntrinsicGrowthRatePerDay * biomass * (1f - biomass / effectiveK);
                        float recruitment = 0f;
                        if (potentialSpecies.EstablishmentSuitability[i] >= profile.EstablishmentThreshold && biomass < effectiveK)
                        {
                            float emptyFraction = Mathf.Clamp01(1f - biomass / effectiveK);
                            recruitment = profile.SeedBankRecruitmentRatePerDay * effectiveK * emptyFraction;
                        }

                        float stress = biomass > effectiveK
                            ? profile.StressMortalityRatePerDay * (biomass - effectiveK)
                            : 0f;
                        delta = (logistic + recruitment - stress) * dt;
                    }

                    float next = Mathf.Max(0f, biomass + delta);
                    float numericalCeiling = Mathf.Max(profile.MaximumBiomassKgPerSquareMeter * 2f, biomass + 1f);
                    next = Mathf.Min(next, numericalCeiling);
                    if (!IsFinite(next))
                        throw new InvalidOperationException($"Living Flora became non-finite for '{profile.StableId}' at sample {i}.");
                    livingSpecies.CurrentBiomassKgPerSquareMeter[i] = next;
                }
            }
        }

        /// <summary>
        /// Removes a fraction of current biomass inside a world-space circle. This is a deterministic disturbance
        /// hook for tests, future fire/harvest systems and designer scenario injection.
        /// </summary>
        public static float RemoveFractionInCircle(
            FloraSimulationData state,
            int speciesIndex,
            Vector2 centerLocalMeters,
            float radiusMeters,
            float fraction)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (speciesIndex < 0 || speciesIndex >= state.SpeciesCount) return 0f;
            radiusMeters = Mathf.Max(0f, radiusMeters);
            fraction = Mathf.Clamp01(fraction);
            if (radiusMeters <= 0f || fraction <= 0f) return 0f;
            radiusMeters = Mathf.Max(radiusMeters, MinimumResolvableRadius(state));

            FloraSimulationSpeciesState species = state.Species[speciesIndex];
            GetCellBounds(state, centerLocalMeters, radiusMeters, out int minX, out int maxX, out int minY, out int maxY);
            float radiusSq = radiusMeters * radiusMeters;
            float removedKg = 0f;

            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = y * state.SampleSpacingZ;
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = x * state.SampleSpacingX;
                    float dx = worldX - centerLocalMeters.x;
                    float dz = worldZ - centerLocalMeters.y;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    int index = state.Index(x, y);
                    float current = species.CurrentBiomassKgPerSquareMeter[index];
                    float removedDensity = current * fraction;
                    species.CurrentBiomassKgPerSquareMeter[index] = current - removedDensity;
                    species.CumulativeRemovedKgPerSquareMeter[index] += removedDensity;
                    removedKg += removedDensity * state.SampleAreaSquareMeters(x, y);
                }
            }
            return removedKg;
        }

        /// <summary>
        /// Consumes biomass proportionally from all samples inside a browse radius, avoiding raster-order bias.
        /// Returns the physical kilograms actually removed.
        /// </summary>
        public static float ConsumeInCircle(
            FloraSimulationData state,
            int speciesIndex,
            Vector2 centerLocalMeters,
            float radiusMeters,
            float requestedKg)
            => ConsumeInCircle(state, speciesIndex, centerLocalMeters, radiusMeters, requestedKg, null);

        public static float ConsumeInCircle(
            FloraSimulationData state,
            int speciesIndex,
            Vector2 centerLocalMeters,
            float radiusMeters,
            float requestedKg,
            List<int> changedSampleIndices)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (speciesIndex < 0 || speciesIndex >= state.SpeciesCount) return 0f;
            radiusMeters = Mathf.Max(0f, radiusMeters);
            requestedKg = Mathf.Max(0f, requestedKg);
            if (radiusMeters <= 0f || requestedKg <= 0f) return 0f;
            radiusMeters = Mathf.Max(radiusMeters, MinimumResolvableRadius(state));

            FloraSimulationSpeciesState species = state.Species[speciesIndex];
            GetCellBounds(state, centerLocalMeters, radiusMeters, out int minX, out int maxX, out int minY, out int maxY);
            float radiusSq = radiusMeters * radiusMeters;
            double availableKg = 0d;

            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = y * state.SampleSpacingZ;
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = x * state.SampleSpacingX;
                    float dx = worldX - centerLocalMeters.x;
                    float dz = worldZ - centerLocalMeters.y;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    availableKg += species.CurrentBiomassKgPerSquareMeter[state.Index(x, y)] * state.SampleAreaSquareMeters(x, y);
                }
            }

            if (availableKg <= Epsilon) return 0f;
            float consumedKg = Mathf.Min(requestedKg, (float)availableKg);
            float removalFraction = Mathf.Clamp01(consumedKg / (float)availableKg);

            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = y * state.SampleSpacingZ;
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = x * state.SampleSpacingX;
                    float dx = worldX - centerLocalMeters.x;
                    float dz = worldZ - centerLocalMeters.y;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    int index = state.Index(x, y);
                    float current = species.CurrentBiomassKgPerSquareMeter[index];
                    float removedDensity = current * removalFraction;
                    species.CurrentBiomassKgPerSquareMeter[index] = current - removedDensity;
                    species.CumulativeRemovedKgPerSquareMeter[index] += removedDensity;
                    if (removedDensity > Epsilon) changedSampleIndices?.Add(index);
                }
            }

            return consumedKg;
        }


        /// <summary>
        /// Returns the physical biomass currently available inside the same raster-aware footprint used by
        /// consumption. This is diagnostic/query state only; it does not mutate Living Flora.
        /// </summary>
        public static float AvailableBiomassKgInCircle(
            FloraSimulationData state,
            int speciesIndex,
            Vector2 centerLocalMeters,
            float radiusMeters)
        {
            if (state == null || speciesIndex < 0 || speciesIndex >= state.SpeciesCount) return 0f;
            radiusMeters = Mathf.Max(0f, radiusMeters);
            if (radiusMeters <= 0f) return 0f;
            radiusMeters = Mathf.Max(radiusMeters, MinimumResolvableRadius(state));

            FloraSimulationSpeciesState species = state.Species[speciesIndex];
            GetCellBounds(state, centerLocalMeters, radiusMeters, out int minX, out int maxX, out int minY, out int maxY);
            float radiusSq = radiusMeters * radiusMeters;
            double availableKg = 0d;

            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = y * state.SampleSpacingZ;
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = x * state.SampleSpacingX;
                    float dx = worldX - centerLocalMeters.x;
                    float dz = worldZ - centerLocalMeters.y;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    availableKg += species.CurrentBiomassKgPerSquareMeter[state.Index(x, y)] * state.SampleAreaSquareMeters(x, y);
                }
            }

            return availableKg >= float.MaxValue ? float.MaxValue : (float)availableKg;
        }

        public static float SampleMeanBiomassDensityInCircle(
            FloraSimulationData state,
            int speciesIndex,
            Vector2 centerLocalMeters,
            float radiusMeters)
        {
            if (state == null || speciesIndex < 0 || speciesIndex >= state.SpeciesCount) return 0f;
            radiusMeters = Mathf.Max(radiusMeters, MinimumResolvableRadius(state));
            GetCellBounds(state, centerLocalMeters, radiusMeters, out int minX, out int maxX, out int minY, out int maxY);
            float radiusSq = radiusMeters * radiusMeters;
            double sum = 0d;
            int count = 0;
            float[] biomass = state.Species[speciesIndex].CurrentBiomassKgPerSquareMeter;
            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = y * state.SampleSpacingZ;
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = x * state.SampleSpacingX;
                    float dx = worldX - centerLocalMeters.x;
                    float dz = worldZ - centerLocalMeters.y;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    sum += biomass[state.Index(x, y)];
                    count++;
                }
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static float MinimumResolvableRadius(FloraSimulationData state)
        {
            // Raster samples represent an area, not infinitesimal vegetation points. On coarse worlds a requested
            // interaction radius can be smaller than the distance to every sample centre; half a sample diagonal
            // is therefore the minimum meaningful footprint and guarantees that local biomass remains reachable.
            float halfX = state.SampleSpacingX * 0.5f;
            float halfZ = state.SampleSpacingZ * 0.5f;
            return Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + 0.0001f;
        }

        private static void GetCellBounds(
            FloraSimulationData state,
            Vector2 center,
            float radius,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY)
        {
            minX = Mathf.Clamp(Mathf.FloorToInt((center.x - radius) / state.SampleSpacingX), 0, state.Resolution - 1);
            maxX = Mathf.Clamp(Mathf.CeilToInt((center.x + radius) / state.SampleSpacingX), 0, state.Resolution - 1);
            minY = Mathf.Clamp(Mathf.FloorToInt((center.y - radius) / state.SampleSpacingZ), 0, state.Resolution - 1);
            maxY = Mathf.Clamp(Mathf.CeilToInt((center.y + radius) / state.SampleSpacingZ), 0, state.Resolution - 1);
        }

        private static void ValidateCompatible(FloraData potential, FloraSimulationData state)
        {
            if (potential.Resolution != state.Resolution || potential.SourceGeographySeed != state.SourceGeographySeed ||
                Mathf.Abs(potential.WidthMeters - state.WidthMeters) > 0.001f ||
                Mathf.Abs(potential.LengthMeters - state.LengthMeters) > 0.001f)
                throw new ArgumentException("Flora potential and living state describe different physical worlds.");
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
