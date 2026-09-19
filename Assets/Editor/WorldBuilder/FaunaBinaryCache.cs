using System;
using System.IO;
using InfinityProject.World.Fauna;
using UnityEditor;
using UnityEngine;

internal static class FaunaBinaryCache
{
    private const int Magic = 0x49464131; // IFA1
    private const int Version = 2;
    private const string CacheFolder = "Library/InfinityFaunaCache";

    public static void Save(FaunaAsset asset, FaunaSimulationData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (data == null) throw new ArgumentNullException(nameof(data));
        Directory.CreateDirectory(CacheFolder);
        using var stream = new FileStream(GetCachePath(asset), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(asset.SimulationRevision);
        writer.Write(data.SpeciesStableId ?? string.Empty);
        writer.Write(data.SpeciesDisplayName ?? string.Empty);
        writer.Write(data.SourceGeographySeed);
        writer.Write(data.WidthMeters);
        writer.Write(data.LengthMeters);
        writer.Write(data.SimulationTimeDays);
        writer.Write(data.AgentCount);
        for (int i = 0; i < data.Agents.Length; i++)
        {
            FaunaAgentState a = data.Agents[i];
            writer.Write(a.Id);
            writer.Write(a.HerdId);
            writer.Write(a.PositionLocalMeters.x);
            writer.Write(a.PositionLocalMeters.y);
            writer.Write(a.Heading.x);
            writer.Write(a.Heading.y);
            writer.Write(a.Hunger01);
            writer.Write(a.Energy01);
            writer.Write(a.Health01);
            writer.Write(a.CumulativeFoodConsumedKg);
            writer.Write(a.Alive);
        }
    }

    public static bool TryLoad(FaunaAsset asset, out FaunaSimulationData data)
    {
        data = null;
        if (asset == null || !asset.HasMetadata) return false;
        string path = GetCachePath(asset);
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return false;
            int simulationRevision = reader.ReadInt32();
            string stableId = reader.ReadString();
            string displayName = reader.ReadString();
            int seed = reader.ReadInt32();
            float width = reader.ReadSingle();
            float length = reader.ReadSingle();
            double timeDays = reader.ReadDouble();
            int count = reader.ReadInt32();
            if (simulationRevision != asset.SimulationRevision || count != asset.AgentCount) return false;
            if (count < 0 || count > 100000) throw new InvalidDataException("Invalid Fauna agent count.");
            var agents = new FaunaAgentState[count];
            for (int i = 0; i < count; i++)
            {
                agents[i] = new FaunaAgentState
                {
                    Id = reader.ReadInt32(),
                    HerdId = reader.ReadInt32(),
                    PositionLocalMeters = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Heading = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Hunger01 = reader.ReadSingle(),
                    Energy01 = reader.ReadSingle(),
                    Health01 = reader.ReadSingle(),
                    CumulativeFoodConsumedKg = reader.ReadSingle(),
                    Alive = reader.ReadBoolean()
                };
            }
            data = new FaunaSimulationData(stableId, displayName, seed, width, length, timeDays, agents);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Infinity Fauna] Could not read cache '{path}'. Reinitialize Fauna.\n{exception.Message}");
            data = null;
            return false;
        }
    }

    private static string GetCachePath(FaunaAsset asset)
    {
        string assetPath = AssetDatabase.GetAssetPath(asset);
        string guid = string.IsNullOrEmpty(assetPath) ? asset.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(assetPath);
        return Path.Combine(CacheFolder, guid + ".ifauna");
    }
}
