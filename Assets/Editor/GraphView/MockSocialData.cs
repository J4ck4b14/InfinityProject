using System;
using System.Collections.Generic;
using UnityEngine;
using static DefaultGuilds;
using Random = UnityEngine.Random;

/// <summary>
/// Root object holding all mock data for the Social Graph Tool.
/// Create this as a ScriptableObject asset to edit in the Editor.
/// </summary>
[CreateAssetMenu(fileName = "MockSocialData", menuName = "Social Graph/Mock Social Data")]
public class MockSocialData : ScriptableObject
{
    public List<MockVillage> villages = new();
}

/// <summary>
/// A village node that contains a list of citizens.
/// </summary>
[Serializable]
public class MockVillage
{
    public string villageName;
    public Vector2 editorPosition;
    public List<MockCitizen> citizens = new();

    public void Randomize(List<MockCitizen> globalPool)
    {
        citizens = new List<MockCitizen>();
        int count = Random.Range(5, 12);
        var pool = new List<MockCitizen>();

        for (int i = 0; i < count; i++)
        {
            var c = new MockCitizen();
            citizens.Add(c);
            pool.Add(c);
            globalPool.Add(c);
        }

        foreach (var c in citizens)
            c.Randomize(pool, globalPool);
    }

}

/// <summary>
/// A mock citizen node within a village.
/// </summary>
[Serializable]
public class MockCitizen
{
    public string guid; // Unique identifier for this citizen

    public string name;
    public int age;
    public int socialRank; // 0 = peasant, 10 = royalty
    public string guild; // name of the guild this citizen belongs to
    public string gender;
    public string profession;
    public string status;
    public float money;

    //--------------------------------------
    //                NEEDS                 
    //--------------------------------------
    public float hunger;        // [0–1]
    public float sleepiness;    // [0–1]
    public float safety;        // [0–1]
    public float socialContact; // [0–1]


    public MockEthicalProfile ethics;

    public Texture2D portrait;

    public Vector2 editorPosition; // Relative to graph

    public List<MockBridge> connections = new(); // Connections to other citizens or villages

    public void Randomize(List<MockCitizen> sameVillagePool, List<MockCitizen> allCitizensPool)
    {
        guid = System.Guid.NewGuid().ToString();

        name = $"Citizen_{Random.Range(1000, 9999)}";
        age = Random.Range(16, 85);
        socialRank = Mathf.RoundToInt(Mathf.Pow(Random.Range(0f, 1f), 2f) * 10); // Bias toward low rank

        hunger = Random.Range(0.2f, 0.9f);
        sleepiness = Random.Range(0.1f, 0.7f);
        safety = Random.Range(0.3f, 1.0f);
        socialContact = Random.Range(0.2f, 0.8f);

        ethics = new MockEthicalProfile
        {
            lawfulness = RandomNormal(),
            justice = RandomNormal(),
            care = RandomNormal(),
            beneficence = RandomNormal(),
            honesty = RandomNormal(),
            loyalty = RandomNormal(),
            autonomy = RandomNormal(),
            respect = RandomNormal(),
            courage = RandomNormal(),
            temperance = RandomNormal()
        };

        profession = GuildLogic.PickRandomProfession();
        guild = GuildLogic.GetGuildForProfession(profession);

        // Assign colored placeholder portrait
        portrait = GenerateFlatColorTexture(Random.ColorHSV(0f, 1f, 0.4f, 1f, 0.6f, 1f));

        connections = new List<MockBridge>();

        // Intra-village connections
        var shuffled = new List<MockCitizen>(sameVillagePool);
        Shuffle(shuffled);
        foreach (var target in shuffled)
        {
            if (target == this || connections.Count >= 3) break;
            connections.Add(RandomConnectionTo(target));
        }

        // Inter-village connection (1 at most)
        if (Random.value < 0.3f && allCitizensPool.Count > 0)
        {
            var target = allCitizensPool[Random.Range(0, allCitizensPool.Count)];
            if (target != this)
                connections.Add(RandomConnectionTo(target));
        }
    }

    private static float RandomNormal()
    {
        return Mathf.Clamp01((float)(RandomGaussian() * 0.5 + 0.0));
    }

    private static double RandomGaussian()
    {
        // Box-Muller transform
        double u1 = 1.0 - Random.value;
        double u2 = 1.0 - Random.value;
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
    }

    private static Texture2D GenerateFlatColorTexture(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private static MockBridge RandomConnectionTo(MockCitizen target)
    {
        return new MockBridge
        {
            targetGuid = target.guid,
            targetType = NodeType.Individual,
            relationshipStrength = Random.Range(0.2f, 1f),
            reputation = new MockEthicalProfile
            {
                lawfulness = RandomNormal(),
                justice = RandomNormal(),
                care = RandomNormal(),
                honesty = RandomNormal()
                // (rest left neutral)
            }
        };
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

}

/// <summary>
/// A social connection between this citizen and another.
/// </summary>
[Serializable]
public class MockBridge
{
    public string targetGuid; // Machine name of the citizen or village connected to
    public NodeType targetType;

    [Range(0f, 1f)]
    public float relationshipStrength;

    public MockEthicalProfile reputation;
}

/// <summary>
/// Simplified ethical vector: 10 moral axes.
/// Values in [-1,1] range.
/// </summary>
[Serializable]
public struct MockEthicalProfile
{
    [Range(-1f, 1f)] public float lawfulness;
    [Range(-1f, 1f)] public float justice;
    [Range(-1f, 1f)] public float care;
    [Range(-1f, 1f)] public float beneficence;
    [Range(-1f, 1f)] public float honesty;
    [Range(-1f, 1f)] public float loyalty;
    [Range(-1f, 1f)] public float autonomy;
    [Range(-1f, 1f)] public float respect;
    [Range(-1f, 1f)] public float courage;
    [Range(-1f, 1f)] public float temperance;

    public static MockEthicalProfile Neutral => new MockEthicalProfile
    {
        lawfulness = 0,
        justice = 0,
        care = 0,
        beneficence = 0,
        honesty = 0,
        loyalty = 0,
        autonomy = 0,
        respect = 0,
        courage = 0,
        temperance = 0
    };

}

public static class DefaultGuilds
{
    public static readonly string[] Names = new[]
    {
        "The Iron Veil",
        "Circle of Embers",
        "The Hollow Mark",
        "Pale Fang",
        "Anvil Union",
        "The Oathbound",
        "The Rooted Maw",
        "Crimson Ledger",
        "Verdant Bloom"
    };

    public static class GuildLogic
    {
        public static readonly string[] Professions = new[]
        {
        "Smith",
        "Hunter",
        "Doctor",
        "Herbalist",
        "Butcher",
        "Farmer",
        "Scholar",
        "Priest",
        "Guard",
        "Merchant",
        "Courier",
        "Sellsword",
        "Bard",
        "Unemployed"
        };

        public static readonly Dictionary<string, List<string>> GuildMatches = new()
        {
        { "Smith",       new() { "The Iron Veil", "Circle of Embers" } },
        { "Hunter",      new() { "Pale Fang", "Verdant Bloom" } },
        { "Doctor",      new() { "Verdant Bloom", "Pale Fang", "The Rooted Maw", "Unaffiliated" } },
        { "Herbalist",   new() { "Verdant Bloom", "Circle of Embers" } },
        { "Butcher",     new() { "The Rooted Maw", "Crimson Ledger" } },
        { "Farmer",      new() { "Verdant Bloom", "Crimson Ledger", "Unaffiliated" } },
        { "Scholar",     new() { "Circle of Embers", "Crimson Ledger" } },
        { "Priest",      new() { "Circle of Embers", "The Iron Veil", "The Oathbound" } },
        { "Guard",       new() { "The Iron Veil", "Pale Fang", "The Oathbound" } },
        { "Merchant",    new() { "Crimson Ledger", "Wandering Ash" } },
        { "Courier",     new() { "Wandering Ash", "Crimson Ledger", "Pale Fang" } },
        { "Sellsword",   new() { "Pale Fang", "The Iron Veil", "Unaffiliated" } },
        { "Bard",        new() { "Wandering Ash", "Circle of Embers", "Unaffiliated" } },
        { "Unemployed",  new() { "Pale Fang", "Wandering Ash", "Unaffiliated" } }
        };

        public static string GetGuildForProfession(string profession)
        {
            if (!GuildMatches.TryGetValue(profession, out var possibleGuilds))
                return "Unaffiliated";
            return possibleGuilds[UnityEngine.Random.Range(0, possibleGuilds.Count)];
        }

        public static string PickRandomProfession()
        {
            return Professions[UnityEngine.Random.Range(0, Professions.Length)];
        }
    }
}

