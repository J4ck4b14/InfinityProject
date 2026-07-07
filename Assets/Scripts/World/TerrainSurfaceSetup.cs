using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Attach this to any Terrain that uses Terrain_Surface.shader.
/// Sets _TerrainBaseY and _TerrainHeight on the terrain material so the
/// shader knows the world-space height range and can compute correct layer blending.
///
/// Call RefreshMaterial() after generating or resizing the terrain,
/// or it runs automatically in OnEnable and in-editor via ExecuteAlways.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Terrain))]
public class TerrainSurfaceSetup : MonoBehaviour
{
    private static readonly int PropBaseY  = Shader.PropertyToID("_TerrainBaseY");
    private static readonly int PropHeight = Shader.PropertyToID("_TerrainHeight");

    private Terrain _terrain;

    private void OnEnable()
    {
        _terrain = GetComponent<Terrain>();
        RefreshMaterial();
    }

#if UNITY_EDITOR
    // Keep in sync when terrain size changes in editor
    private void Update()
    {
        if (!Application.isPlaying)
            RefreshMaterial();
    }
#endif

    public void RefreshMaterial()
    {
        if (_terrain == null) _terrain = GetComponent<Terrain>();
        if (_terrain == null) return;

        var mat = _terrain.materialTemplate;
        if (mat == null) return;

        float baseY  = transform.position.y;
        float height = _terrain.terrainData != null ? _terrain.terrainData.size.y : 150f;

        mat.SetFloat(PropBaseY,  baseY);
        mat.SetFloat(PropHeight, height);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TerrainSurfaceSetup))]
public class TerrainSurfaceSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("Refresh Material Now"))
            ((TerrainSurfaceSetup)target).RefreshMaterial();
    }
}
#endif
