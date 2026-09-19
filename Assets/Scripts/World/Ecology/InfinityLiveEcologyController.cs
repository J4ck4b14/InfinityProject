using System;
using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using InfinityProject.World.Fauna.Presentation;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Timekeeping;
using UnityEngine;

namespace InfinityProject.World.Ecology
{
    [DisallowMultipleComponent]
    public sealed class InfinityLiveEcologyController : MonoBehaviour
    {
        // Ecological rates are currently expressed per simulation day. This conversion is
        // intentionally independent of World Clock day length.
        public const double EcologyRateDaySeconds = 1200d;

        private const double FaunaTickSeconds = 0.25d;
        private const double FloraTickSeconds = 60d;

        private InfinityTimeController _time;
        private Terrain _terrain;
        private GeographyData _geography;
        private FloraData _floraPotential;
        private FloraSimulationData _floraState;
        private FaunaSimulationData _faunaState;
        private FloraGenerationSettings _floraGenerationSettings;
        private FloraSimulationSettings _floraSimulationSettings;
        private FaunaGenerationSettings _faunaSettings;
        private FaunaEcsMovementBackend _movement;
        private FaunaRuntimePresentationPool _presentation;

        private double _faunaAccumulatorSeconds;
        private double _floraAccumulatorSeconds;
        private bool _initialized;

        public event Action FloraStateChanged;

        public bool IsInitialized => _initialized;
        public FaunaSimulationData FaunaState => _faunaState;
        public FloraSimulationData FloraState => _floraState;
        public int AliveCount => _faunaState?.AliveCount ?? 0;
        public int AgentCount => _faunaState?.AgentCount ?? 0;
        public double ProcessedFaunaDays => _faunaState?.SimulationTimeDays ?? 0d;
        public double ProcessedFloraDays => _floraState?.SimulationTimeDays ?? 0d;
        public int PresentationCount => _presentation?.ActiveCount ?? 0;
        public float MeanHunger01 => _faunaState?.MeanHunger01 ?? 0f;
        public float MeanHealth01 => _faunaState?.MeanHealth01 ?? 0f;
        public double ConsumedFoodKg => _faunaState?.CumulativeFoodConsumedKg ?? 0d;

        public void Initialize(
            Terrain terrain,
            InfinityTimeController time,
            GeographyData geography,
            FloraData floraPotential,
            FloraSimulationData floraState,
            FloraGenerationSettings floraGenerationSettings,
            FloraSimulationSettings floraSimulationSettings,
            FaunaSimulationData faunaState,
            FaunaGenerationSettings faunaSettings,
            GameObject doePrefab)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (time == null) throw new ArgumentNullException(nameof(time));
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (floraPotential == null) throw new ArgumentNullException(nameof(floraPotential));
            if (floraState == null) throw new ArgumentNullException(nameof(floraState));
            if (faunaState == null) throw new ArgumentNullException(nameof(faunaState));

            Shutdown();

            _terrain = terrain;
            _time = time;
            _geography = geography;
            _floraPotential = floraPotential;
            _floraState = floraState;
            _faunaState = faunaState;
            _floraGenerationSettings = floraGenerationSettings?.Clone() ?? new FloraGenerationSettings();
            _floraSimulationSettings = floraSimulationSettings?.Clone() ?? new FloraSimulationSettings();
            _faunaSettings = faunaSettings?.Clone() ?? new FaunaGenerationSettings();

            _movement = new FaunaEcsMovementBackend("Infinity Fauna ECS V1 Live");
            _movement.SyncSnapshot(_faunaState);
            if (doePrefab != null)
            {
                _presentation = new FaunaRuntimePresentationPool(_terrain, _geography, doePrefab, transform);
                _presentation?.Sync(_faunaState, 0d);
            }

            _faunaAccumulatorSeconds = 0d;
            _floraAccumulatorSeconds = 0d;
            _time.SimulationStepRequested += OnSimulationStepRequested;
            _initialized = true;
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnSimulationStepRequested(double simulationDeltaSeconds)
        {
            if (!_initialized || simulationDeltaSeconds <= 0d) return;

            _faunaAccumulatorSeconds += simulationDeltaSeconds;

            while (_faunaAccumulatorSeconds + 1e-9d >= FaunaTickSeconds)
            {
                StepFauna(FaunaTickSeconds);
                _faunaAccumulatorSeconds -= FaunaTickSeconds;

                _floraAccumulatorSeconds += FaunaTickSeconds;
                if (_floraAccumulatorSeconds + 1e-9d >= FloraTickSeconds)
                {
                    StepFlora(_floraAccumulatorSeconds);
                    _floraAccumulatorSeconds = 0d;
                }
            }

            _presentation?.Sync(_faunaState, simulationDeltaSeconds);
        }

        private void Update()
        {
            if (!_initialized || _presentation == null || _time == null) return;
            float presentationDelta = _time.IsPaused
                ? 0f
                : UnityEngine.Time.unscaledDeltaTime * Mathf.Max(0f, _time.SimulationSpeed);
            _presentation.Tick(presentationDelta);
        }

        private void StepFauna(double simulationSeconds)
        {
            float deltaDays = (float)(simulationSeconds / EcologyRateDaySeconds);
            _movement.Step(_geography, _floraState, _faunaState, _faunaSettings, deltaDays);
        }

        private void StepFlora(double simulationSeconds)
        {
            float deltaDays = (float)(simulationSeconds / EcologyRateDaySeconds);
            FloraSimulator.Step(
                _floraPotential,
                _floraGenerationSettings,
                _floraState,
                _floraSimulationSettings,
                deltaDays);
            _movement.RefreshAllForage(_floraState);
            FloraStateChanged?.Invoke();
        }

        private void Shutdown()
        {
            if (_time != null) _time.SimulationStepRequested -= OnSimulationStepRequested;
            _presentation?.Dispose();
            _presentation = null;
            _movement?.Dispose();
            _movement = null;
            _initialized = false;
            FloraStateChanged = null;
            _time = null;
        }
    }
}
