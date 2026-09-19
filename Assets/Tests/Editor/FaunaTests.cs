using System.Collections.Generic;
using InfinityProject.World.Climate;
using InfinityProject.World.Ecology;
using InfinityProject.World.Fauna;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using NUnit.Framework;
using UnityEngine;

namespace InfinityProject.Tests
{
    public class FaunaTests
    {
        [Test]
        public void FaunaInitialization_IsDeterministic()
        {
            BuildWorld(8101, out GeographyData geography, out FloraData flora, out FloraGenerationSettings floraSettings, out FloraSimulationData floraState);
            FaunaGenerationSettings settings = DefaultFauna(12);
            FaunaSimulationData a = FaunaSimulator.Initialize(geography, flora, floraState, settings);
            FaunaSimulationData b = FaunaSimulator.Initialize(geography, flora, FloraSimulationData.CreateInitial(flora), settings);
            Assert.AreEqual(a.AgentCount, b.AgentCount);
            for (int i = 0; i < a.AgentCount; i++)
            {
                Assert.AreEqual(a.Agents[i].PositionLocalMeters.x, b.Agents[i].PositionLocalMeters.x, 0.0001f);
                Assert.AreEqual(a.Agents[i].PositionLocalMeters.y, b.Agents[i].PositionLocalMeters.y, 0.0001f);
                Assert.AreEqual(a.Agents[i].HerdId, b.Agents[i].HerdId);
            }
        }

        [Test]
        public void FaunaInitialization_SpawnsOnlyOnTraversableTerrain()
        {
            BuildWorld(8102, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData floraState);
            FaunaGenerationSettings settings = DefaultFauna(20);
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, floraState, settings);
            foreach (FaunaAgentState agent in fauna.Agents)
                Assert.IsTrue(FaunaSimulator.IsTraversable(geography, agent.PositionLocalMeters, settings.Species.MaxTraversableSlopeDegrees));
        }

        [Test]
        public void FaunaStep_ConsumesLivingFlora()
        {
            BuildWorld(8103, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData floraState);
            FaunaGenerationSettings settings = DefaultFauna(6);
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, floraState, settings);
            double before = floraState.TotalBiomassKg(0);
            FaunaSimulator.Step(geography, flora, floraState, fauna, settings, 1f);
            double after = floraState.TotalBiomassKg(0);
            Assert.Greater(fauna.CumulativeFoodConsumedKg, 0d);
            Assert.Less(after, before);
        }

        [Test]
        public void FaunaWithFoodMaintainsLowerHungerThanIdenticalFaunaWithoutFood()
        {
            BuildWorld(8104, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData fedFlora);
            FloraSimulationData emptyFlora = FloraSimulationData.CreateInitial(flora);
            ZeroBiomass(emptyFlora);
            FaunaGenerationSettings settings = DefaultFauna(4);
            FaunaSimulationData baseFauna = FaunaSimulator.Initialize(geography, flora, fedFlora, settings);
            FaunaSimulationData fed = CloneFauna(baseFauna);
            FaunaSimulationData hungry = CloneFauna(baseFauna);
            FaunaSimulator.Step(geography, flora, fedFlora, fed, settings, 1f);
            FaunaSimulator.Step(geography, flora, emptyFlora, hungry, settings, 1f);
            Assert.Less(fed.MeanHunger01, hungry.MeanHunger01);
        }

        [Test]
        public void FaunaStarvationEventuallyDamagesHealth()
        {
            BuildWorld(8105, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData floraState);
            ZeroBiomass(floraState);
            FaunaGenerationSettings settings = DefaultFauna(3);
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, FloraSimulationData.CreateInitial(flora), settings);
            // Use the zero-food raster after deterministic spawn so agents start normally but cannot feed.
            FaunaSimulator.Step(geography, flora, floraState, fauna, settings, 8f);
            Assert.Less(fauna.MeanHealth01, 1f);
        }

        [Test]
        public void EcologyLoop_AdvancesBothClocksAndRecordsGrazing()
        {
            BuildWorld(8106, out GeographyData geography, out FloraData flora, out FloraGenerationSettings floraSettings, out FloraSimulationData floraState);
            FaunaGenerationSettings faunaSettings = DefaultFauna(5);
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, floraState, faunaSettings);
            EcologySimulator.Step(geography, flora, floraSettings, floraState, new FloraSimulationSettings(), fauna, faunaSettings, 3f);
            Assert.AreEqual(3d, floraState.SimulationTimeDays, 0.001d);
            Assert.AreEqual(3d, fauna.SimulationTimeDays, 0.001d);
            Assert.Greater(fauna.CumulativeFoodConsumedKg, 0d);
            Assert.Greater(Sum(floraState.Species[0].CumulativeRemovedKgPerSquareMeter), 0f);
        }

        [Test]
        public void EcologyLoop_RejectsMisalignedAuthoritativeClocks()
        {
            BuildWorld(8111, out GeographyData geography, out FloraData flora, out FloraGenerationSettings floraSettings, out FloraSimulationData floraState);
            FaunaGenerationSettings faunaSettings = DefaultFauna(3);
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, floraState, faunaSettings);
            FloraSimulator.Step(flora, floraSettings, floraState, new FloraSimulationSettings(), 1f);

            Assert.Throws<System.ArgumentException>(() =>
                EcologySimulator.Step(geography, flora, floraSettings, floraState, new FloraSimulationSettings(), fauna, faunaSettings, 1f));
        }


        [Test]
        public void EcologyLoop_LongRunKeepsAuthoritativeClocksExactlyAligned()
        {
            BuildWorld(8112, out GeographyData geography, out FloraData flora, out FloraGenerationSettings floraSettings, out FloraSimulationData floraState);
            FaunaGenerationSettings faunaSettings = DefaultFauna(1);
            faunaSettings.MaximumMovementSubstepDays = 0.25f;
            faunaSettings.ForageDirectionSamples = 4;
            faunaSettings.Validate();
            FaunaSimulationData fauna = FaunaSimulator.Initialize(geography, flora, floraState, faunaSettings);

            EcologySimulator.Step(
                geography,
                flora,
                floraSettings,
                floraState,
                new FloraSimulationSettings(),
                fauna,
                faunaSettings,
                180f);

            Assert.AreEqual(180d, floraState.SimulationTimeDays);
            Assert.AreEqual(180d, fauna.SimulationTimeDays);
            Assert.AreEqual(floraState.SimulationTimeDays, fauna.SimulationTimeDays,
                "Integration substeps must never become authoritative clocks.");
        }


        [Test]
        public void EcologyLoop_RepairsTinyLegacySubstepClockDrift()
        {
            BuildWorld(8113, out GeographyData geography, out FloraData flora, out FloraGenerationSettings floraSettings, out FloraSimulationData floraState);
            FaunaGenerationSettings faunaSettings = DefaultFauna(1);
            FaunaSimulationData initialized = FaunaSimulator.Initialize(geography, flora, floraState, faunaSettings);
            var fauna = new FaunaSimulationData(
                initialized.SpeciesStableId,
                initialized.SpeciesDisplayName,
                initialized.SourceGeographySeed,
                initialized.WidthMeters,
                initialized.LengthMeters,
                floraState.SimulationTimeDays + 0.00012d,
                (FaunaAgentState[])initialized.Agents.Clone());

            EcologySimulator.Step(
                geography,
                flora,
                floraSettings,
                floraState,
                new FloraSimulationSettings(),
                fauna,
                faunaSettings,
                1f);

            Assert.AreEqual(1d, floraState.SimulationTimeDays);
            Assert.AreEqual(floraState.SimulationTimeDays, fauna.SimulationTimeDays);
        }

        [Test]
        public void FaunaTraversability_IsActorSpecificSlopeConstraint()
        {
            const int resolution = 3;
            float[] heights =
            {
                0f, 0.5f, 1f,
                0f, 0.5f, 1f,
                0f, 0.5f, 1f
            };
            var geography = new GeographyData(resolution, 20f, 20f, 100f, 8107, heights);
            Vector2 center = new(10f, 10f);
            Assert.IsFalse(FaunaSimulator.IsTraversable(geography, center, 30f));
            Assert.IsTrue(FaunaSimulator.IsTraversable(geography, center, 89f));
        }


        [Test]
        public void FaunaPathTraversability_RejectsNarrowSteepRidgeBetweenValidEndpoints()
        {
            const int resolution = 5;
            const float size = 40f;
            float[] heights =
            {
                0f, 0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f, 0f
            };
            var geography = new GeographyData(resolution, size, size, 100f, 8110, heights);
            Vector2 west = new(0f, 20f);
            Vector2 east = new(40f, 20f);

            Assert.IsTrue(FaunaSimulator.IsTraversable(geography, west, 30f));
            Assert.IsTrue(FaunaSimulator.IsTraversable(geography, east, 30f));
            Assert.IsFalse(FaunaSimulator.IsPathTraversable(geography, west, east, 30f));
        }

        [Test]
        public void FaunaStep_IsDeterministicForIdenticalStates()
        {
            BuildWorld(8109, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData floraA);
            FloraSimulationData floraB = FloraSimulationData.CreateInitial(flora);
            FaunaGenerationSettings settings = DefaultFauna(6);
            FaunaSimulationData initial = FaunaSimulator.Initialize(geography, flora, floraA, settings);
            FaunaSimulationData faunaA = CloneFauna(initial);
            FaunaSimulationData faunaB = CloneFauna(initial);

            FaunaSimulator.Step(geography, flora, floraA, faunaA, settings, 2f);
            FaunaSimulator.Step(geography, flora, floraB, faunaB, settings, 2f);

            Assert.AreEqual(faunaA.SimulationTimeDays, faunaB.SimulationTimeDays, 0.0001d);
            Assert.AreEqual(faunaA.CumulativeFoodConsumedKg, faunaB.CumulativeFoodConsumedKg, 0.001d);
            for (int i = 0; i < faunaA.AgentCount; i++)
            {
                Assert.AreEqual(faunaA.Agents[i].PositionLocalMeters.x, faunaB.Agents[i].PositionLocalMeters.x, 0.0001f);
                Assert.AreEqual(faunaA.Agents[i].PositionLocalMeters.y, faunaB.Agents[i].PositionLocalMeters.y, 0.0001f);
                Assert.AreEqual(faunaA.Agents[i].Hunger01, faunaB.Agents[i].Hunger01, 0.0001f);
                Assert.AreEqual(faunaA.Agents[i].Health01, faunaB.Agents[i].Health01, 0.0001f);
            }
            CollectionAssert.AreEqual(
                floraA.Species[0].CurrentBiomassKgPerSquareMeter,
                floraB.Species[0].CurrentBiomassKgPerSquareMeter);
        }

        [Test]
        public void FaunaHerdCohesion_PullsSeparatedHerdmatesCloserWithoutForageSignal()
        {
            BuildWorld(8108, out GeographyData geography, out FloraData flora, out _, out FloraSimulationData floraState);
            ZeroBiomass(floraState);
            FaunaGenerationSettings settings = DefaultFauna(2);
            settings.Species.ForageSteeringWeight = 0f;
            settings.Species.HerdCohesionWeight = 2f;
            settings.Species.HeadingPersistenceWeight = 0f;
            settings.Species.MoveSpeedMetersPerDay = 50f;
            settings.Species.HerdCohesionRadiusMeters = 200f;
            settings.Validate();
            var agents = new[]
            {
                new FaunaAgentState { Id = 0, HerdId = 0, PositionLocalMeters = new Vector2(20f, 60f), Heading = Vector2.left, Hunger01 = 0.2f, Energy01 = 1f, Health01 = 1f, Alive = true },
                new FaunaAgentState { Id = 1, HerdId = 0, PositionLocalMeters = new Vector2(100f, 60f), Heading = Vector2.right, Hunger01 = 0.2f, Energy01 = 1f, Health01 = 1f, Alive = true }
            };
            var fauna = new FaunaSimulationData("deer", "Deer", geography.Seed, geography.WidthMeters, geography.LengthMeters, 0d, agents);
            float before = Vector2.Distance(fauna.Agents[0].PositionLocalMeters, fauna.Agents[1].PositionLocalMeters);
            FaunaSimulator.Step(geography, flora, floraState, fauna, settings, 0.1f);
            float after = Vector2.Distance(fauna.Agents[0].PositionLocalMeters, fauna.Agents[1].PositionLocalMeters);
            Assert.Less(after, before);
        }
        private static FaunaGenerationSettings DefaultFauna(int count)
        {
            var settings = new FaunaGenerationSettings();
            settings.Seed = 12345;
            settings.MaximumMovementSubstepDays = 0.04f;
            settings.ForageDirectionSamples = 8;
            settings.Species.AgentCount = count;
            settings.Species.HerdSize = Mathf.Min(4, count);
            settings.Species.MoveSpeedMetersPerDay = 250f;
            settings.Species.ForagePerceptionRadiusMeters = 30f;
            settings.Species.BrowseRadiusMeters = 12f;
            settings.Species.MaxTraversableSlopeDegrees = 40f;
            settings.Species.DailyFoodRequirementKg = 1f;
            settings.Species.HungerIncreasePerDay = 0.35f;
            settings.Species.Diet = new List<FaunaDietPreference>
            {
                new() { FloraSpeciesStableId = "grass", Palatability = 1f }
            };
            settings.Validate();
            return settings;
        }

        private static void BuildWorld(
            int seed,
            out GeographyData geography,
            out FloraData flora,
            out FloraGenerationSettings floraSettings,
            out FloraSimulationData floraState)
        {
            const int resolution = 16;
            const float size = 120f;
            float[] heights = new float[resolution * resolution];
            geography = new GeographyData(resolution, size, size, 80f, seed, heights);
            float[] twi = Constant(resolution * resolution, 8f);
            float[] wet = Constant(resolution * resolution, 0.5f);
            float[] retention = Constant(resolution * resolution, 1f);
            var ground = new GroundConditionData(resolution, size, size, seed, 0L, twi, wet, retention, 8f, 8f, 8f, 8f);
            float[] temperature = Constant(resolution * resolution, 12f);
            float[] precipitation = Constant(resolution * resolution, 1f);
            var climate = new ClimateData(resolution, size, size, seed, temperature, precipitation, 12f, 12f, 1f, 1f);
            var grass = new FloraSpeciesProfile
            {
                StableId = "grass",
                DisplayName = "Grass",
                MinimumTemperatureCelsius = 0f,
                OptimalTemperatureCelsius = 12f,
                MaximumTemperatureCelsius = 25f,
                MinimumWaterAvailabilityPotential = 0f,
                OptimalWaterAvailabilityPotential = 0.5f,
                MaximumWaterAvailabilityPotential = 1f,
                MinimumSoilRetentionPotential = 0f,
                FullSoilRetentionPotential = 0.2f,
                EstablishmentThreshold = 0.05f,
                InitialOccupancyFraction = 0.9f,
                MaximumBiomassKgPerSquareMeter = 1.2f,
                IntrinsicGrowthRatePerDay = 0.08f,
                SeedBankRecruitmentRatePerDay = 0.01f,
                StressMortalityRatePerDay = 0.1f,
                CompetitionStrength = 0f
            };
            floraSettings = new FloraGenerationSettings { Species = new List<FloraSpeciesProfile> { grass } };
            flora = FloraGenerator.Generate(geography, ground, climate, floraSettings);
            floraState = FloraSimulationData.CreateInitial(flora);
        }

        private static void ZeroBiomass(FloraSimulationData state)
        {
            for (int s = 0; s < state.SpeciesCount; s++)
                System.Array.Clear(state.Species[s].CurrentBiomassKgPerSquareMeter, 0, state.Species[s].CurrentBiomassKgPerSquareMeter.Length);
        }

        private static FaunaSimulationData CloneFauna(FaunaSimulationData source)
        {
            var agents = (FaunaAgentState[])source.Agents.Clone();
            return new FaunaSimulationData(source.SpeciesStableId, source.SpeciesDisplayName, source.SourceGeographySeed, source.WidthMeters, source.LengthMeters, source.SimulationTimeDays, agents);
        }

        private static float[] Constant(int count, float value)
        {
            var values = new float[count];
            for (int i = 0; i < values.Length; i++) values[i] = value;
            return values;
        }

        private static float Sum(float[] values)
        {
            float total = 0f;
            for (int i = 0; i < values.Length; i++) total += values[i];
            return total;
        }
    }
}
