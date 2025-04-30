using UnityEngine;
using UnityEditor;
using Unity.Mathematics;

public class HeightMapTab
{
    private Texture2D customHeightmap;
    private readonly int[] mapSizes = { 32, 64, 128, 256, 512, 1024, 2048 };
    private int mapIndex = 3;
    private float baseRoughness = 1f;
    private float roughness = 2f;
    private float persistence = 0.5f;
    private int octaves = 6;
    private float terrainSide = 500f;
    private float terrainHeight = 100f;
    private Terrain targetTerrain;
    private readonly Vector3 terrainPosition = Vector3.zero;

    public void Draw()
    {
        GUILayout.Label("Custom Heightmap Input", EditorStyles.boldLabel);
        customHeightmap = (Texture2D)EditorGUILayout.ObjectField("Custom Heightmap", customHeightmap, typeof(Texture2D), false);

        GUILayout.Space(10);
        GUILayout.Label("Heightmap Generator Settings", EditorStyles.boldLabel);

        mapIndex = EditorGUILayout.Popup("Map Resolution", mapIndex, System.Array.ConvertAll(mapSizes, x => x.ToString()));
        baseRoughness = EditorGUILayout.Slider("Base Roughness", baseRoughness, 0.1f, 5f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 1f, 10f);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0f, 1f);
        octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);

        GUILayout.Space(10);
        GUILayout.Label("Terrain Settings", EditorStyles.boldLabel);
        terrainSide = EditorGUILayout.FloatField("Terrain Extension", terrainSide);
        terrainHeight = EditorGUILayout.FloatField("Terrain Height", terrainHeight);
        targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);

        if (GUILayout.Button("Generate Terrain"))
        {
            GenerateTerrain();
        }
    }

    private void GenerateTerrain()
    {
        if (targetTerrain == null)
        {
            if (EditorUtility.DisplayDialog("No Terrain Assigned", "Do you want to create a new terrain?", "Create Terrain", "Cancel"))
            {
                CreateNewTerrain();
            }
            else
            {
                return;
            }
        }

        ApplyHeightmapToTerrain(GenerateHeightmap());
    }

    private void CreateNewTerrain()
    {
        GameObject terrainObject = new GameObject("Generated Terrain")
        {
            transform = { position = terrainPosition }
        };

        targetTerrain = terrainObject.AddComponent<Terrain>();
        targetTerrain.terrainData = new TerrainData();

        TerrainCollider terrainCollider = terrainObject.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = targetTerrain.terrainData;

        Undo.RegisterCreatedObjectUndo(terrainObject, "Create New Terrain");
    }

    private Texture2D GenerateHeightmap()
    {
        float[,] noiseMap = GenerateNoiseMap(mapSizes[mapIndex], mapSizes[mapIndex]);
        Texture2D heightmap = new Texture2D(mapSizes[mapIndex], mapSizes[mapIndex], TextureFormat.RGB24, false);
        Color[] colors = new Color[noiseMap.Length];

        for (int y = 0; y < mapSizes[mapIndex]; y++)
        {
            for (int x = 0; x < mapSizes[mapIndex]; x++)
            {
                float noiseValue = noiseMap[x, y];
                colors[y * mapSizes[mapIndex] + x] = new Color(noiseValue, noiseValue, noiseValue);
            }
        }

        heightmap.SetPixels(colors);
        heightmap.Apply();
        return heightmap;
    }

    private float[,] GenerateNoiseMap(int width, int height)
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
                float amplitude = 1f;
                float frequency = baseRoughness;
                float noiseHeight = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - width / 2f + offset.x) / width * frequency;
                    float sampleY = (y - height / 2f + offset.y) / height * frequency;

                    float perlinValue = noise.snoise(new float2(sampleX, sampleY)) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistence;
                    frequency *= roughness;
                }

                maxNoiseHeight = Mathf.Max(maxNoiseHeight, noiseHeight);
                minNoiseHeight = Mathf.Min(minNoiseHeight, noiseHeight);
                noiseMap[x, y] = noiseHeight;
            }
        }

        float range = maxNoiseHeight - minNoiseHeight;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseMap[x, y] = (noiseMap[x, y] - minNoiseHeight) / range;
            }
        }

        return noiseMap;
    }

    private void ApplyHeightmapToTerrain(Texture2D heightmap)
    {
        Undo.RecordObject(targetTerrain.terrainData, "Generate Terrain");

        TerrainData terrainData = targetTerrain.terrainData;
        terrainData.heightmapResolution = heightmap.width + 1;
        terrainData.size = new Vector3(terrainSide, terrainHeight, terrainSide);

        float[,] heights = new float[heightmap.height, heightmap.width];
        Color[] pixels = heightmap.GetPixels();

        for (int y = 0; y < heightmap.height; y++)
        {
            for (int x = 0; x < heightmap.width; x++)
            {
                heights[y, x] = pixels[y * heightmap.width + x].grayscale;
            }
        }

        terrainData.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(targetTerrain);
    }
}