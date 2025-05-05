namespace RV.SceneForgeTool.Editor
{
    using System.Collections.Generic;
    using UnityEngine;
        [CreateAssetMenu(menuName = "LevelTools/Scene Position Database")]
        public class ScenePositionDatabase : ScriptableObject
        {
            public List<LevelObjectManagerWindow.PositionEntry> positions = new List<LevelObjectManagerWindow.PositionEntry>();
            public List<PathData> paths = new List<PathData>();
        }

        [System.Serializable]
        public class PathData
        {
            public string name = "New Path";
            public Color pathColor = Color.green;
            public List<int> pointIndices = new List<int>();
            public bool isClosed = false;
            public bool showInScene = true;
            public float resolution = 0.5f;
            [Range(0, 1)] public float tension = 0.5f;
            public bool isExpanded = true;
        }
    
}
