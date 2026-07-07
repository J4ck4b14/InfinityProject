using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Shared data model for the heightmap pipeline.
/// Owns the noise parameters, sculpt layer, undo stack, and snapshot ring buffer.
/// HeightMapTab and TerrainPreviewWindow both hold a reference to the same instance.
/// </summary>
[Serializable]
public class HeightmapData
{
    // -─ Noise Parameters --------------------------

    [Serializable]
    public class NoiseParams
    {
        public float baseRoughness   = 1f;
        public float roughness       = 2f;
        public float persistence     = 0.5f;
        public int   octaves         = 6;
        public float globalSeed      = 1234f;
        public bool  useRidges       = true;
        public float ridgeStrength   = 2f;

        public NoiseParams Clone() => (NoiseParams)MemberwiseClone();
    }

    [Serializable]
    public class ErosionParams
    {
        public bool  doHydraulic         = true;
        public int   hydraulicIterations = 160;
        public float rainRate            = 0.010f;
        public float evaporation         = 0.02f;
        public float flowRate            = 0.7f;
        public float sedimentCapacity    = 4.0f;
        public float erodeSpeed          = 0.35f;
        public float depositSpeed        = 0.35f;
        public float minSlope            = 0.0005f;
        public bool  doThermal           = true;
        public int   thermalPasses       = 20;
        public float talusAngle          = 0.02f;

        public ErosionParams Clone() => (ErosionParams)MemberwiseClone();
    }

    [Serializable]
    public class FeatureParams
    {
        public float waterLevel       = 0.3f;
        public bool  carveRiver       = true;

        [Flags]
        public enum OceanSides { None = 0, North = 1, South = 2, East = 4, West = 8, All = 15 }
        public OceanSides oceanSides  = OceanSides.None;
        public int   beachFadeDistance = 8;

        public FeatureParams Clone() => (FeatureParams)MemberwiseClone();
    }

    [Serializable]
    public class TileParams
    {
        public int   tilesX       = 1;
        public int   tilesY       = 1;
        public int   tileX        = 0;
        public int   tileY        = 0;
        public int   resolution   = 512;   // samples per side (not hmRes)
        public float terrainSide  = 1000f;
        public float terrainHeight= 100f;

        public TileParams Clone() => (TileParams)MemberwiseClone();
    }

    // -─ Public Fields ----------------------------

    public NoiseParams   noise    = new();
    public ErosionParams erosion  = new();
    public FeatureParams features = new();
    public TileParams    tile     = new();

    /// <summary>
    /// Base noise heights [0..1], flat y*res+x. Null until first generation.
    /// </summary>
    public float[] BaseHeights;

    /// <summary>
    /// Sculpt layer — additive delta on top of base noise, same size as BaseHeights.
    /// Values in [-1..1]. Clamped when combined.
    /// </summary>
    public float[] SculptLayer;

    /// <summary>Resolution used when BaseHeights / SculptLayer were last generated.</summary>
    public int CurrentResolution;

    // -─ Combined Output --------------------------─

    /// <summary>Returns base+sculpt, clamped to [0,1]. Allocates each call — cache externally.</summary>
    public float[] GetCombined()
    {
        if (BaseHeights == null) return null;
        int len = BaseHeights.Length;
        var out_ = new float[len];
        for (int i = 0; i < len; i++)
        {
            float s = SculptLayer != null && SculptLayer.Length == len ? SculptLayer[i] : 0f;
            out_[i] = Mathf.Clamp01(BaseHeights[i] + s);
        }
        return out_;
    }

    // -─ Undo Stack -----------------------------─

    private const int MaxUndoSteps = 20;

    private struct UndoEntry
    {
        public float[] SculptDelta; // sparse: only changed indices
        public int[]   Indices;
    }

    private readonly List<UndoEntry> _undoStack = new();

    /// <summary>
    /// Call before any brush stroke. Captures the current sculpt values at the
    /// given indices so they can be restored on undo.
    /// </summary>
    public void PushUndoRegion(int[] indices)
    {
        if (SculptLayer == null || indices == null || indices.Length == 0) return;

        var entry = new UndoEntry
        {
            Indices     = (int[])indices.Clone(),
            SculptDelta = new float[indices.Length]
        };

        for (int i = 0; i < indices.Length; i++)
            entry.SculptDelta[i] = SculptLayer[indices[i]];

        _undoStack.Add(entry);
        if (_undoStack.Count > MaxUndoSteps)
            _undoStack.RemoveAt(0);
    }

    /// <summary>Restores last sculpt state. Returns true if anything was undone.</summary>
    public bool Undo()
    {
        if (_undoStack.Count == 0 || SculptLayer == null) return false;

        var entry = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        for (int i = 0; i < entry.Indices.Length; i++)
        {
            int idx = entry.Indices[i];
            if (idx >= 0 && idx < SculptLayer.Length)
                SculptLayer[idx] = entry.SculptDelta[i];
        }
        return true;
    }

    public bool CanUndo => _undoStack.Count > 0;

    // -─ Snapshot Ring Buffer (Last 4) --------------------

    public const int SnapshotCount = 4;

    [Serializable]
    public class Snapshot
    {
        public float[]       Heights;        // combined (base+sculpt) at snapshot time
        public int           Resolution;
        public NoiseParams   Noise;
        public ErosionParams Erosion;
        public FeatureParams Features;
        public TileParams    Tile;
        public double        Timestamp;      // OADate
        public string        Label;          // auto "HH:mm:ss" or user override
    }

    private readonly Snapshot[] _snapshots = new Snapshot[SnapshotCount];
    private int _snapHead = 0;  // next write position

    /// <summary>Saves a snapshot of current combined heights + all params.</summary>
    public void PushSnapshot(string label = null)
    {
        var combined = GetCombined();
        if (combined == null) return;

        _snapshots[_snapHead] = new Snapshot
        {
            Heights    = (float[])combined.Clone(),
            Resolution = CurrentResolution,
            Noise      = noise.Clone(),
            Erosion    = erosion.Clone(),
            Features   = features.Clone(),
            Tile       = tile.Clone(),
            Timestamp  = DateTime.Now.ToOADate(),
            Label      = label ?? DateTime.Now.ToString("HH:mm:ss")
        };
        _snapHead = (_snapHead + 1) % SnapshotCount;
    }

    /// <summary>
    /// Returns snapshots ordered newest-first. Nulls fill empty slots.
    /// </summary>
    public Snapshot[] GetSnapshots()
    {
        var result = new Snapshot[SnapshotCount];
        for (int i = 0; i < SnapshotCount; i++)
        {
            int idx = ((_snapHead - 1 - i) + SnapshotCount * 2) % SnapshotCount;
            result[i] = _snapshots[idx];
        }
        return result;
    }

    /// <summary>Restores base heights from a snapshot (sculpt layer is cleared).</summary>
    public void RestoreSnapshot(Snapshot snap)
    {
        if (snap == null) return;
        CurrentResolution = snap.Resolution;
        BaseHeights       = (float[])snap.Heights.Clone();
        SculptLayer       = new float[snap.Heights.Length];
        noise    = snap.Noise.Clone();
        erosion  = snap.Erosion.Clone();
        features = snap.Features.Clone();
        tile     = snap.Tile.Clone();
        _undoStack.Clear();
    }

    // -─ Helpers ------------------------------─

    /// <summary>Allocates or clears sculpt layer to match current resolution.</summary>
    public void EnsureSculptLayer()
    {
        int expected = CurrentResolution * CurrentResolution;
        if (SculptLayer == null || SculptLayer.Length != expected)
            SculptLayer = new float[expected];
    }

    /// <summary>Clears all undo history.</summary>
    public void ClearUndo() => _undoStack.Clear();
}

// -─ Noise / Erosion Preset Definitions -------------------

public static class HeightmapPresets
{
    // - Noise --------------------------------

    public static readonly (string name, HeightmapData.NoiseParams p)[] NoisePresets =
    {
        ("Rolling Hills", new HeightmapData.NoiseParams
            { baseRoughness=0.6f, roughness=1.8f, persistence=0.55f, octaves=5, useRidges=false }),
        ("Alpine",        new HeightmapData.NoiseParams
            { baseRoughness=1.4f, roughness=2.2f, persistence=0.45f, octaves=7, useRidges=true,  ridgeStrength=2.5f }),
        ("Desert Dunes",  new HeightmapData.NoiseParams
            { baseRoughness=0.5f, roughness=1.5f, persistence=0.65f, octaves=3, useRidges=false }),
        ("Archipelago",   new HeightmapData.NoiseParams
            { baseRoughness=1.2f, roughness=2.0f, persistence=0.40f, octaves=6, useRidges=true,  ridgeStrength=1.8f }),
        ("Badlands",      new HeightmapData.NoiseParams
            { baseRoughness=1.8f, roughness=2.5f, persistence=0.50f, octaves=6, useRidges=true,  ridgeStrength=3.0f }),
    };

    // - Erosion -------------------------------─

    public static readonly (string name, HeightmapData.ErosionParams p)[] ErosionPresets =
    {
        ("None",             new HeightmapData.ErosionParams { doHydraulic=false, doThermal=false }),
        ("Light Weathering", new HeightmapData.ErosionParams
            { doHydraulic=true,  hydraulicIterations=40,  rainRate=0.006f, evaporation=0.04f,
              flowRate=0.5f, sedimentCapacity=2f, erodeSpeed=0.2f, depositSpeed=0.2f, minSlope=0.001f,
              doThermal=true, thermalPasses=8,  talusAngle=0.03f }),
        ("Moderate Rainfall",new HeightmapData.ErosionParams
            { doHydraulic=true,  hydraulicIterations=160, rainRate=0.010f, evaporation=0.02f,
              flowRate=0.7f, sedimentCapacity=4f, erodeSpeed=0.35f, depositSpeed=0.35f, minSlope=0.0005f,
              doThermal=true, thermalPasses=20, talusAngle=0.02f }),
        ("Heavy Rainfall",   new HeightmapData.ErosionParams
            { doHydraulic=true,  hydraulicIterations=300, rainRate=0.018f, evaporation=0.015f,
              flowRate=0.85f, sedimentCapacity=7f, erodeSpeed=0.5f, depositSpeed=0.4f, minSlope=0.0003f,
              doThermal=true, thermalPasses=35, talusAngle=0.015f }),
        ("Ancient",          new HeightmapData.ErosionParams
            { doHydraulic=true,  hydraulicIterations=400, rainRate=0.022f, evaporation=0.010f,
              flowRate=0.9f, sedimentCapacity=10f, erodeSpeed=0.6f, depositSpeed=0.5f, minSlope=0.0002f,
              doThermal=true, thermalPasses=60, talusAngle=0.010f }),
    };

    // - Features -------------------------------

    public static readonly (string name, HeightmapData.FeatureParams p)[] FeaturePresets =
    {
        ("Flat Coast",     new HeightmapData.FeatureParams
            { waterLevel=0.28f, carveRiver=false,
              oceanSides=HeightmapData.FeatureParams.OceanSides.South, beachFadeDistance=16 }),
        ("Island",         new HeightmapData.FeatureParams
            { waterLevel=0.30f, carveRiver=false,
              oceanSides=HeightmapData.FeatureParams.OceanSides.All,   beachFadeDistance=24 }),
        ("Continental",    new HeightmapData.FeatureParams
            { waterLevel=0.25f, carveRiver=true,
              oceanSides=HeightmapData.FeatureParams.OceanSides.None }),
        ("River Valley",   new HeightmapData.FeatureParams
            { waterLevel=0.30f, carveRiver=true,
              oceanSides=HeightmapData.FeatureParams.OceanSides.None }),
    };
}
