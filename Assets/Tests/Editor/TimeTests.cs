using NUnit.Framework;
using InfinityProject.Time;

namespace InfinityProject.Tests
{
    public class TimeTests
    {
        [Test]
        public void TimeConfig_ScaleValues_HasExpectedLengthAndPositiveValues()
        {
            Assert.AreEqual(5, TimeConfig.ScaleValues.Length);

            for (int i = 0; i < TimeConfig.ScaleValues.Length; i++)
                Assert.Greater(TimeConfig.ScaleValues[i], 0.0,
                    $"ScaleValues[{i}] should be positive");
        }

        [Test]
        public void TimeConfig_ScaleValues_SlowIsLessThanRealTime()
        {
            // Slow (index 0) must be less than 1:1 (index 1)
            Assert.Less(TimeConfig.ScaleValues[0], TimeConfig.ScaleValues[1]);
        }

        [Test]
        public void TimeConfig_ScaleValues_FastModesAreGreaterThanRealTime()
        {
            // Indices 2-4 are fast-forward modes — all greater than 1:1
            for (int i = 2; i < TimeConfig.ScaleValues.Length; i++)
                Assert.Greater(TimeConfig.ScaleValues[i], TimeConfig.ScaleValues[1],
                    $"ScaleValues[{i}] should be greater than 1:1 (ScaleValues[1])");
        }

        [Test]
        public void TimeConfig_SecondsPerYear_IsCorrect()
        {
            // 365 * 24 * 3600
            Assert.AreEqual(31_536_000.0, TimeConfig.SecondsPerYear, 1e-6);
        }

        [Test]
        public void TimeConfig_5MinPerYear_ScaleIsCorrect()
        {
            // 5 real minutes should pass 1 in-game year worth of seconds
            // scale = SecondsPerYear / (5 * 60)
            double expected = TimeConfig.SecondsPerYear / (5 * 60.0);
            Assert.AreEqual(expected, TimeConfig.ScaleValues[2], 1e-6);
        }

        [Test]
        public void GameTime_Defaults_ToZero()
        {
            var gt = new GameTime();
            Assert.AreEqual(0.0, gt.TotalSeconds);
            Assert.AreEqual(0, gt.ScaleIndex);
        }

        [Test]
        public void GameTime_Update_AccumulatesDeltaSeconds_Correctly()
        {
            double realDelta  = 1.234;
            var    gt         = new GameTime { TotalSeconds = 100.0, ScaleIndex = 1 };
            double expected   = realDelta * TimeConfig.ScaleValues[gt.ScaleIndex];

            gt.TotalSeconds  += expected;

            Assert.AreEqual(100.0 + expected, gt.TotalSeconds, 1e-9);
        }

        [Test]
        public void GameTime_FastMode_AccumulatesMoreThanRealTime()
        {
            double realDelta = 1.0; // 1 real second
            var    gtFast    = new GameTime { TotalSeconds = 0.0, ScaleIndex = 2 }; // 5min/year
            var    gtReal    = new GameTime { TotalSeconds = 0.0, ScaleIndex = 1 }; // 1:1

            gtFast.TotalSeconds += realDelta * TimeConfig.ScaleValues[2];
            gtReal.TotalSeconds += realDelta * TimeConfig.ScaleValues[1];

            Assert.Greater(gtFast.TotalSeconds, gtReal.TotalSeconds,
                "Fast mode should accumulate more game-seconds than 1:1 in the same real time");
        }
    }
}
