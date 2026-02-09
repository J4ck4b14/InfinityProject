using UnityEngine;

[CreateAssetMenu(menuName = "WorldBuilder/SavedHeightmap")]
public class SavedHeightmap : ScriptableObject
{
 public string displayName;
 public int heightmapResolution; // hmRes (resolution used by TerrainData)
 public float terrainSide;
 public float terrainHeight;
 public Vector3 terrainPosition;
 public double createdTime;
 public float[] heights; // flattened [y * res + x]
}
