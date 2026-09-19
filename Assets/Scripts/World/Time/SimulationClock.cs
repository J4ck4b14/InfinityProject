using System;

namespace InfinityProject.World.Timekeeping
{
    public sealed class SimulationClock
    {
        private double _elapsedSeconds;
        private double _deltaSeconds;
        private double _speed = 1d;
        private bool _paused;

        public double ElapsedSeconds => _elapsedSeconds;
        public double DeltaSeconds => _deltaSeconds;
        public double Speed => _speed;
        public bool IsPaused => _paused;

        public void SetSpeed(double speed)
        {
            ValidateNonNegativeFinite(speed, nameof(speed));
            _speed = speed;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (paused) _deltaSeconds = 0d;
        }

        public double Tick(double realDeltaSeconds)
        {
            ValidateNonNegativeFinite(realDeltaSeconds, nameof(realDeltaSeconds));

            double delta = _paused ? 0d : realDeltaSeconds * _speed;
            Advance(delta);
            return delta;
        }

        public void Advance(double simulationDeltaSeconds)
        {
            ValidateNonNegativeFinite(simulationDeltaSeconds, nameof(simulationDeltaSeconds));
            _deltaSeconds = simulationDeltaSeconds;
            _elapsedSeconds += simulationDeltaSeconds;
        }

        public void Reset(double elapsedSeconds = 0d)
        {
            ValidateNonNegativeFinite(elapsedSeconds, nameof(elapsedSeconds));
            _elapsedSeconds = elapsedSeconds;
            _deltaSeconds = 0d;
        }

        private static void ValidateNonNegativeFinite(double value, string parameterName)
        {
            if (value < 0d || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
