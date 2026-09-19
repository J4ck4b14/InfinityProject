using System;
using UnityEngine;

namespace InfinityProject.World.Geography
{
    /// <summary>
    /// Adapts Infinity geography data to Unity Terrain.
    /// This class does not own or generate world state.
    /// </summary>
    public static class UnityTerrainPresenter
    {
        public static void Apply(Terrain terrain, GeographyData data)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            TerrainData terrainData = terrain.terrainData;
            int sourceResolution = data.Resolution;
            int unityResolution = sourceResolution + 1;

            terrainData.heightmapResolution = unityResolution;
            terrainData.size = new Vector3(
                data.WidthMeters,
                data.MaxElevationMeters,
                data.LengthMeters);

            var unityHeights = new float[unityResolution, unityResolution];

            for (int y = 0; y < sourceResolution; y++)
            {
                for (int x = 0; x < sourceResolution; x++)
                    unityHeights[y, x] = data.GetNormalizedHeight(x, y);

                unityHeights[y, sourceResolution] = unityHeights[y, sourceResolution - 1];
            }

            for (int x = 0; x < unityResolution; x++)
                unityHeights[sourceResolution, x] = unityHeights[sourceResolution - 1, x];

            terrainData.SetHeights(0, 0, unityHeights);
            terrain.Flush();
        }
    }
}
