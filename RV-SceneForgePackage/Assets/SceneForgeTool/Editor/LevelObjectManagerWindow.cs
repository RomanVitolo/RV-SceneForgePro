using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

namespace RV.SceneForgeTool
{
public class LevelObjectManagerWindow : EditorWindow
{
    ObjectManagerConfig   config;
    ScenePositionDatabase positionDB;

    int    tabIndex               = 0;
    readonly string[] tabs        = new[] { "Objects", "Positions" };
    float  splitterPos            = 0.5f;
    Vector2 prefabScroll, sceneScroll, positionsScroll;
    string prefabSearch = "", sceneSearch = "";

    int     selectedPositionOption = 0;
    Vector3 customSpawnPosition    = Vector3.zero;

    GameObject previewPrefab;
    Texture2D  previewTexture;

    GameObject pendingDragPrefab;

    bool   pickingPosition = false;
    string newPositionName = "NewPosition";

    [MenuItem("Window/Level Object Manager")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<LevelObjectManagerWindow>();
        wnd.titleContent = new GUIContent("Object Manager");
        wnd.minSize = new Vector2(550, 500);
    }

    void OnEnable()
    {
        var cfg = AssetDatabase.FindAssets("t:ObjectManagerConfig").FirstOrDefault();
        if (cfg != null)
            config = AssetDatabase.LoadAssetAtPath<ObjectManagerConfig>(
                AssetDatabase.GUIDToAssetPath(cfg));

        var db = AssetDatabase.FindAssets("t:ScenePositionDatabase").FirstOrDefault();
        if (db != null)
        {
            positionDB = AssetDatabase.LoadAssetAtPath<ScenePositionDatabase>(
                AssetDatabase.GUIDToAssetPath(db));
        }
        else
        {
            positionDB = CreateInstance<ScenePositionDatabase>();
            AssetDatabase.CreateAsset(positionDB, "Assets/ScenePositionDatabase.asset");
            AssetDatabase.SaveAssets();
        }

        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnSceneGUI(SceneView sv)
    {
        var e = Event.current;

        if (pickingPosition && e.type == EventType.MouseDown && e.button == 0)
        {
            var ray   = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float dist))
            {
                var pos = ray.GetPoint(dist);
                positionDB.positions.Add(new PositionEntry { name = newPositionName, position = pos });
                EditorUtility.SetDirty(positionDB);
                AssetDatabase.SaveAssets();
                pickingPosition = false;
                Repaint();
            }
            e.Use();
            return;
        }

        int dragOptionIndex = (positionDB?.positions.Count ?? 0) + 1;
        if (selectedPositionOption == dragOptionIndex
            && (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            && DragAndDrop.objectReferences.Length > 0
            && DragAndDrop.objectReferences[0] is GameObject)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences.OfType<GameObject>())
                {
                    var ray   = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    var plane = new Plane(Vector3.up, Vector3.zero);
                    if (plane.Raycast(ray, out float d))
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(obj);
                        go.transform.position = ray.GetPoint(d);
                        Undo.RegisterCreatedObjectUndo(go, "Drag Instantiate");
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                }
            }
            e.Use();
        }
    }

    void OnGUI()
    {
        tabIndex = GUILayout.Toolbar(tabIndex, tabs);
        EditorGUILayout.Space();

        if (tabIndex == 0) DrawObjectsTab();
        else              DrawPositionsTab();
    }

    void DrawObjectsTab()
    {
        if (config == null)
        {
            EditorGUILayout.HelpBox(
                "No ObjectManagerConfig found.\nCreate one via Assets → Create → LevelTools → Object Manager Config.",
                MessageType.Warning);
            if (GUILayout.Button("Create Config")) CreateConfig();
            return;
        }

        bool is2D = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D;
        EditorGUILayout.LabelField("Editor Mode:", is2D ? "2D" : "3D");
        EditorGUILayout.Space();

        int posCount = positionDB.positions.Count;
        string[] options = new string[posCount + 2];
        options[0] = "Custom";
        for (int i = 0; i < posCount; i++)
            options[i + 1] = positionDB.positions[i].name;
        options[posCount + 1] = "Drag & Drop";

        selectedPositionOption = EditorGUILayout.Popup("Spawn At", selectedPositionOption, options);
        selectedPositionOption = Mathf.Clamp(selectedPositionOption, 0, options.Length - 1);

        Vector3 spawnPos = Vector3.zero;
        if (selectedPositionOption == 0)
        {
            customSpawnPosition = EditorGUILayout.Vector3Field("Position", customSpawnPosition);
            if (is2D) customSpawnPosition.z = 0f;
            spawnPos = customSpawnPosition;
        }
        else if (selectedPositionOption <= posCount)
        {
            spawnPos = positionDB.positions[selectedPositionOption - 1].position;
            EditorGUILayout.LabelField("Position:", spawnPos.ToString("F2"));
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Click a prefab’s **Drag** button, then drop into Scene View to place it.",
                MessageType.Info);
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Prefab Preview", EditorStyles.boldLabel);
        Rect prevRect = GUILayoutUtility.GetRect(120, 120);
        if (previewPrefab != null)
        {
            if (previewTexture == null)
                previewTexture = AssetPreview.GetAssetPreview(previewPrefab)
                               ?? AssetPreview.GetMiniThumbnail(previewPrefab);

            if (previewTexture != null)
                GUI.DrawTexture(prevRect, previewTexture, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(prevRect, Color.gray);

            EditorGUILayout.LabelField(previewPrefab.name, EditorStyles.centeredGreyMiniLabel);
            if (GUILayout.Button("Clear Preview"))
            {
                previewPrefab = null;
                previewTexture = null;
            }
        }
        else
        {
            EditorGUI.DrawRect(prevRect, Color.black * 0.2f);
            EditorGUILayout.LabelField("No prefab selected", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(position.width * splitterPos));
            EditorGUILayout.LabelField("Available Prefabs", EditorStyles.boldLabel);
            prefabSearch = EditorGUILayout.TextField("Filter", prefabSearch);
            prefabScroll = EditorGUILayout.BeginScrollView(prefabScroll);

            foreach (var entry in config.allowedPrefabs
                         .Where(x => x.prefab != null
                                   && x.prefab.name.IndexOf(prefabSearch, System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(entry.prefab, typeof(GameObject), false);

                    var dragButtonRect = GUILayoutUtility.GetRect(new GUIContent("Drag"), GUI.skin.button, GUILayout.Width(50));
                    if (GUI.Button(dragButtonRect, "Drag"))
                    {
                        pendingDragPrefab = entry.prefab;
                    }
                   
                    var evt = Event.current;
                    if (pendingDragPrefab != null &&
                        (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag))
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new Object[] { pendingDragPrefab };
                        DragAndDrop.StartDrag("Dragging Prefab");
                        pendingDragPrefab = null;
                        evt.Use();
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
            EditorGUILayout.LabelField("Scene Objects", EditorStyles.boldLabel);
            sceneSearch = EditorGUILayout.TextField("Filter", sceneSearch);

            sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);
            foreach (var go in SceneManager.GetActiveScene()
                         .GetRootGameObjects()
                         .Where(g => g.name.IndexOf(sceneSearch, System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        Undo.DestroyObjectImmediate(go);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        break;
                    }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    void DrawPositionsTab()
    {
        EditorGUILayout.LabelField("Positions Picker", EditorStyles.boldLabel);
        newPositionName = EditorGUILayout.TextField("Reference Name", newPositionName);

        if (!pickingPosition)
        {
            if (GUILayout.Button("Pick Position in Scene")) pickingPosition = true;
        }
        else
        {
            EditorGUILayout.HelpBox("Click in Scene View to record.", MessageType.Info);
            if (GUILayout.Button("Cancel")) pickingPosition = false;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Saved Positions:", EditorStyles.boldLabel);

        positionsScroll = EditorGUILayout.BeginScrollView(positionsScroll);
        for (int i = 0; i < positionDB.positions.Count; i++)
        {
            var entry = positionDB.positions[i];
            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{entry.name}: {entry.position:F2}");
                if (GUILayout.Button("Go", GUILayout.Width(40)))
                {
                    var sv = SceneView.lastActiveSceneView;
                    sv.pivot = entry.position;
                    sv.Repaint();
                }
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    positionDB.positions.RemoveAt(i);
                    EditorUtility.SetDirty(positionDB);
                    AssetDatabase.SaveAssets();
                    break;
                }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    void CreateConfig()
    {
        var asset = ScriptableObject.CreateInstance<ObjectManagerConfig>();
        AssetDatabase.CreateAsset(asset, "Assets/ObjectManagerConfig.asset");
        AssetDatabase.SaveAssets();
        config = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
}
