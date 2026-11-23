using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Seasons
{
    // Живой и мёртвый мир сезона. После победы мир мгновенно становится серым грейбоксом:
    // все поверхности одного серого цвета, свет обесцвечен, глаза и живые эффекты выключены.
    // Переключение делается под белой заливкой (GDD: мгновенно, без распространения).
    public sealed class DeadWorld : MonoBehaviour
    {
        [Tooltip("Корни, внутри которых всё становится серым (локация, столб, тигр)")]
        public Transform[] worldRoots;
        [Tooltip("Что не трогать (героиня, служебные объекты)")]
        public Transform[] exclude;
        [Tooltip("Материал-основа для серого. Если пусто, берётся материал первого найденного рендерера")]
        public Material greySource;
        [Tooltip("Серые тона мёртвого мира: светлый, средний, тёмный (раздаются по высоте объекта)")]
        public Color[] greyTones = { new Color(0.62f, 0.62f, 0.63f), new Color(0.47f, 0.48f, 0.5f), new Color(0.36f, 0.37f, 0.39f) };
        [Tooltip("Объекты, которые исчезают в мёртвом мире (живые эффекты, глаза)")]
        public GameObject[] aliveOnly;

        public bool IsDead { get; private set; }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

        public void ApplyDead()
        {
            if (IsDead) return;
            IsDead = true;

            var renderers = new List<Renderer>();
            foreach (var root in worldRoots)
                if (root != null) renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            Material source = greySource;
            if (source == null)
                foreach (var r in renderers)
                    if (r.sharedMaterial != null) { source = r.sharedMaterial; break; }
            if (source == null) return;

            var greys = new Material[greyTones.Length];
            for (int i = 0; i < greyTones.Length; i++)
            {
                var m = new Material(source) { name = "Dead grey " + i };
                if (m.HasProperty(BaseColorMap)) m.SetTexture(BaseColorMap, null);
                if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, greyTones[i]);
                if (m.HasProperty("_Color")) m.SetColor("_Color", greyTones[i]);
                if (m.HasProperty(EmissiveColor)) m.SetColor(EmissiveColor, Color.black);
                greys[i] = m;
            }

            foreach (var r in renderers)
            {
                if (IsExcluded(r.transform) || r is ParticleSystemRenderer) continue;
                // Высокие объекты светлее, низкие темнее: так серый мир сохраняет читаемые объёмы.
                float h = r.bounds.size.y;
                var tone = greys[h > 6f ? 0 : h > 1.2f ? 1 : 2];
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = tone;
                r.sharedMaterials = mats;
            }

            foreach (var go in aliveOnly) if (go != null) go.SetActive(false);

            // Свет без цвета, небо серее: без отдельного профиля атмосферы для грейбокса этого достаточно.
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                float g = light.color.grayscale;
                light.color = new Color(g, g, g);
            }
            RenderSettings.fog = false;
        }

        bool IsExcluded(Transform t)
        {
            foreach (var e in exclude)
                if (e != null && t.IsChildOf(e)) return true;
            return false;
        }
    }
}
