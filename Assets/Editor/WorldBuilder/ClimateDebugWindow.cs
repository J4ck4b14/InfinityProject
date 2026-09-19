using InfinityProject.World.Climate;
using UnityEditor;
using UnityEngine;

public sealed class ClimateDebugWindow : EditorWindow
{
    private enum ClimateMap { Temperature, Precipitation }
    private ClimateData _data;
    private ClimateMap _map;
    private Texture2D _texture;
    private Vector2 _scroll;

    public static ClimateDebugWindow Open(ClimateData data)
    {
        var window = GetWindow<ClimateDebugWindow>("Climate Inspector");
        window.minSize = new Vector2(340f, 350f);
        window.SetData(data);
        window.Show();
        return window;
    }

    private void OnDisable() => DestroyTexture();

    private void SetData(ClimateData data)
    {
        _data = data;
        RebuildTexture();
        Repaint();
    }

    private void OnGUI()
    {
        if (_data == null)
        {
            EditorGUILayout.HelpBox("Generate Climate first.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Climate V0", EditorStyles.boldLabel);
        ClimateMap newMap = (ClimateMap)GUILayout.Toolbar((int)_map, new[] { "Temperature", "Precipitation" });
        if (newMap != _map)
        {
            _map = newMap;
            RebuildTexture();
        }

        EditorGUILayout.LabelField(
            $"{_map} | {_data.Resolution}² | {_data.WidthMeters:0} × {_data.LengthMeters:0} m",
            EditorStyles.miniLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMap();
        EditorGUILayout.EndScrollView();

        if (_map == ClimateMap.Temperature)
        {
            EditorGUILayout.HelpBox(
                "Temperature uses a fixed diagnostic range (-20°C to 40°C), so colours remain comparable across worlds and resolutions.",
                MessageType.None);
            EditorGUILayout.LabelField($"Range: {_data.MinimumTemperatureCelsius:0.0}..{_data.MaximumTemperatureCelsius:0.0} °C", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Precipitation Potential is dimensionless 0..1. It expresses static relative atmospheric supply/exposure, not rainfall in millimetres.",
                MessageType.None);
            EditorGUILayout.LabelField($"Range: {_data.MinimumPrecipitationPotential:0.000}..{_data.MaximumPrecipitationPotential:0.000}", EditorStyles.miniLabel);
        }
    }

    private void DrawMap()
    {
        if (_texture == null) return;
        float width = Mathf.Max(position.width - 24f, 64f);
        float height = width * (_data.LengthMeters / _data.WidthMeters);
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(rect, _texture, null, ScaleMode.StretchToFill);
    }

    private void RebuildTexture()
    {
        DestroyTexture();
        if (_data == null) return;
        _texture = new Texture2D(_data.Resolution, _data.Resolution, TextureFormat.RGB24, false, true)
        {
            name = $"Infinity Climate Debug — {_map}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[_data.Resolution * _data.Resolution];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = _map == ClimateMap.Temperature
                ? (Color32)TemperatureColor(_data.TemperatureCelsius[i])
                : (Color32)PrecipitationColor(_data.PrecipitationPotential[i]);
        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
    }

    internal static Color TemperatureColor(float celsius)
    {
        float t = Mathf.InverseLerp(-20f, 40f, celsius);
        if (t < 0.5f)
            return Color.Lerp(new Color(0.06f, 0.18f, 0.55f, 1f), new Color(0.18f, 0.85f, 0.78f, 1f), t * 2f);
        return Color.Lerp(new Color(0.18f, 0.85f, 0.78f, 1f), new Color(0.95f, 0.18f, 0.04f, 1f), (t - 0.5f) * 2f);
    }

    internal static Color PrecipitationColor(float value)
    {
        float t = Mathf.Clamp01(value);
        if (t < 0.5f)
            return Color.Lerp(new Color(0.46f, 0.25f, 0.06f, 1f), new Color(0.25f, 0.52f, 0.28f, 1f), t * 2f);
        return Color.Lerp(new Color(0.25f, 0.52f, 0.28f, 1f), new Color(0.08f, 0.58f, 1f, 1f), (t - 0.5f) * 2f);
    }

    private void DestroyTexture()
    {
        if (_texture != null) DestroyImmediate(_texture);
        _texture = null;
    }
}
