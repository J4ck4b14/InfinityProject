using System;
using System.Collections.Generic;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using Unity.Collections;
using UnityEngine;

namespace InfinityProject.World.Fauna.ECS
{
    /// <summary>
    /// Persistent ECS movement session. Terrain and forage rasters are copied once into native memory and reused;
    /// only cells changed by grazing are refreshed between movement substeps.
    /// </summary>
    public sealed class FaunaEcsMovementBackend : IDisposable
    {
        private readonly FaunaEcsSnapshotBridge _bridge;
        private readonly FaunaEcsMovementSystem _movementSystem;
        private readonly List<int> _changedFloraSamples = new(256);

        private NativeArray<float> _slopeDegrees;
        private NativeArray<float> _forageDensity;
        private NativeArray<FaunaEcsMovementResult> _movementResults;
        private GeographyData _geography;
        private FloraSimulationData _forageState;
        private bool _forageInitialized;
        private int[] _dietIndices = Array.Empty<int>();
        private float[] _dietWeights = Array.Empty<float>();

        public FaunaEcsSnapshotBridge Bridge => _bridge;
        public int LastSubstepCount { get; private set; }
        public int LastDirtyForageSamples { get; private set; }

        public FaunaEcsMovementBackend(string worldName = "Infinity Fauna ECS V1")
        {
            _bridge = new FaunaEcsSnapshotBridge(worldName);
            _movementSystem = _bridge.World.GetOrCreateSystemManaged<FaunaEcsMovementSystem>();
        }

        public void SyncSnapshot(FaunaSimulationData fauna)
        {
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            _bridge.Sync(fauna);
        }

        public void Step(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSimulationData fauna,
            FaunaGenerationSettings settings,
            float deltaDays)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (deltaDays < 0f || float.IsNaN(deltaDays) || float.IsInfinity(deltaDays))
                throw new ArgumentOutOfRangeException(nameof(deltaDays));
            if (deltaDays <= 0f) return;

            settings.Validate();
            ValidateWorld(geography, floraState, fauna, settings.Species);
            EnsureEnvironment(geography);
            ResolveDiet(floraState, settings.Species);
            EnsureForageField(floraState);
            EnsureResultCapacity(fauna.AgentCount);
            _bridge.Sync(fauna);

            double targetTimeDays = fauna.SimulationTimeDays + deltaDays;
            float remaining = deltaDays;
            long absoluteStep = (long)Math.Floor(fauna.SimulationTimeDays / settings.MaximumMovementSubstepDays);
            int herdCapacity = FindHerdCapacity(fauna);
            LastSubstepCount = 0;
            LastDirtyForageSamples = 0;

            while (remaining > 0.000001f)
            {
                float dt = Mathf.Min(remaining, settings.MaximumMovementSubstepDays);
                _movementSystem.Configure(
                    _slopeDegrees,
                    _forageDensity,
                    _movementResults,
                    geography.Resolution,
                    geography.WidthMeters,
                    geography.LengthMeters,
                    herdCapacity,
                    settings.Species,
                    settings,
                    dt,
                    absoluteStep++);
                _movementSystem.Update();

                ApplyMovementResults(fauna);
                _changedFloraSamples.Clear();
                FaunaSimulator.StepFeedingAndPhysiologySubstep(
                    floraState,
                    fauna,
                    settings.Species,
                    _dietIndices,
                    dt,
                    _changedFloraSamples);
                _bridge.SyncPhysiology(fauna);
                RefreshForageSamples(floraState, _changedFloraSamples);

                remaining -= dt;
                LastSubstepCount++;
            }

            fauna.SetSimulationTime(targetTimeDays);
        }

        public void Dispose()
        {
            _bridge.Dispose();
            if (_slopeDegrees.IsCreated) _slopeDegrees.Dispose();
            if (_forageDensity.IsCreated) _forageDensity.Dispose();
            if (_movementResults.IsCreated) _movementResults.Dispose();
            _geography = null;
            _forageState = null;
            _forageInitialized = false;
            _dietIndices = Array.Empty<int>();
            _dietWeights = Array.Empty<float>();
            _changedFloraSamples.Clear();
        }

        private void EnsureResultCapacity(int count)
        {
            if (_movementResults.IsCreated && _movementResults.Length == count) return;
            if (_movementResults.IsCreated) _movementResults.Dispose();
            _movementResults = new NativeArray<FaunaEcsMovementResult>(
                count,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private void ApplyMovementResults(FaunaSimulationData fauna)
        {
            for (int i = 0; i < fauna.Agents.Length; i++)
            {
                FaunaEcsMovementResult result = _movementResults[i];
                FaunaAgentState agent = fauna.Agents[i];
                agent.PositionLocalMeters = new Vector2(result.PositionMeters.x, result.PositionMeters.y);
                agent.Heading = new Vector2(result.Heading.x, result.Heading.y);
                fauna.Agents[i] = agent;
            }
        }

        private void EnsureEnvironment(GeographyData geography)
        {
            int sampleCount = geography.Resolution * geography.Resolution;
            if (ReferenceEquals(_geography, geography) && _slopeDegrees.IsCreated && _slopeDegrees.Length == sampleCount)
                return;

            if (_slopeDegrees.IsCreated) _slopeDegrees.Dispose();
            if (_forageDensity.IsCreated) _forageDensity.Dispose();

            _slopeDegrees = new NativeArray<float>(sampleCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _forageDensity = new NativeArray<float>(sampleCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _slopeDegrees.CopyFrom(geography.SlopeDegrees);
            _geography = geography;
        }

        private void ResolveDiet(FloraSimulationData floraState, FaunaSpeciesProfile profile)
        {
            int count = profile.Diet?.Count ?? 0;
            if (_dietIndices.Length != count)
            {
                _dietIndices = new int[count];
                _dietWeights = new float[count];
            }

            for (int i = 0; i < count; i++)
            {
                FaunaDietPreference preference = profile.Diet[i];
                _dietIndices[i] = preference == null ? -1 : floraState.FindSpeciesIndex(preference.FloraSpeciesStableId);
                _dietWeights[i] = preference == null ? 0f : Mathf.Max(0f, preference.Palatability);
            }
        }


        public void RefreshAllForage(FloraSimulationData floraState)
        {
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (!_forageDensity.IsCreated) return;
            _forageState = floraState;
            RebuildForageField(floraState);
            _forageInitialized = true;
        }

        private void EnsureForageField(FloraSimulationData floraState)
        {
            if (_forageInitialized && ReferenceEquals(_forageState, floraState)) return;
            _forageState = floraState;
            RebuildForageField(floraState);
            _forageInitialized = true;
        }

        private void RebuildForageField(FloraSimulationData floraState)
        {
            for (int sample = 0; sample < _forageDensity.Length; sample++)
                _forageDensity[sample] = CalculateForageDensity(floraState, sample);
        }

        private void RefreshForageSamples(FloraSimulationData floraState, List<int> changedSamples)
        {
            if (changedSamples.Count == 0) return;

            changedSamples.Sort();
            int previous = -1;
            int uniqueCount = 0;
            for (int i = 0; i < changedSamples.Count; i++)
            {
                int sample = changedSamples[i];
                if (sample == previous || sample < 0 || sample >= _forageDensity.Length) continue;
                previous = sample;
                _forageDensity[sample] = CalculateForageDensity(floraState, sample);
                uniqueCount++;
            }
            LastDirtyForageSamples += uniqueCount;
        }

        private float CalculateForageDensity(FloraSimulationData floraState, int sample)
        {
            float density = 0f;
            for (int i = 0; i < _dietIndices.Length; i++)
            {
                int speciesIndex = _dietIndices[i];
                float weight = _dietWeights[i];
                if (speciesIndex < 0 || speciesIndex >= floraState.SpeciesCount || weight <= 0f) continue;
                density += floraState.Species[speciesIndex].CurrentBiomassKgPerSquareMeter[sample] * weight;
            }
            return density;
        }

        private static int FindHerdCapacity(FaunaSimulationData fauna)
        {
            int maxHerd = -1;
            for (int i = 0; i < fauna.Agents.Length; i++)
                if (fauna.Agents[i].Alive) maxHerd = Mathf.Max(maxHerd, fauna.Agents[i].HerdId);
            return Mathf.Max(1, maxHerd + 1);
        }

        private static void ValidateWorld(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSimulationData fauna,
            FaunaSpeciesProfile profile)
        {
            if (geography.Resolution != floraState.Resolution ||
                geography.Seed != floraState.SourceGeographySeed ||
                fauna.SourceGeographySeed != geography.Seed ||
                Mathf.Abs(geography.WidthMeters - floraState.WidthMeters) > 0.001f ||
                Mathf.Abs(geography.LengthMeters - floraState.LengthMeters) > 0.001f ||
                Mathf.Abs(fauna.WidthMeters - geography.WidthMeters) > 0.001f ||
                Mathf.Abs(fauna.LengthMeters - geography.LengthMeters) > 0.001f)
                throw new ArgumentException("Geography, living Flora and Fauna must describe the same physical world.");

            if (!string.Equals(fauna.SpeciesStableId, profile.StableId, StringComparison.Ordinal))
                throw new ArgumentException("Fauna state and selected species profile do not match.");
        }
    }
}
