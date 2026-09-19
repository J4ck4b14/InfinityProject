using System;
using System.IO;
using InfinityProject.World.Ground;
using UnityEditor;
using UnityEngine;

internal static class GroundConditionBinaryCache
{
    private const int Magic = 0x49474331;
    private const int Version = 6;
    private const string CacheFolder = "Library/InfinityGroundCache";

    public static void Save(GroundConditionAsset asset, GroundConditionData data)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset)); if (data == null) throw new ArgumentNullException(nameof(data));
        Directory.CreateDirectory(CacheFolder); using var stream = new FileStream(GetCachePath(asset), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20); using var writer = new BinaryWriter(stream);
        writer.Write(Magic); writer.Write(Version); writer.Write(asset.GenerationRevision); writer.Write(data.Resolution); writer.Write(data.WidthMeters); writer.Write(data.LengthMeters); writer.Write(data.SourceGeographySeed); writer.Write(data.SourceHeightHash);
        writer.Write(data.MinimumWetnessIndex); writer.Write(data.MaximumWetnessIndex); writer.Write(data.Percentile05WetnessIndex); writer.Write(data.Percentile95WetnessIndex);
        WriteFloatArray(writer, data.HydrologicalWetnessIndex); WriteFloatArray(writer, data.RunOnPotential); WriteFloatArray(writer, data.WaterAvailabilityPotential); WriteFloatArray(writer, data.SoilRetentionPotential);
    }

    public static bool TryLoad(GroundConditionAsset asset, out GroundConditionData data)
    {
        data = null; if (asset == null || !asset.HasMetadata) return false; string path = GetCachePath(asset); if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20); using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return false; int revision = reader.ReadInt32(); if (revision != asset.GenerationRevision) return false;
            int n = reader.ReadInt32(); float w = reader.ReadSingle(), l = reader.ReadSingle(); int seed = reader.ReadInt32(); long hash = reader.ReadInt64(); float min=reader.ReadSingle(),max=reader.ReadSingle(),p05=reader.ReadSingle(),p95=reader.ReadSingle(); int expected=n*n;
            float[] twi=ReadFloatArray(reader,expected), runOn=ReadFloatArray(reader,expected), water=ReadFloatArray(reader,expected), retention=ReadFloatArray(reader,expected);
            data = new GroundConditionData(n,w,l,seed,hash,twi,runOn,water,retention,min,max,p05,p95); return true;
        }
        catch(Exception e){Debug.LogWarning($"[Infinity Ground] Could not read cache '{path}'. Regenerate Ground Conditions.\n{e.Message}"); return false;}
    }
    public static void Delete(GroundConditionAsset asset){if(asset==null)return;string p=GetCachePath(asset);if(File.Exists(p))File.Delete(p);}
    private static string GetCachePath(GroundConditionAsset asset){string p=AssetDatabase.GetAssetPath(asset);string guid=string.IsNullOrEmpty(p)?asset.GetInstanceID().ToString():AssetDatabase.AssetPathToGUID(p);return Path.Combine(CacheFolder,guid+".iground");}
    private static void WriteFloatArray(BinaryWriter w,float[] v){w.Write(v.Length);byte[] b=new byte[v.Length*sizeof(float)];Buffer.BlockCopy(v,0,b,0,b.Length);w.Write(b);}
    private static float[] ReadFloatArray(BinaryReader r,int expected){int len=r.ReadInt32();if(len!=expected)throw new InvalidDataException("Ground cache float-array length mismatch.");byte[] b=r.ReadBytes(len*sizeof(float));if(b.Length!=len*sizeof(float))throw new EndOfStreamException();var v=new float[len];Buffer.BlockCopy(b,0,v,0,b.Length);return v;}
}
