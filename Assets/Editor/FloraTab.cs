using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using UnityEditor.Callbacks;

/// <summary>
/// Handles the flora placement tab in the Unity Editor.
/// Allows users to draw a polygonal area, configure flora settings, and generate flora within the area.
/// </summary>
public class FloraTab
{
    // List of flora prefabs to be instantiated
    private List<GameObject> floraPrefabs = new List<GameObject>();

    // Reorderable list for managing flora prefabs in the editor
    private ReorderableList floraList;

    // Density of flora per square meter
    private float density = 1f; // trees per meter

    // Variation in scale for the flora (0 to 1)
    private float scaleVariation = 0.1f;

    // Variation in hue for the flora (0 to 1)
    private float hueVariation = 0.1f;

    // List of points defining the polygonal placement area
    private List<Vector3> polygonPoints = new List<Vector3>();

    // Flag to track whether the user is currently drawing the polygon
    private bool isDrawing = false;

    // --- fields for batched spawn ---
    private List<Vector3> _spawnSamples;
    private Transform _spawnParent;
    private int _spawnIndex;
    private const int _batchSize = 200;

    /// <summary>
    /// Constructor initializes the reorderable list for flora prefabs.
    /// </summary>
    public FloraTab()
    {
        floraList = new ReorderableList(floraPrefabs, typeof(GameObject), true, true, true, true);
        floraList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Flora Prefabs");
        floraList.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            floraPrefabs[index] = (GameObject)EditorGUI.ObjectField(rect, floraPrefabs[index], typeof(GameObject), false);
        };
        floraList.onAddCallback = list => floraPrefabs.Add(null);
    }

    /// <summary>
    /// Draws the flora tab UI in the Unity Editor.
    /// </summary>
    public void Draw()
    {
        EditorGUILayout.LabelField("Flora Settings", EditorStyles.boldLabel);
        floraList.DoLayoutList();

        EditorGUILayout.Space(10);
        density = EditorGUILayout.Slider(new GUIContent("Density (per m²)", "How many flora items to spawn per square meter"), density, 0.001f, 10f);
        scaleVariation = EditorGUILayout.Slider(new GUIContent("Scale Variation", "Y-scale range from 1 - x to 1 + x"), scaleVariation, 0f, 1f);
        hueVariation = EditorGUILayout.Slider(new GUIContent("Hue Variation", "Applies a random hue shift"), hueVariation, 0f, 1f);

        EditorGUILayout.Space(10);
        if (!isDrawing && GUILayout.Button("Draw Placement Area"))
        {
            SceneView.duringSceneGui += OnSceneGUI;
            isDrawing = true;
        }

        if (polygonPoints.Count > 2 && GUILayout.Button("Generate Flora"))
        {
            GenerateFlora();
        }
    }

    /// <summary>
    /// Handles the SceneView GUI for drawing the polygonal placement area.
    /// </summary>
    private void OnSceneGUI(SceneView sceneView)
    {
        Handles.color = new Color(0f, 0.5f, 1f, 0.3f);

        Event e = Event.current;
        // only raycast against "Terrain" layer
        int terrainMask = LayerMask.GetMask("Terrain");
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = false;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainMask))
        {
            // Add points to the polygon when Shift + Left Click is used
            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                Vector3 point = hit.point;
                polygonPoints.Add(point);
                e.Use();
                SceneView.RepaintAll();
            }

            // Draw the polygon lines as the user adds points
            if (polygonPoints.Count > 1)
            {
                Handles.DrawAAPolyLine(4, polygonPoints.ToArray());
                Handles.DrawLine(polygonPoints[polygonPoints.Count - 1], hit.point);
            }
        }

        Physics.queriesHitBackfaces = prevBackfaces;

        // Close the polygon with Enter key
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && polygonPoints.Count > 2)
        {
            isDrawing = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            e.Use();
        }

        // Draw the completed polygon if it is closed
        if (!isDrawing && polygonPoints.Count > 2)
        {
            Handles.DrawAAConvexPolygon(polygonPoints.ToArray());
            Handles.color = new Color(0f, 0.5f, 1f, 0.5f); // Blue, half-transparent
            Handles.DrawSolidRectangleWithOutline(polygonPoints.ToArray(), new Color(0f, 0.5f, 1f, 0.3f), Color.clear);
        }

        HandleUtility.Repaint();
    }

    /// <summary>
    /// Generates flora within the drawn polygon based on the configured settings.
    /// </summary>
    private void GenerateFlora()
    {
        if (floraPrefabs.Count == 0)
        {
            Debug.LogWarning("No flora prefabs assigned.");
            return;
        }

        // Compute the bounding box of the polygon and determine the number of samples
        Bounds bounds = GetPolygonBounds();
        int sampleCount = Mathf.FloorToInt(bounds.size.x * bounds.size.z * density);

        // Find or create a parent GameObject for the generated flora
        GameObject parentGO = GameObject.Find("Generated Flora");
        if (parentGO == null)
            parentGO = new GameObject("Generated Flora");
        Undo.RegisterCreatedObjectUndo(parentGO, "Generate Flora");

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogError("No active Terrain found in scene.");
            return;
        }
        Vector3 terrainOrigin = terrain.GetPosition();
        var rnd = new System.Random();

        // Precompute all valid samples
        _spawnSamples = new List<Vector3>(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            float x = (float)(bounds.min.x + rnd.NextDouble() * bounds.size.x);
            float z = (float)(bounds.min.z + rnd.NextDouble() * bounds.size.z);
            var sample = new Vector3(x, bounds.center.y, z);
            if (!IsPointInPolygon(sample, polygonPoints))
                continue;
            float y = terrain.SampleHeight(sample) + terrainOrigin.y;
            _spawnSamples.Add(new Vector3(x, y, z));
        }

        // set up batched spawn
        _spawnParent = parentGO.transform;
        _spawnIndex = 0;
        EditorApplication.update += SpawnUpdate;
    }

    private void SpawnUpdate()
    {
        var rnd = new System.Random();
        int end = Mathf.Min(_spawnIndex + _batchSize, _spawnSamples.Count);
        for (int i = _spawnIndex; i < end; i++)
        {
            // Pick a random prefab
            var prefab = floraPrefabs[rnd.Next(floraPrefabs.Count)];
            if (prefab == null)
                continue;

            // Instantiate
            var instance = Object.Instantiate(prefab, _spawnSamples[i], Quaternion.identity, _spawnParent);

            // Scale variation
            float s = 1f + ((float)rnd.NextDouble() * 2f - 1f) * scaleVariation;
            instance.transform.localScale = Vector3.one * s;

            // Hue variation
            var rend = instance.GetComponentInChildren<Renderer>();
            if (rend != null && rend.sharedMaterial.HasProperty("_Color"))
            {
                Color col = rend.sharedMaterial.color;
                Color.RGBToHSV(col, out float h, out float sat, out float val);
                h = Mathf.Repeat(h + Random.Range(-hueVariation, hueVariation), 1f);
                rend.sharedMaterial.color = Color.HSVToRGB(h, sat, val);
            }
        }

        _spawnIndex = end;
        if (_spawnIndex >= _spawnSamples.Count)
        {
            // cleanup
            EditorApplication.update -= SpawnUpdate;

            // Combine and chunk the generated flora for performance
            ChunkAndCombine();

            polygonPoints.Clear();
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Combines flora instances into chunks for better performance.
    /// </summary>
    private void ChunkAndCombine()
    {
        const float cellSize = 20f;
        var parent = GameObject.Find("Generated Flora");
        if (parent == null) return;

        // Bucket instances by cell
        var buckets = new Dictionary<Vector2Int, List<Transform>>();
        foreach (Transform t in parent.transform)
        {
            var position = t.position;
            var key = new Vector2Int(
                Mathf.FloorToInt(position.x / cellSize),
                Mathf.FloorToInt(position.z / cellSize)
            );
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<Transform>();
                buckets[key] = list;
            }
            list.Add(t);
        }

        // Combine meshes for each cell
        foreach (var kv in buckets)
        {
            var cellGO = new GameObject($"FloraChunk_{kv.Key.x}_{kv.Key.y}")
            {
                transform = { parent = parent.transform }
            };
            var meshFilter = cellGO.AddComponent<MeshFilter>();
            var meshRenderer = cellGO.AddComponent<MeshRenderer>();

            var combines = new List<CombineInstance>();
            Material sharedMaterial = null;

            foreach (var transform in kv.Value)
            {
                var meshFilterChild = transform.GetComponentInChildren<MeshFilter>();
                if (meshFilterChild == null) continue;

                combines.Add(new CombineInstance
                {
                    mesh = meshFilterChild.sharedMesh,
                    transform = meshFilterChild.transform.localToWorldMatrix
                });

                if (sharedMaterial == null)
                {
                    var renderer = transform.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        sharedMaterial = renderer.sharedMaterial;
                    }
                }

                UnityEngine.Object.DestroyImmediate(transform.gameObject);
            }

            if (sharedMaterial != null)
            {
                meshRenderer.sharedMaterial = sharedMaterial;
            }

            var combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combines.ToArray(), true, true);
            meshFilter.sharedMesh = combinedMesh;

            // Add LODGroup for performance
            var lodGroup = cellGO.AddComponent<LODGroup>();
            lodGroup.SetLODs(new LOD[]
            {
                new LOD(0.5f, new[] { meshRenderer }), // Medium distance
                new LOD(0.1f, new[] { meshRenderer }), // Far distance
                new LOD(0f, new Renderer[0]) // Hidden at furthest
            });
            lodGroup.RecalculateBounds();
        }
    }

    /// <summary>
    /// Calculates the bounding box of the polygon.
    /// </summary>
    private Bounds GetPolygonBounds()
    {
        Vector3 min = polygonPoints[0], max = polygonPoints[0];
        foreach (Vector3 point in polygonPoints)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new Bounds((min + max) / 2f, max - min);
    }

    /// <summary>
    /// Determines if a point is inside a polygon using the ray-casting algorithm.
    /// </summary>
    private bool IsPointInPolygon(Vector3 point, List<Vector3> polygon)
    {
        int count = polygon.Count;
        bool inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((polygon[i].z > point.z) != (polygon[j].z > point.z)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.z - polygon[i].z) / (polygon[j].z - polygon[i].z) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Cleans up the drawing state when the tab is closed or reset.
    /// </summary>
    public void Cleanup()
    {
        if (isDrawing)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            isDrawing = false;
        }
    }
}
