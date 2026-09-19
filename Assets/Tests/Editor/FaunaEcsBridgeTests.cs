using InfinityProject.World.Fauna;
using InfinityProject.World.Fauna.ECS;
using NUnit.Framework;
using UnityEngine;

public sealed class FaunaEcsBridgeTests
{
    [Test]
    public void EcsBridge_MirrorsValidatedFaunaStateLosslessly()
    {
        var agents = new[]
        {
            new FaunaAgentState
            {
                Id = 17,
                HerdId = 3,
                PositionLocalMeters = new Vector2(12.5f, 44.25f),
                Heading = new Vector2(0.6f, 0.8f),
                Hunger01 = 0.42f,
                Energy01 = 0.71f,
                Health01 = 0.93f,
                CumulativeFoodConsumedKg = 18.5f,
                Alive = true
            }
        };
        var fauna = new FaunaSimulationData("deer", "Deer", 500, 2000f, 2000f, 110d, agents);

        using var bridge = new FaunaEcsSnapshotBridge("Fauna ECS bridge test");
        bridge.Sync(fauna);

        Assert.AreEqual(1, bridge.EntityCount);
        Assert.IsTrue(bridge.TryReadAgent(17, out FaunaEcsAgentSnapshot ecs));
        Assert.AreEqual(17, ecs.Identity.AgentId);
        Assert.AreEqual(3, ecs.Identity.HerdId);
        Assert.AreEqual(12.5f, ecs.Position.Meters.x, 0.0001f);
        Assert.AreEqual(44.25f, ecs.Position.Meters.y, 0.0001f);
        Assert.AreEqual(0.6f, ecs.Heading.Value.x, 0.0001f);
        Assert.AreEqual(0.8f, ecs.Heading.Value.y, 0.0001f);
        Assert.AreEqual(0.42f, ecs.Physiology.Hunger01, 0.0001f);
        Assert.AreEqual(0.71f, ecs.Physiology.Energy01, 0.0001f);
        Assert.AreEqual(0.93f, ecs.Physiology.Health01, 0.0001f);
        Assert.AreEqual(18.5f, ecs.Physiology.CumulativeFoodConsumedKg, 0.0001f);
        Assert.AreEqual(1, ecs.Physiology.Alive);
    }

    [Test]
    public void EcsBridge_PreservesEntityIdentityAcrossSnapshotUpdates()
    {
        var agents = new[]
        {
            new FaunaAgentState
            {
                Id = 4,
                HerdId = 0,
                PositionLocalMeters = new Vector2(10f, 20f),
                Heading = Vector2.right,
                Hunger01 = 0.1f,
                Energy01 = 0.9f,
                Health01 = 1f,
                Alive = true
            }
        };
        var fauna = new FaunaSimulationData("deer", "Deer", 500, 2000f, 2000f, 0d, agents);

        using var bridge = new FaunaEcsSnapshotBridge("Fauna ECS identity test");
        bridge.Sync(fauna);
        Assert.IsTrue(bridge.TryGetEntity(4, out var before));

        fauna.Agents[0].PositionLocalMeters = new Vector2(90f, 140f);
        fauna.Agents[0].Hunger01 = 0.8f;
        bridge.Sync(fauna);
        Assert.IsTrue(bridge.TryGetEntity(4, out var after));

        Assert.AreEqual(before, after, "A stable deer ID must keep the same ECS entity across state refreshes.");
        Assert.IsTrue(bridge.TryReadAgent(4, out FaunaEcsAgentSnapshot ecs));
        Assert.AreEqual(90f, ecs.Position.Meters.x, 0.0001f);
        Assert.AreEqual(140f, ecs.Position.Meters.y, 0.0001f);
        Assert.AreEqual(0.8f, ecs.Physiology.Hunger01, 0.0001f);
    }
}
