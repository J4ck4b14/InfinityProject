using System;
using UnityEngine;

namespace InfinityProject.World.Timekeeping
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Infinity/World/Time Controller")]
    public sealed class InfinityTimeController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _simulationSpeed = 1f;
        [SerializeField, Min(0.01f)] private float _worldDayDurationMinutes = 20f;
        [SerializeField, Range(0f, 23.999f)] private float _startingWorldHour = 6f;
        [SerializeField] private bool _startPaused;

        private readonly SimulationClock _simulationClock = new();
        private readonly WorldClock _worldClock = new();
        private bool _initialized;

        public event Action<double> SimulationStepRequested;

        public SimulationClock Simulation => _simulationClock;
        public WorldClock World => _worldClock;
        public float SimulationSpeed => _simulationSpeed;
        public float WorldDayDurationMinutes => _worldDayDurationMinutes;
        public float StartingWorldHour => _startingWorldHour;
        public bool StartPaused => _startPaused;
        public bool IsPaused => _simulationClock.IsPaused;

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            AdvanceRealSeconds(UnityEngine.Time.unscaledDeltaTime);
        }

        public void Initialize()
        {
            _simulationClock.Reset();
            _simulationClock.SetSpeed(_simulationSpeed);
            _simulationClock.SetPaused(_startPaused);

            _worldClock.SetDayDurationSeconds(_worldDayDurationMinutes * 60d);
            _worldClock.Reset(0L, _startingWorldHour);
            _initialized = true;
        }

        public double AdvanceRealSeconds(double realDeltaSeconds)
        {
            EnsureInitialized();
            if (realDeltaSeconds < 0d || double.IsNaN(realDeltaSeconds) || double.IsInfinity(realDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds));

            double simulationDelta = _simulationClock.IsPaused
                ? 0d
                : realDeltaSeconds * _simulationClock.Speed;

            AdvanceSimulationDelta(simulationDelta);
            return simulationDelta;
        }

        public void AdvanceSimulationSeconds(double simulationDeltaSeconds)
        {
            EnsureInitialized();
            if (simulationDeltaSeconds < 0d || double.IsNaN(simulationDeltaSeconds) || double.IsInfinity(simulationDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(simulationDeltaSeconds));

            AdvanceSimulationDelta(simulationDeltaSeconds);
        }

        private void AdvanceSimulationDelta(double simulationDeltaSeconds)
        {
            if (simulationDeltaSeconds > 0d)
                SimulationStepRequested?.Invoke(simulationDeltaSeconds);

            _simulationClock.Advance(simulationDeltaSeconds);
            _worldClock.Advance(simulationDeltaSeconds);
        }

        public void SetSimulationSpeed(float speed)
        {
            _simulationSpeed = Mathf.Max(0f, speed);
            _simulationClock.SetSpeed(_simulationSpeed);
        }

        public void SetWorldDayDurationMinutes(float minutes)
        {
            _worldDayDurationMinutes = Mathf.Max(0.01f, minutes);
            _worldClock.SetDayDurationSeconds(_worldDayDurationMinutes * 60d);
        }

        public void SetStartingWorldHour(float hour)
        {
            _startingWorldHour = Mathf.Clamp(hour, 0f, 23.999f);
        }

        public void SetStartPaused(bool paused)
        {
            _startPaused = paused;
            if (_initialized) _simulationClock.SetPaused(paused);
        }

        public void SetPaused(bool paused)
        {
            EnsureInitialized();
            _simulationClock.SetPaused(paused);
        }

        private void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        private void OnValidate()
        {
            _simulationSpeed = Mathf.Max(0f, _simulationSpeed);
            _worldDayDurationMinutes = Mathf.Max(0.01f, _worldDayDurationMinutes);
            _startingWorldHour = Mathf.Clamp(_startingWorldHour, 0f, 23.999f);

            if (!_initialized) return;
            _simulationClock.SetSpeed(_simulationSpeed);
            _worldClock.SetDayDurationSeconds(_worldDayDurationMinutes * 60d);
        }
    }
}
