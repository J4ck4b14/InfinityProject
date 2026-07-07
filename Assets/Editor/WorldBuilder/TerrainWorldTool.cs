using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

[Overlay(typeof(SceneView), "Terrain Tool", true)]
[Icon("d_Terrain Icon")]
public class TerrainWorldTool : IMGUIOverlay, ITransientOverlay
{
    public bool visible => GetActiveTerrain() != null;

    private float _seed      = 0f;
    private float _scale     = 600f;
    private int   _resIndex  = 2;
    private float _terrainW  = 1000f;
    private float _terrainH  = 150f;

    // Sea
    private float _waterLevel    = 0.3f;
    private float _seabedDepth   = 0.15f;
    private int   _oceanMask     = 0;   // bitfield: 1=North 2=South 4=East 8=West
    private int   _beachFade     = 80;  // metres

    private bool  _generating = false;

    public override void OnCreated()    => Selection.selectionChanged += Repaint;
    public override void OnWillBeDestroyed() => Selection.selectionChanged -= Repaint;

    private void Repaint() => SceneView.RepaintAll();

    public override void OnGUI()
    {
        Terrain terrain = GetActiveTerrain();
        if (terrain == null) return;

        EditorGUILayout.BeginVertical(GUILayout.Width(200));

        EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
        _seed     = EditorGUILayout.FloatField("Seed",       _seed);
        _scale    = EditorGUILayout.Slider("Scale (m)", _scale, 100f, 2000f);
        _resIndex = EditorGUILayout.Popup("Resolution", _resIndex, new[]{"128","256","512","1024"});
        _terrainW = EditorGUILayout.FloatField("Width (m)",  _terrainW);
        _terrainH = EditorGUILayout.FloatField("Height (m)", _terrainH);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Sea", EditorStyles.boldLabel);
        _waterLevel  = EditorGUILayout.Slider("Water Level",   _waterLevel,  0f,    1f);
        _seabedDepth = EditorGUILayout.Slider("Seabed Depth",  _seabedDepth, 0.01f, 0.5f);

        // Ocean edges as individual toggles
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Ocean Edges");
        if (GUILayout.Toggle((_oceanMask&1)!=0, "N", EditorStyles.miniButtonLeft,  GUILayout.Width(26))) _oceanMask|=1; else _oceanMask&= ~1;
        if (GUILayout.Toggle((_oceanMask&2)!=0, "S", EditorStyles.miniButtonMid,   GUILayout.Width(26))) _oceanMask|=2; else _oceanMask&= ~2;
        if (GUILayout.Toggle((_oceanMask&4)!=0, "E", EditorStyles.miniButtonMid,   GUILayout.Width(26))) _oceanMask|=4; else _oceanMask&= ~4;
        if (GUILayout.Toggle((_oceanMask&8)!=0, "W", EditorStyles.miniButtonRight, GUILayout.Width(26))) _oceanMask|=8; else _oceanMask&= ~8;
        EditorGUILayout.EndHorizontal();

        if (_oceanMask != 0)
            _beachFade = EditorGUILayout.IntSlider("Beach Fade (m)", _beachFade, 10, 300);

        EditorGUILayout.Space(6);

        using (new EditorGUI.DisabledScope(_generating))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(32)))
                Generate(terrain);
        }

        EditorGUILayout.EndVertical();
    }

    private void Generate(Terrain terrain)
    {
        int[] res = { 128, 256, 512, 1024 };
        int   r   = res[_resIndex];

        _generating = true;
        try
        {
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Generate Terrain");
            var heights = TerrainGenerator.Generate(r, _seed, _scale,
                _waterLevel, _seabedDepth, _oceanMask, _beachFade, _terrainW);
            TerrainGenerator.ApplyToTerrain(terrain, heights, r, _terrainW, _terrainH, _terrainW);
        }
        catch (System.Exception ex) { Debug.LogException(ex); }
        finally { _generating = false; }
    }

    private static Terrain GetActiveTerrain()
    {
        var go = Selection.activeGameObject;
        return go != null ? go.GetComponent<Terrain>() : null;
    }
}
