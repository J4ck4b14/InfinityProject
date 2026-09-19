using System;
using System.Collections.Generic;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityProject.World.Fauna
{
    /// <summary>
    /// Deer simulation: deterministic spawn, hunger/energy/health, actor-specific terrain traversal,
    /// local forage perception, simple herd cohesion and explicit consumption of living Flora biomass.
    /// </summary>
    public static class FaunaSimulator
    {
        private const float Epsilon = 0.000001f;

        public static FaunaSimulationData Initialize(
            GeographyData geography,
            FloraData floraPotential,
            FloraSimulationData floraState,
            FaunaGenerationSettings settings)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (floraPotential == null) throw new ArgumentNullException(nameof(floraPotential));
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            ValidateWorld(geography, floraPotential, floraState);

            settings = settings?.Clone() ?? new FaunaGenerationSettings();
            settings.Validate();
            FaunaSpeciesProfile profile = settings.Species;
            var random = new Unity.Mathematics.Random(NonZeroSeed((uint)settings.Seed));
            var agents = new FaunaAgentState[profile.AgentCount];
            int herdCount = Mathf.CeilToInt(profile.AgentCount / (float)profile.HerdSize);
            int nextId = 0;

            for (int herd = 0; herd < herdCount && nextId < agents.Length; herd++)
            {
                Vector2 center = FindSpawnPosition(geography, floraState, profile, ref random, null);
                int members = Mathf.Min(profile.HerdSize, agents.Length - nextId);
                for (int m = 0; m < members; m++)
                {
                    Vector2 position = m == 0
                        ? center
                        : FindSpawnPosition(geography, floraState, profile, ref random, center);
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    agents[nextId] = new FaunaAgentState
                    {
                        Id = nextId,
                        HerdId = herd,
                        PositionLocalMeters = position,
                        Heading = new Vector2(math.cos(angle), math.sin(angle)),
                        Hunger01 = random.NextFloat(0.18f, 0.32f),
                        Energy01 = random.NextFloat(0.72f, 0.9f),
                        Health01 = 1f,
                        CumulativeFoodConsumedKg = 0f,
                        Alive = true
                    };
                    nextId++;
                }
            }

            return new FaunaSimulationData(
                profile.StableId,
                profile.DisplayName,
                geography.Seed,
                geography.WidthMeters,
                geography.LengthMeters,
                floraState.SimulationTimeDays,
                agents);
        }

        public static void Step(
            GeographyData geography,
            FloraData floraPotential,
            FloraSimulationData floraState,
            FaunaSimulationData fauna,
            FaunaGenerationSettings settings,
            float deltaDays)
        {
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (floraPotential == null) throw new ArgumentNullException(nameof(floraPotential));
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            if (deltaDays < 0f || float.IsNaN(deltaDays) || float.IsInfinity(deltaDays))
                throw new ArgumentOutOfRangeException(nameof(deltaDays));
            if (deltaDays <= 0f) return;
            ValidateWorld(geography, floraPotential, floraState);
            if (fauna.SourceGeographySeed != geography.Seed ||
                Mathf.Abs(fauna.WidthMeters - geography.WidthMeters) > 0.001f ||
                Mathf.Abs(fauna.LengthMeters - geography.LengthMeters) > 0.001f)
                throw new ArgumentException("Fauna state belongs to a different physical world.");

            settings = settings?.Clone() ?? new FaunaGenerationSettings();
            settings.Validate();
            FaunaSpeciesProfile profile = settings.Species;
            if (!string.Equals(fauna.SpeciesStableId, profile.StableId, StringComparison.Ordinal))
                throw new ArgumentException("Fauna state and selected species profile do not match.");

            // Substeps are an integration detail, not authoritative time. Compute the requested target once
            // and snap the state to it after integration so long runs cannot accumulate clock drift.
            double targetTimeDays = fauna.SimulationTimeDays + (double)deltaDays;
            float remaining = deltaDays;
            long absoluteStep = (long)Math.Floor(fauna.SimulationTimeDays / settings.MaximumMovementSubstepDays);
            while (remaining > Epsilon)
            {
                float dt = Mathf.Min(remaining, settings.MaximumMovementSubstepDays);
                StepSubstep(geography, floraState, fauna, profile, settings, dt, absoluteStep++);
                remaining -= dt;
            }
            fauna.SetSimulationTime(targetTimeDays);
        }

        private static void StepSubstep(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSimulationData fauna,
            FaunaSpeciesProfile profile,
            FaunaGenerationSettings settings,
            float dt,
            long absoluteStep)
        {
            int agentCount = fauna.Agents.Length;
            var herdCentroids = BuildHerdCentroids(fauna.Agents, out int[] herdCounts);
            int[] dietIndices = ResolveDietIndices(floraState, profile);

            for (int a = 0; a < agentCount; a++)
            {
                FaunaAgentState agent = fauna.Agents[a];
                if (!agent.Alive) continue;

                Vector2 herdCenter = agent.HerdId >= 0 && agent.HerdId < herdCentroids.Length && herdCounts[agent.HerdId] > 0
                    ? herdCentroids[agent.HerdId]
                    : agent.PositionLocalMeters;
                Vector2 target = ChooseMovementTarget(
                    geography,
                    floraState,
                    profile,
                    settings,
                    dietIndices,
                    agent,
                    herdCenter,
                    absoluteStep);

                Vector2 toTarget = target - agent.PositionLocalMeters;
                float targetDistance = toTarget.magnitude;
                if (targetDistance > Epsilon)
                {
                    Vector2 desired = toTarget / targetDistance;
                    float maxDistance = profile.MoveSpeedMetersPerDay * dt;
                    float moveDistance = Mathf.Min(maxDistance, targetDistance);
                    Vector2 proposed = ClampWorld(agent.PositionLocalMeters + desired * moveDistance, geography.WidthMeters, geography.LengthMeters);
                    if (IsPathTraversable(geography, agent.PositionLocalMeters, proposed, profile.MaxTraversableSlopeDegrees))
                    {
                        agent.PositionLocalMeters = proposed;
                        agent.Heading = desired;
                    }
                }

                ApplyFeedingAndPhysiology(
                    floraState,
                    dietIndices,
                    profile,
                    ref agent,
                    dt,
                    null);
                fauna.Agents[a] = agent;
            }
        }

        internal static void StepFeedingAndPhysiologySubstep(
            FloraSimulationData floraState,
            FaunaSimulationData fauna,
            FaunaSpeciesProfile profile,
            int[] dietIndices,
            float dt,
            List<int> changedFloraSamples)
        {
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (fauna == null) throw new ArgumentNullException(nameof(fauna));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (dietIndices == null) throw new ArgumentNullException(nameof(dietIndices));
            if (dt <= 0f) return;

            for (int i = 0; i < fauna.Agents.Length; i++)
            {
                FaunaAgentState agent = fauna.Agents[i];
                if (!agent.Alive) continue;
                ApplyFeedingAndPhysiology(
                    floraState,
                    dietIndices,
                    profile,
                    ref agent,
                    dt,
                    changedFloraSamples);
                fauna.Agents[i] = agent;
            }
        }

        private static void ApplyFeedingAndPhysiology(
            FloraSimulationData floraState,
            int[] dietIndices,
            FaunaSpeciesProfile profile,
            ref FaunaAgentState agent,
            float dt,
            List<int> changedFloraSamples)
        {
            float requiredKg = profile.DailyFoodRequirementKg * dt;
            float consumedKg = ConsumeDiet(
                floraState,
                dietIndices,
                profile,
                agent.PositionLocalMeters,
                requiredKg,
                changedFloraSamples);
            agent.CumulativeFoodConsumedKg += consumedKg;
            float ration = requiredKg > Epsilon ? Mathf.Clamp01(consumedKg / requiredKg) : 1f;

            float hungerDelta = profile.HungerIncreasePerDay * dt * (1f - 1.15f * ration);
            agent.Hunger01 = Mathf.Clamp01(agent.Hunger01 + hungerDelta);
            agent.Energy01 = Mathf.Clamp01(
                agent.Energy01 - profile.EnergyUsePerDay * dt + profile.EnergyRecoveryPerFullRationPerDay * ration * dt);

            float starvation = Mathf.Max(
                Mathf.InverseLerp(0.78f, 1f, agent.Hunger01),
                1f - Mathf.InverseLerp(0.05f, 0.28f, agent.Energy01));
            if (starvation > 0f)
                agent.Health01 = Mathf.Clamp01(agent.Health01 - profile.StarvationHealthLossPerDay * starvation * dt);
            else if (ration > 0.75f)
                agent.Health01 = Mathf.Clamp01(agent.Health01 + profile.HealthRecoveryPerDay * dt);

            if (agent.Health01 <= Epsilon) agent.Alive = false;
        }

        private static Vector2 ChooseMovementTarget(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSpeciesProfile profile,
            FaunaGenerationSettings settings,
            int[] dietIndices,
            FaunaAgentState agent,
            Vector2 herdCenter,
            long absoluteStep)
        {
            Vector2 best = agent.PositionLocalMeters;
            float bestScore = ScoreCandidate(geography, floraState, profile, dietIndices, agent, herdCenter, best, agent.Heading);
            float radius = profile.ForagePerceptionRadiusMeters;
            float hungerBias = Mathf.Lerp(0.35f, 1.35f, agent.Hunger01);

            for (int i = 0; i < settings.ForageDirectionSamples; i++)
            {
                float baseAngle = (math.PI * 2f * i / settings.ForageDirectionSamples);
                float jitter = DeterministicSigned01(agent.Id, absoluteStep, i) * 0.13f;
                float angle = baseAngle + jitter;
                float radialFraction = (i & 1) == 0 ? 1f : 0.55f;
                Vector2 direction = new(math.cos(angle), math.sin(angle));
                Vector2 candidate = ClampWorld(
                    agent.PositionLocalMeters + direction * radius * radialFraction,
                    geography.WidthMeters,
                    geography.LengthMeters);
                if (!IsPathTraversable(geography, agent.PositionLocalMeters, candidate, profile.MaxTraversableSlopeDegrees)) continue;
                float score = ScoreCandidate(geography, floraState, profile, dietIndices, agent, herdCenter, candidate, direction);
                // Small seeded exploration breaks perfectly uniform-forage ties without replacing causal forage,
                // terrain or herd steering. It is deterministic for agent + simulation step + direction sample.
                float exploration = profile.ExplorationWeight *
                                    (0.5f + 0.5f * DeterministicSigned01(agent.Id, absoluteStep, i + 2048));
                score = score * hungerBias + exploration;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            // Herd center is an explicit candidate even when forage sampling does not land near it.
            Vector2 herdCandidate = ClampWorld(herdCenter, geography.WidthMeters, geography.LengthMeters);
            if (IsPathTraversable(geography, agent.PositionLocalMeters, herdCandidate, profile.MaxTraversableSlopeDegrees))
            {
                Vector2 herdDirection = herdCandidate - agent.PositionLocalMeters;
                if (herdDirection.sqrMagnitude > Epsilon) herdDirection.Normalize();
                else herdDirection = agent.Heading;
                float score = ScoreCandidate(geography, floraState, profile, dietIndices, agent, herdCenter, herdCandidate, herdDirection);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = herdCandidate;
                }
            }

            return best;
        }

        private static float ScoreCandidate(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSpeciesProfile profile,
            int[] dietIndices,
            FaunaAgentState agent,
            Vector2 herdCenter,
            Vector2 candidate,
            Vector2 candidateDirection)
        {
            float slope = SampleSlopeDegrees(geography, candidate);
            if (slope > profile.MaxTraversableSlopeDegrees) return float.NegativeInfinity;
            float terrainComfort = slope <= profile.ComfortableSlopeDegrees
                ? 1f
                : 1f - Mathf.InverseLerp(profile.ComfortableSlopeDegrees, profile.MaxTraversableSlopeDegrees, slope);

            float forageDensity = 0f;
            for (int i = 0; i < profile.Diet.Count && i < dietIndices.Length; i++)
            {
                int speciesIndex = dietIndices[i];
                if (speciesIndex < 0 || profile.Diet[i].Palatability <= 0f) continue;
                forageDensity += floraState.SampleBiomassDensity(speciesIndex, candidate) * profile.Diet[i].Palatability;
            }
            float forageScore = 1f - Mathf.Exp(-Mathf.Max(0f, forageDensity));

            float herdDistance = Vector2.Distance(candidate, herdCenter);
            float herdScore = 1f - Mathf.Clamp01(herdDistance / profile.HerdCohesionRadiusMeters);
            Vector2 heading = agent.Heading.sqrMagnitude > Epsilon ? agent.Heading.normalized : Vector2.right;
            Vector2 direction = candidateDirection.sqrMagnitude > Epsilon ? candidateDirection.normalized : heading;
            float headingScore = (Vector2.Dot(heading, direction) + 1f) * 0.5f;
            float hungerForageWeight = profile.ForageSteeringWeight * Mathf.Lerp(0.3f, 1.2f, agent.Hunger01);

            return forageScore * hungerForageWeight +
                   herdScore * profile.HerdCohesionWeight +
                   terrainComfort * 0.18f +
                   headingScore * profile.HeadingPersistenceWeight;
        }

        private static float ConsumeDiet(
            FloraSimulationData floraState,
            int[] dietIndices,
            FaunaSpeciesProfile profile,
            Vector2 position,
            float requiredKg,
            List<int> changedFloraSamples = null)
        {
            float remaining = requiredKg;
            float consumed = 0f;
            for (int i = 0; i < profile.Diet.Count && i < dietIndices.Length && remaining > Epsilon; i++)
            {
                int speciesIndex = dietIndices[i];
                float palatability = profile.Diet[i].Palatability;
                if (speciesIndex < 0 || palatability <= 0f) continue;
                float requested = remaining * palatability;
                float taken = FloraSimulator.ConsumeInCircle(
                    floraState,
                    speciesIndex,
                    position,
                    profile.BrowseRadiusMeters,
                    requested,
                    changedFloraSamples);
                consumed += taken;
                remaining -= taken;
            }
            return consumed;
        }


        /// <summary>
        /// Estimates physically accessible preferred food inside the current browse footprint without consuming it.
        /// Palatability weights the diagnostic amount so low-preference fallback foods do not look equivalent to
        /// primary forage merely because their raw biomass is large.
        /// </summary>
        public static float EstimatePreferredFoodAvailableKg(
            FloraSimulationData floraState,
            FaunaSpeciesProfile profile,
            Vector2 position)
        {
            if (floraState == null || profile == null || profile.Diet == null) return 0f;
            float total = 0f;
            for (int i = 0; i < profile.Diet.Count; i++)
            {
                FaunaDietPreference preference = profile.Diet[i];
                if (preference == null || preference.Palatability <= 0f) continue;
                int speciesIndex = floraState.FindSpeciesIndex(preference.FloraSpeciesStableId);
                if (speciesIndex < 0) continue;
                total += FloraSimulator.AvailableBiomassKgInCircle(
                    floraState,
                    speciesIndex,
                    position,
                    profile.BrowseRadiusMeters) * preference.Palatability;
            }
            return total;
        }

        private static Vector2[] BuildHerdCentroids(FaunaAgentState[] agents, out int[] counts)
        {
            int maxHerd = -1;
            for (int i = 0; i < agents.Length; i++) if (agents[i].Alive) maxHerd = Mathf.Max(maxHerd, agents[i].HerdId);
            if (maxHerd < 0)
            {
                counts = Array.Empty<int>();
                return Array.Empty<Vector2>();
            }

            var centers = new Vector2[maxHerd + 1];
            counts = new int[maxHerd + 1];
            for (int i = 0; i < agents.Length; i++)
            {
                if (!agents[i].Alive || agents[i].HerdId < 0) continue;
                centers[agents[i].HerdId] += agents[i].PositionLocalMeters;
                counts[agents[i].HerdId]++;
            }
            for (int i = 0; i < centers.Length; i++) if (counts[i] > 0) centers[i] /= counts[i];
            return centers;
        }

        private static int[] ResolveDietIndices(FloraSimulationData floraState, FaunaSpeciesProfile profile)
        {
            var result = new int[profile.Diet.Count];
            for (int i = 0; i < result.Length; i++) result[i] = floraState.FindSpeciesIndex(profile.Diet[i].FloraSpeciesStableId);
            return result;
        }

        private static Vector2 FindSpawnPosition(
            GeographyData geography,
            FloraSimulationData floraState,
            FaunaSpeciesProfile profile,
            ref Unity.Mathematics.Random random,
            Vector2? near)
        {
            int[] dietIndices = ResolveDietIndices(floraState, profile);
            Vector2 best = new(geography.WidthMeters * 0.5f, geography.LengthMeters * 0.5f);
            float bestScore = float.NegativeInfinity;
            bool foundTraversable = false;
            for (int attempt = 0; attempt < 256; attempt++)
            {
                Vector2 candidate;
                if (near.HasValue && attempt < 160)
                {
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float radius = random.NextFloat(5f, Mathf.Min(45f, profile.HerdCohesionRadiusMeters * 0.6f));
                    candidate = ClampWorld(near.Value + new Vector2(math.cos(angle), math.sin(angle)) * radius, geography.WidthMeters, geography.LengthMeters);
                }
                else
                {
                    candidate = new Vector2(random.NextFloat(0f, geography.WidthMeters), random.NextFloat(0f, geography.LengthMeters));
                }

                float slope = SampleSlopeDegrees(geography, candidate);
                if (slope > profile.MaxTraversableSlopeDegrees) continue;
                foundTraversable = true;
                float food = 0f;
                for (int d = 0; d < dietIndices.Length; d++)
                {
                    if (dietIndices[d] < 0) continue;
                    food += floraState.SampleBiomassDensity(dietIndices[d], candidate) * profile.Diet[d].Palatability;
                }
                float score = food + (1f - slope / profile.MaxTraversableSlopeDegrees) * 0.05f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
                if (food > 0.02f && attempt > 8) return candidate;
            }
            if (!foundTraversable)
                throw new InvalidOperationException($"Could not find traversable spawn ground for '{profile.StableId}' at max slope {profile.MaxTraversableSlopeDegrees:0.##}°.");
            return best;
        }

        public static bool IsTraversable(GeographyData geography, Vector2 positionLocalMeters, float maxSlopeDegrees)
            => SampleSlopeDegrees(geography, positionLocalMeters) <= maxSlopeDegrees;

        /// <summary>
        /// Checks the actual movement segment rather than only its destination so an agent cannot numerically
        /// jump across a narrow over-steep ridge between two otherwise traversable samples.
        /// </summary>
        public static bool IsPathTraversable(
            GeographyData geography,
            Vector2 fromLocalMeters,
            Vector2 toLocalMeters,
            float maxSlopeDegrees)
        {
            if (geography == null) return false;
            Vector2 delta = toLocalMeters - fromLocalMeters;
            float distance = delta.magnitude;
            if (distance <= Epsilon) return IsTraversable(geography, fromLocalMeters, maxSlopeDegrees);

            float sampleSpacing = Mathf.Max(2f, Mathf.Min(geography.SampleSpacingX, geography.SampleSpacingZ) * 0.5f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / sampleSpacing));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 sample = Vector2.Lerp(fromLocalMeters, toLocalMeters, i / (float)steps);
                if (!IsTraversable(geography, sample, maxSlopeDegrees)) return false;
            }
            return true;
        }

        public static float SampleSlopeDegrees(GeographyData geography, Vector2 positionLocalMeters)
        {
            float2 grid = geography.LocalMetersToGrid(new float2(positionLocalMeters.x, positionLocalMeters.y));
            int x = Mathf.Clamp(Mathf.RoundToInt(grid.x), 0, geography.Resolution - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(grid.y), 0, geography.Resolution - 1);
            return geography.SlopeDegrees[geography.Index(x, y)];
        }

        private static Vector2 ClampWorld(Vector2 position, float width, float length)
            => new(Mathf.Clamp(position.x, 0f, width), Mathf.Clamp(position.y, 0f, length));

        private static float DeterministicSigned01(int agentId, long step, int sample)
        {
            unchecked
            {
                uint x = (uint)agentId * 747796405u + (uint)step * 2891336453u + (uint)sample * 277803737u + 0x9E3779B9u;
                x ^= x >> 16;
                x *= 2246822519u;
                x ^= x >> 13;
                x *= 3266489917u;
                x ^= x >> 16;
                return (x / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        private static uint NonZeroSeed(uint seed)
            => seed == 0u ? 1u : seed;

        private static void ValidateWorld(GeographyData geography, FloraData floraPotential, FloraSimulationData floraState)
        {
            if (floraPotential.Resolution != geography.Resolution || floraState.Resolution != geography.Resolution ||
                floraPotential.SourceGeographySeed != geography.Seed || floraState.SourceGeographySeed != geography.Seed ||
                Mathf.Abs(floraPotential.WidthMeters - geography.WidthMeters) > 0.001f ||
                Mathf.Abs(floraPotential.LengthMeters - geography.LengthMeters) > 0.001f ||
                Mathf.Abs(floraState.WidthMeters - geography.WidthMeters) > 0.001f ||
                Mathf.Abs(floraState.LengthMeters - geography.LengthMeters) > 0.001f)
                throw new ArgumentException("Geography, Flora potential and living Flora state must describe the same world.");
        }
    }
}
