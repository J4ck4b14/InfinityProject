using Unity.Entities;

/// <summary>
/// Singleton component tracking in-game time in SECONDS.
/// TotalSeconds accumulates scaled in-game seconds since simulation start.
/// ScaleIndex indexes into TimeConfig.ScaleValues.
/// </summary>
public struct GameTime : IComponentData
{
    public double TotalSeconds;
    public byte   ScaleIndex;   // 0–4 matching TimeConfig.ScaleValues
}
