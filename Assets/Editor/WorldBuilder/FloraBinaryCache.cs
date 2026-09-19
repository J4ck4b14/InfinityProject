using System;
using System.IO;
using InfinityProject.World.Flora;
using UnityEditor;
using UnityEngine;

internal static class FloraBinaryCache
{
    private const int Magic = 0x49464C31; // IFL1
    private const int Version = 3;
    private const string CacheFolder = "Library/InfinityFloraCache";

    public static void Save(FloraAsset asset, FloraData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (data == null) throw new ArgumentNullException(nameof(data));
        Directory.CreateDirectory(CacheFolder);
        using var stream = new FileStream(GetCachePath(asset), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(asset.GenerationRevision);
        writer.Write(data.Resolution);
        writer.Write(data.WidthMeters);
        writer.Write(data.LengthMeters);
        writer.Write(data.SourceGeographySeed);
        writer.Write(data.SpeciesCount);
        foreach (FloraSpeciesData species in data.Species)
        {
            writer.Write(species.StableId ?? string.Empty);
            writer.Write(species.DisplayName ?? string.Empty);
            WriteFloatArray(writer, species.EstablishmentSuitability);
            WriteFloatArray(writer, species.InitialBiomass);
            WriteFloatArray(writer, species.CarryingCapacityKgPerSquareMeter);
            WriteFloatArray(writer, species.InitialBiomassKgPerSquareMeter);
            WriteByteArray(writer, species.LimitingFactor);
        }
    }

    public static bool TryLoad(FloraAsset asset, out FloraData data)
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
            int generationRevision = reader.ReadInt32();
            int resolution = reader.ReadInt32();
            float width = reader.ReadSingle();
            float length = reader.ReadSingle();
            int seed = reader.ReadInt32();
            int speciesCount = reader.ReadInt32();
            if (generationRevision != asset.GenerationRevision || resolution != asset.Resolution ||
                Mathf.Abs(width - asset.WidthMeters) > 0.001f || Mathf.Abs(length - asset.LengthMeters) > 0.001f ||
                seed != asset.SourceGeographySeed || speciesCount != asset.SpeciesCount)
                return false;
            if (speciesCount < 0 || speciesCount > 1024) throw new InvalidDataException("Invalid Flora species count.");
            int expected = resolution * resolution;
            var species = new FloraSpeciesData[speciesCount];
            for (int i = 0; i < speciesCount; i++)
            {
                string stableId = reader.ReadString();
                string displayName = reader.ReadString();
                float[] suitability = ReadFloatArray(reader, expected);
                float[] biomass = ReadFloatArray(reader, expected);
                float[] carryingCapacity = ReadFloatArray(reader, expected);
                float[] biomassDensity = ReadFloatArray(reader, expected);
                byte[] limiting = ReadByteArray(reader, expected);
                species[i] = new FloraSpeciesData(stableId, displayName, suitability, biomass, carryingCapacity, biomassDensity, limiting);
            }
            data = new FloraData(resolution, width, length, seed, species);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Infinity Flora] Could not read cache '{path}'. Regenerate Flora.\n{exception.Message}");
            data = null;
            return false;
        }
    }

    private static string GetCachePath(FloraAsset asset)
    {
        string assetPath = AssetDatabase.GetAssetPath(asset);
        string guid = string.IsNullOrEmpty(assetPath) ? asset.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(assetPath);
        return Path.Combine(CacheFolder, guid + ".iflora");
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
        if (length != expected) throw new InvalidDataException("Flora cache float-array length mismatch.");
        byte[] bytes = reader.ReadBytes(length * sizeof(float));
        if (bytes.Length != length * sizeof(float)) throw new EndOfStreamException();
        var values = new float[length];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    private static byte[] ReadByteArray(BinaryReader reader, int expected)
    {
        int length = reader.ReadInt32();
        if (length != expected) throw new InvalidDataException("Flora cache byte-array length mismatch.");
        byte[] values = reader.ReadBytes(length);
        if (values.Length != length) throw new EndOfStreamException();
        return values;
    }
}
