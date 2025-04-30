namespace InfinityProject.Time
{
    /// <summary>
    /// Configuration for how fast in-game time passes relative to real time.
    /// </summary>
    public static class TimeConfig
    {
        /// <summary>
        /// Real seconds per full in-game day at normal (1×) speed.
        /// </summary>
        public const double DayLengthSeconds = 20 * 60.0;  // 20 minutes

        /// <summary>
        /// In-game days per in-game year.
        /// </summary>
        public const double DaysPerYear = 365.0;

        /// <summary>
        /// Base in-game years per real second at 1× speed.
        /// </summary>
        public const double BaseYearsPerSecond = DaysPerYear / DayLengthSeconds;

        // ---- Helper for the “5-minute” modes below ----

        /// <summary>
        /// Factor converting a 5-minute real interval into “normal” in-game days.
        /// Computed as (DayLengthSeconds) ÷ (5 minutes).
        /// </summary>
        private const double FiveMinuteDayFactor = DayLengthSeconds / (5 * 60.0);

        /// <summary>
        /// Multipliers of <see cref="BaseYearsPerSecond"/>, for each speed mode:
        /// [0]=Paused, [1]=1×, [2]=2×,
        /// [3]=1 week in 5 min, [4]=1 month in 5 min,
        /// [5]=1 year in 5 min, [6]=1 decade in 5 min, [7]=1 century in 5 min.
        /// </summary>
        public static readonly double[] YearScale = new[]
        {
            0.0,                         // Paused
            1.0,                         // 1×   normal
            2.0,                         // 2×   double speed
            7.0  * FiveMinuteDayFactor,  // 1 week per 5 min
            30.0 * FiveMinuteDayFactor,  // 1 month per 5 min
            365.0* FiveMinuteDayFactor,  // 1 year per 5 min
            3650.0*FiveMinuteDayFactor,  // 1 decade per 5 min
            36500.0*FiveMinuteDayFactor  // 1 century per 5 min
        };
    }
}