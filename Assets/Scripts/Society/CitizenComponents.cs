using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Defines all data components and configuration constants for the Agent-First human simulation

// -----------------------------------------------------------------------------
// A) FRACTAL REPUTATION & MEMORY DECAY CONFIGURATION
// -----------------------------------------------------------------------------
public static class ReputationConfig
{
    /// <summary>
    /// alpha: base proportion of a node’s reputation delta transmitted to each direct neighbor.
    /// Scaled at runtime by event.Magnitude (e.g. effectiveAlpha = Alpha * Magnitude).
    /// </summary>
    public const float Alpha = 0.9f;

    /// <summary>
    /// beta: base attenuation factor applied on each propagation hop beyond the first.
    /// Scaled at runtime by event.Magnitude (e.g. effectiveBeta = Beta * Magnitude).
    /// </summary>
    public const float Beta = 0.5f;

    /// <summary>
    /// lambda: baseline temporal decay constant for reputational weight (per second).
    /// Each frame, ReputationScore *= exp(-Lambda * ?t).
    /// </summary>
    public const float Lambda = 0.01f;
}

public static class MemoryConfig
{
    /// <summary>
    /// ?_mem_base: baseline decay constant for memory events (per second).
    /// Actual ?_event will be modulated by magnitude, agent age, buffer fullness, and trauma count:
    ///   ?_event = LambdaBase * (1 + Age * w_age + Fullness * w_full)
    ///                       / (1 + Magnitude * w_mag + TraumaCount * w_trauma)
    /// </summary>
    public const float LambdaBase = 0.01f;

    /// <summary>
    /// Weight factor multiplying effect of event.Magnitude on slowing decay.
    /// </summary>
    public const float WeightMagnitude = 5.0f;

    /// <summary>
    /// Weight factor multiplying effect of agent Age on accelerating decay.
    /// </summary>
    public const float WeightAge = 0.2f;

    /// <summary>
    /// Weight factor multiplying effect of MemoryBuffer fullness on accelerating decay.
    /// </summary>
    public const float WeightFullness = 0.1f;

    /// <summary>
    /// Weight factor multiplying effect of prior trauma count on slowing decay.
    /// </summary>
    public const float WeightTrauma = 3.0f;

    /// <summary>
    /// Maximum number of MemoryEvent entries per agent buffer.
    /// When exceeded, oldest entries prune first.
    /// </summary>
    public const int MaxEvents = 50;
}

// -----------------------------------------------------------------------------
// B) NODE TYPE ENUMERATION
// -----------------------------------------------------------------------------
public enum NodeType : byte
{
    Individual, // Single citizen
    Village,    // Settlement node
    Guild,      // Professional association
    Faction     // Political or military organization
}

// -----------------------------------------------------------------------------
// C) ETHICAL AXES & AGENT PROFILE
// -----------------------------------------------------------------------------
public enum EthicalAxis : byte
{
    Lawfulness, Justice, Care, Beneficence,
    Honesty, Loyalty, Autonomy, Respect,
    Courage, Temperance
}

/// <summary>
/// A 10-dimensional moral vector capturing baseline character.
/// Each value ?[-1,1], e.g. –1 = Unlawful, +1 = Lawful.
/// </summary>
public struct EthicalProfile : IComponentData
{
    public float Lawfulness;
    public float Justice;
    public float Care;
    public float Beneficence;
    public float Honesty;
    public float Loyalty;
    public float Autonomy;
    public float Respect;
    public float Courage;
    public float Temperance;
}

// -----------------------------------------------------------------------------
// D) IDENTIFYING & TRAIT DATA
// -----------------------------------------------------------------------------
public struct Identity : IComponentData
{
    public int NameHash;   // Unique hash key for agent’s name
    public float Age;        // Age in years
    public int SocialRank; // 0=peasant … 10=royalty
}

public struct Traits : IComponentData
{
    public float Intelligence;
    public float Wit;
    public float Serenity;
    public float Wisdom;
}

// -----------------------------------------------------------------------------
// E) PHYSIOLOGICAL & SOCIAL NEEDS
// -----------------------------------------------------------------------------
public struct Needs : IComponentData
{
    public float Hunger;        // [0–1], 1 = starving
    public float Sleepiness;    // [0–1], 1 = exhausted
    public float Safety;        // [0–1], 1 = fully secure
    public float SocialContact; // [0–1], 1 = fully satisfied
}

// -----------------------------------------------------------------------------
// F) MEMORY EVENT BUFFER WITH HYBRID PRUNING & DECAY
// -----------------------------------------------------------------------------
public enum EventType : byte
{
    None, Trade, Insult, Attack, Aid, Rumor, Assassination
}

/// <summary>
/// Represents one perceived event, stored in a dynamic buffer:
/// - EmotionalWeight decays each frame by exp(-?_event * ?t),
///   where ?_event is computed per event via MemoryConfig weights.
/// - Pruning occurs when buffer length exceeds MaxEvents or when
///   EmotionalWeight falls below a negligible threshold.
/// </summary>
[InternalBufferCapacity(MemoryConfig.MaxEvents)]
public struct MemoryEvent : IBufferElementData
{
    public EventType Type;           // Categorical identifier
    public Entity Target;         // Entity involved (agent or object)
    public double Timestamp;      // Game-time at occurrence
    public float EmotionalWeight;// Current emotional weight
    public float Magnitude;      // [0–1] footprint size of event
}

// -----------------------------------------------------------------------------
// G) SOCIAL BRIDGES & MULTIDIMENSIONAL REPUTATION
// -----------------------------------------------------------------------------
public struct SocialBridge : IBufferElementData
{
    public Entity Other;               // Connected node
    public NodeType Type;                // Individual, Village, Guild, or Faction
    public float RelationshipStrength;// w_ij static affinity or proximity

    /// <summary>
    /// ReputationScore holds a full EthicalProfile vector that evolves via:
    ///  1) Local impact injection: ?R_source added to source’s own bridge
    ///  2) First-hop: ReputationScore += effectiveAlpha * w_ij * ?R_source
    ///  3) Further hops: each hop multiplies delta by effectiveBeta
    ///  4) Temporal decay each frame: *= exp(-ReputationConfig.Lambda * ?t) where effectiveAlpha = ReputationConfig.Alpha * event.Magnitude, effectiveBeta = ReputationConfig.Beta * event.Magnitude.
    /// </summary>
    public EthicalProfile ReputationScore;
}

// -----------------------------------------------------------------------------
// H) AMBITION & DECISION TRIGGER COMPONENTS
// -----------------------------------------------------------------------------
public enum GoalType : byte
{
    None, Eat, Sleep, Socialize,
    ImproveReputation, Safety, Work
}

public struct Ambition : IComponentData
{
    public GoalType CurrentGoal; // Goal chosen this update
    public float Urgency;     // [0–1] computed by DecisionSystem
}

public struct DecisionRequest : IComponentData
{
    public bool HasDecision;     // Flag to trigger DecisionSystem
}

// -----------------------------------------------------------------------------
// I) PLANNED ACTION OUTPUT
// -----------------------------------------------------------------------------
public enum ActionType : byte
{
    Idle, MoveTo, PerformTask, Converse, Trade, Flee
}

public struct ActionState : IComponentData
{
    public ActionType NextAction; // Instruction for ActionSystem
    public Entity Target;     // Entity or node to interact with
    public float3 Destination;// World-space coordinate for MoveTo
}

// -----------------------------------------------------------------------------
// J) LOCAL REPUTATION DELTA INJECTION
// -----------------------------------------------------------------------------
public struct LocalReputationDelta : IComponentData
{
    /// <summary>
    /// ?R vector created by an action (e.g. assassination, insult).
    /// Consists of EthicalProfile with per-axis delta in [-1,1].
    /// ActionSystem or PlayerActionSystem writes this component to the source entity; ReputationSystem will consume it and propagate fractally via SocialBridge buffers.
    /// </summary>
    public EthicalProfile DeltaR;
}

// -----------------------------------------------------------------------------
// K) MEMORY EVENT BUFFER PRUNING AND DECAY
// -----------------------------------------------------------------------------
public struct DecayedMemBuffer : IComponentData
{
    public Entity TargetEntity; // Entity this buffer belongs to

    public void PruneAndDecay(DynamicBuffer<MemoryEvent> memBuf)
    {
        // Decay and prune events in the buffer
        int write = 0;
        for (int i = 0; i < memBuf.Length; i++)
        {
            var ev = memBuf[i];
            // compute decayed weight...
            if (ev.EmotionalWeight >= 0.01f)
            {
                memBuf[write++] = ev;
            }
        }
        if (write < memBuf.Length)
        {
            memBuf.ResizeUninitialized(write);
        }
        // enforce capacity
        int excess = memBuf.Length - MemoryConfig.MaxEvents;
        if (excess > 0)
        {
            // remove first 'excess' entries by shifting left or use RemoveRange if available
            for (int k = 0; k < memBuf.Length - excess; k++)
                memBuf[k] = memBuf[k + excess];
            memBuf.ResizeUninitialized(memBuf.Length - excess);
        }
    }
}
