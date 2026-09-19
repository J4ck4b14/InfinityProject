using UnityEngine;

namespace InfinityProject.World.Hydrology
{
    /// <summary>Links a Terrain GameObject to Infinity's persistent hydrology snapshot.</summary>
    [DisallowMultipleComponent]
    public sealed class HydrologyReference : MonoBehaviour
    {
        [SerializeField] private HydrologyAsset hydrology;

        public HydrologyAsset Hydrology
        {
            get => hydrology;
            set => hydrology = value;
        }
    }
}
