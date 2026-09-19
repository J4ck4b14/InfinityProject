using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEngine;

public sealed class HydrologyDebugWindow : EditorWindow
{
    private enum ViewMode
    {
        FlowDirection,
        Accumulation,
        Basins,
        RawSinks,
        DepressionDepth,
        Channels
    }

    private HydrologyData _data;
    private Texture2D _texture;
    private ViewMode _mode = ViewMode.Accumulation;
    private Vector2 _scroll;
    private float _channelThresholdSquareMeters = 50000f;
    private int _channelSampleCount;
    private int _channelSourceCount;
    private int _conditionedTransitSampleCount;

    public static HydrologyDebugWindow Open(HydrologyData data, float channelThresholdSquareMeters)
    {
        var window = GetWindow<HydrologyDebugWindow>("Hydrology Inspector");
        window.minSize = new Vector2(380f, 390f);
        window._channelThresholdSquareMeters = Mathf.Max(1f, channelThresholdSquareMeters);
        window.SetData(data);
        window.Show();
        return window;
    }

    private void OnDisable() => DestroyTexture();

    private void SetData(HydrologyData data)
    {
        _data = data;
        RebuildTexture();
        Repaint();
    }

    private void OnGUI()
    {
        if (_data == null)
        {
            EditorGUILayout.HelpBox("Generate hydrology from the Hydrology Tool first.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Hydrology Data", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Geo seed {_data.SourceGeographySeed}", GUILayout.Width(120f));
            EditorGUILayout.LabelField($"{_data.WidthMeters:0} × {_data.LengthMeters:0} m", GUILayout.Width(150f));
            EditorGUILayout.LabelField($"{_data.Resolution}² samples", GUILayout.Width(130f));
            EditorGUILayout.LabelField($"{_data.BasinCount} outlet basins");
        }

        EditorGUILayout.Space(4f);
        ViewMode nextMode = (ViewMode)GUILayout.Toolbar((int)_mode,
            new[] { "Direction", "Accumulation", "Basins", "Raw Sinks", "Depressions", "Channels" });
        if (nextMode != _mode)
        {
            _mode = nextMode;
            RebuildTexture();
        }

        if (_mode == ViewMode.Channels)
        {
            EditorGUI.BeginChangeCheck();
            float squareKilometres = _channelThresholdSquareMeters / 1_000_000f;
            squareKilometres = Mathf.Max(0.000001f,
                EditorGUILayout.FloatField("Channel threshold (km²)", squareKilometres));
            if (EditorGUI.EndChangeCheck())
            {
                _channelThresholdSquareMeters = squareKilometres * 1_000_000f;
                RebuildTexture();
            }
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
        float height = width * (_data.LengthMeters / _data.WidthMeters);
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(rect, _texture, null, ScaleMode.StretchToFill);
    }

    private void DrawLegendAndStats()
    {
        switch (_mode)
        {
            case ViewMode.FlowDirection:
                EditorGUILayout.HelpBox(
                    "Flow direction — hue is the conditioned D8 receiver bearing. Black = boundary outlet. This is drainage routing, not automatically an exposed stream: inside filled depressions it represents how water ultimately reaches the spill point.",
                    MessageType.None);
                break;

            case ViewMode.Accumulation:
                EditorGUILayout.HelpBox(
                    "Accumulation — logarithmic brightness shows upstream contributing area. The desired result is a branching hierarchy with a few dominant trunks.",
                    MessageType.None);
                break;

            case ViewMode.Basins:
                EditorGUILayout.HelpBox(
                    $"Outlet basins — each colour eventually reaches one boundary outlet. {_data.BasinCount} basins and {_data.OutletCount} boundary outlets.",
                    MessageType.None);
                break;

            case ViewMode.RawSinks:
                EditorGUILayout.HelpBox(
                    $"Raw sinks — red = local minima in the untouched terrain ({_data.RawSinkCount:N0}). Cyan = conditioned boundary outlets ({_data.OutletCount:N0}). Raw sinks remain evidence; they no longer terminate drainage by themselves.",
                    MessageType.None);
                break;

            case ViewMode.DepressionDepth:
                EditorGUILayout.HelpBox(
                    $"Depression depth — brightness shows how far water would have to rise above raw terrain to reach a spill path. {_data.DepressionSampleCount:N0} samples affected; max {_data.MaxDepressionDepthMeters:0.###} m. Geography is not modified.",
                    MessageType.None);
                break;

            case ViewMode.Channels:
                EditorGUILayout.HelpBox(
                    $"Composite candidate channels — thresholded routing carrying at least {_channelThresholdSquareMeters / 1_000_000f:0.###} km² of contributing area. " +
                    $"Cyan = exposed open channel ({_channelSampleCount:N0} samples); blue = conditioned depression transit ({_conditionedTransitSampleCount:N0} samples); bright-cyan crosses = genuine exposed channel heads ({_channelSourceCount:N0}). " +
                    "Blue segments preserve drainage continuity without pretending that water is an exposed stream climbing the raw terrain. Inspect Depressions to see the fill-to-spill domain itself.",
                    MessageType.None);
                break;
        }
    }

    private void RefreshChannelStats()
    {
        _channelSampleCount = 0;
        _channelSourceCount = 0;
        _conditionedTransitSampleCount = 0;
        if (_data == null)
            return;

        for (int i = 0; i < _data.FlowReceiver.Length; i++)
        {
            if (_data.IsCandidateOpenChannel(i, _channelThresholdSquareMeters))
                _channelSampleCount++;
            else if (_data.IsCandidateConditionedTransit(i, _channelThresholdSquareMeters))
                _conditionedTransitSampleCount++;
        }

        _channelSourceCount = _data.CountCandidateChannelSources(_channelThresholdSquareMeters);
    }

    private void RebuildTexture()
    {
        DestroyTexture();
        if (_data == null)
            return;

        int resolution = _data.Resolution;
        _texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, true)
        {
            name = "Infinity Hydrology Debug",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[resolution * resolution];
        float maxLogAccumulation = Mathf.Log10(_data.WorldAreaSquareMeters + 1f);

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (Color32)DebugColor(i, maxLogAccumulation);

        if (_mode == ViewMode.Channels)
        {
            RefreshChannelStats();
            PaintChannelSourceMarkers(pixels, resolution);
        }

        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
    }

    private void PaintChannelSourceMarkers(Color32[] pixels, int resolution)
    {
        Color32 marker = (Color32)new Color(0f, 1f, 1f, 1f);
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!_data.IsCandidateChannelSource(i, _channelThresholdSquareMeters))
                continue;

            int x = i % resolution;
            int y = i / resolution;
            Paint(x, y);
            Paint(x - 1, y);
            Paint(x + 1, y);
            Paint(x, y - 1);
            Paint(x, y + 1);
        }

        void Paint(int x, int y)
        {
            if (x < 0 || y < 0 || x >= resolution || y >= resolution)
                return;
            pixels[y * resolution + x] = marker;
        }
    }

    private Color DebugColor(int index, float maxLogAccumulation)
    {
        switch (_mode)
        {
            case ViewMode.FlowDirection:
            {
                if (_data.FlowReceiver[index] < 0)
                    return Color.black;
                float hue = _data.GetFlowBearingDegrees(index) / 360f;
                return Color.HSVToRGB(hue, 0.85f, 0.95f);
            }

            case ViewMode.Basins:
            {
                int basin = _data.BasinId[index];
                float hue = Mathf.Repeat(basin * 0.61803398875f, 1f);
                return Color.HSVToRGB(hue, 0.55f, 0.90f);
            }

            case ViewMode.RawSinks:
            {
                if (_data.IsRawSink(index))
                    return new Color(1f, 0.08f, 0.04f, 1f);
                if (_data.IsOutlet(index))
                    return new Color(0.05f, 0.85f, 1f, 1f);
                return new Color(0.035f, 0.035f, 0.04f, 1f);
            }

            case ViewMode.DepressionDepth:
            {
                float depth = _data.DepressionDepthMeters[index];
                if (depth <= 0f)
                    return new Color(0.015f, 0.018f, 0.025f, 1f);
                float denominator = Mathf.Max(_data.MaxDepressionDepthMeters, 0.0001f);
                float t = Mathf.Clamp01(Mathf.Log10(depth + 1f) / Mathf.Log10(denominator + 1f));
                return Color.Lerp(new Color(0.04f, 0.12f, 0.26f, 1f), new Color(0.25f, 0.9f, 1f, 1f), t);
            }

            case ViewMode.Channels:
            {
                if (_data.IsCandidateConditionedTransit(index, _channelThresholdSquareMeters))
                {
                    float log = Mathf.Log10(_data.FlowAccumulationSquareMeters[index] + 1f);
                    float t = Mathf.Clamp01(log / Mathf.Max(maxLogAccumulation, 0.0001f));
                    return Color.Lerp(new Color(0.08f, 0.18f, 0.72f, 1f), new Color(0.18f, 0.48f, 1f, 1f), t);
                }

                if (_data.IsCandidateOpenChannel(index, _channelThresholdSquareMeters))
                {
                    float log = Mathf.Log10(_data.FlowAccumulationSquareMeters[index] + 1f);
                    float t = Mathf.Clamp01(log / Mathf.Max(maxLogAccumulation, 0.0001f));
                    return Color.Lerp(new Color(0.02f, 0.48f, 0.58f, 1f), new Color(0f, 1f, 1f, 1f), t);
                }

                return new Color(0.015f, 0.02f, 0.025f, 1f);
            }

            default:
            {
                float localArea = Mathf.Max(_data.SampleSpacingX * _data.SampleSpacingZ, 1f);
                float accumulation = Mathf.Max(_data.FlowAccumulationSquareMeters[index], localArea);
                float numerator = Mathf.Log10(accumulation / localArea + 1f);
                float denominator = Mathf.Log10(_data.WorldAreaSquareMeters / localArea + 1f);
                float t = Mathf.Clamp01(numerator / Mathf.Max(denominator, 0.0001f));
                t = Mathf.Pow(t, 1.8f);
                return Color.Lerp(new Color(0.005f, 0.008f, 0.015f, 1f), new Color(0.4f, 0.9f, 1f, 1f), t);
            }
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
