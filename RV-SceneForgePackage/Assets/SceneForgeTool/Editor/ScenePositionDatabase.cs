using System.Collections.Generic;
using UnityEngine;

namespace RV.SceneForgeTool
{
    [CreateAssetMenu(menuName = "LevelTools/Scene Position Database")]
    public class ScenePositionDatabase : ScriptableObject
    {
        public List<PositionEntry> positions = new List<PositionEntry>();
    }
    
    [System.Serializable]
    public class PositionEntry
    {
        public string name;
        public Vector3 position;
    }
}