using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

using TEventType = UnityEngine.EventType;

/// <summary>
/// Read-only 2D overview of the current terrain heightmap.
/// Opens on demand from TerrainWorldTool. Updates on mouse-up after brush strokes.
/// Zoomable, pannable, greyscale + water tint. Snapshots and favourites on the right.
/// </summary>
public class TerrainMapWindow : EditorWindow
{
    public static TerrainMapWindow Open(Terrain terrain)
    {
        var w = CreateWindow<TerrainMapWindow>("Map View");
        w.minSize = new Vector2(700, 460);
        w._terrain = terrain;
        w.RefreshFromTerrain(terrain);
        return w;
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private Terrain   _terrain;
    private Texture2D _cpuTex;
    private RenderTexture _rt;
    private const int RT_W = 1024, RT_H = 1024;

    // Viewport
    private Vector2 _pan  = Vector2.zero;
    private float   _zoom = 1f;
    private const float MinZoom = 0.25f, MaxZoom = 32f;

    // Snapshots (last 4)
    private const int MaxSnaps = 4;
    private struct Snap { public Texture2D thumb; public float[] heights; public int res; public string label; }
    private readonly Snap[] _snaps = new Snap[MaxSnaps];
    private int _snapHead = 0;

    // Favourites
    private const string FavFolder = "Assets/WorldBuilderSaved/Maps";
    private List<SavedHeightmap> _favs = new();
    private Vector2 _sideScroll;
    private const float SidePanelW = 180f;

    // ─────────────────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        wantsMouseMove = true;
        LoadFavourites();
    }

    private void OnDisable()
    {
        if (_rt     != null) { _rt.Release(); DestroyImmediate(_rt); }
        if (_cpuTex != null) DestroyImmediate(_cpuTex);
        for (int i=0;i<MaxSnaps;i++)
            if (_snaps[i].thumb != null) DestroyImmediate(_snaps[i].thumb);
    }

    private void OnGUI()
    {
        if (_terrain == null)
        {
            EditorGUILayout.HelpBox("No terrain. Select one in the scene.", MessageType.Info);
            return;
        }

        HandleKeyboard();

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

        // Main canvas
        Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawCanvas(canvasRect);
        HandleCanvasInput(canvasRect);

        // Right panel
        DrawSidePanel();

        EditorGUILayout.EndHorizontal();

        // Bottom bar
        DrawBottomBar();
    }

    // ─────────────────────────────────────────────────────────────────────────
    #region Refresh

    public void RefreshFromTerrain(Terrain terrain)
    {
        if (terrain == null) return;
        _terrain = terrain;

        var td    = terrain.terrainData;
        int hmRes = td.heightmapResolution;
        int res   = hmRes - 1;
        var raw   = td.GetHeights(0, 0, hmRes, hmRes);
        float wl  = 0.30f; // default visual water level for display

        // Try to read from HeightColor shader property if it exists
        var mat = terrain.materialTemplate;
        if (mat != null && mat.HasProperty("_WaterLevel"))
            wl = mat.GetFloat("_WaterLevel");

        EnsureCPUTex(res);
        var cols = new Color[res * res];
        for (int y=0;y<res;y++) for (int x=0;x<res;x++)
        {
            float h = raw[y, x];
            if (h < wl)
            {
                float d = h / Mathf.Max(wl, 0.001f);
                cols[y*res+x] = new Color(d*0.12f, d*0.30f, 0.50f+d*0.28f);
            }
            else
            {
                float g = (h-wl)/Mathf.Max(1f-wl,0.001f);
                cols[y*res+x] = new Color(g,g,g);
            }
        }
        _cpuTex.SetPixels(cols);
        _cpuTex.Apply();

        EnsureRT();
        Graphics.Blit(_cpuTex, _rt);

        // Auto-snapshot
        PushSnapshot(raw, hmRes-1);
        Repaint();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Canvas

    private void DrawCanvas(Rect rect)
    {
        if (Event.current.type != TEventType.Repaint) return;
        if (_rt == null) { EditorGUI.DrawRect(rect, new Color(0.15f,0.15f,0.15f)); return; }
        GUI.DrawTexture(GetImageRect(rect), _rt, ScaleMode.StretchToFill, false);

        // Axis labels
        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1,1,1,0.4f) } };
        GUI.Label(new Rect(rect.x+4, rect.yMax-18, 80, 16), "← West  East ->", style);
        GUI.Label(new Rect(rect.x+4, rect.y+2,     80, 16), "↑ North",         style);
    }

    private void HandleCanvasInput(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (e.type == TEventType.ScrollWheel)
        {
            Rect img = GetImageRect(canvasRect);
            Vector2 beforeUV = ScreenToUV(e.mousePosition, img);
            _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 0.85f : 1.18f), MinZoom, MaxZoom);
            Rect newImg = GetImageRect(canvasRect);
            Vector2 afterScreen = new Vector2(newImg.x + beforeUV.x*newImg.width, newImg.y + beforeUV.y*newImg.height);
            _pan += e.mousePosition - afterScreen;
            e.Use(); Repaint();
        }
        else if (e.type == TEventType.MouseDrag && (e.button==2||(e.button==0&&e.alt)))
        {
            _pan += e.delta; e.Use(); Repaint();
        }
    }

    private Rect GetImageRect(Rect canvas)
    {
        float sz = Mathf.Min(canvas.width, canvas.height) * _zoom;
        float cx = canvas.x + canvas.width*0.5f + _pan.x;
        float cy = canvas.y + canvas.height*0.5f + _pan.y;
        return new Rect(cx-sz*0.5f, cy-sz*0.5f, sz, sz);
    }

    private Vector2 ScreenToUV(Vector2 sp, Rect img)
        => new Vector2((sp.x-img.x)/img.width, (sp.y-img.y)/img.height);

    private void HandleKeyboard()
    {
        var e = Event.current;
        if (e.type == TEventType.KeyDown && e.keyCode == KeyCode.F)
        { _zoom=1f; _pan=Vector2.zero; Repaint(); e.Use(); }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Side Panel

    private void DrawSidePanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(SidePanelW), GUILayout.ExpandHeight(true));
        _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll);

        // Snapshots
        EditorGUILayout.LabelField("Recent", EditorStyles.boldLabel);
        for (int i=0;i<MaxSnaps;i++)
        {
            int idx = ((_snapHead-1-i)+MaxSnaps*2)%MaxSnaps;
            var s = _snaps[idx];
            if (s.thumb == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(s.thumb, GUILayout.Width(SidePanelW-16), GUILayout.Height(SidePanelW-16));
            EditorGUILayout.LabelField(s.label, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Restore", GUILayout.Height(18))) RestoreSnapshot(s);
            if (GUILayout.Button("★", GUILayout.Width(22), GUILayout.Height(18))) PromptSaveFavourite(s);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        EditorGUILayout.Space(6);

        // Favourites
        EditorGUILayout.LabelField("Favourites", EditorStyles.boldLabel);
        if (GUILayout.Button("Save Current")) PromptSaveFavourite(null);
        GUILayout.Space(2);
        if (_favs.Count == 0)
            EditorGUILayout.LabelField("None saved.", EditorStyles.miniLabel);
        foreach (var f in _favs)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(f.displayName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Load", GUILayout.Width(38), GUILayout.Height(18))) LoadFavourite(f);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawBottomBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField($"Terrain: {(_terrain ? _terrain.name : "-")}  |  Zoom: {_zoom:F1}×  |  F = Fit", EditorStyles.miniLabel);
        if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(30)))
        { _zoom=1f; _pan=Vector2.zero; Repaint(); }
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Snapshots

    private void PushSnapshot(float[,] raw, int res)
    {
        var heights = new float[res*res];
        for (int y=0;y<res;y++) for (int x=0;x<res;x++) heights[y*res+x] = raw[y,x];

        int ts = 64;
        if (_snaps[_snapHead].thumb == null)
            _snaps[_snapHead].thumb = new Texture2D(ts, ts, TextureFormat.RGB24, false);
        var t = _snaps[_snapHead].thumb;
        var cols = new Color[ts*ts];
        float sc = (float)(res-1)/(ts-1);
        float wl = 0.30f;
        for (int ty=0;ty<ts;ty++) for (int tx=0;tx<ts;tx++)
        {
            float fx=tx*sc, fy=ty*sc;
            int ix=Mathf.Min((int)fx,res-2), iy=Mathf.Min((int)fy,res-2);
            float h = Mathf.Lerp(
                Mathf.Lerp(heights[iy*res+ix],     heights[iy*res+ix+1],     fx-ix),
                Mathf.Lerp(heights[(iy+1)*res+ix], heights[(iy+1)*res+ix+1], fx-ix), fy-iy);
            cols[ty*ts+tx] = h < wl
                ? new Color(0.08f, 0.22f, 0.50f)
                : new Color(h,h,h);
        }
        t.SetPixels(cols); t.Apply();

        _snaps[_snapHead].heights = heights;
        _snaps[_snapHead].res     = res;
        _snaps[_snapHead].label   = System.DateTime.Now.ToString("HH:mm:ss");
        _snapHead = (_snapHead+1)%MaxSnaps;
    }

    private void RestoreSnapshot(Snap s)
    {
        if (_terrain == null || s.heights == null) return;
        Undo.RegisterCompleteObjectUndo(_terrain.terrainData, "Restore Snapshot");
        TerrainGenerator.ApplyToTerrain(_terrain, s.heights, s.res,
            _terrain.terrainData.size.x, _terrain.terrainData.size.y, _terrain.terrainData.size.z);
        RefreshFromTerrain(_terrain);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Favourites

    private void PromptSaveFavourite(Snap? snap)
    {
        FavNamePopup.Show(System.DateTime.Now.ToString("Map_yyyyMMdd_HHmm"), name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!AssetDatabase.IsValidFolder("Assets/WorldBuilderSaved"))
                AssetDatabase.CreateFolder("Assets","WorldBuilderSaved");
            if (!AssetDatabase.IsValidFolder(FavFolder))
                AssetDatabase.CreateFolder("Assets/WorldBuilderSaved","Maps");

            var saved = ScriptableObject.CreateInstance<SavedHeightmap>();
            saved.displayName = name;
            saved.createdTime = System.DateTime.Now.ToOADate();

            if (snap.HasValue && snap.Value.heights != null)
            {
                saved.heights = (float[])snap.Value.heights.Clone();
                saved.heightmapResolution = snap.Value.res + 1;
            }
            else if (_terrain != null)
            {
               // saved.heights = TerrainGenerator.ReadFromTerrain(_terrain);
                saved.heightmapResolution = _terrain.terrainData.heightmapResolution;
                saved.terrainSide   = _terrain.terrainData.size.x;
                saved.terrainHeight = _terrain.terrainData.size.y;
                saved.terrainPosition = _terrain.transform.position;
            }

            string path = $"{FavFolder}/{name}.asset";
            AssetDatabase.CreateAsset(saved, path);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            LoadFavourites();
        });
    }

    private void LoadFavourite(SavedHeightmap f)
    {
        if (_terrain == null || f.heights == null) return;
        Undo.RegisterCompleteObjectUndo(_terrain.terrainData, "Load Favourite");
        int res = f.heightmapResolution - 1;
        TerrainGenerator.ApplyToTerrain(_terrain, f.heights, res,
            f.terrainSide > 0 ? f.terrainSide : _terrain.terrainData.size.x,
            f.terrainHeight > 0 ? f.terrainHeight : _terrain.terrainData.size.y,
            f.terrainSide > 0 ? f.terrainSide : _terrain.terrainData.size.z);
        RefreshFromTerrain(_terrain);
    }

    private void LoadFavourites()
    {
        _favs.Clear();
        if (!AssetDatabase.IsValidFolder(FavFolder)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:SavedHeightmap", new[]{FavFolder}))
        {
            var f = AssetDatabase.LoadAssetAtPath<SavedHeightmap>(AssetDatabase.GUIDToAssetPath(guid));
            if (f != null) _favs.Add(f);
        }
        _favs.Sort((a,b) => b.createdTime.CompareTo(a.createdTime));
    }

    private void EnsureCPUTex(int res)
    {
        if (_cpuTex != null && _cpuTex.width == res) return;
        if (_cpuTex != null) DestroyImmediate(_cpuTex);
        _cpuTex = new Texture2D(res, res, TextureFormat.RGB24, false)
            { filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp };
    }

    private void EnsureRT()
    {
        if (_rt != null && _rt.width == RT_W) return;
        if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
        _rt = new RenderTexture(RT_W, RT_H, 0, RenderTextureFormat.ARGB32)
            { filterMode=FilterMode.Bilinear };
        _rt.Create();
    }

    // Inline name popup
    private class FavNamePopup : EditorWindow
    {
        private System.Action<string> _cb; private string _name="";
        public static void Show(string def, System.Action<string> cb)
        {
            var w = CreateInstance<FavNamePopup>();
            w._cb=cb; w._name=def;
            w.titleContent=new GUIContent("Save Favourite");
            w.position=new Rect(Screen.width/2f-190, Screen.height/2f-30, 380, 60);
            w.ShowModalUtility();
        }
        private void OnGUI()
        {
            _name = EditorGUILayout.TextField("Name", _name);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))   { _cb?.Invoke(_name); Close(); }
            if (GUILayout.Button("Cancel")) Close();
            EditorGUILayout.EndHorizontal();
        }
    }

    #endregion
}
