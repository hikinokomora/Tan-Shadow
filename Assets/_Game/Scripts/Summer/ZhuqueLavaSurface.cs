using UnityEngine;

namespace TanShadow.Summer
{
    // Вид текущей лавы (ARCHITECTURE, лава Чжуцюэ: «SG_LavaFlow» — в прототипе материалы HDRP Lit/Unlit):
    // корка из остывших плит еле ползёт по потоку, под ней ещё медленнее течёт раскалённая магма (видна
    // в трещинах), свечение медленно «дышит». UV поверхностей — в метрах (v — вдоль потока от валуна-
    // источника), поэтому скорости здесь — в метрах в секунду. Меши и материалы строит SummerLavaLook.
    public sealed class ZhuqueLavaSurface : MonoBehaviour
    {
        [Tooltip("[Tbd] Скорость корки вдоль потока, м/с. Лава вязкая — еле движется")]
        [Min(0)] public float crustSpeed = 0.12f;
        [Tooltip("[Tbd] Скорость магмы под коркой, м/с")]
        [Min(0)] public float moltenSpeed = 0.05f;
        [Tooltip("[Tbd] Размер плит корки: метров на повтор текстуры")]
        [Min(0.5f)] public float crustTile = 14f;
        [Tooltip("[Tbd] Метров на повтор текстуры магмы")]
        [Min(0.5f)] public float moltenTile = 13f;
        [Tooltip("[Tbd] Метров вдоль края на повтор текстуры каймы")]
        [Min(0.5f)] public float rimTile = 10f;
        [Tooltip("[Tbd] Насколько «дышит» свечение, доля")]
        [Range(0, 1)] public float pulse = 0.12f;
        [Tooltip("[Tbd] Период «дыхания» свечения, с")]
        [Min(0.1f)] public float pulsePeriod = 6f;

        [Header("Заполняет SummerLavaLook")]
        public Renderer[] crust;
        public Renderer[] molten;
        public Renderer[] rims;
        [ColorUsage(false, true)] public Color crustEmission;
        [ColorUsage(false, true)] public Color moltenEmission;
        [ColorUsage(false, true)] public Color rimEmission;

        static readonly int BaseST = Shader.PropertyToID("_BaseColorMap_ST");
        static readonly int UnlitST = Shader.PropertyToID("_UnlitColorMap_ST");
        static readonly int EmissiveST = Shader.PropertyToID("_EmissiveColorMap_ST");
        static readonly int Emissive = Shader.PropertyToID("_EmissiveColor");
        MaterialPropertyBlock block;

        void LateUpdate()
        {
            block ??= new MaterialPropertyBlock();
            float t = Time.time;
            float breath = 1f + pulse * Mathf.Sin(t * 2f * Mathf.PI / pulsePeriod);
            Apply(crust, crustTile, crustSpeed, t, breath, crustEmission);
            Apply(molten, moltenTile, moltenSpeed, t, breath * (1f + pulse * 0.5f * Mathf.Sin(t * 1.7f)), moltenEmission);
            Apply(rims, rimTile, 0f, t, breath, rimEmission, true);
        }

        // acrossFixed — у каймы v уже 0…1 поперёк полосы: масштабируем только u (вдоль края).
        void Apply(Renderer[] list, float tile, float speed, float t, float breath, Color emission, bool acrossFixed = false)
        {
            if (list == null) return;
            float scale = 1f / tile;
            for (int i = 0; i < list.Length; i++)
            {
                var r = list[i];
                if (r == null) continue;
                // Каждый поток чуть сдвинут и не в такт с соседями.
                var st = acrossFixed
                    ? new Vector4(scale, 1f, i * 0.37f, 0f)
                    : new Vector4(scale, scale, i * 0.37f, Mathf.Repeat(-t * speed * scale + i * 0.21f, 1f));
                r.GetPropertyBlock(block);
                block.SetVector(BaseST, st);
                block.SetVector(UnlitST, st);
                block.SetVector(EmissiveST, st);
                block.SetColor(Emissive, emission * (breath * (1f + 0.06f * Mathf.Sin(t * 0.9f + i * 1.9f))));
                r.SetPropertyBlock(block);
            }
        }
    }
}
