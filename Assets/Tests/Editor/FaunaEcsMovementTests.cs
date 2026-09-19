using System.Collections.Generic;
using InfinityProject.World.Climate;
using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using NUnit.Framework;
using UnityEngine;

public sealed class FaunaEcsMovementTests
{
    [Test]
    public void EcsMovement_MatchesV0ForSingleAgentSubstep()
    {
        BuildWorld(9201, out GeographyData geography, out FloraData flora);
        FloraSimulationData floraV0 = FloraSimulationData.CreateInitial(flora);
        FloraSimulationData floraEcs = FloraSimulationData.CreateInitial(flora);
        FaunaGenerationSettings settings = DefaultFauna(1);
        settings.Species.HerdCohesionWeight = 0f;
        settings.Species.ExplorationWeight = 0.2f;
        settings.Validate();

        FaunaSimulationData v0 = CreateFauna(geography, new Vector2(45f, 55f), new Vector2(0.6f, 0.8f), 0.72f);
        FaunaSimulationData ecs = CloneFauna(v0);
        float dt = settings.MaximumMovementSubstepDays;

        FaunaSimulator.Step(geography, flora, floraV0, v0, settings, dt);
        using var backend = new FaunaEcsMovementBackend("Fauna ECS movement parity test");
        backend.Step(geography, floraEcs, ecs, settings, dt);

        Assert.AreEqual(v0.Agents[0].PositionLocalMeters.x, ecs.Agents[0].PositionLocalMeters.x, 0.01f);
        Assert.AreEqual(v0.Agents[0].PositionLocalMeters.y, ecs.Agents[0].PositionLocalMeters.y, 0.01f);
        Assert.AreEqual(v0.Agents[0].Heading.x, ecs.Agents[0].Heading.x, 0.001f);
        Assert.AreEqual(v0.Agents[0].Heading.y, ecs.Agents[0].Heading.y, 0.001f);
        Assert.AreEqual(v0.Agents[0].Hunger01, ecs.Agents[0].Hunger01, 0.0001f);
        Assert.AreEqual(v0.Agents[0].CumulativeFoodConsumedKg, ecs.Agents[0].CumulativeFoodConsumedKg, 0.001f);
    }

    [Test]
    public void EcsMovement_MatchesV0HerdSteeringWithoutForageSignal()
    {
        BuildWorld(9202, out GeographyData geography, out FloraData flora);
        FloraSimulationData floraV0 = FloraSimulationData.CreateInitial(flora);
        FloraSimulationData floraEcs = FloraSimulationData.CreateInitial(flora);
        ZeroBiomass(floraV0);
        ZeroBiomass(floraEcs);

        FaunaGenerationSettings settings = DefaultFauna(2);
        settings.Species.ForageSteeringWeight = 0f;
        settings.Species.HerdCohesionWeight = 2f;
        settings.Species.HeadingPersistenceWeight = 0f;
        settings.Species.ExplorationWeight = 0f;
        settings.Species.MoveSpeedMetersPerDay = 50f;
        settings.Species.HerdCohesionRadiusMeters = 200f;
        settings.Validate();

        var agents = new[]
        {
            new FaunaAgentState { Id = 0, HerdId = 0, PositionLocalMeters = new Vector2(20f, 60f), Heading = Vector2.left, Hunger01 = 0.2f, Energy01 = 1f, Health01 = 1f, Alive = true },
            new FaunaAgentState { Id = 1, HerdId = 0, PositionLocalMeters = new Vector2(100f, 60f), Heading = Vector2.right, Hunger01 = 0.2f, Energy01 = 1f, Health01 = 1f, Alive = true }
        };
        var v0 = new FaunaSimulationData("deer", "Deer", geography.Seed, geography.WidthMeters, geography.LengthMeters, 0d, (FaunaAgentState[])agents.Clone());
        var ecs = CloneFauna(v0);
        float dt = settings.MaximumMovementSubstepDays;

        FaunaSimulator.Step(geography, flora, floraV0, v0, settings, dt);
        using var backend = new FaunaEcsMovementBackend("Fauna ECS herd parity test");
        backend.Step(geography, floraEcs, ecs, settings, dt);

        for (int i = 0; i < v0.AgentCount; i++)
        {
            Assert.AreEqual(v0.Agents[i].PositionLocalMeters.x, ecs.Agents[i].PositionLocalMeters.x, 0.01f);
            Assert.AreEqual(v0.Agents[i].PositionLocalMeters.y, ecs.Agents[i].PositionLocalMeters.y, 0.01f);
        }
    }

    [Test]
    public void EcsMovement_PreservesEntityIdentityAndAccumulatesTravel()
    {
        BuildWorld(9203, out GeographyData geography, out FloraData flora);
        FloraSimulationData floraState = FloraSimulationData.CreateInitial(flora);
        FaunaGenerationSettings settings = DefaultFauna(1);
        settings.Species.HerdCohesionWeight = 0f;
        settings.Species.ExplorationWeight = 0.2f;
        settings.Validate();
        FaunaSimulationData fauna = CreateFauna(geography, new Vector2(45f, 55f), Vector2.right, 0.8f);

        using var backend = new FaunaEcsMovementBackend("Fauna ECS identity movement test");
        backend.SyncSnapshot(fauna);
        Assert.IsTrue(backend.Bridge.TryGetEntity(0, out var before));

        backend.Step(geography, floraState, fauna, settings, settings.MaximumMovementSubstepDays);
        backend.Step(geography, floraState, fauna, settings, settings.MaximumMovementSubstepDays);

        Assert.IsTrue(backend.Bridge.TryGetEntity(0, out var after));
        Assert.AreEqual(before, after);
        Assert.IsTrue(backend.Bridge.TryReadAgent(0, out FaunaEcsAgentSnapshot snapshot));
        Assert.Greater(snapshot.Motion.CumulativeDistanceMeters, 0f);
    }

    private static FaunaGenerationSettings DefaultFauna(int count)
    {
        var settings = new FaunaGenerationSettings
        {
            Seed = 12345,
            MaximumMovementSubstepDays = 0.04f,
            ForageDirectionSamples = 8
        };
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

    private static void BuildWorld(int seed, out GeographyData geography, out FloraData flora)
    {
        const int resolution = 16;
        const float size = 120f;
        float[] heights = new float[resolution * resolution];
        geography = new GeographyData(resolution, size, size, 80f, seed, heights);
        var ground = new GroundConditionData(
            resolution, size, size, seed, 0L,
            Constant(resolution * resolution, 8f),
            Constant(resolution * resolution, 0.5f),
            Constant(resolution * resolution, 1f),
            8f, 8f, 8f, 8f);
        var climate = new ClimateData(
            resolution, size, size, seed,
            Constant(resolution * resolution, 12f),
            Constant(resolution * resolution, 1f),
            12f, 12f, 1f, 1f);
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
        flora = FloraGenerator.Generate(
            geography,
            ground,
            climate,
            new FloraGenerationSettings { Species = new List<FloraSpeciesProfile> { grass } });
    }

    private static FaunaSimulationData CreateFauna(GeographyData geography, Vector2 position, Vector2 heading, float hunger)
    {
        var agents = new[]
        {
            new FaunaAgentState
            {
                Id = 0,
                HerdId = 0,
                PositionLocalMeters = position,
                Heading = heading,
                Hunger01 = hunger,
                Energy01 = 0.9f,
                Health01 = 1f,
                Alive = true
            }
        };
        return new FaunaSimulationData("deer", "Deer", geography.Seed, geography.WidthMeters, geography.LengthMeters, 0d, agents);
    }

    private static FaunaSimulationData CloneFauna(FaunaSimulationData source)
        => new(
            source.SpeciesStableId,
            source.SpeciesDisplayName,
            source.SourceGeographySeed,
            source.WidthMeters,
            source.LengthMeters,
            source.SimulationTimeDays,
            (FaunaAgentState[])source.Agents.Clone());

    private static void ZeroBiomass(FloraSimulationData state)
    {
        for (int i = 0; i < state.SpeciesCount; i++)
            System.Array.Clear(state.Species[i].CurrentBiomassKgPerSquareMeter, 0, state.Species[i].CurrentBiomassKgPerSquareMeter.Length);
    }

    private static float[] Constant(int count, float value)
    {
        var result = new float[count];
        for (int i = 0; i < result.Length; i++) result[i] = value;
        return result;
    }
}
