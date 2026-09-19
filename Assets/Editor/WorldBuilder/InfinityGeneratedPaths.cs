using UnityEditor;

internal static class InfinityGeneratedPaths
{
    public const string Root = "Assets/InfinityGenerated";

    public static string Ensure(string childFolder)
    {
        if (!AssetDatabase.IsValidFolder(Root))
            AssetDatabase.CreateFolder("Assets", "InfinityGenerated");

        if (string.IsNullOrWhiteSpace(childFolder))
            return Root;

        string folder = Root + "/" + childFolder;
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(Root, childFolder);

        return folder;
    }
}
