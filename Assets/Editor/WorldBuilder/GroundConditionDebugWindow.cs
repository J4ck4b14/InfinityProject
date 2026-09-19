using InfinityProject.World.Ground;
using UnityEditor;
using UnityEngine;

public sealed class GroundConditionDebugWindow : EditorWindow
{
    private enum GroundMap { RelativeTwi, RunOnPotential, WaterAvailability, SoilRetention }
    private GroundConditionData _data; private Texture2D _texture; private Vector2 _scroll; private GroundMap _map = GroundMap.WaterAvailability;
    public static GroundConditionDebugWindow Open(GroundConditionData data) { var w = GetWindow<GroundConditionDebugWindow>("Ground Conditions Inspector"); w.minSize = new Vector2(380f, 350f); w.SetData(data); w.Show(); return w; }
    private void OnDisable() => DestroyTexture();
    private void SetData(GroundConditionData data) { _data = data; RebuildTexture(); Repaint(); }
    private void OnGUI()
    {
        if (_data == null) { EditorGUILayout.HelpBox("Generate Ground Conditions first.", MessageType.Info); return; }
        EditorGUILayout.Space(6f); EditorGUILayout.LabelField("Infinity Ground Conditions", EditorStyles.boldLabel);
        GroundMap next = (GroundMap)GUILayout.Toolbar((int)_map, new[] { "Relative TWI", "Run-on", "Water", "Retention" });
        if (next != _map) { _map = next; RebuildTexture(); }
        string name = _map switch { GroundMap.RelativeTwi => "Relative TWI (display only)", GroundMap.RunOnPotential => "Run-on Potential", GroundMap.WaterAvailability => "Water Availability Potential", _ => "Soil Retention Potential" };
        EditorGUILayout.LabelField($"{name} | {_data.Resolution}² | {_data.WidthMeters:0} × {_data.LengthMeters:0} m", EditorStyles.miniLabel);
        EditorGUILayout.Space(4f); _scroll = EditorGUILayout.BeginScrollView(_scroll); DrawMap(); EditorGUILayout.EndScrollView(); EditorGUILayout.Space(4f);
        string help = _map switch
        {
            GroundMap.RelativeTwi => "Relative TWI maximizes within-world drainage contrast. It is diagnostic only and is not an ecological moisture percentage.",
            GroundMap.RunOnPotential => "Run-on Potential is driven by excess physical contributing area above a fixed local support area. It isolates terrain concentration from climatic supply.",
            GroundMap.WaterAvailability => "Water Availability combines direct Climate precipitation potential with the additional supply made possible by run-on. This is Flora's water input in V0.4; it is still a potential, not volumetric soil moisture.",
            _ => "Soil Retention is derived from Ground-scale landform slope. Fine Geography corrugation remains available to other systems but no longer directly defines ecological retention."
        };
        EditorGUILayout.HelpBox(help, MessageType.None);
        if (_map == GroundMap.RelativeTwi) EditorGUILayout.LabelField($"TWI min {_data.MinimumWetnessIndex:0.###} | p05 {_data.Percentile05WetnessIndex:0.###} | p95 {_data.Percentile95WetnessIndex:0.###} | max {_data.MaximumWetnessIndex:0.###}", EditorStyles.miniLabel);
    }
    private void DrawMap() { if (_texture == null) return; float w = Mathf.Max(position.width - 24f, 64f), h = w * (_data.LengthMeters / _data.WidthMeters); Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(true)); EditorGUI.DrawPreviewTexture(r, _texture, null, ScaleMode.StretchToFill); }
    private void RebuildTexture()
    {
        DestroyTexture(); if (_data == null) return; int n = _data.Resolution;
        _texture = new Texture2D(n, n, TextureFormat.RGB24, false, true) { name = $"Infinity Ground Debug — {_map}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var p = new Color32[n * n];
        for (int i = 0; i < p.Length; i++) p[i] = _map switch
        {
            GroundMap.RelativeTwi => (Color32)WetnessColor(_data.GetRelativeWetness01(i)),
            GroundMap.RunOnPotential => (Color32)RunOnColor(_data.RunOnPotential[i]),
            GroundMap.WaterAvailability => (Color32)WaterColor(_data.WaterAvailabilityPotential[i]),
            _ => (Color32)RetentionColor(_data.SoilRetentionPotential[i])
        };
        _texture.SetPixels32(p); _texture.Apply(false, true);
    }
    internal static Color WetnessColor(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? Color.Lerp(new Color(0.18f, 0.07f, 0.025f), new Color(0.35f, 0.42f, 0.18f), t * 2f) : Color.Lerp(new Color(0.35f, 0.42f, 0.18f), new Color(0.02f, 0.9f, 1f), (t - 0.5f) * 2f); }
    internal static Color RunOnColor(float t) { t = Mathf.Clamp01(t); return Color.Lerp(new Color(0.12f, 0.10f, 0.07f), new Color(0.05f, 0.72f, 0.95f), t); }
    internal static Color WaterColor(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? Color.Lerp(new Color(0.58f, 0.28f, 0.08f), new Color(0.32f, 0.62f, 0.25f), t * 2f) : Color.Lerp(new Color(0.32f, 0.62f, 0.25f), new Color(0.05f, 0.82f, 0.95f), (t - 0.5f) * 2f); }
    internal static Color RetentionColor(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? Color.Lerp(new Color(0.13f, 0.14f, 0.16f), new Color(0.42f, 0.28f, 0.12f), t * 2f) : Color.Lerp(new Color(0.42f, 0.28f, 0.12f), new Color(0.33f, 0.56f, 0.20f), (t - 0.5f) * 2f); }
    private void DestroyTexture() { if (_texture != null) DestroyImmediate(_texture); _texture = null; }
}
