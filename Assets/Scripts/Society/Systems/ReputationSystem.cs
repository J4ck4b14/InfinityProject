using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Consumes one‐shot LocalReputationDelta components,
/// propagates them fractally via SocialBridge buffers
/// (using α for first hop, β for further hops),
/// and applies per‐frame exponential decay (λ).
/// Implements mouth-to-mouth multi-hop propagation with hop-limit and edge-cap.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class ReputationSystem : SystemBase
{
    private EntityCommandBufferSystem _ecbSystem;

    // Tunable limits to control cost of propagation
    private const int MaxHops = 4; // include first-hop (1) .. MaxHops
    private const int MaxProcessedEdges = 200000; // safety cap per frame

    protected override void OnCreate()
    {
        base.OnCreate();
        _ecbSystem = World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();

        // Collect deltas into a NativeList
        var localDeltas = new NativeList<LocalDelta>(Allocator.TempJob);

        var collectHandle = Entities
            .WithName("CollectLocalReputationDeltas")
            .ForEach((Entity e, in LocalReputationDelta lrd) =>
            {
                localDeltas.Add(new LocalDelta { Source = e, Delta = lrd.DeltaR });
            })
            .ScheduleParallel(Dependency);

        // Ensure collection completes before processing propagation
        collectHandle.Complete();

        // Clear LocalReputationDelta by removing the component via ECB in a job
        var clearHandle = Entities
            .WithName("ClearLocalReputationDeltas")
            .ForEach((Entity e, int entityInQueryIndex) =>
            {
                ecb.RemoveComponent<LocalReputationDelta>(entityInQueryIndex, e);
            })
            .ScheduleParallel(Dependency);

        _ecbSystem.AddJobHandleForProducer(clearHandle);
        clearHandle.Complete();

        // If no deltas, just run decay and exit
        if (localDeltas.Length == 0)
        {
            localDeltas.Dispose();
            // Decay pass
            float decay = math.exp(-ReputationConfig.Lambda * deltaTime);
            Entities
                .WithName("DecayReputationScores")
                .ForEach((ref DynamicBuffer<SocialBridge> buf) =>
                {
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var b = buf[i];
                        b.ReputationScore = MultiplyProfile(b.ReputationScore, decay);
                        buf[i] = b;
                    }
                })
                .ScheduleParallel();

            return;
        }

        // Prepare frontier: start with the original sources as nodes carrying their DeltaR payload
        var frontierMap = new NativeParallelHashMap<Entity, EthicalProfile>(localDeltas.Length * 2, Allocator.Temp);
        var nextMap = new NativeParallelHashMap<Entity, EthicalProfile>(localDeltas.Length * 2, Allocator.Temp);
        var visited = new NativeParallelHashMap<Entity, byte>(localDeltas.Length * 2, Allocator.Temp);

        // Seed frontierMap with sources
        for (int i = 0; i < localDeltas.Length; i++)
        {
            var s = localDeltas[i];
            if (frontierMap.TryGetValue(s.Source, out var existing))
            {
                frontierMap[s.Source] = AddProfiles(existing, s.Delta);
            }
            else
            {
                frontierMap.TryAdd(s.Source, s.Delta);
            }
        }

        localDeltas.Dispose();

        int edgesProcessed = 0;
        float attenuation = ReputationConfig.Alpha; // alpha for first-hop

        // BFS-style multi-hop propagation (synchronous), up to MaxHops
        for (int hop = 1; hop <= MaxHops; hop++)
        {
            // Clear nextMap
            nextMap.Clear();

            // Iterate over current frontier entries
            var keys = frontierMap.GetKeyArray(Allocator.Temp);
            try
            {
                for (int ki = 0; ki < keys.Length; ki++)
                {
                    var node = keys[ki];

                    // If this node has already been processed in an earlier hop, skip to avoid echoes
                    if (visited.ContainsKey(node))
                        continue;

                    // Retrieve payload for this node
                    if (!frontierMap.TryGetValue(node, out var payload))
                        continue;

                    // Mark node as visited (processed)
                    visited.TryAdd(node, 1);

                    // Read the node's SocialBridge buffer; if missing, nothing to broadcast
                    if (!EntityManager.HasComponent<SocialBridge>(node))
                        continue;

                    var bridges = EntityManager.GetBuffer<SocialBridge>(node);

                    for (int bi = 0; bi < bridges.Length; bi++)
                    {
                        if (edgesProcessed >= MaxProcessedEdges)
                            break; // global cap

                        var bridge = bridges[bi];

                        // Weighted delta sent to this neighbor
                        var weighted = MultiplyProfile(payload, attenuation * bridge.RelationshipStrength);

                        // Update this node's bridge reputation score (node broadcasting about others)
                        bridge.ReputationScore = AddProfiles(bridge.ReputationScore, weighted);
                        bridges[bi] = bridge;

                        // Accumulate for neighbor to propagate in next hop
                        var neighbor = bridge.Other;
                        if (visited.ContainsKey(neighbor))
                        {
                            // neighbor already processed earlier; skip adding
                        }
                        else
                        {
                            if (nextMap.TryGetValue(neighbor, out var accum))
                            {
                                nextMap[neighbor] = AddProfiles(accum, weighted);
                            }
                            else
                            {
                                nextMap.TryAdd(neighbor, weighted);
                            }
                        }

                        edgesProcessed++;
                    }

                    if (edgesProcessed >= MaxProcessedEdges)
                        break;
                }
            }
            finally
            {
                keys.Dispose();
            }

            if (edgesProcessed >= MaxProcessedEdges)
                break;

            // Prepare for next hop: frontierMap = nextMap, attenuation *= Beta
            frontierMap.Clear();

            // Move entries from nextMap into frontierMap
            var nextKeys = nextMap.GetKeyArray(Allocator.Temp);
            try
            {
                for (int ni = 0; ni < nextKeys.Length; ni++)
                {
                    var k = nextKeys[ni];
                    if (nextMap.TryGetValue(k, out var v))
                    {
                        frontierMap.TryAdd(k, v);
                    }
                }
            }
            finally
            {
                nextKeys.Dispose();
            }

            attenuation *= ReputationConfig.Beta;

            // If frontier empty, stop
            if (frontierMap.Count() == 0)
                break;
        }

        // Dispose maps
        frontierMap.Dispose();
        nextMap.Dispose();
        visited.Dispose();

        // Apply temporal decay to every reputation score (jobified)
        float decayVal = math.exp(-ReputationConfig.Lambda * deltaTime);
        Entities
            .WithName("DecayReputationScores")
            .ForEach((ref DynamicBuffer<SocialBridge> buf) =>
            {
                for (int i = 0; i < buf.Length; i++)
                {
                    var b = buf[i];
                    b.ReputationScore = MultiplyProfile(b.ReputationScore, decayVal);
                    buf[i] = b;
                }
            })
            .ScheduleParallel();
    }

    // Helper struct to batch deltas
    private struct LocalDelta
    {
        public Entity Source;
        public EthicalProfile Delta;
    }

    // Multiply every axis of an EthicalProfile by scalar s
    private static EthicalProfile MultiplyProfile(in EthicalProfile p, float s) => new EthicalProfile
    {
        Lawfulness = p.Lawfulness * s,
        Justice = p.Justice * s,
        Care = p.Care * s,
        Beneficence = p.Beneficence * s,
        Honesty = p.Honesty * s,
        Loyalty = p.Loyalty * s,
        Autonomy = p.Autonomy * s,
        Respect = p.Respect * s,
        Courage = p.Courage * s,
        Temperance = p.Temperance * s
    };

    // Add two EthicalProfiles axis‐wise
    private static EthicalProfile AddProfiles(in EthicalProfile a, in EthicalProfile b) => new EthicalProfile
    {
        Lawfulness = a.Lawfulness + b.Lawfulness,
        Justice = a.Justice + b.Justice,
        Care = a.Care + b.Care,
        Beneficence = a.Beneficence + b.Beneficence,
        Honesty = a.Honesty + b.Honesty,
        Loyalty = a.Loyalty + b.Loyalty,
        Autonomy = a.Autonomy + b.Autonomy,
        Respect = a.Respect + b.Respect,
        Courage = a.Courage + b.Courage,
        Temperance = a.Temperance + b.Temperance
    };
}