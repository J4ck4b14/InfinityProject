using UnityEngine;

namespace InfinityProject.World.Ground
{
    /// <summary>Links a Terrain GameObject to Infinity's persistent ground-condition metadata.</summary>
    [DisallowMultipleComponent]
    public sealed class GroundConditionReference : MonoBehaviour
    {
        [SerializeField] private GroundConditionAsset groundConditions;

        public GroundConditionAsset GroundConditions
        {
            get => groundConditions;
            set => groundConditions = value;
        }
    }
}
