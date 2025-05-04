using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Editor tab for procedural generation of medieval‐style cities.
/// Allows defining a footprint polygon, picking an entry gate, and spawning structures in batches.
/// </summary>
public class StructureTab
{
    // === Inspector data ===

    /// <summary>List of building definitions to spawn.</summary>
    private readonly List<StructureEntry> structures = new List<StructureEntry>();

    /// <summary>Reorderable UI list to manage structures.</summary>
    private readonly ReorderableList structureList;

    /// <summary>Polygon points defining city boundary.</summary>
    private readonly List<Vector3> polygonPoints = new List<Vector3>();

    /// <summary>Selected entry‐gate location.</summary>
    private Vector3? entryPoint = null;

    private bool isDrawing = false; // drawing boundary
    private bool settingEntry = false; // picking entry

    // === Batched spawn state ===

    private struct PendingSpawn
    {
        public StructureEntry entry;
        public Vector3 position;
    }

    private List<PendingSpawn> _pendingSpawns;
    private int _spawnIndex;
    private const int _batchSize = 20;

    /// <summary>
    /// Constructor: sets up reorderable list callbacks for StructureEntry.
    /// </summary>
    public StructureTab()
    {
        structureList = new ReorderableList(structures, typeof(StructureEntry), true, true, true, true)
        {
            drawHeaderCallback = r => EditorGUI.LabelField(r, "Structure Prefabs"),
            elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 7 + 6,
            drawElementCallback = DrawEntry,
            onAddCallback = _ => structures.Add(new StructureEntry())
        };
    }

    /// <summary>
    /// Renders the Structures tab UI.
    /// </summary>
    public void Draw()
    {
        EditorGUILayout.LabelField("Structures Configuration", EditorStyles.boldLabel);
        structureList.DoLayoutList();

        GUILayout.Space(10);
        if (!isDrawing && GUILayout.Button("Draw City Area"))
            StartDrawing();
        if (polygonPoints.Count > 2 && !settingEntry && GUILayout.Button("Pick City Entry Point"))
            StartSettingEntry();
        if (polygonPoints.Count > 2 && entryPoint.HasValue && GUILayout.Button("Generate Structures"))
            BeginGenerateStructures();
        if (settingEntry && GUILayout.Button("Cancel Entry Point"))
            CancelEntryPoint();
    }

    /// <summary>
    /// Draw callback for each StructureEntry in the list.
    /// </summary>
    private void DrawEntry(Rect rect, int index, bool _, bool __)
    {
        var entry = structures[index];
        float lh = EditorGUIUtility.singleLineHeight + 2;

        entry.prefab = (GameObject)EditorGUI.ObjectField(
            new Rect(rect.x, rect.y, rect.width, lh),
            new GUIContent("Prefab", "Building prefab"),
            entry.prefab, typeof(GameObject), false);

        entry.type = (StructureType)EditorGUI.EnumPopup(
            new Rect(rect.x, rect.y + lh * 1, rect.width, lh),
            new GUIContent("Type", "Role in city"),
            entry.type);

        entry.size = (StructureSize)EditorGUI.EnumPopup(
            new Rect(rect.x, rect.y + lh * 2, rect.width, lh),
            new GUIContent("Size", "Footprint size"),
            entry.size);

        entry.affordability = (ClassLevel)EditorGUI.EnumPopup(
            new Rect(rect.x, rect.y + lh * 3, rect.width, lh),
            new GUIContent("Affordability", "Socioeconomic tier"),
            entry.affordability);

        entry.populationCapacity = EditorGUI.IntField(
            new Rect(rect.x, rect.y + lh * 4, rect.width, lh),
            new GUIContent("Space", "Max inhabitants"),
            entry.populationCapacity);

        entry.scaleVariation = EditorGUI.Slider(
            new Rect(rect.x, rect.y + lh * 5, rect.width, lh),
            new GUIContent("Scale Var.", "± fraction"),
            entry.scaleVariation, 0f, 1f);

        entry.hueVariation = EditorGUI.Slider(
            new Rect(rect.x, rect.y + lh * 6, rect.width, lh),
            new GUIContent("Hue Var.", "± fraction"),
            entry.hueVariation, 0f, 1f);

        structures[index] = entry; // write back
    }

    /// <summary>
    /// Begins listening to SceneView to draw footprint polygon.
    /// </summary>
    private void StartDrawing()
    {
        if (!isDrawing)
        {
            SceneView.duringSceneGui += OnSceneGUI;
            isDrawing = true;
        }
    }

    /// <summary>
    /// Begins listening to SceneView to pick entry‐gate location.
    /// </summary>
    private void StartSettingEntry()
    {
        if (!settingEntry)
        {
            SceneView.duringSceneGui += OnSceneGUI;
            settingEntry = true;
        }
    }

    /// <summary>
    /// Cancels entry‐point selection and unsubscribes.
    /// </summary>
    private void CancelEntryPoint()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        settingEntry = false;
    }

    /// <summary>
    /// Handles SceneView mouse/keyboard events for drawing and entry picking.
    /// Shift+Click to add; Enter to finalize polygon.
    /// </summary>
    private void OnSceneGUI(SceneView sv)
    {
        var mask = 1 << LayerMask.NameToLayer("Terrain");
        var e = Event.current;
        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        // Raycast against terrain
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask) &&
            e.type == EventType.MouseDown && e.button == 0 && e.shift)
        {
            // Drawing mode
            if (isDrawing)
            {
                // Close polygon if near start
                if (polygonPoints.Count > 2 &&
                    Vector3.Distance(hit.point, polygonPoints[0]) < 2f)
                {
                    isDrawing = false;
                    SceneView.duringSceneGui -= OnSceneGUI;
                }
                else
                {
                    polygonPoints.Add(hit.point);
                }
            }
            // Entry‐picking mode
            else if (settingEntry)
            {
                entryPoint = hit.point;
                settingEntry = false;
                SceneView.duringSceneGui -= OnSceneGUI;
            }

            e.Use();
            SceneView.RepaintAll();
        }

        // Live outline
        Handles.color = Color.cyan;
        if (polygonPoints.Count > 1)
        {
            Handles.DrawAAPolyLine(4, polygonPoints.ToArray());
            if (isDrawing)
                Handles.DrawLine(polygonPoints[^1], HandleUtility.GUIPointToWorldRay(e.mousePosition).origin);
        }

        // Finalize on Enter
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && polygonPoints.Count > 2)
        {
            isDrawing = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            e.Use();
        }

        // Fill polygon when closed
        if (!isDrawing && polygonPoints.Count > 2)
        {
            Handles.color = new Color(1f, 0.5f, 0f, 0.25f);
            Handles.DrawAAConvexPolygon(polygonPoints.ToArray());
        }

        HandleUtility.Repaint();
    }

    /// <summary>
    /// Queues up each StructureEntry for batched spawning at entryPoint.
    /// </summary>
    private void BeginGenerateStructures()
    {
        _pendingSpawns = new List<PendingSpawn>();
        foreach (var entry in structures)
            _pendingSpawns.Add(new PendingSpawn { entry = entry, position = entryPoint.Value });

        // Parent for spawned buildings
        var parent = GameObject.Find("Generated City") ?? new GameObject("Generated City");
        Undo.RegisterCreatedObjectUndo(parent, "Generate Structures");

        _spawnIndex = 0;
        EditorApplication.update += SpawnStructuresBatch;
    }

    /// <summary>
    /// Spawns up to _batchSize structures per frame to keep UI responsive.
    /// </summary>
    private void SpawnStructuresBatch()
    {
        var parentTrans = GameObject.Find("Generated City")?.transform;
        if (parentTrans == null) return;

        int end = Mathf.Min(_spawnIndex + _batchSize, _pendingSpawns.Count);
        for (int i = _spawnIndex; i < end; i++)
        {
            var ps = _pendingSpawns[i];
            if (ps.entry.prefab == null) continue;

            // Instantiate prefab
            var go = (GameObject)PrefabUtility.InstantiatePrefab(ps.entry.prefab);
            Undo.RegisterCreatedObjectUndo(go, "Spawn Structure");
            go.transform.position = ps.position;
            go.transform.parent = parentTrans;

            // Random scale
            float s = 1f + Random.Range(-ps.entry.scaleVariation, ps.entry.scaleVariation);
            go.transform.localScale = Vector3.one * s;

            // Random hue
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null && rend.sharedMaterial.HasProperty("_Color"))
            {
                Color c = rend.sharedMaterial.color;
                Color.RGBToHSV(c, out float h, out float sat, out float val);
                h = Mathf.Repeat(h + Random.Range(-ps.entry.hueVariation, ps.entry.hueVariation), 1f);
                rend.sharedMaterial.color = Color.HSVToRGB(h, sat, val);
            }
        }

        _spawnIndex = end;

        // Finish
        if (_spawnIndex >= _pendingSpawns.Count)
        {
            EditorApplication.update -= SpawnStructuresBatch;
            ChunkAndAddLODs();
            polygonPoints.Clear();
            entryPoint = null;
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Groups spawned buildings into grid cells, combines meshes, and adds LODGroups.
    /// </summary>
    private void ChunkAndAddLODs()
    {
        const float cellSize = 30f;
        var parentGO = GameObject.Find("Generated City");
        if (parentGO == null) return;

        var buckets = new Dictionary<Vector2Int, List<Transform>>();
        foreach (Transform t in parentGO.transform)
        {
            var pos = t.position;
            var key = new Vector2Int(
                Mathf.FloorToInt(pos.x / cellSize),
                Mathf.FloorToInt(pos.z / cellSize)
            );
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<Transform>();
            list.Add(t);
        }

        // Combine per cell
        foreach (var kv in buckets)
        {
            var cellGO = new GameObject($"CityChunk_{kv.Key.x}_{kv.Key.y}")
            {
                transform = { parent = parentGO.transform }
            };
            var mf = cellGO.AddComponent<MeshFilter>();
            var mr = cellGO.AddComponent<MeshRenderer>();

            var combines = new List<CombineInstance>();
            Material mat = null;
            foreach (var t in kv.Value)
            {
                var cmf = t.GetComponentInChildren<MeshFilter>();
                if (cmf == null) continue;
                combines.Add(new CombineInstance
                {
                    mesh = cmf.sharedMesh,
                    transform = cmf.transform.localToWorldMatrix
                });
                if (mat == null)
                    mat = t.GetComponentInChildren<Renderer>()?.sharedMaterial;
                Object.DestroyImmediate(t.gameObject);
            }

            if (mat != null)
                mr.sharedMaterial = mat;

            var mesh = new Mesh();
            mesh.CombineMeshes(combines.ToArray(), true, true);
            mf.sharedMesh = mesh;

            var lod = cellGO.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.5f, new[]{ mr }),
                new LOD(0.1f, new[]{ mr }),
                new LOD(0f,  new Renderer[0])
            });
            lod.RecalculateBounds();
        }
    }

    /// <summary>
    /// Represents one user‐defined structure with associated metadata.
    /// </summary>
    [System.Serializable]
    public struct StructureEntry
    {
        [Tooltip("The building prefab to spawn")]
        public GameObject prefab;
        [Tooltip("Role of this structure in the city")]
        public StructureType type;
        [Tooltip("Relative footprint size")]
        public StructureSize size;
        [Tooltip("Socioeconomic tier this building suits")]
        public ClassLevel affordability;
        [Tooltip("Maximum inhabitants/employees this building can hold")]
        public int populationCapacity;
        [Tooltip("Random size variation ± fraction")]
        public float scaleVariation;
        [Tooltip("Random color hue shift ± fraction")]
        public float hueVariation;
    }

    public enum StructureType { Wall, TownEntrance, Archway, Tabern, House, Manor, Slum, Castle, Church, Square, Farm, Market, Blacksmith, Watchtower }
    public enum StructureSize { None, Small, Medium, Large }
    public enum ClassLevel { King, Noble, Church, Bourgeoisie, Common, Peasant, Lowlife }

    /// <summary>
    /// Unsubscribes from any active SceneView or editor update callbacks.
    /// </summary>
    public void Cleanup()
    {
        if (isDrawing || settingEntry)
            SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= SpawnStructuresBatch;
        isDrawing = settingEntry = false;
    }
}
