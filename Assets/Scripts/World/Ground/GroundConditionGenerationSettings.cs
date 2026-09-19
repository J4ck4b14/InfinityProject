using System;
using UnityEngine;

namespace InfinityProject.World.Ground
{
    /// <summary>
    /// Ground V0.4 settings. Geography keeps native local derivatives; Ground interprets landform and water
    /// concentration at explicit physical scales before exposing ecology-facing conditions.
    /// </summary>
    [Serializable]
    public sealed class GroundConditionGenerationSettings
    {
        [Header("Physical landform scale")]
        [Tooltip("Radius in metres used to box-filter elevation before deriving Ground-scale slope. Zero uses Geography's native local slope for diagnostics only.")]
        [Min(0f)] public float SlopeAnalysisRadiusMeters = 32f;

        [Header("Topographic wetness diagnostic")]
        [Min(0.01f)] public float MinimumSlopeDegrees = 0.25f;

        [Header("Run-on / water availability")]
        [Tooltip("Physical area around a sample treated as local support before additional upstream area counts as run-on concentration.")]
        [Min(1f)] public float RunOnLocalSupportAreaSquareMeters = 3200f;
        [Tooltip("Excess contributing area that produces roughly 63% of the saturating run-on response.")]
        [Min(1f)] public float RunOnResponseAreaSquareMeters = 18000f;
        [Tooltip("Maximum fraction of remaining water headroom that run-on can add above direct precipitation supply.")]
        [Range(0f, 1f)] public float RunOnWaterBoostStrength = 0.78f;

        [Header("Soil retention")]
        [Range(0f, 45f)] public float FullRetentionSlopeDegrees = 7f;
        [Range(1f, 89f)] public float NoRetentionSlopeDegrees = 42f;

        public GroundConditionGenerationSettings Clone() => new()
        {
            SlopeAnalysisRadiusMeters = SlopeAnalysisRadiusMeters,
            MinimumSlopeDegrees = MinimumSlopeDegrees,
            RunOnLocalSupportAreaSquareMeters = RunOnLocalSupportAreaSquareMeters,
            RunOnResponseAreaSquareMeters = RunOnResponseAreaSquareMeters,
            RunOnWaterBoostStrength = RunOnWaterBoostStrength,
            FullRetentionSlopeDegrees = FullRetentionSlopeDegrees,
            NoRetentionSlopeDegrees = NoRetentionSlopeDegrees
        };

        public void Validate()
        {
            SlopeAnalysisRadiusMeters = Mathf.Clamp(SlopeAnalysisRadiusMeters, 0f, 10000f);
            MinimumSlopeDegrees = Mathf.Clamp(MinimumSlopeDegrees, 0.01f, 10f);
            RunOnLocalSupportAreaSquareMeters = Mathf.Clamp(RunOnLocalSupportAreaSquareMeters, 1f, 10000000f);
            RunOnResponseAreaSquareMeters = Mathf.Clamp(RunOnResponseAreaSquareMeters, 1f, 100000000f);
            RunOnWaterBoostStrength = Mathf.Clamp01(RunOnWaterBoostStrength);
            FullRetentionSlopeDegrees = Mathf.Clamp(FullRetentionSlopeDegrees, 0f, 80f);
            NoRetentionSlopeDegrees = Mathf.Clamp(NoRetentionSlopeDegrees, FullRetentionSlopeDegrees + 0.1f, 89f);
        }
    }
}
