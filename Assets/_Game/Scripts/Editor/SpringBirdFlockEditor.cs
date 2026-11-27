using TanShadow.Spring;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpringBirdFlock))]
public sealed class SpringBirdFlockEditor : Editor
{
    int previewRoute;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var flock = (SpringBirdFlock)target;
        var routes = serializedObject.FindProperty("routes");
        if (routes.arraySize > 0)
        {
            var labels = new string[routes.arraySize];
            for (int i = 0; i < labels.Length; i++)
            {
                var route = routes.GetArrayElementAtIndex(i).objectReferenceValue;
                labels[i] = route == null ? $"Участок {i + 1}" : route.name;
            }

            previewRoute = EditorGUILayout.Popup("Участок предпросмотра",
                Mathf.Clamp(previewRoute, 0, labels.Length - 1), labels);
        }
        using (new EditorGUI.DisabledScope(!flock.isActiveAndEnabled))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Показать пролёт")) flock.PreviewFlight(previewRoute);
            if (GUILayout.Button("Остановить")) flock.StopPreview();
        }
    }
}
