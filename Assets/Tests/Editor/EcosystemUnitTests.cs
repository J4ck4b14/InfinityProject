using NUnit.Framework;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using InfinityProject.Time;
using System;
using System.Threading;
using System.Diagnostics;
using Unity.Mathematics;

namespace InfinityProject.Tests
{
    public class EcosystemUnitTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        World CreateTestWorld(string name = "TestWorld")
        {
            var original = World.DefaultGameObjectInjectionWorld;
            var w = new World(name);
            World.DefaultGameObjectInjectionWorld = w;
            return w;
        }

        // Creates a GameTime singleton using TotalSeconds (new API)
        Entity CreateGameTime(EntityManager em, byte scaleIndex = 1)
        {
            var e = em.CreateEntity(typeof(GameTime));
            em.SetComponentData(e, new GameTime { TotalSeconds = 0.0, ScaleIndex = scaleIndex });
            return e;
        }

        bool WaitFor(Func<bool> condition, World world, int timeoutMs = 1000, int sleepMs = 10)
        {
            var em       = world.EntityManager;
            var beginSim = world.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            var sw       = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                em.CompleteAllTrackedJobs();
                if (condition()) return true;

                beginSim?.Update();
                em.CompleteAllTrackedJobs();
                if (condition()) return true;

                world.Update();
                em.CompleteAllTrackedJobs();
                if (condition()) return true;

                Thread.Sleep(sleepMs);
            }
            return false;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void Reproduction_CreatesChild()
        {
            var original = World.DefaultGameObjectInjectionWorld;
            World world  = null;
            try
            {
                world = CreateTestWorld("ReproCreateWorld");
                var em = world.EntityManager;
                CreateGameTime(em);

                var beginSim     = world.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
                var reproduction = world.GetOrCreateSystemManaged<AnimalReproductionSystem>();

                var prefab = em.CreateEntity(typeof(Lifespan), typeof(HealthDamage), typeof(LocalTransform));
                em.SetComponentData(prefab, new Lifespan    { MinAge = 0f, MaxAge = 10f, Age = 0f, IsLegendary = false });
                em.SetComponentData(prefab, new HealthDamage{ CurrentHealth = 100f, MaxHealth = 100f });
                em.SetComponentData(prefab, LocalTransform.Identity);

                var parent = em.CreateEntity(
                    typeof(ReproductionData), typeof(HealthDamage),
                    typeof(Lifespan), typeof(PrefabRef), typeof(LocalTransform));

                em.SetComponentData(parent, new ReproductionData
                {
                    ReproductionTimer = 0f,
                    FertilityWindow   = new float2(0f, 10f)
                });
                em.SetComponentData(parent, new PrefabRef    { Prefab = prefab });
                em.SetComponentData(parent, new HealthDamage { CurrentHealth = 100f, MaxHealth = 100f });
                em.SetComponentData(parent, new Lifespan     { MinAge = 0f, MaxAge = 10f, Age = 1f, IsLegendary = false });
                em.SetComponentData(parent, LocalTransform.Identity);

                var q      = em.CreateEntityQuery(typeof(Lifespan));
                int before = q.CalculateEntityCount();

                TimeTestShim.OverrideDeltaSeconds = 1f;
                reproduction.Update();
                em.CompleteAllTrackedJobs();
                beginSim.Update();
                em.CompleteAllTrackedJobs();
                TimeTestShim.OverrideDeltaSeconds = 0f;

                bool created = WaitFor(() => q.CalculateEntityCount() > before, world, 2000);
                Assert.IsTrue(created,
                    $"Reproduction did not create a child (before={before}, after={q.CalculateEntityCount()}).");
            }
            finally
            {
                TimeTestShim.OverrideDeltaSeconds = 0f;
                world?.Dispose();
                World.DefaultGameObjectInjectionWorld = original;
            }
        }

        [Test]
        public void Reproduction_AdvancesParentTimer()
        {
            var original = World.DefaultGameObjectInjectionWorld;
            World world  = null;
            try
            {
                world = CreateTestWorld("ReproTimerWorld");
                var em = world.EntityManager;
                CreateGameTime(em);

                var beginSim     = world.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
                var reproduction = world.GetOrCreateSystemManaged<AnimalReproductionSystem>();

                var prefab = em.CreateEntity(typeof(Lifespan), typeof(HealthDamage), typeof(LocalTransform));
                em.SetComponentData(prefab, new Lifespan    { MinAge = 0f, MaxAge = 10f, Age = 0f });
                em.SetComponentData(prefab, LocalTransform.Identity);

                var parent = em.CreateEntity(
                    typeof(ReproductionData), typeof(PrefabRef),
                    typeof(HealthDamage), typeof(LocalTransform), typeof(Lifespan));

                em.SetComponentData(parent, new ReproductionData
                {
                    ReproductionTimer = 0f,
                    FertilityWindow   = new float2(0f, 10f)
                });
                em.SetComponentData(parent, new PrefabRef    { Prefab = prefab });
                em.SetComponentData(parent, new HealthDamage { CurrentHealth = 100f, MaxHealth = 100f });
                em.SetComponentData(parent, new Lifespan     { MinAge = 0f, MaxAge = 10f, Age = 1f });
                em.SetComponentData(parent, LocalTransform.Identity);

                TimeTestShim.OverrideDeltaSeconds = 1f;
                reproduction.Update();
                em.CompleteAllTrackedJobs();
                beginSim.Update();
                em.CompleteAllTrackedJobs();
                TimeTestShim.OverrideDeltaSeconds = 0f;

                bool ok = WaitFor(() =>
                    em.GetComponentData<ReproductionData>(parent).ReproductionTimer > 0f,
                    world, 2000);

                var final = em.GetComponentData<ReproductionData>(parent);
                Assert.IsTrue(ok,
                    $"Parent reproduction timer did not advance (final={final.ReproductionTimer}).");
            }
            finally
            {
                TimeTestShim.OverrideDeltaSeconds = 0f;
                world?.Dispose();
                World.DefaultGameObjectInjectionWorld = original;
            }
        }

        [Test]
        public void Aging_IncrementsAge_ForNonLegendary()
        {
            var original = World.DefaultGameObjectInjectionWorld;
            World world  = null;
            try
            {
                world = CreateTestWorld("AgingWorld");
                var em = world.EntityManager;
                CreateGameTime(em);

                var aging = world.GetOrCreateSystemManaged<AnimalAgingSystem>();

                var e = em.CreateEntity(typeof(Lifespan));
                em.SetComponentData(e, new Lifespan { MinAge = 0f, MaxAge = 10f, Age = 2f, IsLegendary = false });

                TimeTestShim.OverrideDeltaSeconds = 1f;
                aging.Update();
                em.CompleteAllTrackedJobs();
                TimeTestShim.OverrideDeltaSeconds = 0f;

                var lf = em.GetComponentData<Lifespan>(e);
                Assert.Greater(lf.Age, 2f, "Age should increase for non-legendary entities.");
            }
            finally
            {
                TimeTestShim.OverrideDeltaSeconds = 0f;
                world?.Dispose();
                World.DefaultGameObjectInjectionWorld = original;
            }
        }

        [Test]
        public void Aging_DoesNotIncrement_ForLegendary()
        {
            var original = World.DefaultGameObjectInjectionWorld;
            World world  = null;
            try
            {
                world = CreateTestWorld("LegendaryAgingWorld");
                var em = world.EntityManager;
                CreateGameTime(em);

                var aging = world.GetOrCreateSystemManaged<AnimalAgingSystem>();

                var e = em.CreateEntity(typeof(Lifespan));
                em.SetComponentData(e, new Lifespan { MinAge = 0f, MaxAge = 10f, Age = 2f, IsLegendary = true });

                TimeTestShim.OverrideDeltaSeconds = 1f;
                aging.Update();
                em.CompleteAllTrackedJobs();
                TimeTestShim.OverrideDeltaSeconds = 0f;

                var lf = em.GetComponentData<Lifespan>(e);
                Assert.AreEqual(2f, lf.Age, 1e-6f, "Legendary entities should not age.");
            }
            finally
            {
                TimeTestShim.OverrideDeltaSeconds = 0f;
                world?.Dispose();
                World.DefaultGameObjectInjectionWorld = original;
            }
        }

        [Test]
        public void MemorySystem_DecaysOrPrunesEvents()
        {
            var original = World.DefaultGameObjectInjectionWorld;
            World world  = null;
            try
            {
                world = CreateTestWorld("MemoryWorld");
                var em = world.EntityManager;

                var memory = world.GetOrCreateSystemManaged<MemorySystem>();

                var id = em.CreateEntity(typeof(Identity));
                em.SetComponentData(id, new Identity { NameHash = 1, Age = 30f, SocialRank = 1 });
                var buf = em.AddBuffer<MemoryEvent>(id);
                buf.Add(new MemoryEvent
                {
                    Type            = EventType.Aid,
                    Target          = Entity.Null,
                    Timestamp       = 0.0,
                    EmotionalWeight = 1f,
                    Magnitude       = 0.5f
                });

                TimeTestShim.OverrideDeltaSeconds = 1f;
                memory.Update();
                em.CompleteAllTrackedJobs();
                TimeTestShim.OverrideDeltaSeconds = 0f;

                var afterBuf = em.GetBuffer<MemoryEvent>(id);
                Assert.LessOrEqual(afterBuf.Length, 1,
                    "MemorySystem should prune or keep the same number of events.");
                if (afterBuf.Length > 0)
                    Assert.Less(afterBuf[0].EmotionalWeight, 1f,
                        "EmotionalWeight should decay over time.");
            }
            finally
            {
                TimeTestShim.OverrideDeltaSeconds = 0f;
                world?.Dispose();
                World.DefaultGameObjectInjectionWorld = original;
            }
        }
    }
}
