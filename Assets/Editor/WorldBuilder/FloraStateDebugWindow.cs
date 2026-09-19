using InfinityProject.World.Flora;
using UnityEditor;
using UnityEngine;

public sealed class FloraStateDebugWindow : EditorWindow
{
    private const int MaxPreviewResolution = 512;
    private static readonly string[] MapLabels = { "Biomass / K", "kg / m²", "Removed" };

    private enum LiveMap { BiomassVsCapacity, BiomassDensity, CumulativeRemoved }

    private FloraData _potential;
    private FloraSimulationData _state;
    private int _speciesIndex;
    private LiveMap _map;
    private Texture2D _texture;
    private Vector2 _scroll;
    private string[] _speciesNames = System.Array.Empty<string>();

    public static FloraStateDebugWindow Open(FloraData potential, FloraSimulationData state, int speciesIndex = 0)
    {
        var window = GetWindow<FloraStateDebugWindow>("Living Flora Inspector");
        window.minSize = new Vector2(380f, 430f);
        window.SetData(potential, state, speciesIndex);
        window.Show();
        return window;
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        DestroyTexture();
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode && state != PlayModeStateChange.ExitingPlayMode) return;
        ClearTransientState();
        Repaint();
    }

    public void SetData(FloraData potential, FloraSimulationData state, int speciesIndex = 0)
    {
        _potential = potential;
        _state = state;
        CacheSpeciesNames();
        _speciesIndex = HasUsableState() ? Mathf.Clamp(speciesIndex, 0, _state.SpeciesCount - 1) : 0;
        RebuildTexture();
        Repaint();
    }

    private void OnGUI()
    {
        if (!HasUsableState())
        {
            EditorGUILayout.HelpBox(
                "Living Flora data is not available in this editor window. Reopen it from Ecology Lab after initializing or reloading living Flora.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Infinity Flora V1 — living biomass", EditorStyles.boldLabel);

        if (_speciesNames.Length != _state.SpeciesCount) CacheSpeciesNames();
        int nextSpecies = EditorGUILayout.Popup("Species", _speciesIndex, _speciesNames);
        LiveMap nextMap = (LiveMap)GUILayout.Toolbar((int)_map, MapLabels);
        if (nextSpecies != _speciesIndex || nextMap != _map)
        {
            _speciesIndex = Mathf.Clamp(nextSpecies, 0, _state.SpeciesCount - 1);
            _map = nextMap;
            RebuildTexture();
        }

        FloraSimulationSpeciesState stateSpecies = _state.Species[_speciesIndex];
        int potentialIndex = _potential.FindSpeciesIndex(stateSpecies.StableId);
        FloraSpeciesData potentialSpecies = potentialIndex >= 0 ? _potential.Species[potentialIndex] : null;
        EditorGUILayout.LabelField($"{stateSpecies.DisplayName} | day {_state.SimulationTimeDays:0.00} | {_state.Resolution}²", EditorStyles.miniLabel);
        if (potentialSpecies != null)
        {
            double currentKg = _state.TotalBiomassKg(_speciesIndex);
            double carryingKg = _state.IntegrateDensityKg(potentialSpecies.CarryingCapacityKgPerSquareMeter);
            double removedKg = _state.IntegrateDensityKg(stateSpecies.CumulativeRemovedKgPerSquareMeter);
            EditorGUILayout.LabelField($"Current {currentKg:N0} kg | environmental K {carryingKg:N0} kg | removed {removedKg:N0} kg", EditorStyles.miniLabel);
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMap();
        EditorGUILayout.EndScrollView();

        switch (_map)
        {
            case LiveMap.BiomassVsCapacity:
                EditorGUILayout.HelpBox("Current biomass divided by current environmental carrying capacity. Green ≈ at capacity; yellow/red = legacy biomass temporarily above a newly reduced capacity and therefore under stress.", MessageType.None);
                break;
            case LiveMap.BiomassDensity:
                EditorGUILayout.HelpBox("Current living biomass density in kg dry biomass per m², normalized for display against this species' maximum current carrying capacity.", MessageType.None);
                break;
            default:
                EditorGUILayout.HelpBox("Cumulative biomass removed by grazing or injected disturbance. This is history, not current scarcity.", MessageType.None);
                break;
        }
    }

    private bool HasUsableState()
    {
        if (_potential == null || _state == null || _state.SpeciesCount <= 0 || _state.Species == null) return false;
        if (_speciesIndex < 0 || _speciesIndex >= _state.SpeciesCount) return false;
        FloraSimulationSpeciesState species = _state.Species[_speciesIndex];
        int expected = _state.Resolution * _state.Resolution;
        return species != null &&
               species.CurrentBiomassKgPerSquareMeter != null && species.CurrentBiomassKgPerSquareMeter.Length == expected &&
               species.CumulativeRemovedKgPerSquareMeter != null && species.CumulativeRemovedKgPerSquareMeter.Length == expected;
    }

    private void CacheSpeciesNames()
    {
        int count = _state?.SpeciesCount ?? 0;
        if (count <= 0 || _state.Species == null)
        {
            _speciesNames = System.Array.Empty<string>();
            return;
        }

        if (_speciesNames.Length != count) _speciesNames = new string[count];
        for (int i = 0; i < count; i++)
            _speciesNames[i] = _state.Species[i]?.DisplayName ?? $"Species {i}";
    }

    private void DrawMap()
    {
        if (_texture == null || _state == null) return;
        float width = Mathf.Max(position.width - 24f, 64f);
        float height = width * (_state.LengthMeters / Mathf.Max(0.001f, _state.WidthMeters));
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(rect, _texture, null, ScaleMode.StretchToFill);
    }

    private void RebuildTexture()
    {
        DestroyTexture();
        if (!HasUsableState()) return;

        FloraSimulationSpeciesState stateSpecies = _state.Species[_speciesIndex];
        int potentialIndex = _potential.FindSpeciesIndex(stateSpecies.StableId);
        if (potentialIndex < 0) return;
        FloraSpeciesData potentialSpecies = _potential.Species[potentialIndex];

        int previewResolution = Mathf.Min(MaxPreviewResolution, _state.Resolution);
        _texture = new Texture2D(previewResolution, previewResolution, TextureFormat.RGB24, false, true)
        {
            name = $"Infinity Living Flora — {stateSpecies.StableId} — {_map}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[previewResolution * previewResolution];
        float maxCapacity = Max(potentialSpecies.CarryingCapacityKgPerSquareMeter);
        float maxRemoved = Max(stateSpecies.CumulativeRemovedKgPerSquareMeter);
        int sourceResolution = _state.Resolution;

        for (int py = 0; py < previewResolution; py++)
        {
            int sy = Mathf.RoundToInt((float)py / Mathf.Max(1, previewResolution - 1) * (sourceResolution - 1));
            for (int px = 0; px < previewResolution; px++)
            {
                int sx = Mathf.RoundToInt((float)px / Mathf.Max(1, previewResolution - 1) * (sourceResolution - 1));
                int sourceIndex = sy * sourceResolution + sx;
                float value;
                Color color;
                switch (_map)
                {
                    case LiveMap.BiomassVsCapacity:
                        float k = potentialSpecies.CarryingCapacityKgPerSquareMeter[sourceIndex];
                        value = k > 0.000001f ? stateSpecies.CurrentBiomassKgPerSquareMeter[sourceIndex] / k : (stateSpecies.CurrentBiomassKgPerSquareMeter[sourceIndex] > 0.000001f ? 2f : 0f);
                        color = BiomassRatioColor(value);
                        break;
                    case LiveMap.BiomassDensity:
                        value = maxCapacity > 0.000001f ? stateSpecies.CurrentBiomassKgPerSquareMeter[sourceIndex] / maxCapacity : 0f;
                        color = FloraDebugWindow.BiomassColor(value);
                        break;
                    default:
                        value = maxRemoved > 0.000001f ? Mathf.Log(1f + stateSpecies.CumulativeRemovedKgPerSquareMeter[sourceIndex]) / Mathf.Log(1f + maxRemoved) : 0f;
                        color = Color.Lerp(new Color(0.025f, 0.02f, 0.02f, 1f), new Color(1f, 0.45f, 0.05f, 1f), Mathf.Clamp01(value));
                        break;
                }
                pixels[py * previewResolution + px] = (Color32)color;
            }
        }

        _texture.SetPixels32(pixels);
        _texture.Apply(false, true);
    }

    internal static Color BiomassRatioColor(float ratio)
    {
        if (ratio <= 1f)
            return Color.Lerp(new Color(0.015f, 0.025f, 0.01f, 1f), new Color(0.12f, 0.92f, 0.24f, 1f), Mathf.Clamp01(ratio));
        return Color.Lerp(new Color(0.95f, 0.82f, 0.08f, 1f), new Color(0.95f, 0.12f, 0.04f, 1f), Mathf.Clamp01(ratio - 1f));
    }

    private static float Max(float[] values)
    {
        if (values == null) return 0f;
        float max = 0f;
        for (int i = 0; i < values.Length; i++) if (values[i] > max) max = values[i];
        return max;
    }

    private void ClearTransientState()
    {
        DestroyTexture();
        _potential = null;
        _state = null;
        _speciesNames = System.Array.Empty<string>();
        _speciesIndex = 0;
    }

    private void DestroyTexture()
    {
        if (_texture != null) DestroyImmediate(_texture);
        _texture = null;
    }
}
