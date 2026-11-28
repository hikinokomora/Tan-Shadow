using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(QinglongGroundRelief))]
internal sealed class QinglongGroundReliefEditor : Editor
{
    private const string UndoName = "Adjust Qinglong ground relief";
    private float pendingStrength;
    private float pendingSize;

    [InitializeOnLoadMethod]
    private static void RegisterCallbacks()
    {
        Undo.undoRedoEvent += RefreshColliderAfterUndo;
        EditorSceneManager.sceneSaved += SaveGroundMesh;
    }

    private void OnEnable()
    {
        ReadAppliedValues();
        Undo.undoRedoPerformed += ReadAppliedValues;
    }

    private void OnDisable() => Undo.undoRedoPerformed -= ReadAppliedValues;

    public override void OnInspectorGUI()
    {
        var relief = (QinglongGroundRelief)target;
        var mesh = relief.GetComponent<MeshFilter>().sharedMesh;
        if (!mesh || !relief.HasBaseHeights(mesh.vertexCount))
        {
            EditorGUILayout.HelpBox("The source heights do not match the ground mesh.", MessageType.Warning);
            return;
        }

        pendingStrength = EditorGUILayout.FloatField(new GUIContent("Strength (m)", "Maximum height added by the noise. Zero removes the hills."), pendingStrength);
        pendingSize = EditorGUILayout.FloatField(new GUIContent("Hill Size", "Scale of the noise pattern. Larger values make broader hills."), pendingSize);
        bool valid = float.IsFinite(pendingStrength) && pendingStrength >= 0f && float.IsFinite(pendingSize) && pendingSize > 0f;
        bool changed = !Mathf.Approximately(pendingStrength, relief.Strength) || !Mathf.Approximately(pendingSize, relief.HillSize);
        using (new EditorGUI.DisabledScope(!valid || !changed || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Apply Relief"))
                ApplyRelief(relief, pendingStrength, pendingSize);
        }
    }

    private void ReadAppliedValues()
    {
        if (target is not QinglongGroundRelief relief || !relief)
            return;
        pendingStrength = relief.Strength;
        pendingSize = relief.HillSize;
        Repaint();
    }

    private static void ApplyRelief(QinglongGroundRelief relief, float strength, float size)
    {
        var mesh = relief.GetComponent<MeshFilter>().sharedMesh;
        var collider = relief.GetComponent<MeshCollider>();
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].y = relief.GetHeight(i, relief.transform.TransformPoint(vertices[i]), strength, size);

        Physics.SyncTransforms();
        var planted = new List<(Transform plant, float height)>();
        foreach (var brush in relief.transform.parent.GetComponentsInChildren<QinglongFoliage>(true))
        {
            if (brush.PaintSurface != collider || !brush.Instances)
                continue;
            foreach (Transform plant in brush.Instances)
            {
                if (TrySample(collider, plant.position, out float height))
                    planted.Add((plant, height));
            }
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        Undo.RegisterCompleteObjectUndo(mesh, UndoName);
        Undo.RecordObject(relief, UndoName);

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        RefreshCollider(relief);

        var modified = new List<(Transform plant, float delta)>();
        foreach (var sample in planted)
        {
            if (TrySample(collider, sample.plant.position, out float height) && Mathf.Abs(height - sample.height) > 0.00001f)
                modified.Add((sample.plant, height - sample.height));
        }
        if (modified.Count > 0)
        {
            Undo.RecordObjects(modified.ConvertAll<Object>(item => item.plant).ToArray(), UndoName);
            foreach (var item in modified)
            {
                item.plant.position += Vector3.up * item.delta;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.plant);
            }
        }

        var data = new SerializedObject(relief);
        data.FindProperty("strength").floatValue = strength;
        data.FindProperty("hillSize").floatValue = size;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mesh);
        EditorSceneManager.MarkSceneDirty(relief.gameObject.scene);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
    }

    private static bool TrySample(MeshCollider collider, Vector3 position, out float height)
    {
        Bounds bounds = collider.bounds;
        var origin = new Vector3(position.x, bounds.max.y + 1f, position.z);
        bool found = collider.Raycast(new Ray(origin, Vector3.down), out var hit, bounds.size.y + 2f);
        height = hit.point.y;
        return found;
    }

    private static void RefreshCollider(QinglongGroundRelief relief)
    {
        var collider = relief.GetComponent<MeshCollider>();
        collider.sharedMesh = null;
        collider.sharedMesh = relief.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
    }

    private static void RefreshColliderAfterUndo(in UndoRedoInfo info)
    {
        if (info.undoName != UndoName)
            return;
        foreach (var relief in FindObjectsByType<QinglongGroundRelief>(FindObjectsSortMode.None))
            RefreshCollider(relief);
    }

    private static void SaveGroundMesh(Scene scene)
    {
        foreach (var relief in FindObjectsByType<QinglongGroundRelief>(FindObjectsSortMode.None))
        {
            if (relief.gameObject.scene == scene && relief.GetComponent<MeshFilter>().sharedMesh is Mesh mesh)
                AssetDatabase.SaveAssetIfDirty(mesh);
        }
    }
}
