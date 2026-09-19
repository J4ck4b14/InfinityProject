using InfinityProject.World.Flora;
using UnityEditor;
using UnityEngine;

public sealed class FloraDebugWindow : EditorWindow
{
    public enum FloraMap { Suitability, InitialOccupancy, CarryingCapacity, InitialBiomassDensity, LimitingFactor }

    private FloraData _data;
    private int _speciesIndex;
    private FloraMap _map;
    private Texture2D _texture;
    private Vector2 _scroll;

    public static FloraDebugWindow Open(FloraData data, int speciesIndex = 0)
    {
        var window = GetWindow<FloraDebugWindow>("Flora Potential Inspector");
        window.minSize = new Vector2(380f, 420f);
        window.SetData(data, speciesIndex);
        window.Show();
        return window;
    }

    private void OnDisable() => DestroyTexture();

    private void SetData(FloraData data, int speciesIndex)
    {
        _data = data;
        _speciesIndex = data != null && data.SpeciesCount > 0 ? Mathf.Clamp(speciesIndex, 0, data.SpeciesCount - 1) : 0;
        RebuildTexture();
        Repaint();
    }

    private void OnGUI()
    {
        if (_data == null || _data.SpeciesCount == 0)
        {
            EditorGUILayout.HelpBox("Generate Flora with at least one species profile first.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Flora V1.1 — environmental potential", EditorStyles.boldLabel);
        string[] names = new string[_data.SpeciesCount];
        for (int i = 0; i < names.Length; i++) names[i] = _data.Species[i].DisplayName;
        int nextSpecies = EditorGUILayout.Popup("Species", _speciesIndex, names);
        FloraMap nextMap = (FloraMap)GUILayout.SelectionGrid((int)_map, new[] { "Suitability", "Initial 0..1", "Carrying K", "Initial kg/m²", "Limiting Factor" }, 3);
        if (nextSpecies != _speciesIndex || nextMap != _map)
        {
            _speciesIndex = nextSpecies;
            _map = nextMap;
            RebuildTexture();
        }

        FloraSpeciesData species = _data.Species[_speciesIndex];
        EditorGUILayout.LabelField($"{species.DisplayName} | {_data.Resolution}² | {_data.WidthMeters:0} × {_data.LengthMeters:0} m", EditorStyles.miniLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMap();
        EditorGUILayout.EndScrollView();

        switch (_map)
        {
            case FloraMap.LimitingFactor:
                EditorGUILayout.HelpBox("Limiting cause: red temperature, blue/cyan water availability, brown soil retention, magenta multiple co-limiters, light grey none.", MessageType.None);
                break;
            case FloraMap.Suitability:
                EditorGUILayout.HelpBox("Establishment suitability is the weakest species response (law of the minimum). It is environmental potential, not current occupancy.", MessageType.None);
                break;
            case FloraMap.InitialOccupancy:
                EditorGUILayout.HelpBox("Generated 0..1 initial occupancy above the establishment threshold. Living biomass uses this only to initialize a separate mutable state.", MessageType.None);
                break;
            case FloraMap.CarryingCapacity:
                EditorGUILayout.HelpBox("Environment-derived carrying capacity in kg dry biomass per m². Display is normalized against this species' maximum current K.", MessageType.None);
                break;
            default:
                EditorGUILayout.HelpBox("Generated starting biomass density in kg dry biomass per m². Once living Flora is initialized, current biomass is authoritative in Flora State instead.", MessageType.None);
                break;
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
        if (_data == null || _data.SpeciesCount == 0) return;
        FloraSpeciesData species = _data.Species[Mathf.Clamp(_speciesIndex, 0, _data.SpeciesCount - 1)];
        _texture = new Texture2D(_data.Resolution, _data.Resolution, TextureFormat.RGB24, false, true)
        {
            name = $"Infinity Flora Potential — {species.StableId} — {_map}",
            filterMode = _map == FloraMap.LimitingFactor ? FilterMode.Point : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[_data.Resolution * _data.Resolution];
        float maxCapacity = Max(species.CarryingCapacityKgPerSquareMeter);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = _map switch
            {
                FloraMap.Suitability => (Color32)SuitabilityColor(species.EstablishmentSuitability[i]),
                FloraMap.InitialOccupancy => (Color32)BiomassColor(species.InitialBiomass[i]),
                FloraMap.CarryingCapacity => (Color32)BiomassColor(maxCapacity > 0.000001f ? species.CarryingCapacityKgPerSquareMeter[i] / maxCapacity : 0f),
                FloraMap.InitialBiomassDensity => (Color32)BiomassColor(maxCapacity > 0.000001f ? species.InitialBiomassKgPerSquareMeter[i] / maxCapacity : 0f),
                _ => (Color32)LimitingFactorColor((FloraLimitingFactor)species.LimitingFactor[i])
            };
        }
        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
    }

    internal static Color SuitabilityColor(float value)
    {
        float t = Mathf.Clamp01(value);
        if (t < 0.5f) return Color.Lerp(new Color(0.14f, 0.04f, 0.03f, 1f), new Color(0.62f, 0.48f, 0.08f, 1f), t * 2f);
        return Color.Lerp(new Color(0.62f, 0.48f, 0.08f, 1f), new Color(0.08f, 0.82f, 0.16f, 1f), (t - 0.5f) * 2f);
    }

    internal static Color BiomassColor(float value)
    {
        float t = Mathf.Clamp01(value);
        return Color.Lerp(new Color(0.02f, 0.04f, 0.015f, 1f), new Color(0.12f, 0.92f, 0.24f, 1f), t);
    }

    internal static Color LimitingFactorColor(FloraLimitingFactor factor)
        => factor switch
        {
            FloraLimitingFactor.Temperature => new Color(0.95f, 0.12f, 0.08f, 1f),
            FloraLimitingFactor.WaterAvailability => new Color(0.02f, 0.66f, 0.95f, 1f),
            FloraLimitingFactor.SoilRetention => new Color(0.48f, 0.27f, 0.09f, 1f),
            FloraLimitingFactor.Multiple => new Color(0.90f, 0.10f, 0.78f, 1f),
            _ => new Color(0.78f, 0.80f, 0.82f, 1f)
        };

    private static float Max(float[] values)
    {
        float max = 0f;
        for (int i = 0; i < values.Length; i++) if (values[i] > max) max = values[i];
        return max;
    }

    private void DestroyTexture()
    {
        if (_texture != null) DestroyImmediate(_texture);
        _texture = null;
    }
}
