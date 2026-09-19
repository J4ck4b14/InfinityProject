using System;
using System.IO;
using InfinityProject.World.Flora;
using UnityEditor;
using UnityEngine;

internal static class FloraStateBinaryCache
{
    private const int Magic = 0x49465331; // IFS1
    private const int Version = 2;
    private const string CacheFolder = "Library/InfinityFloraStateCache";

    public static void Save(FloraStateAsset asset, FloraSimulationData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (data == null) throw new ArgumentNullException(nameof(data));
        Directory.CreateDirectory(CacheFolder);
        using var stream = new FileStream(GetCachePath(asset), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(asset.SimulationRevision);
        writer.Write(data.Resolution);
        writer.Write(data.WidthMeters);
        writer.Write(data.LengthMeters);
        writer.Write(data.SourceGeographySeed);
        writer.Write(data.SimulationTimeDays);
        writer.Write(data.SpeciesCount);
        foreach (FloraSimulationSpeciesState species in data.Species)
        {
            writer.Write(species.StableId ?? string.Empty);
            writer.Write(species.DisplayName ?? string.Empty);
            WriteFloatArray(writer, species.CurrentBiomassKgPerSquareMeter);
            WriteFloatArray(writer, species.CumulativeRemovedKgPerSquareMeter);
        }
    }

    public static bool TryLoad(FloraStateAsset asset, out FloraSimulationData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        string path = GetCachePath(asset);
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return false;
            int simulationRevision = reader.ReadInt32();
            int resolution = reader.ReadInt32();
            float width = reader.ReadSingle();
            float length = reader.ReadSingle();
            int seed = reader.ReadInt32();
            double timeDays = reader.ReadDouble();
            int speciesCount = reader.ReadInt32();
            if (simulationRevision != asset.SimulationRevision || resolution != asset.Resolution ||
                seed != asset.SourceFlora.SourceGeographySeed || speciesCount != asset.SpeciesCount)
                return false;
            if (resolution < 2 || speciesCount < 0 || speciesCount > 1024)
                throw new InvalidDataException("Invalid living Flora cache header.");
            int expected = checked(resolution * resolution);
            var species = new FloraSimulationSpeciesState[speciesCount];
            for (int i = 0; i < speciesCount; i++)
            {
                string stableId = reader.ReadString();
                string displayName = reader.ReadString();
                float[] biomass = ReadFloatArray(reader, expected);
                float[] removed = ReadFloatArray(reader, expected);
                species[i] = new FloraSimulationSpeciesState(stableId, displayName, biomass, removed);
            }
            data = new FloraSimulationData(resolution, width, length, seed, timeDays, species);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Infinity Flora State] Could not read cache '{path}'. Reinitialize/rebind living Flora.\n{exception.Message}");
            data = null;
            return false;
        }
    }

    private static string GetCachePath(FloraStateAsset asset)
    {
        string assetPath = AssetDatabase.GetAssetPath(asset);
        string guid = string.IsNullOrEmpty(assetPath) ? asset.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(assetPath);
        return Path.Combine(CacheFolder, guid + ".ifstate");
    }

    private static void WriteFloatArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        writer.Write(bytes);
    }

    private static float[] ReadFloatArray(BinaryReader reader, int expected)
    {
        int length = reader.ReadInt32();
        if (length != expected) throw new InvalidDataException("Living Flora cache array length mismatch.");
        byte[] bytes = reader.ReadBytes(length * sizeof(float));
        if (bytes.Length != length * sizeof(float)) throw new EndOfStreamException();
        var values = new float[length];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
