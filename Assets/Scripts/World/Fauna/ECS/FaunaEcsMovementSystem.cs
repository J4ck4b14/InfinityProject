using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;

namespace InfinityProject.World.Fauna.ECS
{
    /// <summary>
    /// Owns deer locomotion state. Steering reads immutable terrain/forage fields for the current substep;
    /// feeding updates those fields before the following substep.
    /// </summary>
    [DisableAutoCreation]
    public partial class FaunaEcsMovementSystem : SystemBase
    {
        private NativeArray<float2> _herdSums;
        private NativeArray<int> _herdCounts;

        private NativeArray<float> _slopeDegrees;
        private NativeArray<float> _forageDensity;
        private NativeArray<FaunaEcsMovementResult> _results;

        private int _resolution;
        private int _forageDirectionSamples;
        private float _widthMeters;
        private float _lengthMeters;
        private float _sampleSpacingX;
        private float _sampleSpacingZ;
        private float _deltaDays;
        private long _absoluteStep;

        private float _moveSpeedMetersPerDay;
        private float _foragePerceptionRadiusMeters;
        private float _maxTraversableSlopeDegrees;
        private float _comfortableSlopeDegrees;
        private float _forageSteeringWeight;
        private float _herdCohesionWeight;
        private float _herdCohesionRadiusMeters;
        private float _headingPersistenceWeight;
        private float _explorationWeight;

        private bool _configured;

        public void Configure(
            NativeArray<float> slopeDegrees,
            NativeArray<float> forageDensity,
            NativeArray<FaunaEcsMovementResult> results,
            int resolution,
            float widthMeters,
            float lengthMeters,
            int herdCapacity,
            FaunaSpeciesProfile profile,
            FaunaGenerationSettings settings,
            float deltaDays,
            long absoluteStep)
        {
            _slopeDegrees = slopeDegrees;
            _forageDensity = forageDensity;
            _results = results;
            _resolution = resolution;
            _widthMeters = widthMeters;
            _lengthMeters = lengthMeters;
            _sampleSpacingX = widthMeters / (resolution - 1);
            _sampleSpacingZ = lengthMeters / (resolution - 1);
            _forageDirectionSamples = settings.ForageDirectionSamples;
            _deltaDays = deltaDays;
            _absoluteStep = absoluteStep;

            _moveSpeedMetersPerDay = profile.MoveSpeedMetersPerDay;
            _foragePerceptionRadiusMeters = profile.ForagePerceptionRadiusMeters;
            _maxTraversableSlopeDegrees = profile.MaxTraversableSlopeDegrees;
            _comfortableSlopeDegrees = profile.ComfortableSlopeDegrees;
            _forageSteeringWeight = profile.ForageSteeringWeight;
            _herdCohesionWeight = profile.HerdCohesionWeight;
            _herdCohesionRadiusMeters = profile.HerdCohesionRadiusMeters;
            _headingPersistenceWeight = profile.HeadingPersistenceWeight;
            _explorationWeight = profile.ExplorationWeight;

            EnsureHerdCapacity(math.max(1, herdCapacity));
            _configured = true;
        }

        protected override void OnUpdate()
        {
            if (!_configured || !_slopeDegrees.IsCreated || !_forageDensity.IsCreated) return;

            for (int i = 0; i < _herdSums.Length; i++)
            {
                _herdSums[i] = float2.zero;
                _herdCounts[i] = 0;
            }

            JobHandle herdHandle = new AccumulateHerdJob
            {
                Sums = _herdSums,
                Counts = _herdCounts
            }.Schedule(Dependency);

            Dependency = new MoveJob
            {
                SlopeDegrees = _slopeDegrees,
                ForageDensity = _forageDensity,
                Results = _results,
                HerdSums = _herdSums,
                HerdCounts = _herdCounts,
                Resolution = _resolution,
                WidthMeters = _widthMeters,
                LengthMeters = _lengthMeters,
                SampleSpacingX = _sampleSpacingX,
                SampleSpacingZ = _sampleSpacingZ,
                DeltaDays = _deltaDays,
                AbsoluteStep = _absoluteStep,
                ForageDirectionSamples = _forageDirectionSamples,
                MoveSpeedMetersPerDay = _moveSpeedMetersPerDay,
                ForagePerceptionRadiusMeters = _foragePerceptionRadiusMeters,
                MaxTraversableSlopeDegrees = _maxTraversableSlopeDegrees,
                ComfortableSlopeDegrees = _comfortableSlopeDegrees,
                ForageSteeringWeight = _forageSteeringWeight,
                HerdCohesionWeight = _herdCohesionWeight,
                HerdCohesionRadiusMeters = _herdCohesionRadiusMeters,
                HeadingPersistenceWeight = _headingPersistenceWeight,
                ExplorationWeight = _explorationWeight
            }.ScheduleParallel(herdHandle);

            // Explicit ecology stepping needs positions immediately for feeding. A runtime scheduler can own
            // this dependency later without changing the movement job itself.
            Dependency.Complete();
            Dependency = default;
        }

        protected override void OnDestroy()
        {
            if (_herdSums.IsCreated) _herdSums.Dispose();
            if (_herdCounts.IsCreated) _herdCounts.Dispose();
        }

        private void EnsureHerdCapacity(int capacity)
        {
            if (_herdSums.IsCreated && _herdSums.Length >= capacity) return;

            if (_herdSums.IsCreated) _herdSums.Dispose();
            if (_herdCounts.IsCreated) _herdCounts.Dispose();
            _herdSums = new NativeArray<float2>(capacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _herdCounts = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        [BurstCompile]
        private partial struct AccumulateHerdJob : IJobEntity
        {
            public NativeArray<float2> Sums;
            public NativeArray<int> Counts;

            public void Execute(
                in FaunaEcsIdentity identity,
                in FaunaEcsLocalPosition position,
                in FaunaEcsPhysiology physiology)
            {
                if (physiology.Alive == 0 || identity.HerdId < 0 || identity.HerdId >= Counts.Length) return;
                int herd = identity.HerdId;
                Sums[herd] = Sums[herd] + position.Meters;
                Counts[herd] = Counts[herd] + 1;
            }
        }

        [BurstCompile]
        private partial struct MoveJob : IJobEntity
        {
            [ReadOnly] public NativeArray<float> SlopeDegrees;
            [ReadOnly] public NativeArray<float> ForageDensity;
            [NativeDisableParallelForRestriction] public NativeArray<FaunaEcsMovementResult> Results;
            [ReadOnly] public NativeArray<float2> HerdSums;
            [ReadOnly] public NativeArray<int> HerdCounts;

            public int Resolution;
            public int ForageDirectionSamples;
            public float WidthMeters;
            public float LengthMeters;
            public float SampleSpacingX;
            public float SampleSpacingZ;
            public float DeltaDays;
            public long AbsoluteStep;

            public float MoveSpeedMetersPerDay;
            public float ForagePerceptionRadiusMeters;
            public float MaxTraversableSlopeDegrees;
            public float ComfortableSlopeDegrees;
            public float ForageSteeringWeight;
            public float HerdCohesionWeight;
            public float HerdCohesionRadiusMeters;
            public float HeadingPersistenceWeight;
            public float ExplorationWeight;

            private const float Epsilon = 0.000001f;

            public void Execute(
                ref FaunaEcsLocalPosition position,
                ref FaunaEcsHeading heading,
                ref FaunaEcsMotion motion,
                in FaunaEcsIdentity identity,
                in FaunaEcsPhysiology physiology)
            {
                motion.LastStepDistanceMeters = 0f;
                float2 current = position.Meters;
                float2 currentHeading = NormalizeOr(heading.Value, new float2(0f, 1f));
                if (physiology.Alive == 0)
                {
                    WriteResult(identity.StateIndex, current, currentHeading);
                    return;
                }
                float2 herdCenter = current;
                if (identity.HerdId >= 0 && identity.HerdId < HerdCounts.Length && HerdCounts[identity.HerdId] > 0)
                    herdCenter = HerdSums[identity.HerdId] / HerdCounts[identity.HerdId];

                float2 target = ChooseTarget(identity.AgentId, current, currentHeading, physiology.Hunger01, herdCenter);
                float2 toTarget = target - current;
                float distance = math.length(toTarget);
                if (distance <= Epsilon)
                {
                    WriteResult(identity.StateIndex, current, currentHeading);
                    return;
                }

                float2 desired = toTarget / distance;
                float moveDistance = math.min(MoveSpeedMetersPerDay * DeltaDays, distance);
                float2 proposed = ClampWorld(current + desired * moveDistance);
                if (!IsPathTraversable(current, proposed))
                {
                    WriteResult(identity.StateIndex, current, currentHeading);
                    return;
                }

                float actualDistance = math.distance(current, proposed);
                position.Meters = proposed;
                heading.Value = desired;
                motion.LastStepDistanceMeters = actualDistance;
                motion.CumulativeDistanceMeters += actualDistance;
                WriteResult(identity.StateIndex, proposed, desired);
            }

            private void WriteResult(int stateIndex, float2 position, float2 heading)
            {
                if (stateIndex < 0 || stateIndex >= Results.Length) return;
                Results[stateIndex] = new FaunaEcsMovementResult
                {
                    PositionMeters = position,
                    Heading = heading
                };
            }

            private float2 ChooseTarget(int agentId, float2 position, float2 heading, float hunger01, float2 herdCenter)
            {
                float2 best = position;
                float bestScore = ScoreCandidate(position, heading, hunger01, herdCenter, position, heading);
                float hungerBias = math.lerp(0.35f, 1.35f, hunger01);

                for (int i = 0; i < ForageDirectionSamples; i++)
                {
                    float baseAngle = math.PI * 2f * i / ForageDirectionSamples;
                    float jitter = DeterministicSigned01(agentId, AbsoluteStep, i) * 0.13f;
                    float angle = baseAngle + jitter;
                    float radialFraction = (i & 1) == 0 ? 1f : 0.55f;
                    float2 direction = new(math.cos(angle), math.sin(angle));
                    float2 candidate = ClampWorld(position + direction * ForagePerceptionRadiusMeters * radialFraction);
                    if (!IsPathTraversable(position, candidate)) continue;

                    float score = ScoreCandidate(position, heading, hunger01, herdCenter, candidate, direction);
                    float exploration = ExplorationWeight *
                                        (0.5f + 0.5f * DeterministicSigned01(agentId, AbsoluteStep, i + 2048));
                    score = score * hungerBias + exploration;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                float2 herdCandidate = ClampWorld(herdCenter);
                if (IsPathTraversable(position, herdCandidate))
                {
                    float2 herdDirection = NormalizeOr(herdCandidate - position, heading);
                    float score = ScoreCandidate(position, heading, hunger01, herdCenter, herdCandidate, herdDirection);
                    if (score > bestScore) best = herdCandidate;
                }

                return best;
            }

            private float ScoreCandidate(
                float2 currentPosition,
                float2 currentHeading,
                float hunger01,
                float2 herdCenter,
                float2 candidate,
                float2 candidateDirection)
            {
                float slope = SampleSlopeDegrees(candidate);
                if (slope > MaxTraversableSlopeDegrees) return float.NegativeInfinity;

                float terrainComfort = 1f;
                if (slope > ComfortableSlopeDegrees)
                {
                    float range = math.max(Epsilon, MaxTraversableSlopeDegrees - ComfortableSlopeDegrees);
                    terrainComfort = 1f - math.saturate((slope - ComfortableSlopeDegrees) / range);
                }

                float forageDensity = SampleForageDensity(candidate);
                float forageScore = 1f - math.exp(-math.max(0f, forageDensity));
                float herdDistance = math.distance(candidate, herdCenter);
                float herdScore = 1f - math.saturate(herdDistance / math.max(Epsilon, HerdCohesionRadiusMeters));
                float2 heading = NormalizeOr(currentHeading, new float2(1f, 0f));
                float2 direction = NormalizeOr(candidateDirection, heading);
                float headingScore = (math.dot(heading, direction) + 1f) * 0.5f;
                float hungerForageWeight = ForageSteeringWeight * math.lerp(0.3f, 1.2f, hunger01);

                return forageScore * hungerForageWeight +
                       herdScore * HerdCohesionWeight +
                       terrainComfort * 0.18f +
                       headingScore * HeadingPersistenceWeight;
            }

            private bool IsPathTraversable(float2 from, float2 to)
            {
                float2 delta = to - from;
                float distance = math.length(delta);
                if (distance <= Epsilon) return SampleSlopeDegrees(from) <= MaxTraversableSlopeDegrees;

                float sampleSpacing = math.max(2f, math.min(SampleSpacingX, SampleSpacingZ) * 0.5f);
                int steps = math.max(1, (int)math.ceil(distance / sampleSpacing));
                for (int i = 1; i <= steps; i++)
                {
                    float2 sample = math.lerp(from, to, i / (float)steps);
                    if (SampleSlopeDegrees(sample) > MaxTraversableSlopeDegrees) return false;
                }
                return true;
            }

            private float SampleSlopeDegrees(float2 position)
                => SlopeDegrees[SampleIndex(position)];

            private float SampleForageDensity(float2 position)
                => ForageDensity[SampleIndex(position)];

            private int SampleIndex(float2 position)
            {
                int x = math.clamp((int)math.round(math.saturate(position.x / WidthMeters) * (Resolution - 1)), 0, Resolution - 1);
                int y = math.clamp((int)math.round(math.saturate(position.y / LengthMeters) * (Resolution - 1)), 0, Resolution - 1);
                return y * Resolution + x;
            }

            private float2 ClampWorld(float2 position)
                => new(math.clamp(position.x, 0f, WidthMeters), math.clamp(position.y, 0f, LengthMeters));

            private static float2 NormalizeOr(float2 value, float2 fallback)
            {
                float lengthSq = math.lengthsq(value);
                return lengthSq > Epsilon ? value * math.rsqrt(lengthSq) : fallback;
            }

            private static float DeterministicSigned01(int agentId, long step, int sample)
            {
                unchecked
                {
                    uint x = (uint)agentId * 747796405u + (uint)step * 2891336453u +
                             (uint)sample * 277803737u + 0x9E3779B9u;
                    x ^= x >> 16;
                    x *= 2246822519u;
                    x ^= x >> 13;
                    x *= 3266489917u;
                    x ^= x >> 16;
                    return (x / (float)uint.MaxValue) * 2f - 1f;
                }
            }
        }
    }
}
