using UnityEngine;
using UnityEditor;
using Unity.Mathematics;

public class HeightMapGenerator : EditorWindow
{
    private int[] mapSizes = { 32, 64, 128, 256 };
    private int mapIndex = 3;
    private int mapRes = 256;
    private float baseRoughness = 1f;
    private float roughness = 2f;
    private float persistence = 0.5f;
    private int octaves = 6;
    private float terrainSide = 500f;
    private float terrainHeight = 100f;
    private Terrain targetTerrain;
    private Vector3 terrainPosition = Vector3.zero;

    [MenuItem("Tools/Heightmap Generator")]
    public static void ShowWindow()
    {
        GetWindow<HeightMapGenerator>("Heightmap Generator");
    }

    private void OnGUI()
    {
        // Map Resolution
        GUILayout.Label("Heightmap Generator Settings", EditorStyles.boldLabel);
        mapIndex = EditorGUILayout.Popup("Map Resolution", mapIndex, System.Array.ConvertAll(mapSizes, x => x.ToString()));
        mapRes = mapSizes[mapIndex];

        // Noise Parameters
        baseRoughness = EditorGUILayout.Slider("Base Roughness", baseRoughness, 0.1f, 5f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 1f, 10f);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0f, 1f);
        octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);

        // Spacing things nicer
        GUILayout.Space(10);

        // Terrain Extension
        GUILayout.Label("Terrain Settings", EditorStyles.boldLabel);
        terrainSide = EditorGUILayout.FloatField("Terrain Extension", terrainSide);

        // Terrain Height
        terrainHeight = EditorGUILayout.FloatField("Terrain Height", terrainHeight);

        // Create Terrain
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);

        if (GUILayout.Button("Generate Terrain"))
        {
            GenerateTerrain();
        }
    }

    void GenerateTerrain()
    {
        if (targetTerrain == null)
        {
            int option = EditorUtility.DisplayDialogComplex("No Terrain Assigned", "Do you want to create a new terrain?", "Create Terrain", "Cancel", null);
            switch (option)
            {
                case 0: // "Create terrain" button pressed
                    CreateNewTerrain();
                    break;
                case 1: // "Cancel" button pressed
                case 2:
                    return;
            }
        }

        Texture2D heightmap = GenerateHeightmap();
        ApplyHeightmapToTerrain(heightmap);
    }

    void CreateNewTerrain()
    {
        // Create a new terrain game object
        GameObject terrainObject = new GameObject("Generated Terrain");
        terrainObject.transform.position = terrainPosition;

        // Add Terrain component
        targetTerrain = terrainObject.AddComponent<Terrain>();
        targetTerrain.terrainData = new TerrainData();

        // Add TerrainCollider component
        TerrainCollider terrainCollider = terrainObject.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = targetTerrain.terrainData;

        // Register the creation in the undo system
        Undo.RegisterCreatedObjectUndo(terrainObject, "Create New Terrain");
    }

    /// <summary>
    /// TL;DR: generates a heightmap for terrain using Perlin noise
    /// 
    /// 0 - Creates a new Texture2D with dimensions mapRes x mapRes.
    /// 1 - Generates a noise map using Perlin noise.
    /// 2 - Converts the noise map to grayscale colors.
    /// 3 - Sets the processed colors to the heightmap texture.
    /// 4 - Applies the changes to the texture and returns it.
    /// </summary>
    /// <returns></returns>
    Texture2D GenerateHeightmap()
    {
        Texture2D heightmap = new Texture2D(mapRes, mapRes);
        float[,] noiseMap = GenerateNoiseMap(mapRes, mapRes);
        Color[] colors = new Color[mapRes * mapRes];

        for (int y = 0; y < mapRes; y++)
        {
            for (int x = 0; x < mapRes; x++)
            {
                float noiseValue = noiseMap[x, y];
                colors[y * mapRes + x] = new Color(noiseValue, noiseValue, noiseValue);
            }
        }

        heightmap.SetPixels(colors);
        heightmap.Apply();

        return heightmap;
    }

    /// <summary>
    /// TL;DR: Generates a 2D noise map using Perlin noise
    /// 
    /// 0 - Creates a 2D array to store noise values
    /// 1 - Generates a random offset for the noise
    /// 2 - Iterates through each point in the map:
    ///     · Calculates noise value using multiple octaves of Perlin noise
    ///     · Adjusts amplitude and frequency for each octave
    /// 3 - Normalizes the noise values to range [0, 1]
    /// 4 - Returns the generated noise map
    /// </summary>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <returns></returns>
    float[,] GenerateNoiseMap(int width, int height)
    {
        float[,] noiseMap = new float[width, height];

        System.Random prng = new System.Random(UnityEngine.Random.Range(0, 100000));
        float2 offset = new float2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1;
                float frequency = baseRoughness;
                float noiseHeight = 0;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - width / 2f + offset.x) / width * frequency;
                    float sampleY = (y - height / 2f + offset.y) / height * frequency;

                    float perlinValue = noise.snoise(new float2(sampleX, sampleY)) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistence;
                    frequency *= roughness;
                }

                if (noiseHeight > maxNoiseHeight)
                    maxNoiseHeight = noiseHeight;
                else if (noiseHeight < minNoiseHeight)
                    minNoiseHeight = noiseHeight;

                noiseMap[x, y] = noiseHeight;
            }
        }

        // Normalize the noise map
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
            }
        }

        return noiseMap;
    }

    /// <summary>
    /// TL;DR: takes a 2D image (heightmap) and uses it to sculpt a 3D terrain
    /// 
    /// 0 - Registers the terrain data for Unity's undo system
    /// 1 - Retrieves the TerrainData component from the target terrain object
    /// 2 - Sets the resolution based on the heightmap
    /// 3 - Sets the physical size of the terrain.
    /// 4 - Creates a 2D array to store height data
    /// 5 - Converts the heightmap into values (Iterates through each pixel && translates grayscale color to height value[0 to 1])
    /// 6 - Applies the calculated heights and ensures the changes are saved
    /// </summary>
    /// <param name="heightmap"></param>
    void ApplyHeightmapToTerrain(Texture2D heightmap)
    {
        // Store the change so the user can undo it
        Undo.RecordObject(targetTerrain.terrainData, "Generate Terrain");

        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmap.width;
        terrainData.size = new Vector3(terrainSide, terrainHeight, terrainSide);

        float[,] heights = new float[terrainData.heightmapResolution, terrainData.heightmapResolution];

        for (int y = 0; y < heightmap.height; y++)
        {
            for (int x = 0; x < heightmap.width; x++)
            {
                heights[y, x] = heightmap.GetPixel(x, y).grayscale;
            }
        }

        terrainData.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(targetTerrain);
    }
}
