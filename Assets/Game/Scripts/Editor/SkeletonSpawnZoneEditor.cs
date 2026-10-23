using UnityEditor;
using UnityEngine;

// Зона появления скелетов в редакторе: маркер на краю круга тянет радиус; создание из меню GameObject.
[CustomEditor(typeof(SkeletonSpawnZone))]
public class SkeletonSpawnZoneEditor : Editor
{
    void OnSceneGUI()
    {
        var zone = (SkeletonSpawnZone)target;
        Vector3 center = zone.transform.position;
        Vector3 edge = center + Vector3.right * zone.radius;
        Handles.color = new Color(0.95f, 0.45f, 0.35f);
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.Slider(edge, Vector3.right, HandleUtility.GetHandleSize(edge) * 0.12f,
                                       Handles.SphereHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(zone, "Радиус зоны скелетов");
            zone.radius = Mathf.Max(0.5f, moved.x - center.x);
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var zone = (SkeletonSpawnZone)target;
        EditorGUILayout.HelpBox($"Всего появится: {zone.creeps + zone.shields + zone.archers}. " +
                                "Круг виден только в редакторе; радиус -- маркер на краю круга.", MessageType.None);
        if (Application.isPlaying && !zone.HasSpawned && GUILayout.Button("Появиться сейчас")) zone.Spawn();
    }

    [MenuItem("GameObject/BeerAndDragon/Skeleton Spawn Zone", false, 10)]
    public static void Create(MenuCommand command)
    {
        var go = new GameObject("SkeletonSpawnZone");
        go.AddComponent<SkeletonSpawnZone>();          // Reset() подставит префабы скелетов
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;
        Undo.RegisterCreatedObjectUndo(go, "Зона скелетов");
        Selection.activeObject = go;
    }
}
