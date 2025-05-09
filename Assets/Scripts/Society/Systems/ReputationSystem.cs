using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Consumes one‐shot LocalReputationDelta components,
/// propagates them fractally via SocialBridge buffers
/// (using α for first hop, TODO: β for further hops),
/// and applies per‐frame exponential decay (λ).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class ReputationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1) Cache Δt for decay calculations
        float deltaTime = SystemAPI.Time.DeltaTime;

        // 2) Collect all LocalReputationDelta this frame
        var localDeltas = new NativeList<LocalDelta>(Allocator.Temp);
        Entities
            .WithName("CollectLocalReputationDeltas")
            .ForEach((Entity e, in LocalReputationDelta lrd) =>
            {
                localDeltas.Add(new LocalDelta { Source = e, Delta = lrd.DeltaR });
            })
            .Run();

        // 3) Remove each LocalReputationDelta so it fires only once
        Entities
            .WithName("ClearLocalReputationDeltas")
            .WithStructuralChanges()
            .ForEach((Entity e) =>
            {
                EntityManager.RemoveComponent<LocalReputationDelta>(e);
            })
            .Run();

        // 4) First‐hop propagation into neighbors
        for (int i = 0; i < localDeltas.Length; i++)
        {
            var source = localDeltas[i].Source;
            var deltaProf = localDeltas[i].Delta;
            float effectiveAlpha = ReputationConfig.Alpha; // Optionally multiply by magnitude

            // Get all edges from this node
            var bridges = EntityManager.GetBuffer<SocialBridge>(source);
            for (int j = 0; j < bridges.Length; j++)
            {
                var bridge = bridges[j];
                // α × w_ij × ΔR
                var weighted = MultiplyProfile(deltaProf, effectiveAlpha * bridge.RelationshipStrength);
                bridge.ReputationScore = AddProfiles(bridge.ReputationScore, weighted);
                bridges[j] = bridge;

                // TODO: enqueue weighted*β into a queue for neighbor‐of‐neighbor hops
            }
        }

        // 5) Apply temporal decay to every reputation score
        Entities
            .WithName("DecayReputationScores")
            .ForEach((ref DynamicBuffer<SocialBridge> buf) =>
            {
                float decay = math.exp(-ReputationConfig.Lambda * deltaTime);
                for (int i = 0; i < buf.Length; i++)
                {
                    var b = buf[i];
                    b.ReputationScore = MultiplyProfile(b.ReputationScore, decay);
                    buf[i] = b;
                }
            })
            .ScheduleParallel();

        localDeltas.Dispose();
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