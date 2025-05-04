using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Editor tab for procedural placement of flora prefabs within a user-drawn polygon area.
/// Supports density, scale variation, hue variation, batched instantiation, and chunked combining.
/// </summary>
public class FloraTab
{
    // === Inspector data ===

    /// <summary>List of flora prefabs user may populate.</summary>
    private readonly List<GameObject> floraPrefabs = new List<GameObject>();

    /// <summary>Reorderable UI list to edit floraPrefabs.</summary>
    private readonly ReorderableList floraList;

    /// <summary>Instances per square meter to spawn.</summary>
    private float density = 1f;

    /// <summary>± fraction for random Y‐scale variation.</summary>
    private float scaleVariation = 0.1f;

    /// <summary>± fraction for random hue shift.</summary>
    private float hueVariation = 0.1f;

    // === Scene drawing state ===

    /// <summary>Points of the polygon being drawn.</summary>
    private readonly List<Vector3> polygonPoints = new List<Vector3>();

    /// <summary>True if user is in drawing mode.</summary>
    private bool isDrawing = false;

    // === Batched spawn state ===

    private List<Vector3> _spawnSamples;
    private Transform _spawnParent;
    private int _spawnIndex;
    private const int _batchSize = 200;

    /// <summary>
    /// Constructor: sets up the reorderable list callbacks.
    /// </summary>
    public FloraTab()
    {
        floraList = new ReorderableList(floraPrefabs, typeof(GameObject), true, true, true, true)
        {
            drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, "Flora Prefabs"),
            drawElementCallback = (rect, idx, _, _) =>
            {
                floraPrefabs[idx] = (GameObject)EditorGUI.ObjectField(
                    rect, floraPrefabs[idx], typeof(GameObject), false);
            },
            onAddCallback = _ => floraPrefabs.Add(null)
        };
    }

    /// <summary>
    /// Draws the Flora tab UI in the editor.
    /// </summary>
    public void Draw()
    {
        EditorGUILayout.LabelField("Flora Settings", EditorStyles.boldLabel);
        floraList.DoLayoutList();

        GUILayout.Space(10);
        density = EditorGUILayout.Slider("Density (per m^2)", density, 0.001f, 10f);
        scaleVariation = EditorGUILayout.Slider("Scale Variation", scaleVariation, 0f, 1f);
        hueVariation = EditorGUILayout.Slider("Hue Variation", hueVariation, 0f, 1f);

        GUILayout.Space(10);
        if (!isDrawing && GUILayout.Button("Draw Placement Area"))
        {
            SceneView.duringSceneGui += OnSceneGUI;
            isDrawing = true;
        }

        if (polygonPoints.Count > 2 && GUILayout.Button("Generate Flora"))
            GenerateFlora();
    }

    /// <summary>
    /// SceneView callback for drawing the polygon and handling input.
    /// Shift+Click to add points; Enter to close.
    /// </summary>
    private void OnSceneGUI(SceneView sv)
    {
        Handles.color = new Color(0f, 0.5f, 1f, 0.3f);
        Event e = Event.current;

        // Raycast only against the "Terrain" layer
        int mask = LayerMask.GetMask("Terrain");
        bool prevBack = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = false;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask))
        {
            // Add vertex on Shift + Left-click
            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                polygonPoints.Add(hit.point);
                e.Use();
                SceneView.RepaintAll();
            }

            // Draw live edge
            if (polygonPoints.Count > 1)
            {
                Handles.DrawAAPolyLine(4, polygonPoints.ToArray());
                Handles.DrawLine(polygonPoints[^1], hit.point);
            }
        }

        Physics.queriesHitBackfaces = prevBack;

        // Close polygon on Enter if at least 3 points
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && polygonPoints.Count > 2)
        {
            isDrawing = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            e.Use();
        }

        // Fill if polygon is closed
        if (!isDrawing && polygonPoints.Count > 2)
        {
            Handles.DrawAAConvexPolygon(polygonPoints.ToArray());
            Handles.DrawSolidRectangleWithOutline(
                polygonPoints.ToArray(),
                new Color(0f, 0.5f, 1f, 0.3f),
                Color.clear
            );
        }

        HandleUtility.Repaint();
    }

    /// <summary>
    /// Prepares spawn samples inside the polygon and begins batched instantiation.
    /// </summary>
    private void GenerateFlora()
    {
        if (floraPrefabs.Count == 0)
        {
            Debug.LogWarning("No flora prefabs assigned.");
            return;
        }

        Bounds bounds = GetPolygonBounds();
        int sampleCount = Mathf.FloorToInt(bounds.size.x * bounds.size.z * density);

        // Parent for all instances
        GameObject parentGO = GameObject.Find("Generated Flora") ?? new GameObject("Generated Flora");
        Undo.RegisterCreatedObjectUndo(parentGO, "Generate Flora");

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogError("No active Terrain found.");
            return;
        }

        Vector3 origin = terrain.GetPosition();
        var rnd = new System.Random();
        _spawnSamples = new List<Vector3>(sampleCount);

        // Generate uniform random samples within bounds
        for (int i = 0; i < sampleCount; i++)
        {
            float x = (float)(bounds.min.x + rnd.NextDouble() * bounds.size.x);
            float z = (float)(bounds.min.z + rnd.NextDouble() * bounds.size.z);
            var sample = new Vector3(x, bounds.center.y, z);

            if (!IsPointInPolygon(sample, polygonPoints))
                continue;

            float y = terrain.SampleHeight(sample) + origin.y;
            _spawnSamples.Add(new Vector3(x, y, z));
        }

        _spawnParent = parentGO.transform;
        _spawnIndex = 0;
        EditorApplication.update += SpawnUpdate;
    }

    /// <summary>
    /// Batches instantiation to avoid editor freeze: spawns _batchSize per frame.
    /// </summary>
    private void SpawnUpdate()
    {
        var rnd = new System.Random();
        int end = Mathf.Min(_spawnIndex + _batchSize, _spawnSamples.Count);

        for (int i = _spawnIndex; i < end; i++)
        {
            // Randomly choose a prefab
            var prefab = floraPrefabs[rnd.Next(floraPrefabs.Count)];
            if (prefab == null) continue;

            // Instantiate under parent
            var inst = Object.Instantiate(prefab, _spawnSamples[i], Quaternion.identity, _spawnParent);

            // Apply scale variation
            float factor = 1f + ((float)rnd.NextDouble() * 2f - 1f) * scaleVariation;
            inst.transform.localScale = Vector3.one * factor;

            // Apply hue variation if material supports _Color
            var rend = inst.GetComponentInChildren<Renderer>();
            if (rend != null && rend.sharedMaterial.HasProperty("_Color"))
            {
                Color col = rend.sharedMaterial.color;
                Color.RGBToHSV(col, out float h, out float s, out float v);
                h = Mathf.Repeat(h + Random.Range(-hueVariation, hueVariation), 1f);
                rend.sharedMaterial.color = Color.HSVToRGB(h, s, v);
            }
        }

        _spawnIndex = end;

        // When done, cleanup and combine for performance
        if (_spawnIndex >= _spawnSamples.Count)
        {
            EditorApplication.update -= SpawnUpdate;
            ChunkAndCombine();
            polygonPoints.Clear();
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Divides instances into grid cells, combines meshes, and adds LODGroups.
    /// </summary>
    private void ChunkAndCombine()
    {
        const float cellSize = 20f;
        var parent = GameObject.Find("Generated Flora");
        if (parent == null) return;

        var buckets = new Dictionary<Vector2Int, List<Transform>>();

        // Group children by cell coordinate
        foreach (Transform t in parent.transform)
        {
            var p = t.position;
            var key = new Vector2Int(
                Mathf.FloorToInt(p.x / cellSize),
                Mathf.FloorToInt(p.z / cellSize)
            );
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<Transform>();
            list.Add(t);
        }

        // Combine per cell
        foreach (var kv in buckets)
        {
            GameObject cellGO = new GameObject($"FloraChunk_{kv.Key.x}_{kv.Key.y}")
            {
                transform = { parent = parent.transform }
            };
            var mf = cellGO.AddComponent<MeshFilter>();
            var mr = cellGO.AddComponent<MeshRenderer>();

            var combines = new List<CombineInstance>();
            Material sharedMat = null;

            // Collect mesh data
            foreach (var t in kv.Value)
            {
                var childMF = t.GetComponentInChildren<MeshFilter>();
                if (childMF == null) continue;

                combines.Add(new CombineInstance
                {
                    mesh = childMF.sharedMesh,
                    transform = childMF.transform.localToWorldMatrix
                });

                if (sharedMat == null)
                    sharedMat = t.GetComponentInChildren<Renderer>()?.sharedMaterial;

                Object.DestroyImmediate(t.gameObject);
            }

            if (sharedMat != null)
                mr.sharedMaterial = sharedMat;

            // Merge meshes
            var combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combines.ToArray(), true, true);
            mf.sharedMesh = combinedMesh;

            // Add LODGroup
            var lod = cellGO.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.5f, new[]{ mr }), // mid-range
                new LOD(0.1f, new[]{ mr }), // far-range
                new LOD(0f,  new Renderer[0]) // culled
            });
            lod.RecalculateBounds();
        }
    }

    /// <summary>
    /// Calculates axis-aligned bounding box of the polygon points.
    /// </summary>
    private Bounds GetPolygonBounds()
    {
        Vector3 min = polygonPoints[0], max = polygonPoints[0];
        foreach (var p in polygonPoints)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new Bounds((min + max) / 2f, max - min);
    }

    /// <summary>
    /// Ray-casting algorithm to test if a point lies inside a polygon.
    /// </summary>
    private bool IsPointInPolygon(Vector3 point, List<Vector3> poly)
    {
        bool inside = false;
        int count = poly.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((poly[i].z > point.z) != (poly[j].z > point.z)) &&
                (point.x < (poly[j].x - poly[i].x) * (point.z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Unsubscribes from SceneView events when tab is closed or toggled away.
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
