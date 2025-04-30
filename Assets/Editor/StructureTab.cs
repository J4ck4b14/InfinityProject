using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Provides an Editor tab for defining a city footprint polygon, picking an entry point,
/// and procedurally spawning and batching structures with automatic LOD setup.
/// </summary>
public class StructureTab
{
    // === Editor state ===

    /// <summary>List of structure definitions (prefab + metadata) to place.</summary>
    private readonly List<StructureEntry> structures = new List<StructureEntry>();

    /// <summary>Reorderable UI list to edit <see cref="structures"/>.</summary>
    private readonly ReorderableList structureList;

    /// <summary>Points defining the drawn city-area polygon.</summary>
    private readonly List<Vector3> polygonPoints = new List<Vector3>();

    /// <summary>Location where the city gate/entry will be placed.</summary>
    private Vector3? entryPoint = null;

    /// <summary>True while user is drawing the polygon.</summary>
    private bool isDrawing = false;

    /// <summary>True while user is selecting the entry point.</summary>
    private bool settingEntry = false;

    // === Batched spawn state ===

    private struct PendingSpawn
    {
        public StructureEntry entry;   // which entry to spawn
        public Vector3 position;       // where to spawn it
    }

    private List<PendingSpawn> _pendingSpawns;
    private int _spawnIndex;
    private const int _batchSize = 20; // number per editor frame

    /// <summary>
    /// Initializes the reorderable list callbacks and layout.
    /// </summary>
    public StructureTab()
    {
        // Configure the ReorderableList for our StructureEntry list
        structureList = new ReorderableList(structures, typeof(StructureEntry), true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Structure Prefabs"),
            elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 7 + EditorGUIUtility.standardVerticalSpacing * 6 + 4,
            drawElementCallback = (rect, index, _, __) =>
            {
                var entry = structures[index];
                float line = EditorGUIUtility.singleLineHeight + 2;

                // Prefab field
                entry.prefab = (GameObject)EditorGUI.ObjectField(
                    new Rect(rect.x, rect.y, rect.width, line),
                    new GUIContent("Prefab", "The building prefab to spawn"),
                    entry.prefab,
                    typeof(GameObject),
                    false);

                // Enum fields
                entry.type = (StructureType)EditorGUI.EnumPopup(
                    new Rect(rect.x, rect.y + line * 1, rect.width, line),
                    new GUIContent("Type", "Role of this structure in city"),
                    entry.type);

                entry.size = (StructureSize)EditorGUI.EnumPopup(
                    new Rect(rect.x, rect.y + line * 2, rect.width, line),
                    new GUIContent("Size", "Relative footprint size"),
                    entry.size);

                entry.affordability = (ClassLevel)EditorGUI.EnumPopup(
                    new Rect(rect.x, rect.y + line * 3, rect.width, line),
                    new GUIContent("Affordability", "Socioeconomic class this building suits"),
                    entry.affordability);

                // Numeric fields
                entry.populationCapacity = EditorGUI.IntField(
                    new Rect(rect.x, rect.y + line * 4, rect.width, line),
                    new GUIContent("Space", "Max inhabitants/employees"),
                    entry.populationCapacity);

                entry.scaleVariation = EditorGUI.Slider(
                    new Rect(rect.x, rect.y + line * 5, rect.width, line),
                    new GUIContent("Scale Var.", "Random size offset ± this fraction"),
                    entry.scaleVariation, 0f, 1f);

                entry.hueVariation = EditorGUI.Slider(
                    new Rect(rect.x, rect.y + line * 6, rect.width, line),
                    new GUIContent("Hue Var.", "Random color shift ± this fraction"),
                    entry.hueVariation, 0f, 1f);

                // Commit back
                structures[index] = entry;
            },
            onAddCallback = _ => structures.Add(new StructureEntry())
        };
    }

    /// <summary>
    /// Renders the tab UI in the custom Editor window.
    /// </summary>
    public void Draw()
    {
        EditorGUILayout.LabelField("Structures Configuration", EditorStyles.boldLabel);
        structureList.DoLayoutList();
        GUILayout.Space(10);

        // Draw polygon area button
        if (!isDrawing && GUILayout.Button(new GUIContent("Draw City Area", "Shift+Click to add points; Enter to close")))
            StartDrawing();

        // Pick entry-point button
        if (polygonPoints.Count > 2 && !settingEntry &&
            GUILayout.Button(new GUIContent("Pick City Entry Point", "Shift+Click to select entry gate")))
            StartSettingEntry();

        // Begin spawn button
        if (polygonPoints.Count > 2 && entryPoint.HasValue &&
            GUILayout.Button(new GUIContent("Generate Structures", "Spawn all structures at entry point")))
            BeginGenerateStructures();

        // Cancel entry selection
        if (settingEntry && GUILayout.Button("Cancel Entry Point"))
            CancelEntryPoint();
    }

    /// <summary>
    /// Starts listening for SceneView input to draw area polygon.
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
    /// Starts listening for SceneView input to pick entry point.
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
    /// Cancels entry-point picking mode and stops SceneView input.
    /// </summary>
    private void CancelEntryPoint()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        settingEntry = false;
    }

    /// <summary>
    /// Handles SceneView interactions: drawing poly, picking entry, rendering handles.
    /// </summary>
    private void OnSceneGUI(SceneView sceneView)
    {
        // Only raycast against Terrain layer to avoid unnecessary hits
        var mask = 1 << LayerMask.NameToLayer("Terrain");
        var e = Event.current;
        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        // Perform raycast
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask))
        {
            // SHIFT+LMB logic
            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                if (isDrawing)
                {
                    // close if near start
                    if (polygonPoints.Count > 2 && Vector3.Distance(hit.point, polygonPoints[0]) < 2f)
                    {
                        isDrawing = false;
                        SceneView.duringSceneGui -= OnSceneGUI;
                    }
                    else
                    {
                        polygonPoints.Add(hit.point);
                    }
                }
                else if (settingEntry)
                {
                    entryPoint = hit.point;
                    settingEntry = false;
                    SceneView.duringSceneGui -= OnSceneGUI;
                }

                e.Use();
                SceneView.RepaintAll();
            }

            // draw live edges
            Handles.color = Color.cyan;
            if (polygonPoints.Count > 1)
            {
                Handles.DrawAAPolyLine(4, polygonPoints.ToArray());
                Handles.DrawLine(polygonPoints[^1], hit.point);
            }
        }

        // Enter key: finalize polygon
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && polygonPoints.Count > 2)
        {
            isDrawing = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            e.Use();
        }

        // fill area when closed
        if (!isDrawing && polygonPoints.Count > 2)
        {
            Handles.color = new Color(1f, 0.5f, 0f, 0.25f);
            Handles.DrawAAConvexPolygon(polygonPoints.ToArray());
        }

        HandleUtility.Repaint();
    }

    /// <summary>
    /// Prepares batched spawning of structures at the selected entry point.
    /// </summary>
    private void BeginGenerateStructures()
    {
        // create spawn queue
        _pendingSpawns = new List<PendingSpawn>();
        foreach (var entry in structures)
            _pendingSpawns.Add(new PendingSpawn { entry = entry, position = entryPoint.Value });

        // ensure parent GameObject
        var parent = GameObject.Find("Generated City") ?? new GameObject("Generated City");
        Undo.RegisterCreatedObjectUndo(parent, "Generate Structures");

        _spawnIndex = 0;
        EditorApplication.update += SpawnStructuresBatch;
    }

    /// <summary>
    /// Spawns _batchSize structures per editor frame to keep UI responsive.
    /// </summary>
    private void SpawnStructuresBatch()
    {
        var parent = GameObject.Find("Generated City")?.transform;
        if (parent == null) return;

        int end = Mathf.Min(_spawnIndex + _batchSize, _pendingSpawns.Count);
        for (int i = _spawnIndex; i < end; i++)
        {
            var ps = _pendingSpawns[i];
            if (ps.entry.prefab == null) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(ps.entry.prefab);
            Undo.RegisterCreatedObjectUndo(go, "Spawn Structure");
            go.transform.position = ps.position;
            go.transform.parent = parent;

            // scale variation
            float s = 1f + Random.Range(-ps.entry.scaleVariation, ps.entry.scaleVariation);
            go.transform.localScale = Vector3.one * s;

            // hue variation
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
        if (_spawnIndex >= _pendingSpawns.Count)
        {
            // done spawning
            EditorApplication.update -= SpawnStructuresBatch;
            _pendingSpawns = null;
            ChunkAndAddLODs();
            polygonPoints.Clear();
            entryPoint = null;
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Groups spawned structures into spatial cells, combines meshes and adds an LODGroup.
    /// </summary>
    private void ChunkAndAddLODs()
    {
        const float cellSize = 30f;
        var parentGO = GameObject.Find("Generated City");
        if (parentGO == null) return;

        var buckets = new Dictionary<Vector2Int, List<Transform>>();
        foreach (Transform t in parentGO.transform)
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

        // combine per cell
        foreach (var kv in buckets)
        {
            var cell = new GameObject($"CityChunk_{kv.Key.x}_{kv.Key.y}")
            {
                transform = { parent = parentGO.transform }
            };
            var mf = cell.AddComponent<MeshFilter>();
            var mr = cell.AddComponent<MeshRenderer>();

            var combines = new List<CombineInstance>();
            Material mat = null;
            foreach (var t in kv.Value)
            {
                var childMF = t.GetComponentInChildren<MeshFilter>();
                if (childMF == null) continue;
                combines.Add(new CombineInstance { mesh = childMF.sharedMesh, transform = childMF.transform.localToWorldMatrix });
                if (mat == null)
                    mat = t.GetComponentInChildren<Renderer>()?.sharedMaterial;
                Object.DestroyImmediate(t.gameObject);
            }

            if (mat != null) mr.sharedMaterial = mat;
            var mesh = new Mesh();
            mesh.CombineMeshes(combines.ToArray(), true, true);
            mf.sharedMesh = mesh;

            // add LODGroup
            var lod = cell.AddComponent<LODGroup>();
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
    /// Represents one structure-to-spawn entry with metadata for clustering and color variation.
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
        [Tooltip("Max inhabitants/employees this building can hold")]
        public int populationCapacity;
        [Tooltip("Random size variation ± fraction")]
        public float scaleVariation;
        [Tooltip("Random color hue shift ± fraction")]
        public float hueVariation;
    }

    public enum StructureType
    {
        Wall, TownEntrance, Archway, Tabern, House, Manor, Slum,
        Castle, Church, Square, Farm, Market, Blacksmith, Watchtower
    }

    public enum StructureSize { None, Small, Medium, Large }
    public enum ClassLevel { King, Noble, Church, Bourgeoisie, Common, Peasant, Lowlife }

    /// <summary>
    /// Cleans up any active SceneView hooks or pending coroutines when tab is closed.
    /// </summary>
    public void Cleanup()
    {
        if (isDrawing || settingEntry)
            SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= SpawnStructuresBatch;
        isDrawing = settingEntry = false;
    }
}
