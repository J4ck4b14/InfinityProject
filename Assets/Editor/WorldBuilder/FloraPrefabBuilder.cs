using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class FloraPrefabBuilder
{
    internal const string GrassPrefabPath = "Assets/Prefabs/Flora/TemperateGrass.prefab";
    internal const string ShrubPrefabPath = "Assets/Prefabs/Flora/RiparianShrub.prefab";
    internal const string ConiferPrefabPath = "Assets/Prefabs/Flora/ColdTolerantConifer.prefab";

    internal const string GrassMaterialPath = "Assets/Materials/Flora/M_Grass_Instanced.mat";
    internal const string ShrubMaterialPath = "Assets/Materials/Flora/M_Shrub_Instanced.mat";
    internal const string ConiferMaterialPath = "Assets/Materials/Flora/M_Conifer_Instanced.mat";

    private const string TreeSourcePath = "Assets/FBX/Flora/Trees/LP_Tree.fbx";
    private const string TreeAlbedoPath = "Assets/Textures/Flora/Trees/LP_Tree/LP_Tree_DefaultMaterial_AlbedoTransparency.png";
    private const string GrassMeshPath = "Assets/Prefabs/Flora/Generated/GrassCross.asset";
    private const string ShrubMeshPath = "Assets/Prefabs/Flora/Generated/ShrubCross.asset";
    private const string PresentationShaderName = "InfinityProject/Flora/Instanced";

    [InitializeOnLoadMethod]
    private static void EnsureAfterReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (PrefabsAndMaterialsAreCurrent()) return;
            BuildAll();
        };
    }

    [MenuItem("Infinity/World/Rebuild Flora Prefabs")]
    public static void RebuildFromMenu() => BuildAll();

    public static void BuildAll()
    {
        EnsureFolder("Assets/Prefabs", "Flora");
        EnsureFolder("Assets/Prefabs/Flora", "Generated");
        EnsureFolder("Assets/Materials", "Flora");

        Shader shader = Shader.Find(PresentationShaderName);
        if (shader == null)
        {
            Debug.LogError($"[Infinity Flora] Required presentation shader '{PresentationShaderName}' was not found. Flora prefabs were not rebuilt.");
            return;
        }

        BuildCrossPrefab(
            GrassPrefabPath,
            GrassMeshPath,
            GrassMaterialPath,
            "TemperateGrass",
            new Color(0.24f, 0.48f, 0.16f, 1f),
            0.65f,
            0.55f,
            3,
            shader);

        BuildCrossPrefab(
            ShrubPrefabPath,
            ShrubMeshPath,
            ShrubMaterialPath,
            "RiparianShrub",
            new Color(0.16f, 0.38f, 0.18f, 1f),
            1.15f,
            1.45f,
            4,
            shader);

        BuildConiferPrefab(shader);
        AssetDatabase.SaveAssets();
    }

    public static void EnsureCurrent()
    {
        if (!PrefabsAndMaterialsAreCurrent()) BuildAll();
    }

    public static GameObject LoadGrass() => AssetDatabase.LoadAssetAtPath<GameObject>(GrassPrefabPath);
    public static GameObject LoadShrub() => AssetDatabase.LoadAssetAtPath<GameObject>(ShrubPrefabPath);
    public static GameObject LoadConifer() => AssetDatabase.LoadAssetAtPath<GameObject>(ConiferPrefabPath);

    private static bool PrefabsAndMaterialsAreCurrent()
    {
        Shader shader = Shader.Find(PresentationShaderName);
        if (shader == null) return false;
        return LoadGrass() != null && LoadShrub() != null && LoadConifer() != null &&
               UsesShader(GrassMaterialPath, shader) && UsesShader(ShrubMaterialPath, shader) && UsesShader(ConiferMaterialPath, shader);
    }

    private static bool UsesShader(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        return material != null && material.shader == shader && material.enableInstancing;
    }

    private static void BuildCrossPrefab(
        string prefabPath,
        string meshPath,
        string materialPath,
        string name,
        Color color,
        float width,
        float height,
        int planes,
        Shader shader)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = BuildCrossMesh(name + "Mesh", width, height, planes);
            AssetDatabase.CreateAsset(mesh, meshPath);
        }

        Material material = EnsureMaterial(materialPath, shader, color, null, false);

        GameObject root = new(name);
        try
        {
            MeshFilter filter = root.AddComponent<MeshFilter>();
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildConiferPrefab(Shader shader)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(TreeSourcePath);
        if (source == null)
        {
            Debug.LogWarning($"Flora source model not found at '{TreeSourcePath}'. Conifer instances will be unavailable.");
            return;
        }

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TreeAlbedoPath);
        Material material = EnsureMaterial(ConiferMaterialPath, shader, Color.white, albedo, true);

        GameObject root = new("ColdTolerantConifer");
        try
        {
            GameObject model = Object.Instantiate(source, root.transform);
            model.name = "Model";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int m = 0; m < materials.Length; m++) materials[m] = material;
                renderers[i].sharedMaterials = materials;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, ConiferPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Material EnsureMaterial(string path, Shader shader, Color color, Texture2D texture, bool alphaClip)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.enableInstancing = true;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_UseTexture", texture != null ? 1f : 0f);
        material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        material.SetFloat("_Cutoff", alphaClip ? 0.35f : 0f);
        material.SetFloat("_Cull", 0f);
        if (texture != null) material.SetTexture("_BaseMap", texture);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh BuildCrossMesh(string name, float width, float height, int planes)
    {
        planes = Mathf.Clamp(planes, 2, 6);
        var vertices = new Vector3[planes * 4];
        var uvs = new Vector2[planes * 4];
        var triangles = new int[planes * 12];

        for (int p = 0; p < planes; p++)
        {
            float angle = p * Mathf.PI / planes;
            Vector3 right = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 half = right * (width * 0.5f);
            int v = p * 4;
            Vector3 topHalf = half * 0.16f;
            vertices[v] = -half;
            vertices[v + 1] = half;
            vertices[v + 2] = -topHalf + Vector3.up * height;
            vertices[v + 3] = topHalf + Vector3.up * height;
            uvs[v] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(1f, 0f);
            uvs[v + 2] = new Vector2(0f, 1f);
            uvs[v + 3] = new Vector2(1f, 1f);

            int t = p * 12;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
            triangles[t + 6] = v + 1;
            triangles[t + 7] = v + 2;
            triangles[t + 8] = v;
            triangles[t + 9] = v + 3;
            triangles[t + 10] = v + 2;
            triangles[t + 11] = v + 1;
        }

        var mesh = new Mesh { name = name };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}
