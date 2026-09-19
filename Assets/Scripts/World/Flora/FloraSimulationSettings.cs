using System;
using UnityEngine;

namespace InfinityProject.World.Flora
{
    [Serializable]
    public sealed class FloraSimulationSettings
    {
        [Tooltip("Maximum numerical integration substep in simulated days.")]
        [Range(0.01f, 2f)] public float MaximumSubstepDays = 2f;

        [Tooltip("Minimum fraction of environmental carrying capacity retained under maximal inter-species competition.")]
        [Range(0.01f, 1f)] public float MinimumCompetitionCapacityFraction = 0.15f;

        public FloraSimulationSettings Clone()
            => (FloraSimulationSettings)MemberwiseClone();

        public void Validate()
        {
            MaximumSubstepDays = Mathf.Clamp(MaximumSubstepDays, 0.01f, 2f);
            MinimumCompetitionCapacityFraction = Mathf.Clamp(MinimumCompetitionCapacityFraction, 0.01f, 1f);
        }
    }
}
