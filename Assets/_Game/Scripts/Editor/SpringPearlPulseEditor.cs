using TanShadow.Spring;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpringPearlPulse))]
public sealed class SpringPearlPulseEditor : Editor
{
    static readonly string[] StageLabels =
    {
        "0 · Целая", "1 · Первые трещины", "2 · Разветвлённые трещины",
        "3 · Перед разрушением", "4 · Разрушение"
    };

    bool showSettings;

    public override void OnInspectorGUI()
    {
        var pearl = (SpringPearlPulse)target;
        EditorGUILayout.LabelField("Предпросмотр VFX", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!pearl.isActiveAndEnabled))
        {
            int current = (int)pearl.PreviewStage;
            int selected = EditorGUILayout.Popup("Стадия", current, StageLabels);
            if (selected != current)
            {
                if (selected == 4) Shatter(pearl);
                else pearl.SetStage((SpringPearlPulse.Stage)selected);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(pearl.PreviewStage == SpringPearlPulse.Stage.Shattered))
                {
                    if (GUILayout.Button("Засчитанный удар"))
                    {
                        if (pearl.PreviewStage == SpringPearlPulse.Stage.Fractured) Shatter(pearl);
                        else
                        {
                            pearl.SetStage(pearl.PreviewStage + 1);
                            Hit(pearl);
                        }
                    }
                    if (GUILayout.Button("Повторный удар")) Hit(pearl);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Проиграть разрушение")) Shatter(pearl);
                if (GUILayout.Button("Сбросить просмотр")) pearl.SetStage(SpringPearlPulse.Stage.Intact);
            }

            if (!Application.isPlaying)
                pearl.EditorPreviewSpeed = EditorGUILayout.ToggleLeft(
                    "Замедлить предпросмотр в 4 раза", pearl.EditorPreviewSpeed < 1f) ? .25f : 1f;
        }

        EditorGUILayout.HelpBox("Предпросмотр работает в Scene View и не меняет стартовую стадию. Повторный удар проигрывает вспышку без новых трещин.", MessageType.Info);
        showSettings = EditorGUILayout.Foldout(showSettings, "Настройки материала и эффектов", true);
        if (showSettings) DrawDefaultInspector();
    }

    static void Hit(SpringPearlPulse pearl)
    {
        Vector3 point = GetContact(pearl, out Vector3 normal);
        pearl.PlayHit(point, normal);
    }

    static void Shatter(SpringPearlPulse pearl)
    {
        Vector3 point = GetContact(pearl, out Vector3 normal);
        pearl.PlayShatter(point, normal);
    }

    static Vector3 GetContact(SpringPearlPulse pearl, out Vector3 normal)
    {
        Bounds bounds = pearl.GetComponent<MeshRenderer>().bounds;
        Camera view = Application.isPlaying ? Camera.main : SceneView.lastActiveSceneView?.camera;
        normal = view == null ? Vector3.forward : (view.transform.position - bounds.center).normalized;
        normal = (normal + Vector3.up * .18f + (view == null ? Vector3.right : view.transform.right) * .25f).normalized;
        return bounds.center + normal * bounds.extents.x * 1.015f;
    }
}
