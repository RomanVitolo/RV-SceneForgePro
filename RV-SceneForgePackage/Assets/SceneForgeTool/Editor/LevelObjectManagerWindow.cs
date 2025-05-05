using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditorInternal;
using System.Linq;

namespace RV.SceneForgeTool.Editor
{
    public class LevelObjectManagerWindow : EditorWindow
    {
        // Config and Database
        ObjectManagerConfig config;
        ScenePositionDatabase positionDB;

        // ReorderableList for Positions
        SerializedObject serializedDB;
        ReorderableList posList;
        ReorderableList pathList;
        string filterTag = "";

        // UI State
        int tabIndex = 0;
        string[] tabs = new[] { "Objects", "Positions" };
        float splitterPos = 0.5f;
        private Vector2 prefabScroll, sceneScroll; 
        private Vector2 positionsScroll;
        string prefabSearch = "", sceneSearch = "";

        // Spawn logic
        int selectedPositionOption = 0;
        Vector3 customSpawnPosition = Vector3.zero;

        // Preview
        GameObject previewPrefab;
        Texture2D previewTexture;

        // Position picker
        bool pickingPosition = false;
        string newPositionName = "NewPosition";

        // Pattern distribution settings
        int patternType = 0; // Grid, Circle, Line
        int gridRows = 1;
        int gridCols = 1;
        float gridSpacing = 1f;
        int circleCount = 8;
        float circleRadius = 5f;
        int lineCount = 5;
        float lineSpacing = 1f;

        // Path editing
        int selectedPathIndex = -1;
        bool editingPath = false;
        bool addingToPath = false;
        float pathObjectSpacing = 1f;
        int pathObjectCount = 10;

        [MenuItem("Window/Level Object Manager")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<LevelObjectManagerWindow>();
            wnd.titleContent = new GUIContent("Object Manager");
            wnd.minSize = new Vector2(600, 700);
        }

        void OnEnable()
        {
            // Load config asset
            var cfg = AssetDatabase.FindAssets("t:ObjectManagerConfig").FirstOrDefault();
            if (!string.IsNullOrEmpty(cfg))
                config = AssetDatabase.LoadAssetAtPath<ObjectManagerConfig>(AssetDatabase.GUIDToAssetPath(cfg));

            // Load or create position database
            var db = AssetDatabase.FindAssets("t:ScenePositionDatabase").FirstOrDefault();
            if (!string.IsNullOrEmpty(db))
                positionDB = AssetDatabase.LoadAssetAtPath<ScenePositionDatabase>(AssetDatabase.GUIDToAssetPath(db));
            else
            {
                positionDB = CreateInstance<ScenePositionDatabase>();
                AssetDatabase.CreateAsset(positionDB, "Assets/ScenePositionDatabase.asset");
                AssetDatabase.SaveAssets();
            }

            // Setup Positions ReorderableList
            serializedDB = new SerializedObject(positionDB);
            posList = new ReorderableList(serializedDB,
                serializedDB.FindProperty("positions"),
                true, true, true, true);

            posList.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, "Saved Positions");

            posList.drawElementCallback = (rect, index, active, focused) =>
            {
                var element = posList.serializedProperty.GetArrayElementAtIndex(index);

                // Filtrado por tag
                if (!string.IsNullOrEmpty(filterTag) &&
                    element.FindPropertyRelative("tag").stringValue.IndexOf(filterTag, StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                float lineH = EditorGUIUtility.singleLineHeight;
                rect.y += 2;
                float x = rect.x;

                // Nombre con ancho fijo
                float nameW = 80f;
                EditorGUI.PropertyField(
                    new Rect(x, rect.y, nameW, lineH),
                    element.FindPropertyRelative("name"), GUIContent.none);
                x += nameW + 5;

                // Posición con ancho fijo
                float posW = 140f;
                var posVal = element.FindPropertyRelative("position").vector3Value;
                EditorGUI.LabelField(new Rect(x, rect.y, posW, lineH), posVal.ToString("F2"));
                x += posW + 5;

                // Botón Go
                if (GUI.Button(new Rect(x, rect.y, 30, lineH), "Go"))
                    RestoreCameraSettings(positionDB.positions[index]);
                x += 35;

                // Botón Capture
                if (GUI.Button(new Rect(x, rect.y, 80, lineH), "Capture"))
                    CaptureSnapshot(positionDB.positions[index]);
                x += 85;

                // Botón Del
                if (GUI.Button(new Rect(x, rect.y, 40, lineH), "Del"))
                {
                    serializedDB.Update();
                    posList.serializedProperty.DeleteArrayElementAtIndex(index);
                    serializedDB.ApplyModifiedProperties();
                    serializedDB.Update();
                    EditorUtility.SetDirty(positionDB);
                    return;
                }

                // Foldout manual
                var foldProp = element.FindPropertyRelative("isExpanded");
                if (GUI.Button(new Rect(rect.xMax - 20, rect.y, 20, lineH),
                                   foldProp.boolValue ? "-" : "+"))
                    foldProp.boolValue = !foldProp.boolValue;

                // Detalles expandidos
                if (foldProp.boolValue)
                {
                    rect.y += lineH + 4;
                    EditorGUI.indentLevel++;
                    EditorGUI.PropertyField(
                        new Rect(rect.x + 10, rect.y, rect.width - 10,
                                 EditorGUI.GetPropertyHeight(element)),
                        element, true);
                    EditorGUI.indentLevel--;
                }
            };

            posList.elementHeightCallback = index =>
            {
                var element = posList.serializedProperty.GetArrayElementAtIndex(index);
                var tagValue = element.FindPropertyRelative("tag").stringValue;
                if (!string.IsNullOrEmpty(filterTag) &&
                    tagValue.IndexOf(filterTag, System.StringComparison.OrdinalIgnoreCase) < 0)
                    return 0f;
                var foldProp = element.FindPropertyRelative("isExpanded");
                float h = EditorGUIUtility.singleLineHeight + 4;
                if (foldProp.boolValue)
                    h += EditorGUI.GetPropertyHeight(element) + 4;
                return h;
            };

        pathList = new ReorderableList(serializedDB, 
    serializedDB.FindProperty("paths"),
    true, true, true, true);

pathList.drawHeaderCallback = rect => 
    EditorGUI.LabelField(rect, "Saved Paths");

pathList.drawElementCallback = (rect, index, active, focused) => {
    var element = pathList.serializedProperty.GetArrayElementAtIndex(index);
    var nameProp = element.FindPropertyRelative("name");
    var colorProp = element.FindPropertyRelative("pathColor");
    var showProp = element.FindPropertyRelative("showInScene");
    var expandedProp = element.FindPropertyRelative("isExpanded");

    rect.y += 2;
    float lineH = EditorGUIUtility.singleLineHeight;
    
    // Foldout
    expandedProp.boolValue = EditorGUI.Foldout(
        new Rect(rect.x, rect.y, 20, lineH), 
        expandedProp.boolValue, GUIContent.none);
    
    // Nombre
    EditorGUI.PropertyField(
        new Rect(rect.x + 20, rect.y, rect.width - 120, lineH),
        nameProp, GUIContent.none);
    
    // Color
    EditorGUI.PropertyField(
        new Rect(rect.x + rect.width - 100, rect.y, 60, lineH),
        colorProp, GUIContent.none);
    
    // Mostrar/ocultar
    EditorGUI.PropertyField(
        new Rect(rect.x + rect.width - 30, rect.y, 30, lineH),
        showProp, GUIContent.none);
    
    if (expandedProp.boolValue)
    {
        rect.y += lineH + 4;
        float contentWidth = rect.width - 10;
        
        // Configuración de path
        var closedProp = element.FindPropertyRelative("isClosed");
        var resProp = element.FindPropertyRelative("resolution");
        var tensionProp = element.FindPropertyRelative("tension");
        
        // Usar campos más estrechos para mejor ajuste
        EditorGUI.LabelField(new Rect(rect.x, rect.y, 80, lineH), "Closed:");
        closedProp.boolValue = EditorGUI.Toggle(new Rect(rect.x + 85, rect.y, 20, lineH), closedProp.boolValue);
        rect.y += lineH + 2;
        
        EditorGUI.LabelField(new Rect(rect.x, rect.y, 80, lineH), "Resolution:");
        resProp.floatValue = EditorGUI.FloatField(new Rect(rect.x + 85, rect.y, 60, lineH), resProp.floatValue);
        rect.y += lineH + 2;
        
        EditorGUI.LabelField(new Rect(rect.x, rect.y, 80, lineH), "Tension:");
        tensionProp.floatValue = EditorGUI.Slider(new Rect(rect.x + 85, rect.y, 100, lineH), tensionProp.floatValue, 0, 1);
        rect.y += lineH + 2;
        
        // Puntos del path
        EditorGUI.LabelField(new Rect(rect.x, rect.y, contentWidth, lineH), 
            $"Points: {positionDB.paths[index].pointIndices.Count}");
    }
};

pathList.elementHeightCallback = index =>
{
    var element = pathList.serializedProperty.GetArrayElementAtIndex(index);
    var expandedProp = element.FindPropertyRelative("isExpanded");
    
    float height = EditorGUIUtility.singleLineHeight + 6; // Altura base con padding
    
    if (expandedProp.boolValue)
    {
        // Altura cuando está expandido (línea base + 4 líneas más + espaciado)
        height += (EditorGUIUtility.singleLineHeight * 4) + 8;
    }
    
    return height;
};

pathList.onSelectCallback = list => {
    selectedPathIndex = list.index;
};

            pathList.onSelectCallback = list => {
                selectedPathIndex = list.index;
            };

            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        void OnSceneGUI(SceneView sv)
        {
            DrawPathsInScene();

            if (positionDB != null)
            {
                foreach (var entry in positionDB.positions)
                {
                    Handles.color = entry.gizmoColor;
                    Handles.DrawWireDisc(entry.position, Vector3.up, 0.3f);
                    var style = new GUIStyle(EditorStyles.boldLabel)
                        { normal = { textColor = entry.labelColor } };
                    Handles.Label(entry.position + Vector3.up * 0.5f, entry.name, style);
                }
            }

            if (!pickingPosition && !editingPath) return;
            
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var plane = new Plane(Vector3.up, Vector3.zero);
                if (plane.Raycast(ray, out float dist))
                {
                    var pos = ray.GetPoint(dist);
                    
                    if (pickingPosition)
                    {
                        var newEntry = new PositionEntry(newPositionName, pos);
                        newEntry.InitializeCamera(SceneView.lastActiveSceneView);
                        positionDB.positions.Add(newEntry);
                        EditorUtility.SetDirty(positionDB);
                        AssetDatabase.SaveAssets();
                        pickingPosition = false;
                        Repaint();
                    }
                    else if (editingPath && selectedPathIndex >= 0 && selectedPathIndex < positionDB.paths.Count)
                    {
                        // Encontrar el punto más cercano
                        float minDist = float.MaxValue;
                        int closestIdx = -1;
                        
                        for (int i = 0; i < positionDB.positions.Count; i++)
                        {
                            float d = Vector3.Distance(pos, positionDB.positions[i].position);
                            if (d < minDist && d < 1.0f) // 1 unidad de radio
                            {
                                minDist = d;
                                closestIdx = i;
                            }
                        }
                        
                        if (closestIdx >= 0)
                        {
                            serializedDB.Update();
                            var pathProp = pathList.serializedProperty.GetArrayElementAtIndex(selectedPathIndex);
                            var indicesProp = pathProp.FindPropertyRelative("pointIndices");
                            
                            if (addingToPath)
                            {
                                // Agregar al final
                                indicesProp.arraySize++;
                                indicesProp.GetArrayElementAtIndex(indicesProp.arraySize - 1).intValue = closestIdx;
                            }
                            else
                            {
                                // Reemplazar selección
                                if (positionDB.paths[selectedPathIndex].pointIndices.Count > 0)
                                {
                                    indicesProp.GetArrayElementAtIndex(
                                        positionDB.paths[selectedPathIndex].pointIndices.Count - 1).intValue = closestIdx;
                                }
                                else
                                {
                                    indicesProp.arraySize++;
                                    indicesProp.GetArrayElementAtIndex(0).intValue = closestIdx;
                                }
                            }
                            
                            serializedDB.ApplyModifiedProperties();
                            EditorUtility.SetDirty(positionDB);
                        }
                    }
                }

                e.Use();
            }
        }

        void DrawPathsInScene()
        {
            if (positionDB == null || positionDB.paths == null) return;
            
            for (int i = 0; i < positionDB.paths.Count; i++)
            {
                var path = positionDB.paths[i];
                if (!path.showInScene) continue;
                
                if (path.pointIndices.Count < 2) continue;
                
                Handles.color = path.pathColor;
                
                // Dibujar puntos y conexiones
                var points = new Vector3[path.pointIndices.Count];
                for (int j = 0; j < path.pointIndices.Count; j++)
                {
                    int idx = path.pointIndices[j];
                    if (idx >= 0 && idx < positionDB.positions.Count)
                    {
                        points[j] = positionDB.positions[idx].position;
                        Handles.DrawSolidDisc(points[j], Vector3.up, 0.2f);
                    }
                }
                
                // Dibujar spline
                if (points.Length >= 2)
                {
                    var splinePoints = GetSplinePoints(path);
                    for (int k = 0; k < splinePoints.Count - 1; k++)
                    {
                        Handles.DrawLine(splinePoints[k], splinePoints[k+1]);
                    }
                }
                
                // Resaltar path seleccionado
                if (i == selectedPathIndex)
                {
                    Handles.color = new Color(1, 1, 1, 0.3f);
                    for (int j = 0; j < path.pointIndices.Count; j++)
                    {
                        int idx = path.pointIndices[j];
                        if (idx >= 0 && idx < positionDB.positions.Count)
                        {
                            Handles.DrawWireDisc(positionDB.positions[idx].position, 
                                               Vector3.up, 0.3f);
                        }
                    }
                }
            }
        }

        List<Vector3> GetSplinePoints(PathData path)
        {
            var points = new List<Vector3>();
            if (path.pointIndices.Count < 2) return points;
            
            // Obtener puntos reales
            var controlPoints = new List<Vector3>();
            foreach (var idx in path.pointIndices)
            {
                if (idx >= 0 && idx < positionDB.positions.Count)
                {
                    controlPoints.Add(positionDB.positions[idx].position);
                }
            }
            
            if (controlPoints.Count < 2) return points;
            
            // Catmull-Rom spline
            for (int i = 0; i < controlPoints.Count; i++)
            {
                if (path.isClosed || (i > 0 && i < controlPoints.Count - 1))
                {
                    Vector3 p0 = controlPoints[i-1 >= 0 ? i-1 : controlPoints.Count-1];
                    Vector3 p1 = controlPoints[i];
                    Vector3 p2 = controlPoints[(i+1) % controlPoints.Count];
                    Vector3 p3 = controlPoints[(i+2) % controlPoints.Count];
                    
                    int steps = Mathf.CeilToInt(Vector3.Distance(p1, p2) / path.resolution);
                    steps = Mathf.Max(steps, 2);
                    
                    for (int j = 0; j < steps; j++)
                    {
                        float t = j / (float)(steps - 1);
                        points.Add(CalculateCatmullRomPoint(t, p0, p1, p2, p3, path.tension));
                    }
                }
                else if (i == 0)
                {
                    points.Add(controlPoints[0]);
                }
            }
            
            if (!path.isClosed)
            {
                points.Add(controlPoints[controlPoints.Count - 1]);
            }
            
            return points;
        }

        Vector3 CalculateCatmullRomPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float tension)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            
            Vector3 a = 2f * p1;
            Vector3 b = (p2 - p0) * tension;
            Vector3 c = (2f * p0 - 5f * p1 + 4f * p2 - p3) * tension;
            Vector3 d = (-p0 + 3f * p1 - 3f * p2 + p3) * tension;
            
            return 0.5f * (a + b * t + c * t2 + d * t3);
        }

        void OnGUI()
        {
            tabIndex = GUILayout.Toolbar(tabIndex, tabs);
            EditorGUILayout.Space();
            if (tabIndex == 0) DrawObjectsTab();
            else DrawPositionsTab();
        }

        void DrawObjectsTab()
        {
            if (config == null)
            {
                EditorGUILayout.HelpBox(
                    "Create an ObjectManagerConfig via Assets→Create→LevelTools→Object Manager Config.",
                    MessageType.Warning);
                if (GUILayout.Button("Create Config")) CreateConfig();
                return;
            }

            bool is2D = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D;
            EditorGUILayout.LabelField("Editor Mode:", is2D ? "2D" : "3D");
            EditorGUILayout.Space();

            // Spawn At
            int count = positionDB.positions.Count;
            string[] options = new string[count + 1];
            options[0] = "Custom";
            for (int i = 0; i < count; i++) options[i + 1] = positionDB.positions[i].name;
            selectedPositionOption = EditorGUILayout.Popup("Spawn At", selectedPositionOption, options);
            selectedPositionOption = Mathf.Clamp(selectedPositionOption, 0, options.Length - 1);
            Vector3 spawnPos;
            if (selectedPositionOption == 0)
            {
                customSpawnPosition = EditorGUILayout.Vector3Field("Position", customSpawnPosition);
                if (is2D) customSpawnPosition.z = 0;
                spawnPos = customSpawnPosition;
            }
            else
            {
                spawnPos = positionDB.positions[selectedPositionOption - 1].position;
                EditorGUILayout.LabelField("Position:", spawnPos.ToString("F2"));
            }

            EditorGUILayout.Space();

            // Preview
            EditorGUILayout.LabelField("Prefab Preview", EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(100, 100);
            if (previewPrefab != null)
            {
                if (previewTexture == null)
                    previewTexture = AssetPreview.GetAssetPreview(previewPrefab) ??
                                     AssetPreview.GetMiniThumbnail(previewPrefab);
                if (previewTexture != null)
                    GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit);
                if (GUILayout.Button("Clear")) previewPrefab = null;
            }
            else EditorGUI.DrawRect(rect, Color.black * 0.2f);

            EditorGUILayout.Space();

            // Spawn All & Patterns
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn All at Saved Positions"))
            {
                if (previewPrefab != null)
                    foreach (var e in positionDB.positions)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
                        go.transform.position = e.position;
                        Undo.RegisterCreatedObjectUndo(go, "SpawnAll");
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Pattern Distribution", EditorStyles.boldLabel);
            patternType = EditorGUILayout.Popup("Pattern", patternType, new[] { "Grid", "Circle", "Line" });
            switch (patternType)
            {
                case 0: // Grid
                    gridRows = EditorGUILayout.IntField("Rows", gridRows);
                    gridCols = EditorGUILayout.IntField("Cols", gridCols);
                    gridSpacing = EditorGUILayout.FloatField("Spacing", gridSpacing);
                    if (GUILayout.Button("Apply Grid") && previewPrefab != null)
                        for (int r = 0; r < gridRows; r++)
                        for (int c = 0; c < gridCols; c++)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
                            go.transform.position = spawnPos + new Vector3(c * gridSpacing, 0, r * gridSpacing);
                            Undo.RegisterCreatedObjectUndo(go, "SpawnGrid");
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }

                    break;
                case 1: // Circle
                    circleCount = EditorGUILayout.IntField("Count", circleCount);
                    circleRadius = EditorGUILayout.FloatField("Radius", circleRadius);
                    if (GUILayout.Button("Apply Circle") && previewPrefab != null)
                        for (int i = 0; i < circleCount; i++)
                        {
                            float a = Mathf.PI * 2 * i / circleCount;
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
                            go.transform.position =
                                spawnPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * circleRadius;
                            Undo.RegisterCreatedObjectUndo(go, "SpawnCircle");
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }

                    break;
                case 2: // Line
                    lineCount = EditorGUILayout.IntField("Count", lineCount);
                    lineSpacing = EditorGUILayout.FloatField("Spacing", lineSpacing);
                    if (GUILayout.Button("Apply Line") && previewPrefab != null)
                        for (int i = 0; i < lineCount; i++)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
                            go.transform.position = spawnPos + Vector3.right * i * lineSpacing;
                            Undo.RegisterCreatedObjectUndo(go, "SpawnLine");
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }

                    break;
            }

            EditorGUILayout.Space();
            // Lists
            EditorGUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(position.width * splitterPos));
            prefabSearch = EditorGUILayout.TextField("Filter", prefabSearch);
            prefabScroll = EditorGUILayout.BeginScrollView(prefabScroll);
            foreach (var entry in config.allowedPrefabs.Where(x =>
                         x.prefab != null && x.prefab.name.IndexOf(prefabSearch, StringComparison.OrdinalIgnoreCase) >=
                         0))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(entry.prefab, typeof(GameObject), false);
                if (GUILayout.Button("Add", GUILayout.Width(50)))
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
                    go.transform.position = spawnPos;
                    Undo.RegisterCreatedObjectUndo(go, "Add");
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }

                if (GUILayout.Button("Preview", GUILayout.Width(60)))
                {
                    previewPrefab = entry.prefab;
                    previewTexture = null;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            sceneSearch = EditorGUILayout.TextField("Filter", sceneSearch);
            sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects().Where(g =>
                         g.name.IndexOf(sceneSearch, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    Undo.DestroyObjectImmediate(go);
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    EditorGUILayout.EndHorizontal();
                    continue;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

       void DrawPositionsTab()
{
    EditorGUILayout.BeginHorizontal();
    if (!pickingPosition)
    {
        if (GUILayout.Button("Pick Position in Scene"))
            pickingPosition = true;
    }
    else
    {
        if (GUILayout.Button("Cancel Pick"))
            pickingPosition = false;
    }
    EditorGUILayout.EndHorizontal();
    
    // Sección de posiciones
    EditorGUILayout.LabelField("Saved Positions", EditorStyles.boldLabel);
    EditorGUILayout.LabelField("Filter by Tag:", EditorStyles.miniBoldLabel);
    filterTag = EditorGUILayout.TextField(filterTag);
    
    serializedDB.Update();
    posList.DoLayoutList();
    serializedDB.ApplyModifiedProperties();

    GUILayout.Space(8);
    if (GUILayout.Button("Create Gizmo Drawer Component"))
    {
        var root = new GameObject("Position Gizmo Drawer");
        Undo.RegisterCreatedObjectUndo(root, "Create Gizmo Drawer");
        var drawer = root.AddComponent<PositionGizmoDrawer>();
        drawer.positionDatabase = positionDB;

        foreach (var entry in positionDB.positions)
        {
            var marker = new GameObject(entry.name);
            Undo.RegisterCreatedObjectUndo(marker, "Create Position Marker");
            marker.transform.parent = root.transform;
            marker.transform.position = entry.position;
        }
    }

    // Sección de Paths - Mejor organizada
    EditorGUILayout.Space(10);
    EditorGUILayout.LabelField("Path Editing", EditorStyles.boldLabel);
    
    // Área de scroll solo para la lista de paths
    positionsScroll = EditorGUILayout.BeginScrollView(positionsScroll, GUILayout.Height(200));
    serializedDB.Update();
    pathList.DoLayoutList();
    serializedDB.ApplyModifiedProperties();
    EditorGUILayout.EndScrollView();

    // Controles de edición de path - Fuera del área de scroll
    if (selectedPathIndex >= 0 && selectedPathIndex < positionDB.paths.Count)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        if (!editingPath)
        {
            if (GUILayout.Button("Start Editing Path", EditorStyles.miniButton))
            {
                editingPath = true;
                addingToPath = false;
            }
        }
        else
        {
            if (GUILayout.Button("Stop Editing Path", EditorStyles.miniButton))
            {
                editingPath = false;
            }
        }
        
        addingToPath = GUILayout.Toggle(addingToPath, "Add Mode", "Button");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Path", EditorStyles.miniButton))
        {
            serializedDB.Update();
            var pathProp = pathList.serializedProperty.GetArrayElementAtIndex(selectedPathIndex);
            var indicesProp = pathProp.FindPropertyRelative("pointIndices");
            indicesProp.arraySize = 0;
            serializedDB.ApplyModifiedProperties();
            EditorUtility.SetDirty(positionDB);
        }
        
        if (GUILayout.Button("Reverse Path", EditorStyles.miniButton))
        {
            positionDB.paths[selectedPathIndex].pointIndices.Reverse();
            EditorUtility.SetDirty(positionDB);
        }
        EditorGUILayout.EndHorizontal();

        // Distribución de objetos
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Distribute Objects Along Path", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        pathObjectSpacing = EditorGUILayout.FloatField("Spacing", pathObjectSpacing, GUILayout.Width(150));
        pathObjectCount = EditorGUILayout.IntField("Count", pathObjectCount, GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();

        if (previewPrefab != null && GUILayout.Button("Distribute Objects", EditorStyles.miniButton))
        {
            DistributeObjectsAlongPath();
        }
        
        EditorGUILayout.EndVertical();
    }

    // Botón para crear nuevo path - Ahora bien separado
    EditorGUILayout.Space(10);
    if (GUILayout.Button("Create New Path", GUILayout.Height(25)))
    {
        serializedDB.Update();
        pathList.serializedProperty.arraySize++;
        var newPath = new PathData();
        positionDB.paths.Add(newPath);
        serializedDB.ApplyModifiedProperties();
        EditorUtility.SetDirty(positionDB);
        selectedPathIndex = positionDB.paths.Count - 1;
    }
}

        void CreateConfig()
        {
            var asset = CreateInstance<ObjectManagerConfig>();
            AssetDatabase.CreateAsset(asset, "Assets/ObjectManagerConfig.asset");
            AssetDatabase.SaveAssets();
            config = asset;
            EditorGUIUtility.PingObject(asset);
        }

        void RestoreCameraSettings(PositionEntry entry)
        {
            var sv = SceneView.lastActiveSceneView;
            sv.pivot = entry.position;
            sv.rotation = entry.camRotation;
            sv.orthographic = entry.orthographic;
            sv.size = entry.camSize;
            sv.Repaint();
        }

        void CaptureSnapshot(PositionEntry entry)
        {
            var sv = SceneView.lastActiveSceneView;
            var cam = sv.camera;
            int w = (int)sv.position.width, h = (int)sv.position.height;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            DestroyImmediate(rt);
            var dir = Path.Combine(Application.dataPath, "Screenshots");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{entry.name}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
            AssetDatabase.Refresh();
        }
        
        private void DistributeObjectsAlongPath()
{
    if (selectedPathIndex < 0 || selectedPathIndex >= positionDB.paths.Count) return;
    if (previewPrefab == null) return;

    var path = positionDB.paths[selectedPathIndex];
    var splinePoints = GetSplinePoints(path);
    
    if (splinePoints.Count < 2) return;
    
    float totalLength = 0;
    for (int i = 1; i < splinePoints.Count; i++)
    {
        totalLength += Vector3.Distance(splinePoints[i-1], splinePoints[i]);
    }
    
    float step = pathObjectSpacing;
    if (pathObjectCount > 0)
    {
        step = totalLength / (pathObjectCount - 1);
    }
    
    float currentDist = 0;
    int segmentStart = 0;
    
    for (int i = 0; i < (pathObjectCount > 0 ? pathObjectCount : int.MaxValue); i++)
    {
        if (currentDist >= totalLength && i > 0) break;
        
        // Encontrar segmento actual
        while (segmentStart < splinePoints.Count - 1 && 
               currentDist > Vector3.Distance(splinePoints[0], splinePoints[segmentStart + 1]))
        {
            segmentStart++;
        }
        
        if (segmentStart >= splinePoints.Count - 1) break;
        
        // Calcular posición exacta
        float segStartDist = segmentStart > 0 ? 
            Vector3.Distance(splinePoints[0], splinePoints[segmentStart]) : 0;
        float segLength = Vector3.Distance(
            splinePoints[segmentStart], splinePoints[segmentStart + 1]);
        
        float t = (currentDist - segStartDist) / segLength;
        Vector3 pos = Vector3.Lerp(
            splinePoints[segmentStart], splinePoints[segmentStart + 1], t);
        
        // Instanciar objeto
        var go = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
        go.transform.position = pos;
        
        // Orientar objeto en dirección del path
        if (segmentStart < splinePoints.Count - 1)
        {
            Vector3 direction = (splinePoints[segmentStart + 1] - splinePoints[segmentStart]).normalized;
            if (direction != Vector3.zero)
            {
                go.transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        
        Undo.RegisterCreatedObjectUndo(go, "Distribute Along Path");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        
        currentDist += step;
    }
}

        [System.Serializable]
        public class PositionEntry
        {
            public string name;
            public string tag = "";
            public Vector3 position;
            public bool isExpanded;
            public Color gizmoColor = Color.yellow;
            public Color labelColor = Color.white;
            public Vector3 camPosition;
            public Quaternion camRotation;
            public bool orthographic;
            public float camSize;
            public float fieldOfView;

            public PositionEntry(string name, Vector3 pos)
            {
                this.name = name;
                position = pos;
            }

            public PositionEntry(PositionEntry other)
            {
                name = other.name;
                tag = other.tag;
                position = other.position;
                gizmoColor = other.gizmoColor;
                labelColor = other.labelColor;
                camPosition = other.camPosition;
                camRotation = other.camRotation;
                orthographic = other.orthographic;
                camSize = other.camSize;
                fieldOfView = other.fieldOfView;
            }

            public void InitializeCamera(SceneView sv)
            {
                camPosition = sv.camera.transform.position;
                camRotation = sv.camera.transform.rotation;
                orthographic = sv.orthographic;
                camSize = sv.size;
                fieldOfView = sv.camera.fieldOfView;
            }
            
            
        }
    }

    [ExecuteInEditMode]
    public class PositionGizmoDrawer : MonoBehaviour
    {
        public ScenePositionDatabase positionDatabase;

        void OnDrawGizmos()
        {
            if (positionDatabase == null) return;
            foreach (var e in positionDatabase.positions)
            {
                Gizmos.color = e.gizmoColor;
                Gizmos.DrawWireSphere(e.position, 0.3f);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (positionDatabase == null) return;
            foreach (var e in positionDatabase.positions)
            {
                Handles.color = e.labelColor;
                Handles.Label(e.position + Vector3.up * 0.5f, e.name);
            }
        }
#endif
    }
}