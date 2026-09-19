using InfinityProject.World.Geography;
using UnityEditor;
using UnityEngine;

public sealed class GeographyDebugWindow : EditorWindow
{
    private enum ViewMode
    {
        Height,
        Slope,
        Aspect
    }

    private GeographyData _data;
    private Texture2D _texture;
    private ViewMode _mode;
    private Vector2 _scroll;

    public static GeographyDebugWindow Open(GeographyData data)
    {
        var window = GetWindow<GeographyDebugWindow>("Geography Inspector");
        window.minSize = new Vector2(340f, 360f);
        window.SetData(data);
        window.Show();
        return window;
    }

    private void OnDisable()
    {
        DestroyTexture();
    }

    private void SetData(GeographyData data)
    {
        _data = data;
        RebuildTexture();
        Repaint();
    }

    private void OnGUI()
    {
        if (_data == null)
        {
            EditorGUILayout.HelpBox("Generate geography from the Terrain Tool first.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Geography Data", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Seed {_data.Seed}", GUILayout.Width(110f));
            EditorGUILayout.LabelField($"{_data.WidthMeters:0} × {_data.LengthMeters:0} m", GUILayout.Width(150f));
            EditorGUILayout.LabelField($"{_data.Resolution}² samples", GUILayout.Width(130f));
            EditorGUILayout.LabelField($"Δ {_data.SampleSpacingX:0.00} × {_data.SampleSpacingZ:0.00} m");
        }

        EditorGUILayout.Space(4f);

        ViewMode nextMode = (ViewMode)GUILayout.Toolbar((int)_mode, new[] { "Height", "Slope", "Aspect" });
        if (nextMode != _mode)
        {
            _mode = nextMode;
            RebuildTexture();
        }

        EditorGUILayout.Space(6f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMap();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4f);
        DrawLegendAndStats();
    }

    private void DrawMap()
    {
        if (_texture == null)
            return;

        float width = Mathf.Max(position.width - 24f, 64f);
        float height = width * ((float)_data.LengthMeters / _data.WidthMeters);
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(rect, _texture, null, ScaleMode.StretchToFill);
    }

    private void DrawLegendAndStats()
    {
        switch (_mode)
        {
            case ViewMode.Height:
                GetMinMax(_data.NormalizedHeight, out float minH, out float maxH);
                EditorGUILayout.HelpBox(
                    $"Height — black = 0 m, white = {_data.MaxElevationMeters:0.#} m. " +
                    $"Generated range: {minH * _data.MaxElevationMeters:0.#}–{maxH * _data.MaxElevationMeters:0.#} m.",
                    MessageType.None);
                break;

            case ViewMode.Slope:
                GetMinMax(_data.SlopeDegrees, out float minSlope, out float maxSlope);
                EditorGUILayout.HelpBox(
                    $"Slope — black = flat, white = 60° or steeper. " +
                    $"Generated range: {minSlope:0.0}–{maxSlope:0.0}°.",
                    MessageType.None);
                break;

            case ViewMode.Aspect:
                EditorGUILayout.HelpBox(
                    "Aspect — hue shows the downslope bearing: 0° = north (+Z), 90° = east (+X), " +
                    "180° = south, 270° = west. Flat samples use 0° by convention.",
                    MessageType.None);
                break;
        }
    }

    private void RebuildTexture()
    {
        DestroyTexture();
        if (_data == null)
            return;

        int resolution = _data.Resolution;
        _texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, true)
        {
            name = "Infinity Geography Debug",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                pixels[index] = DebugColor(index);
            }
        }

        _texture.SetPixels(pixels);
        _texture.Apply(false, true);
    }

    private Color DebugColor(int index)
    {
        switch (_mode)
        {
            case ViewMode.Slope:
            {
                float t = Mathf.Clamp01(_data.SlopeDegrees[index] / 60f);
                return new Color(t, t, t, 1f);
            }
            case ViewMode.Aspect:
            {
                float hue = Mathf.Repeat(_data.AspectDegrees[index], 360f) / 360f;
                return Color.HSVToRGB(hue, 0.75f, 0.95f);
            }
            default:
            {
                float h = Mathf.Clamp01(_data.NormalizedHeight[index]);
                return new Color(h, h, h, 1f);
            }
        }
    }

    private static void GetMinMax(float[] values, out float min, out float max)
    {
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;

        for (int i = 0; i < values.Length; i++)
        {
            min = Mathf.Min(min, values[i]);
            max = Mathf.Max(max, values[i]);
        }
    }

    private void DestroyTexture()
    {
        if (_texture == null)
            return;

        DestroyImmediate(_texture);
        _texture = null;
    }
}
