using System.Collections;
using TanShadow.Seasons;
using UnityEngine;

namespace TanShadow.Cosmos
{
    // Звёзды четырёх секторов неба по сторонам света: восток — весна, юг — лето, запад — осень, север — зима
    // (как четыре созвездия-символа китайского неба). Сектора уже убитых боссов тёмные с самого начала,
    // сектор только что убитого гаснет на глазах у игрока.
    // Звёзды едут вместе с камерой: они бесконечно далеко, параллакса нет.
    public sealed class StarSectors : MonoBehaviour
    {
        public SpaceTuning tuning;
        [Tooltip("Звёзды секторов: весна (восток), лето (юг), осень (запад), зима (север)")]
        public Renderer[] sectors = new Renderer[4];
        [Tooltip("За кем едут звёзды; пусто — главная камера")]
        public Transform follow;

        static readonly int NitsId = Shader.PropertyToID("_Nits");
        MaterialPropertyBlock block;
        Season current;

        // До того как спадёт заливка: прошлые боссы — тёмные, текущий и будущие — горят.
        public void Prepare(Season justDefeated)
        {
            current = justDefeated;
            for (int i = 0; i < sectors.Length; i++)
                SetBrightness(i, i < (int)justDefeated ? 0f : 1f);
        }

        // Погасить сектор только что убитого босса.
        public void Extinguish() => StartCoroutine(Fade((int)current));

        IEnumerator Fade(int i)
        {
            for (float t = 0f; t < tuning.extinguishDelay; t += Time.deltaTime) yield return null;
            for (float t = 0f; t < tuning.extinguishSeconds; t += Time.deltaTime)
            {
                // Сначала медленно, потом быстрее: звёзды «уходят», а не выключаются.
                float k = t / tuning.extinguishSeconds;
                SetBrightness(i, 1f - k * k);
                yield return null;
            }
            SetBrightness(i, 0f);
        }

        void SetBrightness(int i, float k)
        {
            var r = sectors[i];
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetFloat(NitsId, r.sharedMaterial.GetFloat(NitsId) * k);
            r.SetPropertyBlock(block);
            r.enabled = k > 0f;
        }

        void LateUpdate()
        {
            var target = follow != null ? follow : (Camera.main != null ? Camera.main.transform : null);
            if (target != null) transform.position = target.position;
        }
    }
}
