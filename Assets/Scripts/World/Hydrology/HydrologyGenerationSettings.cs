using System;
using UnityEngine;

namespace InfinityProject.World.Hydrology
{
    /// <summary>
    /// Designer-facing settings for Hydrology V0.
    /// The routing model itself is derived from geography; channel threshold is diagnostic.
    /// </summary>
    [Serializable]
    public sealed class HydrologyGenerationSettings
    {
        [Tooltip("Minimum physical height difference required before a neighbouring sample counts as downhill.")]
        [Min(0f)] public float MinimumDropMeters = 0.0001f;

        [Tooltip("Contributing catchment area required before a sample is shown as a candidate channel.")]
        [Min(1f)] public float ChannelInitiationAreaSquareMeters = 50000f;

        public HydrologyGenerationSettings Clone()
            => (HydrologyGenerationSettings)MemberwiseClone();

        public void Validate()
        {
            MinimumDropMeters = Mathf.Max(0f, MinimumDropMeters);
            ChannelInitiationAreaSquareMeters = Mathf.Max(1f, ChannelInitiationAreaSquareMeters);
        }
    }
}
