using UnityEngine;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Links a world-space Terrain representation to Infinity's persistent geography data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeographyReference : MonoBehaviour
    {
        [SerializeField] private GeographyAsset geography;

        public GeographyAsset Geography
        {
            get => geography;
            set => geography = value;
        }
    }
}
