using System;
using System.IO;
using InfinityProject.World.Hydrology;
using UnityEditor;
using UnityEngine;

internal static class HydrologyBinaryCache
{
    private const int Magic = 0x49485631; // IHV1
    private const int Version = 2;
    private const string CacheFolder = "Library/InfinityHydrologyCache";

    public static void Save(HydrologyAsset asset, HydrologyData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (data == null) throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(CacheFolder);
        string path = GetCachePath(asset);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(data.Resolution);
        writer.Write(data.WidthMeters);
        writer.Write(data.LengthMeters);
        writer.Write(data.SourceGeographySeed);
        writer.Write(data.SourceHeightHash);
        writer.Write(data.BasinCount);
        writer.Write(data.RawSinkCount);
        writer.Write(data.OutletCount);
        writer.Write(data.DepressionSampleCount);
        writer.Write(data.MaxDepressionDepthMeters);

        WriteIntArray(writer, data.FlowReceiver);
        WriteFloatArray(writer, data.FlowAccumulationSquareMeters);
        WriteIntArray(writer, data.BasinId);
        WriteByteArray(writer, data.TerminalType);
        WriteByteArray(writer, data.RawSinkMask);
        WriteFloatArray(writer, data.DepressionDepthMeters);
    }

    public static bool TryLoad(HydrologyAsset asset, out HydrologyData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata)
            return false;

        string path = GetCachePath(asset);
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                return false;

            int resolution = reader.ReadInt32();
            float widthMeters = reader.ReadSingle();
            float lengthMeters = reader.ReadSingle();
            int seed = reader.ReadInt32();
            long heightHash = reader.ReadInt64();
            int basinCount = reader.ReadInt32();
            int rawSinkCount = reader.ReadInt32();
            int outletCount = reader.ReadInt32();
            int depressionSampleCount = reader.ReadInt32();
            float maxDepressionDepthMeters = reader.ReadSingle();

            int expected = resolution * resolution;
            int[] receiver = ReadIntArray(reader, expected);
            float[] accumulation = ReadFloatArray(reader, expected);
            int[] basinId = ReadIntArray(reader, expected);
            byte[] terminalType = ReadByteArray(reader, expected);
            byte[] rawSinkMask = ReadByteArray(reader, expected);
            float[] depressionDepth = ReadFloatArray(reader, expected);

            data = new HydrologyData(
                resolution,
                widthMeters,
                lengthMeters,
                seed,
                heightHash,
                receiver,
                accumulation,
                basinId,
                terminalType,
                rawSinkMask,
                depressionDepth,
                basinCount,
                rawSinkCount,
                outletCount,
                depressionSampleCount,
                maxDepressionDepthMeters);

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Infinity Hydrology] Could not read cache '{path}'. Regenerate hydrology.\n{exception.Message}");
            data = null;
            return false;
        }
    }

    public static void Delete(HydrologyAsset asset)
    {
        if (asset == null) return;
        string path = GetCachePath(asset);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetCachePath(HydrologyAsset asset)
    {
        string assetPath = AssetDatabase.GetAssetPath(asset);
        string guid = string.IsNullOrEmpty(assetPath)
            ? asset.GetInstanceID().ToString()
            : AssetDatabase.AssetPathToGUID(assetPath);
        return Path.Combine(CacheFolder, guid + ".ihydro");
    }

    private static void WriteIntArray(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        byte[] bytes = new byte[values.Length * sizeof(int)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteFloatArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    private static int[] ReadIntArray(BinaryReader reader, int expected)
    {
        int length = reader.ReadInt32();
        if (length != expected) throw new InvalidDataException("Hydrology cache int-array length mismatch.");
        byte[] bytes = reader.ReadBytes(length * sizeof(int));
        if (bytes.Length != length * sizeof(int)) throw new EndOfStreamException();
        var values = new int[length];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static float[] ReadFloatArray(BinaryReader reader, int expected)
    {
        int length = reader.ReadInt32();
        if (length != expected) throw new InvalidDataException("Hydrology cache float-array length mismatch.");
        byte[] bytes = reader.ReadBytes(length * sizeof(float));
        if (bytes.Length != length * sizeof(float)) throw new EndOfStreamException();
        var values = new float[length];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static byte[] ReadByteArray(BinaryReader reader, int expected)
    {
        int length = reader.ReadInt32();
        if (length != expected) throw new InvalidDataException("Hydrology cache byte-array length mismatch.");
        byte[] values = reader.ReadBytes(length);
        if (values.Length != length) throw new EndOfStreamException();
        return values;
    }
}
