using InfinityProject.World.Timekeeping;
using NUnit.Framework;
using UnityEngine;

public sealed class TimeV1Tests
{
    [Test]
    public void SimulationClock_ScalesRealTime()
    {
        var clock = new SimulationClock();
        clock.SetSpeed(4d);

        double delta = clock.Tick(0.5d);

        Assert.AreEqual(2d, delta, 1e-9);
        Assert.AreEqual(2d, clock.ElapsedSeconds, 1e-9);
    }

    [Test]
    public void SimulationClock_PauseStopsSimulationWithoutLosingSpeed()
    {
        var clock = new SimulationClock();
        clock.SetSpeed(8d);
        clock.SetPaused(true);

        Assert.AreEqual(0d, clock.Tick(1d), 1e-9);
        Assert.AreEqual(0d, clock.ElapsedSeconds, 1e-9);

        clock.SetPaused(false);
        Assert.AreEqual(8d, clock.Tick(1d), 1e-9);
    }

    [Test]
    public void WorldClock_MapsConfiguredSimulationDurationToOneDay()
    {
        var clock = new WorldClock();
        clock.SetDayDurationSeconds(20d * 60d);
        clock.Reset(0, 6d);

        clock.Advance(20d * 60d);

        Assert.AreEqual(1L, clock.DayIndex);
        Assert.AreEqual(6d, clock.Hour, 1e-9);
    }

    [Test]
    public void WorldClock_DayLengthChangeDoesNotRewriteCurrentWorldTime()
    {
        var clock = new WorldClock();
        clock.SetDayDurationSeconds(1200d);
        clock.Reset(2, 12d);
        clock.Advance(300d);

        double before = clock.TotalDays;
        clock.SetDayDurationSeconds(2400d);

        Assert.AreEqual(before, clock.TotalDays, 1e-12);
        clock.Advance(600d);
        Assert.AreEqual(before + 0.25d, clock.TotalDays, 1e-12);
    }

    [Test]
    public void WorldClock_DoesNotAffectSimulationClockRate()
    {
        var simulation = new SimulationClock();
        var shortDay = new WorldClock();
        var longDay = new WorldClock();
        shortDay.SetDayDurationSeconds(600d);
        longDay.SetDayDurationSeconds(2400d);

        double delta = simulation.Tick(5d);
        shortDay.Advance(delta);
        longDay.Advance(delta);

        Assert.AreEqual(5d, simulation.ElapsedSeconds, 1e-9);
        Assert.Greater(shortDay.TotalDays, longDay.TotalDays);
    }

    [Test]
    public void TimeController_RequestsSimulationBeforeCommittingClocks()
    {
        GameObject go = new("Time test");
        try
        {
            InfinityTimeController controller = go.AddComponent<InfinityTimeController>();
            controller.Initialize();

            double elapsedWhenRequested = -1d;
            double requested = -1d;
            controller.SimulationStepRequested += delta =>
            {
                requested = delta;
                elapsedWhenRequested = controller.Simulation.ElapsedSeconds;
            };

            controller.AdvanceSimulationSeconds(5d);

            Assert.AreEqual(5d, requested, 1e-9);
            Assert.AreEqual(0d, elapsedWhenRequested, 1e-9);
            Assert.AreEqual(5d, controller.Simulation.ElapsedSeconds, 1e-9);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
