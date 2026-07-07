using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// --------------------------------------─
// WaterBody - base component for all water instances (ocean, lake, river)
// --------------------------------------─

/// <summary>
/// Distinguishes how a water body behaves.
/// Ocean: locked to Y=0, Gerstner waves, foam at shores.
/// Lake:  arbitrary Y, calm ripple, no forced waves.
/// River: spline-driven, flowing current, handled by WaterRiver component.
/// </summary>
public enum WaterBodyType { Ocean, Lake, River }

/// <summary>
/// Shared runtime component on all water GameObjects.
/// Drives the material properties each frame based on type and settings.
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class WaterBody : MonoBehaviour
{
    [Header("Type")]
    public WaterBodyType bodyType = WaterBodyType.Ocean;

    [Header("Appearance")]
    public Color shallowColor     = new(0.12f, 0.45f, 0.62f, 0.85f);
    public Color deepColor        = new(0.02f, 0.15f, 0.35f, 0.95f);
    public float depthFadeDistance = 3f;

    [Header("Ocean / Lake Surface")]
    [Tooltip("Gerstner wave amplitude (Ocean only, set to 0 for Lake)")]
    public float waveAmplitude  = 0.4f;
    [Tooltip("Wave frequency")]
    public float waveFrequency  = 0.8f;
    [Tooltip("Wave speed")]
    public float waveSpeed      = 1.2f;
    [Tooltip("Number of Gerstner waves summed")]
    [Range(1, 4)]
    public int   waveCount      = 3;

    [Header("Lake Ripple")]
    public float rippleAmplitude = 0.04f;
    public float rippleSpeed     = 0.3f;

    [Header("Foam")]
    public bool  enableFoam       = true;
    [Tooltip("World-space distance from shore that foam appears")]
    public float foamDistance     = 1.5f;
    public float foamSpeed        = 0.6f;
    public Color foamColor        = new(0.92f, 0.95f, 0.98f, 0.9f);

    [Header("Reflection / Refraction (URP)")]
    public bool  planarReflection = false;

    [Header("Shore depth sampling")]
    [Tooltip("Terrain used to compute depth for foam/depth fade. Auto-found if null.")]
    public Terrain sourceTerrain;

    // - Runtime -------------------------------─
    private Material  _mat;
    private MeshFilter _mf;
    private static readonly int
        SpropShallowColor    = Shader.PropertyToID("_ShallowColor"),
        SpropDeepColor       = Shader.PropertyToID("_DeepColor"),
        SpropDepthFade       = Shader.PropertyToID("_DepthFade"),
        SpropWaveAmplitude   = Shader.PropertyToID("_WaveAmplitude"),
        SpropWaveFrequency   = Shader.PropertyToID("_WaveFrequency"),
        SpropWaveSpeed       = Shader.PropertyToID("_WaveSpeed"),
        SpropWaveCount       = Shader.PropertyToID("_WaveCount"),
        SpropFoamEnabled     = Shader.PropertyToID("_FoamEnabled"),
        SpropFoamDistance    = Shader.PropertyToID("_FoamDistance"),
        SpropFoamSpeed       = Shader.PropertyToID("_FoamSpeed"),
        SpropFoamColor       = Shader.PropertyToID("_FoamColor"),
        SpropTime            = Shader.PropertyToID("_WaterTime"),
        SpropBodyType        = Shader.PropertyToID("_BodyType");

    private void Awake()
    {
        _mf  = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();

        // Each instance gets its own material so we can set per-instance props
        _mat = new Material(WaterMaterials.GetMaterial(bodyType));
        mr.sharedMaterial = _mat;

        if (sourceTerrain == null) sourceTerrain = Terrain.activeTerrain;
    }

    private void Start()
    {
        // Ocean: snap to sea level
        if (bodyType == WaterBodyType.Ocean)
        {
            var pos = transform.position;
            pos.y = 0f;
            transform.position = pos;
        }
        PushMaterialProperties();
    }

    private void Update()
    {
        _mat.SetFloat(SpropTime, Time.time);

        // Live-edit support: re-push props in editor play mode
#if UNITY_EDITOR
        PushMaterialProperties();
#endif
    }

    public void PushMaterialProperties()
    {
        if (_mat == null) return;
        _mat.SetColor(SpropShallowColor,  shallowColor);
        _mat.SetColor(SpropDeepColor,     deepColor);
        _mat.SetFloat(SpropDepthFade,     depthFadeDistance);
        _mat.SetFloat(SpropBodyType,      (float)bodyType);

        // Wave params - zero out for lakes (calm)
        float amp  = bodyType == WaterBodyType.Lake ? rippleAmplitude : waveAmplitude;
        float freq = waveFrequency;
        float spd  = bodyType == WaterBodyType.Lake ? rippleSpeed      : waveSpeed;
        int   cnt  = bodyType == WaterBodyType.Lake ? 1                : waveCount;
        _mat.SetFloat(SpropWaveAmplitude,  amp);
        _mat.SetFloat(SpropWaveFrequency,  freq);
        _mat.SetFloat(SpropWaveSpeed,      spd);
        _mat.SetInt  (SpropWaveCount,      cnt);

        // Foam - off for lakes by default, on for ocean
        bool foam = enableFoam && bodyType != WaterBodyType.Lake;
        _mat.SetFloat(SpropFoamEnabled,   foam ? 1f : 0f);
        _mat.SetFloat(SpropFoamDistance,  foamDistance);
        _mat.SetFloat(SpropFoamSpeed,     foamSpeed);
        _mat.SetColor(SpropFoamColor,     foamColor);
    }
}

// --------------------------------------─
// WaterRiver - spline-driven river
// --------------------------------------─

/// <summary>
/// Defines a river using a list of control points.
/// The mesh is rebuilt whenever points change.
/// Points can be set manually (hybrid mode) or auto-traced from terrain hydraulics.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterRiver : MonoBehaviour
{
    [Header("Spline")]
    [Tooltip("River control points in world space. Y is set automatically unless overrideY is true.")]
    public List<Vector3> controlPoints = new();

    [Tooltip("If true, Y of each control point is respected as-is. " +
             "If false, Y is sampled from the terrain + a small offset.")]
    public bool overrideY = false;

    [Tooltip("How far above the terrain surface the river mesh sits.")]
    public float terrainOffset = 0.05f;

    [Header("Shape")]
    [Range(0.5f, 20f)] public float width = 4f;
    [Range(2, 64)]     public int   segmentsPerSpan = 8;

    [Header("Flow")]
    public float flowSpeed    = 0.8f;
    public float flowTurbulence = 0.3f;

    [Header("Appearance")]
    public Color  shallowColor = new(0.28f, 0.62f, 0.55f, 0.80f);
    public Color  deepColor    = new(0.05f, 0.22f, 0.35f, 0.92f);
    public float  foamDistance = 0.6f;

    private MeshFilter   _mf;
    private MeshRenderer _mr;
    private Material     _mat;
    private Terrain      _terrain;

    private static readonly int
        SpropFlowSpeed    = Shader.PropertyToID("_FlowSpeed"),
        SpropFlowTurb     = Shader.PropertyToID("_FlowTurbulence"),
        SpropShallow      = Shader.PropertyToID("_ShallowColor"),
        SpropDeep         = Shader.PropertyToID("_DeepColor"),
        SpropFoamDist     = Shader.PropertyToID("_FoamDistance"),
        SpropTime         = Shader.PropertyToID("_WaterTime");

    private void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mat = new Material(WaterMaterials.GetRiverMaterial());
        _mr.sharedMaterial = _mat;
        _terrain = Terrain.activeTerrain;
    }

    private void Start() => RebuildMesh();

    private void Update()
    {
        _mat.SetFloat(SpropTime, Time.time);
    }

    /// <summary>
    /// Rebuilds the river mesh from the current control points.
    /// Call this whenever points change (editor or runtime).
    /// </summary>
    public void RebuildMesh()
    {
        if (controlPoints == null || controlPoints.Count < 2) return;

        // Snap Y to terrain if not overriding
        var pts = new List<Vector3>(controlPoints);
        if (!overrideY && _terrain != null)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                p.y = _terrain.SampleHeight(p) + terrainOffset;
                pts[i] = p;
            }
        }

        // Build Catmull-Rom spline samples
        var splinePts = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Mathf.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
            for (int s = 0; s < segmentsPerSpan; s++)
            {
                float t = (float)s / segmentsPerSpan;
                splinePts.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        splinePts.Add(pts[pts.Count - 1]);

        // Build ribbon mesh
        int vCount = splinePts.Count * 2;
        var verts  = new Vector3[vCount];
        var uvs    = new Vector2[vCount];
        var tris   = new int[(splinePts.Count - 1) * 6];

        for (int i = 0; i < splinePts.Count; i++)
        {
            Vector3 forward = (i < splinePts.Count - 1)
                ? (splinePts[i + 1] - splinePts[i]).normalized
                : (splinePts[i] - splinePts[i - 1]).normalized;
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;

            verts[i * 2]     = splinePts[i] - right * width * 0.5f;
            verts[i * 2 + 1] = splinePts[i] + right * width * 0.5f;

            float v = (float)i / (splinePts.Count - 1);
            uvs[i * 2]     = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);
        }

        int ti = 0;
        for (int i = 0; i < splinePts.Count - 1; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
            tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
            tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
        }

        var mesh = new Mesh { name = "RiverMesh" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        _mf.sharedMesh = mesh;

        // Push material props
        _mat.SetFloat(SpropFlowSpeed,  flowSpeed);
        _mat.SetFloat(SpropFlowTurb,   flowTurbulence);
        _mat.SetColor(SpropShallow,    shallowColor);
        _mat.SetColor(SpropDeep,       deepColor);
        _mat.SetFloat(SpropFoamDist,   foamDistance);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (controlPoints == null || controlPoints.Count < 2) return;
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
        for (int i = 0; i < controlPoints.Count - 1; i++)
            Gizmos.DrawLine(controlPoints[i], controlPoints[i + 1]);
        Gizmos.color = Color.cyan;
        foreach (var p in controlPoints)
            Gizmos.DrawSphere(p, 0.4f);
    }
#endif
}

// --------------------------------------─
// WaterBodyFactory - creates water instances from editor or code
// --------------------------------------─

public static class WaterBodyFactory
{
    /// <summary>Creates an ocean plane centered at world origin, Y=0.</summary>
    public static WaterBody CreateOcean(float size = 2000f, string name = "Ocean")
    {
        var go  = new GameObject(name);
        var wb  = go.AddComponent<WaterBody>();
        wb.bodyType = WaterBodyType.Ocean;
        go.GetComponent<MeshFilter>().sharedMesh = BuildPlaneMesh(size, size, 128, 128);
        go.transform.position = Vector3.zero;
        return wb;
    }

    /// <summary>Creates a lake plane at the given world position and height.</summary>
    public static WaterBody CreateLake(Vector3 center, float width, float depth,
        float waterY = 0f, string name = "Lake")
    {
        var go  = new GameObject(name);
        var wb  = go.AddComponent<WaterBody>();
        wb.bodyType      = WaterBodyType.Lake;
        wb.waveAmplitude = 0f;
        wb.enableFoam    = false;
        go.GetComponent<MeshFilter>().sharedMesh = BuildPlaneMesh(width, depth, 32, 32);
        go.transform.position = new Vector3(center.x, waterY, center.z);
        return wb;
    }

    /// <summary>Creates an empty river GameObject ready for control points.</summary>
    public static WaterRiver CreateRiver(string name = "River")
    {
        var go = new GameObject(name);
        return go.AddComponent<WaterRiver>();
    }

    private static Mesh BuildPlaneMesh(float w, float d, int xSegs, int zSegs)
    {
        var verts = new Vector3[(xSegs + 1) * (zSegs + 1)];
        var uvs   = new Vector2[verts.Length];
        var tris  = new int[xSegs * zSegs * 6];
        int vi = 0, ti = 0;

        for (int z = 0; z <= zSegs; z++)
        for (int x = 0; x <= xSegs; x++)
        {
            float fx = (float)x / xSegs - 0.5f;
            float fz = (float)z / zSegs - 0.5f;
            verts[vi] = new Vector3(fx * w, 0f, fz * d);
            uvs[vi]   = new Vector2((float)x / xSegs, (float)z / zSegs);
            vi++;
        }

        for (int z = 0; z < zSegs; z++)
        for (int x = 0; x < xSegs; x++)
        {
            int a = z * (xSegs + 1) + x;
            int b = a + 1, c = a + (xSegs + 1), d2 = c + 1;
            tris[ti++]=a; tris[ti++]=c; tris[ti++]=b;
            tris[ti++]=b; tris[ti++]=c; tris[ti++]=d2;
        }

        var m = new Mesh { name = "WaterPlane" };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}

// --------------------------------------─
// Editor - WaterBody inspector
// --------------------------------------─

#if UNITY_EDITOR

[CustomEditor(typeof(WaterBody))]
public class WaterBodyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var wb = (WaterBody)target;
        EditorGUILayout.Space(6);
        if (GUILayout.Button("Refresh Material Properties"))
            wb.PushMaterialProperties();
    }
}

[CustomEditor(typeof(WaterRiver))]
public class WaterRiverEditor : Editor
{
    private WaterRiver _river;
    private bool _editingPoints = false;

    private void OnEnable()  => _river = (WaterRiver)target;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Mesh")) _river.RebuildMesh();
        _editingPoints = GUILayout.Toggle(_editingPoints,
            _editingPoints ? "◉ Editing Points" : "○ Edit Points",
            _editingPoints ? EditorStyles.miniButtonMid : EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Point"))
        {
            var last = _river.controlPoints.Count > 0
                ? _river.controlPoints[_river.controlPoints.Count - 1]
                : _river.transform.position;
            Undo.RecordObject(_river, "Add River Point");
            _river.controlPoints.Add(last + Vector3.forward * 5f);
            _river.RebuildMesh();
        }
        if (GUILayout.Button("Clear Points"))
        {
            Undo.RecordObject(_river, "Clear River Points");
            _river.controlPoints.Clear();
            _river.GetComponent<MeshFilter>().sharedMesh = null;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Auto-Trace From Terrain (hydraulic path)"))
            AutoTrace();

        if (_river.controlPoints.Count < 2)
            EditorGUILayout.HelpBox("Add at least 2 control points.", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        if (!_editingPoints || _river.controlPoints == null) return;

        Handles.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        for (int i = 0; i < _river.controlPoints.Count; i++)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(_river.controlPoints[i], Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_river, "Move River Point");
                _river.controlPoints[i] = newPos;
                _river.RebuildMesh();
            }

            // Label
            Handles.Label(_river.controlPoints[i] + Vector3.up * 1.5f,
                $"P{i}", EditorStyles.miniLabel);
        }

        // Draw spline preview
        if (_river.controlPoints.Count >= 2)
        {
            Handles.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            for (int i = 0; i < _river.controlPoints.Count - 1; i++)
                Handles.DrawLine(_river.controlPoints[i], _river.controlPoints[i + 1], 2f);
        }
    }

    /// <summary>
    /// Traces the steepest-descent path from the first control point
    /// across the active terrain, populating control points automatically.
    /// The user can then move individual points to override.
    /// </summary>
    private void AutoTrace()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        { EditorUtility.DisplayDialog("No Terrain", "An active terrain is required.", "OK"); return; }

        if (_river.controlPoints.Count == 0)
        { EditorUtility.DisplayDialog("No Start Point", "Add at least one control point as the river source.", "OK"); return; }

        Undo.RecordObject(_river, "Auto-Trace River");
        Vector3 start = _river.controlPoints[0];
        var td = terrain.terrainData;
        int hmRes = td.heightmapResolution;

        // Convert to heightmap coords
        Vector3 tp = terrain.transform.position;
        int hx = Mathf.Clamp(Mathf.RoundToInt((start.x - tp.x) / td.size.x * (hmRes - 1)), 0, hmRes - 1);
        int hy = Mathf.Clamp(Mathf.RoundToInt((start.z - tp.z) / td.size.z * (hmRes - 1)), 0, hmRes - 1);
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        var newPts = new List<Vector3> { start };
        int maxSteps = hmRes * 4;
        var visited  = new HashSet<(int, int)>();

        for (int step = 0; step < maxSteps; step++)
        {
            if (!visited.Add((hx, hy))) break;

            float cur = heights[hy, hx];
            float best = cur;
            int bx = hx, bz = hy;

            void Check(int nx, int nz)
            {
                if (nx < 0 || nz < 0 || nx >= hmRes || nz >= hmRes) return;
                float h = heights[nz, nx];
                if (h < best) { best = h; bx = nx; bz = nz; }
            }
            Check(hx+1,hy); Check(hx-1,hy); Check(hx,hy+1); Check(hx,hy-1);
            Check(hx+1,hy+1); Check(hx-1,hy+1); Check(hx+1,hy-1); Check(hx-1,hy-1);

            if (bx == hx && bz == hy) break; // local minimum - end of river

            hx = bx; hy = bz;

            float wx = tp.x + (float)hx / (hmRes - 1) * td.size.x;
            float wz = tp.z + (float)hy / (hmRes - 1) * td.size.z;
            float wy = terrain.SampleHeight(new Vector3(wx, 0, wz)) + _river.terrainOffset;
            newPts.Add(new Vector3(wx, wy, wz));

            // Thin out intermediate points - add every N steps to avoid huge arrays
            if (newPts.Count > 2 && step % 8 != 0)
                newPts.RemoveAt(newPts.Count - 1);

            if (best <= 0.001f) break; // reached sea level
        }

        _river.controlPoints = newPts;
        _river.RebuildMesh();
        SceneView.RepaintAll();
    }
}

/// <summary>
/// Menu items to quickly create water objects from the Unity menu.
/// </summary>
public static class WaterMenuItems
{
    [MenuItem("GameObject/Water/Ocean", false, 10)]
    static void CreateOcean()
    {
        var wb = WaterBodyFactory.CreateOcean();
        Undo.RegisterCreatedObjectUndo(wb.gameObject, "Create Ocean");
        Selection.activeGameObject = wb.gameObject;
    }

    [MenuItem("GameObject/Water/Lake", false, 11)]
    static void CreateLake()
    {
        Vector3 center = SceneView.lastActiveSceneView?.pivot ?? Vector3.zero;
        var wb = WaterBodyFactory.CreateLake(center, 80f, 60f, center.y);
        Undo.RegisterCreatedObjectUndo(wb.gameObject, "Create Lake");
        Selection.activeGameObject = wb.gameObject;
    }

    [MenuItem("GameObject/Water/River", false, 12)]
    static void CreateRiver()
    {
        var wr = WaterBodyFactory.CreateRiver();
        Undo.RegisterCreatedObjectUndo(wr.gameObject, "Create River");
        Selection.activeGameObject = wr.gameObject;
    }
}

#endif
