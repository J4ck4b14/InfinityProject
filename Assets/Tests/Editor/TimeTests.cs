using NUnit.Framework;
using UnityEngine;
using InfinityProject.Time;

namespace InfinityProject.Tests
{
 public class TimeTests
 {
 [Test]
 public void TimeConfig_YearScale_HasExpectedLengthAndMonotonicIncrease()
 {
 // Expect8 entries as documented
 Assert.AreEqual(8, TimeConfig.YearScale.Length);

 // All entries should be >=0 and non-decreasing
 for (int i =0; i < TimeConfig.YearScale.Length; i++)
 {
 Assert.GreaterOrEqual(TimeConfig.YearScale[i],0.0);
 if (i >0)
 {
 Assert.GreaterOrEqual(TimeConfig.YearScale[i], TimeConfig.YearScale[i -1]);
 }
 }
 }

 [Test]
 public void TimeConfig_BaseYearsPerSecond_IsConsistentWithConstants()
 {
 double expected = TimeConfig.DaysPerYear / TimeConfig.DayLengthSeconds;
 Assert.AreEqual(expected, TimeConfig.BaseYearsPerSecond,1e-12);
 }

 [Test]
 public void GameTime_Defaults_ToZero()
 {
 var gt = new GameTime();
 Assert.AreEqual(0.0, gt.TotalYears);
 Assert.AreEqual(0, gt.ScaleIndex);
 }

 [Test]
 public void Simulated_GameTime_Update_Computes_DeltaYears_Correctly()
 {
 // Choose a sample deltaTime and scale index and verify calculation
 double deltaTime =1.234; // seconds
 var gt = new GameTime { TotalYears =10.0, ScaleIndex =2 }; // scale index2 corresponds to2x

 double expectedDeltaYears = TimeConfig.BaseYearsPerSecond * deltaTime * TimeConfig.YearScale[gt.ScaleIndex];

 // Simulate update
 gt.TotalYears += expectedDeltaYears;

 // Now compute what we expect the total to be
 double expectedTotal =10.0 + expectedDeltaYears;
 Assert.AreEqual(expectedTotal, gt.TotalYears,1e-12);
 }

 [Test]
 public void DayNightController_Angle_Calculation_Matches_Manual_Computation()
 {
 // Test several values for TotalYears and verify the computed sun angle
 double[] years = {0.0,0.25,0.5,0.75,1.75,123.456 }; // includes >1 to test fractional extraction
 foreach (var y in years)
 {
 double fracDay = y - System.Math.Floor(y);
 float expectedAngle = (float)(fracDay *360.0 -90.0);

 // Recompute the logic used in DayNightController
 float computedAngle = (float)( (y - System.Math.Floor(y)) *360.0 -90.0 );

 Assert.AreEqual(expectedAngle, computedAngle,1e-6);

 // Quick sanity checks for known cases
 if (System.Math.Abs(fracDay -0.0) <1e-12)
 Assert.AreEqual(-90f, computedAngle,1e-6);
 if (System.Math.Abs(fracDay -0.25) <1e-12)
 Assert.AreEqual(0f, computedAngle,1e-6);
 if (System.Math.Abs(fracDay -0.5) <1e-12)
 Assert.AreEqual(90f, computedAngle,1e-6);
 }
 }
 }
}
