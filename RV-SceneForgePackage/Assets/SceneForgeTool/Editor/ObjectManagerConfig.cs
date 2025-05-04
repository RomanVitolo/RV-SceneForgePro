using UnityEngine;

namespace RV.SceneForgeTool
{
    [CreateAssetMenu(menuName = "LevelTools/Object Manager Config")]
    public class ObjectManagerConfig : ScriptableObject
    {
        [Tooltip("Drag your designer-approved prefabs here and tag each as 2D or 3D")]
        public PrefabEntry[] allowedPrefabs;
    }

    public enum PrefabType { TwoD, ThreeD }

    [System.Serializable]
    public struct PrefabEntry
    {
        public GameObject prefab;
        public PrefabType type;
    }
}