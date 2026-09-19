using System;
using System.IO;
using InfinityProject.World.Climate;
using UnityEditor;
using UnityEngine;

internal static class ClimateBinaryCache
{
    private const int Magic = 0x49434C31; // ICL1
    private const int Version = 1;
    private const string CacheFolder = "Library/InfinityClimateCache";

    public static void Save(ClimateAsset asset, ClimateData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (data == null) throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(CacheFolder);
        using var stream = new FileStream(GetCachePath(asset), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(data.Resolution);
        writer.Write(data.WidthMeters);
        writer.Write(data.LengthMeters);
        writer.Write(data.SourceGeographySeed);
        writer.Write(data.MinimumTemperatureCelsius);
        writer.Write(data.MaximumTemperatureCelsius);
        writer.Write(data.MinimumPrecipitationPotential);
        writer.Write(data.MaximumPrecipitationPotential);
        WriteFloatArray(writer, data.TemperatureCelsius);
        WriteFloatArray(writer, data.PrecipitationPotential);
    }

    public static bool TryLoad(ClimateAsset asset, out ClimateData data)
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

            int resolution = reader.ReadInt32();
            float width = reader.ReadSingle();
            float length = reader.ReadSingle();
            int seed = reader.ReadInt32();
            float minT = reader.ReadSingle();
            float maxT = reader.ReadSingle();
            float minP = reader.ReadSingle();
            float maxP = reader.ReadSingle();
            int expected = resolution * resolution;
            float[] temperature = ReadFloatArray(reader, expected);
            float[] precipitation = ReadFloatArray(reader, expected);
            data = new ClimateData(resolution, width, length, seed, temperature, precipitation, minT, maxT, minP, maxP);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Infinity Climate] Could not read cache '{path}'. Regenerate Climate.\n{exception.Message}");
            data = null;
            return false;
        }
    }

    private static string GetCachePath(ClimateAsset asset)
    {
        string assetPath = AssetDatabase.GetAssetPath(asset);
        string guid = string.IsNullOrEmpty(assetPath) ? asset.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(assetPath);
        return Path.Combine(CacheFolder, guid + ".iclimate");
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
        if (length != expected) throw new InvalidDataException("Climate cache float-array length mismatch.");
        byte[] bytes = reader.ReadBytes(length * sizeof(float));
        if (bytes.Length != length * sizeof(float)) throw new EndOfStreamException();
        var values = new float[length];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
