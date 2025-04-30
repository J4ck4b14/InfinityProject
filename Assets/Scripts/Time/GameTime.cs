using Unity.Entities;

public struct GameTime : IComponentData
{
    public double TotalYears;
    public byte ScaleIndex;  // 0–7 matching TimeConfig.YearScale
}