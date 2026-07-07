namespace InfinityProject.Time
{
    /// <summary>
    /// All time in the simulation is measured in IN-GAME SECONDS.
    ///
    /// Each frame: deltaGameSeconds = realDeltaTime * ScaleValues[ScaleIndex]
    ///
    /// Useful constants (in in-game seconds):
    ///   1 minute  =        60
    ///   1 hour    =     3 600
    ///   1 day     =    86 400
    ///   1 year    = 31 536 000
    ///   1 decade  = 315 360 000
    /// </summary>
    public static class TimeConfig
    {
        // ── Handy constants ───────────────────────────────────────────────────
        public const double SecondsPerMinute = 60.0;
        public const double SecondsPerHour   = 3_600.0;
        public const double SecondsPerDay    = 86_400.0;
        public const double SecondsPerYear   = 31_536_000.0;
        public const double SecondsPerDecade = 315_360_000.0;

        // ── Scale values: in-game seconds per real second ─────────────────────
        // [0] Slow      — 0.5  game-s per real-s (half speed)
        // [1] 1:1       — 1.0  game-s per real-s (real time)
        // [2] 5min/year — 5 real minutes compress 1 in-game year
        // [3] 5min/dec  — 5 real minutes compress 1 in-game decade
        // [4] 1min/year — 1 real minute  compresses 1 in-game year
        public static readonly double[] ScaleValues = new double[]
        {
            0.5,          // [0] Slow
            1.0,          // [1] 1:1
            105_120.0,    // [2] 5 min / year    (31_536_000 / 300)
            1_051_200.0,  // [3] 5 min / decade  (315_360_000 / 300)
            525_600.0,    // [4] 1 min / year    (31_536_000 / 60)
        };
    }
}
