using System;

namespace InfinityProject.World.Timekeeping
{
    public sealed class WorldClock
    {
        private double _secondsPerDay = 1200d;
        private double _totalDays;

        public double SecondsPerDay => _secondsPerDay;
        public double TotalDays => _totalDays;
        public long DayIndex => (long)Math.Floor(_totalDays);
        public double TimeOfDay01 => _totalDays - Math.Floor(_totalDays);
        public double Hour => TimeOfDay01 * 24d;

        public void SetDayDurationSeconds(double secondsPerDay)
        {
            if (secondsPerDay <= 0d || double.IsNaN(secondsPerDay) || double.IsInfinity(secondsPerDay))
                throw new ArgumentOutOfRangeException(nameof(secondsPerDay));

            _secondsPerDay = secondsPerDay;
        }

        public void Advance(double simulationDeltaSeconds)
        {
            if (simulationDeltaSeconds < 0d || double.IsNaN(simulationDeltaSeconds) || double.IsInfinity(simulationDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(simulationDeltaSeconds));

            _totalDays += simulationDeltaSeconds / _secondsPerDay;
        }

        public void Reset(long dayIndex = 0L, double hour = 0d)
        {
            if (dayIndex < 0L) throw new ArgumentOutOfRangeException(nameof(dayIndex));
            if (hour < 0d || hour >= 24d || double.IsNaN(hour) || double.IsInfinity(hour))
                throw new ArgumentOutOfRangeException(nameof(hour));

            _totalDays = dayIndex + hour / 24d;
        }
    }
}
