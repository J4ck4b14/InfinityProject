using System;
using System.Collections.Generic;
using InfinityProject.World.Climate;
using InfinityProject.World.Flora;
using InfinityProject.World.Geography;
using InfinityProject.World.Ground;
using InfinityProject.World.Hydrology;

/// <summary>
/// Weak shared cache for heavy immutable editor-side World snapshots.
///
/// Multiple inspectors/tools may need the same million-sample derived data. Without sharing, each tool can
/// independently materialize another Geography/Hydrology/Ground/Climate/Flora copy. Weak references allow tools
/// to reuse an already-live snapshot without forcing that snapshot to remain resident after every consumer lets go.
/// Mutable Living Flora and Fauna state are deliberately excluded.
/// </summary>
internal static class WorldEditorDataCache
{
    private sealed class Entry<T> where T : class
    {
        public int Revision;
        public WeakReference<T> Data;
    }

    private static readonly Dictionary<int, Entry<GeographyData>> Geography = new();
    private static readonly Dictionary<int, Entry<HydrologyData>> Hydrology = new();
    private static readonly Dictionary<int, Entry<GroundConditionData>> Ground = new();
    private static readonly Dictionary<int, Entry<ClimateData>> Climate = new();
    private static readonly Dictionary<int, Entry<FloraData>> Flora = new();

    public static GeographyData GetGeography(GeographyAsset asset)
    {
        if (asset == null || !asset.HasData) return null;
        int key = asset.GetInstanceID();
        if (TryGet(Geography, key, asset.GenerationRevision, out GeographyData cached))
            return cached;

        GeographyData data = asset.ToData();
        Put(Geography, key, asset.GenerationRevision, data);
        return data;
    }

    public static bool TryGetHydrology(HydrologyAsset asset, out HydrologyData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        int key = asset.GetInstanceID();
        if (TryGet(Hydrology, key, asset.GenerationRevision, out data)) return true;
        if (!HydrologyBinaryCache.TryLoad(asset, out data)) return false;
        Put(Hydrology, key, asset.GenerationRevision, data);
        return true;
    }

    public static bool TryGetGround(GroundConditionAsset asset, out GroundConditionData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        int key = asset.GetInstanceID();
        if (TryGet(Ground, key, asset.GenerationRevision, out data)) return true;
        if (!GroundConditionBinaryCache.TryLoad(asset, out data)) return false;
        Put(Ground, key, asset.GenerationRevision, data);
        return true;
    }

    public static bool TryGetClimate(ClimateAsset asset, out ClimateData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        int key = asset.GetInstanceID();
        if (TryGet(Climate, key, asset.GenerationRevision, out data)) return true;
        if (!ClimateBinaryCache.TryLoad(asset, out data)) return false;
        Put(Climate, key, asset.GenerationRevision, data);
        return true;
    }

    public static bool TryGetFlora(FloraAsset asset, out FloraData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        int key = asset.GetInstanceID();
        if (TryGet(Flora, key, asset.GenerationRevision, out data)) return true;
        if (!FloraBinaryCache.TryLoad(asset, out data)) return false;
        Put(Flora, key, asset.GenerationRevision, data);
        return true;
    }


    public static void Remember(GeographyAsset asset, GeographyData data)
    {
        if (asset == null || data == null) return;
        Put(Geography, asset.GetInstanceID(), asset.GenerationRevision, data);
    }

    public static void Remember(HydrologyAsset asset, HydrologyData data)
    {
        if (asset == null || data == null) return;
        Put(Hydrology, asset.GetInstanceID(), asset.GenerationRevision, data);
    }

    public static void Remember(GroundConditionAsset asset, GroundConditionData data)
    {
        if (asset == null || data == null) return;
        Put(Ground, asset.GetInstanceID(), asset.GenerationRevision, data);
    }

    public static void Remember(ClimateAsset asset, ClimateData data)
    {
        if (asset == null || data == null) return;
        Put(Climate, asset.GetInstanceID(), asset.GenerationRevision, data);
    }

    public static void Remember(FloraAsset asset, FloraData data)
    {
        if (asset == null || data == null) return;
        Put(Flora, asset.GetInstanceID(), asset.GenerationRevision, data);
    }

    public static void Invalidate(GeographyAsset asset)
    {
        if (asset != null) Geography.Remove(asset.GetInstanceID());
    }

    public static void Invalidate(HydrologyAsset asset)
    {
        if (asset != null) Hydrology.Remove(asset.GetInstanceID());
    }

    public static void Invalidate(GroundConditionAsset asset)
    {
        if (asset != null) Ground.Remove(asset.GetInstanceID());
    }

    public static void Invalidate(ClimateAsset asset)
    {
        if (asset != null) Climate.Remove(asset.GetInstanceID());
    }

    public static void Invalidate(FloraAsset asset)
    {
        if (asset != null) Flora.Remove(asset.GetInstanceID());
    }

    private static bool TryGet<T>(Dictionary<int, Entry<T>> cache, int key, int revision, out T data)
        where T : class
    {
        data = null;
        if (!cache.TryGetValue(key, out Entry<T> entry) || entry.Revision != revision)
            return false;
        if (entry.Data != null && entry.Data.TryGetTarget(out data) && data != null)
            return true;
        cache.Remove(key);
        data = null;
        return false;
    }

    private static void Put<T>(Dictionary<int, Entry<T>> cache, int key, int revision, T data)
        where T : class
    {
        if (data == null) return;
        cache[key] = new Entry<T>
        {
            Revision = revision,
            Data = new WeakReference<T>(data)
        };
    }
}
