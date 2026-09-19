using InfinityProject.World.Fauna.Presentation;
using UnityEditor;
using UnityEngine;

internal static class DoePrefabBuilder
{
    internal const string SourcePath = "Assets/FBX/Fauna/Doe/Doe.fbx";
    internal const string MaterialPath = "Assets/Materials/Fauna/M_Doe.mat";
    internal const string PrefabPath = "Assets/Prefabs/Fauna/Doe.prefab";

    [MenuItem("Infinity/Fauna/Rebuild Doe Prefab")]
    public static void RebuildFromMenu()
    {
        Build();
    }

    public static GameObject Build()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"Doe source model was not found at '{SourcePath}'.");
            return null;
        }

        EnsureFolder("Assets/Prefabs", "Fauna");

        GameObject wrapper = new("Doe");
        try
        {
            GameObject model = Object.Instantiate(source, wrapper.transform);
            model.name = "Model";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterial = material;
            }

            Transform root = RequireBone(model.transform, "Root");
            Transform pelvis = RequireBone(model.transform, "Pelvis");
            Transform spine1 = RequireBone(model.transform, "Spine1");
            Transform spine2 = RequireBone(model.transform, "Spine2");
            Transform neck1 = RequireBone(model.transform, "Neck1");
            Transform neck2 = RequireBone(model.transform, "Neck2");
            Transform head = RequireBone(model.transform, "Head");

            Vector3 forward = head.position - pelvis.position;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.000001f
                ? wrapper.transform.InverseTransformDirection(forward.normalized)
                : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f) forward = Vector3.forward;
            forward.Normalize();

            float bottom = 0f;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                float candidate = renderers[i].bounds.min.y;
                if (!hasBounds || candidate < bottom) bottom = candidate;
                hasBounds = true;
            }

            DoePresentationRig rig = wrapper.AddComponent<DoePresentationRig>();
            wrapper.AddComponent<FaunaPresentationAgent>();
            rig.Configure(
                root,
                pelvis,
                spine1,
                spine2,
                neck1,
                neck2,
                head,
                RequireBone(model.transform, "FrontLeg_L_Upper"),
                RequireBone(model.transform, "FrontLeg_L_Lower"),
                RequireBone(model.transform, "FrontHoof_L"),
                RequireBone(model.transform, "FrontLeg_R_Upper"),
                RequireBone(model.transform, "FrontLeg_R_Lower"),
                RequireBone(model.transform, "FrontHoof_R"),
                RequireBone(model.transform, "HindLeg_L_Upper"),
                RequireBone(model.transform, "HindLeg_L_Lower"),
                RequireBone(model.transform, "HindHoof_L"),
                RequireBone(model.transform, "HindLeg_R_Upper"),
                RequireBone(model.transform, "HindLeg_R_Lower"),
                RequireBone(model.transform, "HindHoof_R"),
                renderers,
                forward,
                hasBounds ? wrapper.transform.position.y - bottom : 0f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Doe prefab rebuilt at '{PrefabPath}'.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(wrapper);
        }
    }

    private static Transform RequireBone(Transform parent, string name)
    {
        Transform bone = FindDeepChild(parent, name);
        if (bone == null)
            throw new System.InvalidOperationException($"Doe rig is missing required bone '{name}'.");
        return bone;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}
