using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Одинаковая гладкость (smoothness) у всех освещаемых материалов и слоёв террейна во всех сценах Build Settings,
// чтобы блики под мягким белым светом (SoftWhiteLighting) везде были одинаковыми.
//   HDRP/Lit и шейдеры с теми же полями: _Smoothness = Value; если гладкость идёт из маски — диапазон
//   _SmoothnessRemapMin..Max сжимается в Value (маска больше не даёт разброса); вклад детальной карты — 0.
//   Autodesk Interactive: шероховатость _Glossiness = 1 − Value, карта шероховатости выключена.
//   SG_Colossus_Master: _Smoothness = Value, влияние карты шероховатости — 0.
//   Слои террейна: гладкость = Value, в т. ч. из маски (альфа-канал маски сжимается в Value).
// Неосвещаемые материалы (Unlit, VFX, частицы, декали) не трогаются. Металличность не меняется.
public static class UnifySmoothness
{
    public const float Value = 0.3f;   // [Tbd] единая гладкость; 0.3 — медиана по сценам на 28.11.2026

    [MenuItem("Tools/Tan Shadow/Lighting/Unify smoothness (all scenes)")]
    public static void Apply()
    {
        var materials = new HashSet<string>();
        var layers = new HashSet<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled) continue;
            foreach (var dep in AssetDatabase.GetDependencies(scene.path, true))
            {
                var type = AssetDatabase.GetMainAssetTypeAtPath(dep);
                if (type == typeof(Material)) materials.Add(dep);
                else if (type == typeof(TerrainLayer)) layers.Add(dep);
            }
        }

        int changed = 0, skipped = 0;
        foreach (var path in materials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || !Unify(m)) { skipped++; continue; }
            EditorUtility.SetDirty(m);
            changed++;
        }
        foreach (var path in layers)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) continue;
            Unify(layer);
            EditorUtility.SetDirty(layer);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Smoothness: {Value} — материалов изменено {changed}, пропущено (неосвещаемые) {skipped}, слоёв террейна {layers.Count}");
    }

    static bool Unify(Material m)
    {
        bool touched = false;
        if (m.HasProperty("_Glossiness") && m.HasProperty("_UseRoughnessMap"))
        {
            // Autodesk Interactive: поле — шероховатость
            m.SetFloat("_Glossiness", 1f - Value);
            m.SetFloat("_UseRoughnessMap", 0f);
            return true;
        }
        if (m.HasProperty("_Smoothness")) { m.SetFloat("_Smoothness", Value); touched = true; }
        if (m.HasProperty("_SmoothnessRemapMin") && m.HasProperty("_SmoothnessRemapMax"))
        {
            m.SetFloat("_SmoothnessRemapMin", Value);
            m.SetFloat("_SmoothnessRemapMax", Value);
            touched = true;
        }
        if (m.HasProperty("_DetailSmoothnessScale")) m.SetFloat("_DetailSmoothnessScale", 0f);
        if (m.HasProperty("_RoughnessInfluence")) m.SetFloat("_RoughnessInfluence", 0f);
        return touched;
    }

    static void Unify(TerrainLayer layer)
    {
        layer.smoothness = Value;
        var min = layer.maskMapRemapMin; min.w = Value; layer.maskMapRemapMin = min;
        var max = layer.maskMapRemapMax; max.w = Value; layer.maskMapRemapMax = max;
        // В Unity 6.6 у слоя есть источник гладкости (константа / альфа альбедо) — ставим константу, если поле есть.
        var source = typeof(TerrainLayer).GetProperty("smoothnessSource", BindingFlags.Public | BindingFlags.Instance);
        if (source != null && source.PropertyType.IsEnum)
            foreach (var name in System.Enum.GetNames(source.PropertyType))
                if (name.StartsWith("Constant")) { source.SetValue(layer, System.Enum.Parse(source.PropertyType, name)); break; }
    }
}
