using System;
using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using UnityEngine;

namespace InfinityProject.World.Ecology
{
    /// <summary>
    /// Process coordinator for the first closed ecological loop. It owns no state: Flora owns biomass and Fauna
    /// owns animals. This class only defines deterministic process ordering through simulated time.
    /// </summary>
    public static class EcologySimulator
    {
        // Legacy Fauna V0 accumulated float movement substeps into its authoritative double clock. Differences
        // below this threshold are numerical residue, not meaningfully different ecological histories.
        public const double NumericalClockRepairToleranceDays = 0.001d;

        public static void StepEcsMovement(
            GeographyData geography,
            FloraData floraPotential,
            FloraGenerationSettings floraGenerationSettings,
            FloraSimulationData floraState,
            FloraSimulationSettings floraSimulationSettings,
            FaunaSimulationData faunaState,
            FaunaGenerationSettings faunaSettings,
            FaunaEcsMovementBackend movementBackend,
            float deltaDays)
        {
            if (movementBackend == null) throw new ArgumentNullException(nameof(movementBackend));
            ValidateStepRequest(floraState, faunaState, deltaDays);
            if (deltaDays <= 0f) return;

            RepairLegacyClockResidue(floraState, faunaState);
            double targetTimeDays = floraState.SimulationTimeDays + deltaDays;
            float remaining = deltaDays;

            while (remaining > 0.000001f)
            {
                float dt = Mathf.Min(2f, remaining);
                movementBackend.Step(geography, floraState, faunaState, faunaSettings, dt);
                FloraSimulator.Step(floraPotential, floraGenerationSettings, floraState, floraSimulationSettings, dt);
                movementBackend.RefreshAllForage(floraState);
                remaining -= dt;
            }

            floraState.SetSimulationTime(targetTimeDays);
            faunaState.SetSimulationTime(targetTimeDays);
        }

        public static void Step(
            GeographyData geography,
            FloraData floraPotential,
            FloraGenerationSettings floraGenerationSettings,
            FloraSimulationData floraState,
            FloraSimulationSettings floraSimulationSettings,
            FaunaSimulationData faunaState,
            FaunaGenerationSettings faunaSettings,
            float deltaDays)
        {
            ValidateStepRequest(floraState, faunaState, deltaDays);
            if (deltaDays <= 0f) return;
            RepairLegacyClockResidue(floraState, faunaState);

            // Authoritative time belongs to the coupled operation, not to either system's integration cadence.
            // Both states therefore finish on the exact same requested target even though Fauna and Flora use
            // very different internal substep sizes.
            double targetTimeDays = floraState.SimulationTimeDays + (double)deltaDays;

            // Two-day maximum ecological coupling keeps growth/consumption interleaved without forcing a
            // million-sample Flora raster pass for every locomotion substep. Fauna still integrates movement
            // internally at a much finer cadence.
            float remaining = deltaDays;
            while (remaining > 0.000001f)
            {
                float dt = Mathf.Min(2f, remaining);
                FaunaSimulator.Step(geography, floraPotential, floraState, faunaState, faunaSettings, dt);
                FloraSimulator.Step(floraPotential, floraGenerationSettings, floraState, floraSimulationSettings, dt);
                remaining -= dt;
            }

            floraState.SetSimulationTime(targetTimeDays);
            faunaState.SetSimulationTime(targetTimeDays);
        }

        private static void ValidateStepRequest(
            FloraSimulationData floraState,
            FaunaSimulationData faunaState,
            float deltaDays)
        {
            if (deltaDays < 0f || float.IsNaN(deltaDays) || float.IsInfinity(deltaDays))
                throw new ArgumentOutOfRangeException(nameof(deltaDays));
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (faunaState == null) throw new ArgumentNullException(nameof(faunaState));

            double clockDifference = Math.Abs(floraState.SimulationTimeDays - faunaState.SimulationTimeDays);
            if (clockDifference > NumericalClockRepairToleranceDays)
                throw new ArgumentException("Living Flora and Fauna clocks must be aligned before coupled ecology stepping.");
        }

        private static void RepairLegacyClockResidue(FloraSimulationData floraState, FaunaSimulationData faunaState)
        {
            double clockDifference = Math.Abs(floraState.SimulationTimeDays - faunaState.SimulationTimeDays);
            if (clockDifference > 0d) faunaState.SetSimulationTime(floraState.SimulationTimeDays);
        }
    }
}
