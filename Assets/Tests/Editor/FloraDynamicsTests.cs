using System.Collections.Generic;
using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using NUnit.Framework;
using UnityEngine;

namespace InfinityProject.Tests
{
    public class FloraDynamicsTests
    {
        [Test]
        public void LivingFlora_DisturbanceRecoversTowardCarryingCapacity()
        {
            BuildPotential(7201, 1f, out FloraData potential, out FloraGenerationSettings settings);
            FloraSimulationData state = FloraSimulationData.CreateInitial(potential);
            float before = state.Species[0].CurrentBiomassKgPerSquareMeter[0];
            FloraSimulator.RemoveFractionInCircle(state, 0, new Vector2(50f, 50f), 200f, 0.8f);
            float disturbed = state.Species[0].CurrentBiomassKgPerSquareMeter[0];
            Assert.Less(disturbed, before * 0.25f);

            FloraSimulator.Step(potential, settings, state, new FloraSimulationSettings(), 30f);
            float recovered = state.Species[0].CurrentBiomassKgPerSquareMeter[0];
            Assert.Greater(recovered, disturbed);
            Assert.LessOrEqual(recovered, potential.Species[0].CarryingCapacityKgPerSquareMeter[0] * 1.05f);
        }

        [Test]
        public void LivingFlora_ConsumptionRemovesPhysicalMassAndTracksHistory()
        {
            BuildPotential(7202, 1f, out FloraData potential, out _);
            FloraSimulationData state = FloraSimulationData.CreateInitial(potential);
            double before = state.TotalBiomassKg(0);
            float consumed = FloraSimulator.ConsumeInCircle(state, 0, new Vector2(50f, 50f), 200f, 2.5f);
            double after = state.TotalBiomassKg(0);
            Assert.AreEqual(2.5f, consumed, 0.01f);
            Assert.AreEqual(2.5d, before - after, 0.05d);
            Assert.Greater(state.Species[0].CumulativeRemovedKgPerSquareMeter[0], 0f);
        }

        [Test]
        public void LivingFlora_RebindPreservesBiomassAcrossEnvironmentalRevision()
        {
            BuildPotential(7203, 1f, out FloraData original, out _);
            FloraSimulationData state = FloraSimulationData.CreateInitial(original);
            state.Species[0].CurrentBiomassKgPerSquareMeter[0] = 0.321f;
            FloraSimulator.Step(original, new FloraGenerationSettings { Species = new List<FloraSpeciesProfile> { new FloraSpeciesProfile { StableId = "grass", DisplayName = "Grass", MinimumTemperatureCelsius = 0f, OptimalTemperatureCelsius = 10f, MaximumTemperatureCelsius = 20f, MinimumWaterAvailabilityPotential = 0f, OptimalWaterAvailabilityPotential = 0.5f, MaximumWaterAvailabilityPotential = 1f, MinimumSoilRetentionPotential = 0f, FullSoilRetentionPotential = 0.5f, EstablishmentThreshold = 0.05f, InitialOccupancyFraction = 0.8f, MaximumBiomassKgPerSquareMeter = 1f, IntrinsicGrowthRatePerDay = 0f, SeedBankRecruitmentRatePerDay = 0f, StressMortalityRatePerDay = 0f, CompetitionStrength = 0f } } }, state, new FloraSimulationSettings(), 17f);

            BuildPotential(7203, 0.35f, out FloraData changed, out _);
            FloraSimulationData rebound = FloraSimulationData.RebindPreservingBiomass(state, changed);
            Assert.AreEqual(17d, rebound.SimulationTimeDays, 0.0001d);
            Assert.AreEqual(0.321f, rebound.Species[0].CurrentBiomassKgPerSquareMeter[0], 0.0001f);
            Assert.Less(changed.Species[0].CarryingCapacityKgPerSquareMeter[0], original.Species[0].CarryingCapacityKgPerSquareMeter[0]);
        }

        [Test]
        public void LivingFlora_ReducedCapacityCausesDiebackInsteadOfInstantReset()
        {
            BuildPotential(7204, 1f, out FloraData original, out FloraGenerationSettings settings);
            FloraSimulationData state = FloraSimulationData.CreateInitial(original);
            float oldBiomass = state.Species[0].CurrentBiomassKgPerSquareMeter[0];

            BuildPotential(7204, 0.3f, out FloraData changed, out _);
            FloraSimulationData rebound = FloraSimulationData.RebindPreservingBiomass(state, changed);
            Assert.AreEqual(oldBiomass, rebound.Species[0].CurrentBiomassKgPerSquareMeter[0], 0.0001f);
            FloraSimulator.Step(changed, settings, rebound, new FloraSimulationSettings(), 20f);
            Assert.Less(rebound.Species[0].CurrentBiomassKgPerSquareMeter[0], oldBiomass);
            Assert.GreaterOrEqual(rebound.Species[0].CurrentBiomassKgPerSquareMeter[0], 0f);
        }

        [Test]
        public void LivingFlora_IsDeterministicForSameInitialStateAndStep()
        {
            BuildPotential(7205, 1f, out FloraData potential, out FloraGenerationSettings settings);
            FloraSimulationData a = FloraSimulationData.CreateInitial(potential);
            FloraSimulationData b = FloraSimulationData.CreateInitial(potential);
            FloraSimulator.Step(potential, settings, a, new FloraSimulationSettings(), 12.5f);
            FloraSimulator.Step(potential, settings, b, new FloraSimulationSettings(), 12.5f);
            CollectionAssert.AreEqual(a.Species[0].CurrentBiomassKgPerSquareMeter, b.Species[0].CurrentBiomassKgPerSquareMeter);
        }


        [Test]
        public void LivingFlora_UniformDensityIntegratesToExactWorldArea()
        {
            BuildPotential(7207, 1f, out FloraData potential, out _);
            FloraSimulationData state = FloraSimulationData.CreateInitial(potential);
            float[] biomass = state.Species[0].CurrentBiomassKgPerSquareMeter;
            for (int i = 0; i < biomass.Length; i++) biomass[i] = 1f;
            Assert.AreEqual(state.WidthMeters * state.LengthMeters, state.TotalBiomassKg(0), 0.01d);
        }

        [Test]
        public void LivingFlora_CoarseRasterStillAllowsLocalConsumptionBelowSampleSpacing()
        {
            BuildPotential(7208, 1f, out FloraData potential, out _);
            FloraSimulationData state = FloraSimulationData.CreateInitial(potential);
            float consumed = FloraSimulator.ConsumeInCircle(state, 0, new Vector2(50f, 50f), 1f, 1f);
            Assert.Greater(consumed, 0f);
        }

        [Test]
        public void LivingFlora_CompetitionReducesEquilibriumGrowthWhenSpeciesOverlap()
        {
            BuildPotential(7206, 1f, out FloraData singlePotential, out FloraGenerationSettings singleSettings);
            FloraSpeciesProfile first = singleSettings.Species[0].Clone();
            FloraSpeciesProfile second = singleSettings.Species[0].Clone();
            first.StableId = "grass_a";
            first.CompetitionStrength = 1f;
            second.StableId = "grass_b";
            second.CompetitionStrength = 1f;
            var competitiveSettings = new FloraGenerationSettings { Species = new List<FloraSpeciesProfile> { first, second } };

            // Rebuild the same flat environmental inputs for the two-species potential.
            const int resolution = 4;
            const float size = 100f;
            var geography = new GeographyData(resolution, size, size, 100f, 7206, new float[resolution * resolution]);
            float[] twi = Constant(resolution * resolution, 8f);
            var ground = new GroundConditionData(resolution, size, size, 7206, 0L, twi, Constant(resolution * resolution, 0.5f), Constant(resolution * resolution, 1f), 8f, 8f, 8f, 8f);
            var climate = new ClimateData(resolution, size, size, 7206, Constant(resolution * resolution, 10f), Constant(resolution * resolution, 1f), 10f, 10f, 1f, 1f);
            FloraData potential = FloraGenerator.Generate(geography, ground, climate, competitiveSettings);
            FloraSimulationData competitive = FloraSimulationData.CreateInitial(potential);
            FloraSimulationData noCompetition = FloraSimulationData.CreateInitial(potential);

            FloraGenerationSettings noCompetitionSettings = competitiveSettings.Clone();
            foreach (FloraSpeciesProfile profile in noCompetitionSettings.Species) profile.CompetitionStrength = 0f;

            FloraSimulator.Step(potential, competitiveSettings, competitive, new FloraSimulationSettings(), 20f);
            FloraSimulator.Step(potential, noCompetitionSettings, noCompetition, new FloraSimulationSettings(), 20f);
            Assert.Less(competitive.Species[0].CurrentBiomassKgPerSquareMeter[0], noCompetition.Species[0].CurrentBiomassKgPerSquareMeter[0]);
            Assert.Less(competitive.Species[1].CurrentBiomassKgPerSquareMeter[0], noCompetition.Species[1].CurrentBiomassKgPerSquareMeter[0]);
        }
        private static void BuildPotential(int seed, float precipitation, out FloraData flora, out FloraGenerationSettings settings)
        {
            const int resolution = 4;
            const float size = 100f;
            float[] heights = new float[resolution * resolution];
            var geography = new GeographyData(resolution, size, size, 100f, seed, heights);
            float[] twi = Constant(resolution * resolution, 8f);
            float[] wet = Constant(resolution * resolution, 0.5f * precipitation);
            float[] retention = Constant(resolution * resolution, 1f);
            var ground = new GroundConditionData(resolution, size, size, seed, 0L, twi, wet, retention, 8f, 8f, 8f, 8f);
            float[] temp = Constant(resolution * resolution, 10f);
            float[] precip = Constant(resolution * resolution, precipitation);
            var climate = new ClimateData(resolution, size, size, seed, temp, precip, 10f, 10f, precipitation, precipitation);
            var species = new FloraSpeciesProfile
            {
                StableId = "grass",
                DisplayName = "Grass",
                MinimumTemperatureCelsius = 0f,
                OptimalTemperatureCelsius = 10f,
                MaximumTemperatureCelsius = 20f,
                MinimumWaterAvailabilityPotential = 0f,
                OptimalWaterAvailabilityPotential = 0.5f,
                MaximumWaterAvailabilityPotential = 1f,
                MinimumSoilRetentionPotential = 0f,
                FullSoilRetentionPotential = 0.5f,
                EstablishmentThreshold = 0.05f,
                InitialOccupancyFraction = 0.8f,
                MaximumBiomassKgPerSquareMeter = 1f,
                IntrinsicGrowthRatePerDay = 0.08f,
                SeedBankRecruitmentRatePerDay = 0.01f,
                StressMortalityRatePerDay = 0.15f,
                CompetitionStrength = 0f
            };
            settings = new FloraGenerationSettings { Species = new List<FloraSpeciesProfile> { species } };
            flora = FloraGenerator.Generate(geography, ground, climate, settings);
        }

        private static float[] Constant(int count, float value)
        {
            var values = new float[count];
            for (int i = 0; i < values.Length; i++) values[i] = value;
            return values;
        }
    }
}
