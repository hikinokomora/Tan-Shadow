using System;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

[CustomEditor(typeof(QinglongFoliage))]
internal sealed class QinglongFoliageEditor : Editor
{
    private const string StrokeUndoName = "Paint Qinglong foliage";
    private int speciesIndex;
    private float radius = 2.5f;
    private int plantsPerStamp = 5;
    private Vector2 scaleRange = new(0.8f, 1.2f);
    private bool painting;
    private int strokeGroup = -1;
    private Vector3 lastStamp;

    public override void OnInspectorGUI()
    {
        var foliage = (QinglongFoliage)target;
        var palette = foliage.Palette;
        bool ready = foliage.PaintSurface && foliage.Instances && palette is { Length: > 0 };
        using (new EditorGUI.DisabledScope(!ready || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button(painting ? "Stop Painting" : "Paint Foliage"))
            {
                painting = !painting;
                SceneView.RepaintAll();
            }

            if (ready)
            {
                string[] names = Array.ConvertAll(palette, species => species.name);
                speciesIndex = EditorGUILayout.Popup("Species", Mathf.Clamp(speciesIndex, 0, names.Length - 1), names);
            }

            radius = EditorGUILayout.Slider("Brush Radius", radius, 0.25f, 20f);
            plantsPerStamp = EditorGUILayout.IntSlider("Plants per Stamp", plantsPerStamp, 1, 30);
            EditorGUILayout.MinMaxSlider("Scale Variation", ref scaleRange.x, ref scaleRange.y, 0.2f, 3f);
            EditorGUILayout.HelpBox("Drag with the left mouse button to paint. Hold Shift to erase painted foliage. Each stroke supports Undo.", MessageType.Info);
        }

        if (!ready)
            EditorGUILayout.HelpBox("Assign a mesh collider, an instances group and at least one plant species.", MessageType.Warning);
        DrawDefaultInspector();
    }

    private void OnEnable() => SceneView.duringSceneGui += DrawBrush;

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawBrush;
        FinishStroke();
    }

    private void DrawBrush(SceneView view)
    {
        var foliage = (QinglongFoliage)target;
        if (!painting || !foliage || Selection.activeGameObject != foliage.gameObject ||
            EditorApplication.isPlayingOrWillChangePlaymode || !foliage.PaintSurface || !foliage.Instances ||
            foliage.Palette is not { Length: > 0 })
            return;

        Event current = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (current.type == EventType.MouseUp && current.button == 0 && GUIUtility.hotControl == id)
        {
            GUIUtility.hotControl = 0;
            FinishStroke();
            current.Use();
            return;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            painting = false;
            if (GUIUtility.hotControl == id)
                GUIUtility.hotControl = 0;
            FinishStroke();
            current.Use();
            Repaint();
            SceneView.RepaintAll();
            return;
        }

        if (current.type is EventType.MouseMove or EventType.MouseDrag or EventType.KeyUp or EventType.KeyDown)
            view.Repaint();

        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        if (!foliage.PaintSurface.Raycast(ray, out RaycastHit hit, 3000f))
            return;

        if (current.type == EventType.Layout && !current.alt)
            HandleUtility.AddDefaultControl(id);

        if (current.type == EventType.Repaint)
        {
            Color outline = current.shift ? new Color(1f, 0.31f, 0.2f) : new Color(0.60f, 1f, 0.39f);
            Vector3 center = hit.point + hit.normal * 0.04f;
            using (new Handles.DrawingScope(new Color(0.05f, 0.05f, 0.05f, 0.9f)))
                Handles.DrawWireDisc(center, hit.normal, radius, 5f);
            using (new Handles.DrawingScope(outline))
                Handles.DrawWireDisc(center, hit.normal, radius, 2.5f);
        }

        if (current.alt || current.button != 0)
            return;

        if (current.type == EventType.MouseDown && HandleUtility.nearestControl == id)
        {
            GUIUtility.hotControl = id;
            Undo.IncrementCurrentGroup();
            strokeGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(StrokeUndoName);
            lastStamp = hit.point;
            ApplyStamp(foliage, lastStamp, current.shift);
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            Vector3 segment = hit.point - lastStamp;
            float spacing = Mathf.Max(0.15f, radius * 0.55f);
            float distance = segment.magnitude;
            Vector3 direction = segment.normalized;
            // Limit work per event if the cursor jumps between distant parts of the canyon.
            if (distance > radius * 6f)
            {
                lastStamp = hit.point;
                ApplyStamp(foliage, lastStamp, current.shift);
            }
            else
            {
                int count = Mathf.FloorToInt(distance / spacing);
                for (int i = 0; i < count; i++)
                {
                    lastStamp += direction * spacing;
                    ApplyStamp(foliage, lastStamp, current.shift);
                }
            }
            current.Use();
        }
    }

    private void ApplyStamp(QinglongFoliage foliage, Vector3 point, bool erase)
    {
        if (erase)
        {
            for (int i = foliage.Instances.childCount - 1; i >= 0; i--)
            {
                Transform plant = foliage.Instances.GetChild(i);
                if ((plant.position - point).sqrMagnitude <= radius * radius)
                    Undo.DestroyObjectImmediate(plant.gameObject);
            }
            return;
        }

        var variants = foliage.Palette[Mathf.Clamp(speciesIndex, 0, foliage.Palette.Length - 1)].variants;
        if (variants is not { Length: > 0 })
            return;

        for (int i = 0; i < plantsPerStamp; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 origin = point + new Vector3(offset.x, 4f, offset.y);
            if (!foliage.PaintSurface.Raycast(new Ray(origin, Vector3.down), out RaycastHit ground, 8f))
                continue;

            var prefab = variants[Random.Range(0, variants.Length)];
            if (!prefab)
                continue;

            var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, foliage.Instances);
            plant.transform.SetPositionAndRotation(ground.point, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            plant.transform.localScale *= Random.Range(scaleRange.x, scaleRange.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(plant.transform);
            Undo.RegisterCreatedObjectUndo(plant, StrokeUndoName);
        }
    }

    private void FinishStroke()
    {
        if (strokeGroup < 0)
            return;
        Undo.CollapseUndoOperations(strokeGroup);
        Undo.IncrementCurrentGroup();
        strokeGroup = -1;
    }
}
