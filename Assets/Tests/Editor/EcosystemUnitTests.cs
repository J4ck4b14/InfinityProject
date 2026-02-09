using NUnit.Framework;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using InfinityProject.Time;
using System;
using System.Threading;
using System.Diagnostics;
using Unity.Mathematics;

namespace InfinityProject.Tests
{
 public class EcosystemUnitTests
 {
 World CreateTestWorld(string name = "TestWorld")
 {
 var original = World.DefaultGameObjectInjectionWorld;
 var w = new World(name);
 World.DefaultGameObjectInjectionWorld = w;
 return w;
 }

 void FinishWork(World world)
 {
 var em = world.EntityManager;
 world.Update();
 em.CompleteAllTrackedJobs();
 world.Update();
 }

 bool WaitFor(Func<bool> condition, World world, int timeoutMs =1000, int sleepMs =10)
 {
 var em = world.EntityManager;
 var sw = Stopwatch.StartNew();
 var beginSim = world.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>();
 while (sw.ElapsedMilliseconds < timeoutMs)
 {
 // Ensure any scheduled jobs are completed and ECBs applied, then run a world step
 em.CompleteAllTrackedJobs();
 if (condition()) return true;

 // Run BeginSimulation ECB playback to apply command buffers created by systems
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

 [Test]
 public void Reproduction_CreatesChild()
 {
 var original = World.DefaultGameObjectInjectionWorld;
 World world = null;
 try
 {
 world = CreateTestWorld("ReproCreateWorld");
 var em = world.EntityManager;

 // GameTime singleton
 var gt = em.CreateEntity(typeof(GameTime));
 em.SetComponentData(gt, new GameTime { TotalYears =0.0, ScaleIndex =1 });

 // Ensure systems exist and get reproduction system instance
 var beginSim = world.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
 var reproduction = world.GetOrCreateSystemManaged<AnimalReproductionSystem>();

 // Prefab entity
 var prefab = em.CreateEntity(typeof(Lifespan), typeof(HealthDamage), typeof(LocalTransform));
 em.SetComponentData(prefab, new Lifespan { MinAge =0f, MaxAge =10f, Age =0f, IsLegendary = false });
 em.SetComponentData(prefab, new HealthDamage { CurrentHealth =100f, MaxHealth =100f, MinDamage =0f, MaxDamage =0f });
 em.SetComponentData(prefab, new LocalTransform { Position = new float3(0f,0f,0f), Rotation = quaternion.identity, Scale =1f });

 // Parent configured to reproduce immediately
 var parent = em.CreateEntity(typeof(ReproductionData), typeof(HealthDamage), typeof(Lifespan), typeof(PrefabRef), typeof(LocalTransform));
 em.SetComponentData(parent, new ReproductionData { ReproductionTimer =0f, FertilityWindow = new Unity.Mathematics.float2(0f,10f) });
 em.SetComponentData(parent, new PrefabRef { Prefab = prefab });
 em.SetComponentData(parent, new HealthDamage { CurrentHealth =100f, MaxHealth =100f, MinDamage =0f, MaxDamage =0f });
 em.SetComponentData(parent, new LocalTransform { Position = new float3(0f,0f,0f), Rotation = quaternion.identity, Scale =1f });

 var q = em.CreateEntityQuery(typeof(Lifespan));
 int before = q.CalculateEntityCount();

 // Run reproduction system synchronously, then allow ECB playback
 reproduction.Update();
 em.CompleteAllTrackedJobs();

 // Force the BeginSimulation ECB system to run so instantiation is applied
 beginSim.Update();
 em.CompleteAllTrackedJobs();
 world.Update();
 em.CompleteAllTrackedJobs();

 // Wait for child creation as a robust check
 bool created = WaitFor(() => q.CalculateEntityCount() > before, world,2000);

 int after = q.CalculateEntityCount();
 Assert.IsTrue(created, $"Reproduction did not create a child in time (before={before}, after={after}).");
 }
 finally
 {
 if (world != null)
 {
 world.Dispose();
 }
 World.DefaultGameObjectInjectionWorld = original;
 }
 }

 [Test]
 public void Reproduction_AdvancesParentTimer()
 {
 var original = World.DefaultGameObjectInjectionWorld;
 World world = null;
 try
 {
 world = CreateTestWorld("ReproTimerWorld");
 var em = world.EntityManager;

 var gt = em.CreateEntity(typeof(GameTime));
 em.SetComponentData(gt, new GameTime { TotalYears =0.0, ScaleIndex =1 });

 var beginSim = world.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
 var reproduction = world.GetOrCreateSystemManaged<AnimalReproductionSystem>();

 var prefab = em.CreateEntity(typeof(Lifespan), typeof(HealthDamage), typeof(LocalTransform));
 em.SetComponentData(prefab, new Lifespan { MinAge =0f, MaxAge =10f, Age =0f, IsLegendary = false });
 em.SetComponentData(prefab, new LocalTransform { Position = new float3(0f,0f,0f), Rotation = quaternion.identity, Scale =1f });

 var parent = em.CreateEntity(typeof(ReproductionData), typeof(PrefabRef), typeof(HealthDamage), typeof(LocalTransform), typeof(Lifespan));
 em.SetComponentData(parent, new ReproductionData { ReproductionTimer =0f, FertilityWindow = new Unity.Mathematics.float2(0f,10f) });
 em.SetComponentData(parent, new PrefabRef { Prefab = prefab });
 em.SetComponentData(parent, new HealthDamage { CurrentHealth =100f, MaxHealth =100f, MinDamage =0f, MaxDamage =0f });
 em.SetComponentData(parent, new LocalTransform { Position = new float3(0f,0f,0f), Rotation = quaternion.identity, Scale =1f });
 em.SetComponentData(parent, new Lifespan { MinAge =0f, MaxAge =10f, Age =1f, IsLegendary = false });

 // Run reproduction synchronously
 reproduction.Update();
 em.CompleteAllTrackedJobs();
 beginSim.Update();
 em.CompleteAllTrackedJobs();
 world.Update();
 em.CompleteAllTrackedJobs();

 // Wait until parent timer is advanced, with timeout
 bool ok = WaitFor(() =>
 {
 var r = em.GetComponentData<ReproductionData>(parent);
 return r.ReproductionTimer >0f;
 }, world,2000);

 var final = em.GetComponentData<ReproductionData>(parent);
 Assert.IsTrue(ok, $"Parent reproduction timer did not advance in time (final value = {final.ReproductionTimer}).");
 }
 finally
 {
 if (world != null) world.Dispose();
 World.DefaultGameObjectInjectionWorld = original;
 }
 }

 [Test]
 public void Aging_IncrementsAge_ForNonLegendary()
 {
 var original = World.DefaultGameObjectInjectionWorld;
 World world = null;
 try
 {
 world = CreateTestWorld("AgingWorld");
 var em = world.EntityManager;

 var gt = em.CreateEntity(typeof(GameTime));
 em.SetComponentData(gt, new GameTime { TotalYears =0.0, ScaleIndex =1 });

 var aging = world.GetOrCreateSystemManaged<AnimalAgingSystem>();

 var e = em.CreateEntity(typeof(Lifespan));
 em.SetComponentData(e, new Lifespan { MinAge =0f, MaxAge =10f, Age =2f, IsLegendary = false });

 // Use test shim to force a non-zero delta so aging advances
 TimeTestShim.OverrideDeltaSeconds =1f;

 // Run aging system synchronously
 aging.Update();
 em.CompleteAllTrackedJobs();

 // Clear test shim
 TimeTestShim.OverrideDeltaSeconds =0f;

 var lf = em.GetComponentData<Lifespan>(e);
 Assert.Greater(lf.Age,2f, "Aging system should increase Age for non-legendary entities.");
 }
 finally
 {
 if (world != null) world.Dispose();
 World.DefaultGameObjectInjectionWorld = original;
 }
 }

 [Test]
 public void MemorySystem_DecaysOrPrunesEvents()
 {
 var original = World.DefaultGameObjectInjectionWorld;
 World world = null;
 try
 {
 world = CreateTestWorld("MemoryWorld");
 var em = world.EntityManager;

 var memory = world.GetOrCreateSystemManaged<MemorySystem>();

 var id = em.CreateEntity(typeof(Identity));
 em.SetComponentData(id, new Identity { NameHash =1, Age =30f, SocialRank =1 });
 var buf = em.AddBuffer<MemoryEvent>(id);
 buf.Add(new MemoryEvent { Type = EventType.Aid, Target = Entity.Null, Timestamp =0.0, EmotionalWeight =1f, Magnitude =0.5f });

 // Use test shim to force a non-zero delta so decay happens
 TimeTestShim.OverrideDeltaSeconds =1f;

 // Run memory system synchronously
 memory.Update();
 em.CompleteAllTrackedJobs();

 // Clear test shim
 TimeTestShim.OverrideDeltaSeconds =0f;

 var afterBuf = em.GetBuffer<MemoryEvent>(id);
 Assert.LessOrEqual(afterBuf.Length,1, "MemorySystem should prune or keep same number of events.");
 if (afterBuf.Length >0)
 Assert.Less(afterBuf[0].EmotionalWeight,1f, "EmotionalWeight should decay over time.");
 }
 finally
 {
 if (world != null) world.Dispose();
 World.DefaultGameObjectInjectionWorld = original;
 }
 }
 }
}
