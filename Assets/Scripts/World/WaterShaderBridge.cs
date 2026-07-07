using UnityEngine;

/// <summary>
/// Updated WaterMaterials that references the three InfinityProject water shaders.
/// Drop this file (and the three .shader files) into Assets/Shaders/Water/.
/// WaterBody.cs and WaterRiver.cs will automatically pick up the correct shader
/// per water type through GetMaterial() / GetRiverMaterial().
/// </summary>
public static class WaterMaterials
{
    // Shader paths — must match the "Shader" declaration in each .shader file
    private const string OceanShaderPath  = "InfinityProject/Water/Ocean";
    private const string LakeShaderPath   = "InfinityProject/Water/Lake";
    private const string RiverShaderPath  = "InfinityProject/Water/River";

    private static Material _oceanMat, _lakeMat, _riverMat;

    public static Material GetMaterial(WaterBodyType type)
    {
        return type switch
        {
            WaterBodyType.Ocean => GetOrCreate(ref _oceanMat,  OceanShaderPath,  "Water_Ocean"),
            WaterBodyType.Lake  => GetOrCreate(ref _lakeMat,   LakeShaderPath,   "Water_Lake"),
            _                   => GetOrCreate(ref _oceanMat,  OceanShaderPath,  "Water_Ocean")
        };
    }

    public static Material GetRiverMaterial() =>
        GetOrCreate(ref _riverMat, RiverShaderPath, "Water_River");

    private static Material GetOrCreate(ref Material mat, string shaderPath, string name)
    {
        if (mat != null) return mat;

        Shader sh = Shader.Find(shaderPath);
        if (sh == null)
        {
            Debug.LogWarning(
                $"[WaterMaterials] Shader '{shaderPath}' not found. " +
                $"Make sure the .shader file is in your project. Falling back to URP Lit.");
            sh = Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Standard");
        }

        mat = new Material(sh) { name = name };

        // Set transparent rendering defaults
        mat.SetFloat("_Surface", 1f);   // Transparent (URP Lit)
        mat.SetFloat("_Blend",   0f);   // Alpha blend
        mat.renderQueue = 3000;

        return mat;
    }

    /// <summary>
    /// Call this if you hot-reload shaders in editor — clears cached instances
    /// so next GetMaterial() call re-creates them.
    /// </summary>
    public static void ClearCache()
    {
        _oceanMat = _lakeMat = _riverMat = null;
    }
}
