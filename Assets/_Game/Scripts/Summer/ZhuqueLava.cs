using System;
using TanShadow.Seasons;
using UnityEngine;

namespace TanShadow.Summer
{
    // Два состояния арены. Сухое: русла засыпаны пеплом (dryBeds), лавы нет вовсе.
    // Лава: пепел убран, реки поднимаются в руслах, касание и долгий жар убивают.
    // Логика опасности взята из SummerLava (KaVQ), только вместо телепорта — событие для испытания.
    public sealed class ZhuqueLava : MonoBehaviour
    {
        public ZhuqueTuning tuning;
        public HeroBridge hero;
        [Tooltip("Пепел, засыпающий будущие русла в сухой фазе")]
        public GameObject dryBeds;
        [Tooltip("Реки, берега, жар и пар — всё, что появляется после третьего удара")]
        public GameObject lavaRoot;
        [Tooltip("Поверхности лавы: по ним считается касание и «тень над лавой»")]
        public MeshCollider[] rivers;
        [Tooltip("На сколько метров ниже уровня лава начинает подниматься")]
        public float riseDepth = 1.4f;

        public bool IsOpen { get; private set; }
        // Опасность включает испытание: не во время полёта на хвосте и не после победы.
        public bool HazardArmed { get; set; }
        public float Heat { get; private set; }

        public event Action HeroBurned;

        Vector3[] restPositions;
        float rise = 1f, riseDuration = 1f;
        readonly Collider[] nearby = new Collider[64];

        void Awake()
        {
            restPositions = new Vector3[rivers.Length];
            for (int i = 0; i < rivers.Length; i++) restPositions[i] = rivers[i].transform.position;
        }

        public void SetDry()
        {
            IsOpen = false;
            Heat = 0f;
            rise = 1f;
            for (int i = 0; i < rivers.Length; i++) rivers[i].transform.position = restPositions[i];
            if (dryBeds != null) dryBeds.SetActive(true);
            if (lavaRoot != null) lavaRoot.SetActive(false);
        }

        // Русла раскрываются и заливаются: лава поднимается из глубины за duration секунд.
        public void Open(float duration)
        {
            IsOpen = true;
            Heat = 0f;
            rise = 0f;
            riseDuration = Mathf.Max(0.01f, duration);
            if (dryBeds != null) dryBeds.SetActive(false);
            if (lavaRoot != null) lavaRoot.SetActive(true);
            PlaceRivers();
        }

        // Есть ли под точкой (по вертикали) лава. margin — допуск: лава ближе этого тоже считается.
        public bool IsOverLava(Vector3 point, float margin = 0f)
        {
            if (!IsOpen) return false;
            if (RiverBelow(point)) return true;
            if (margin <= 0f) return false;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                if (RiverBelow(point + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * margin)) return true;
            }
            return false;
        }

        bool RiverBelow(Vector3 point)
        {
            var ray = new Ray(new Vector3(point.x, point.y + 80f, point.z), Vector3.down);
            foreach (var river in rivers)
                if (river != null && river.Raycast(ray, out _, 400f)) return true;
            return false;
        }

        // Стоит ли героиня на сухом пепле, засыпающем будущее русло. Лаву не открываем у неё под ногами:
        // без пепла она оказалась бы в яме с крутыми берегами и сгорела бы без шанса.
        public bool IsOnDryChannel(Vector3 feet)
        {
            if (IsOpen || dryBeds == null || !dryBeds.activeInHierarchy) return false;
            return Physics.Raycast(feet + Vector3.up, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                   && hit.transform.IsChildOf(dryBeds.transform);
        }

        void PlaceRivers()
        {
            float k = Mathf.SmoothStep(0f, 1f, rise);
            for (int i = 0; i < rivers.Length; i++)
                rivers[i].transform.position = restPositions[i] + Vector3.down * (riseDepth * (1f - k));
        }

        void Update()
        {
            if (IsOpen && rise < 1f)
            {
                rise = Mathf.Min(1f, rise + Time.deltaTime / riseDuration);
                PlaceRivers();
            }
            if (hero == null || tuning == null) return;

            Vector3 feet = hero.Body.position;
            if (HazardArmed && feet.y < tuning.killHeight) { Burn(); return; }
            if (!IsOpen || !HazardArmed) { Heat = 0f; return; }

            Vector3 foot = feet + Vector3.up * 0.12f;
            bool exposed = false;
            int count = Physics.OverlapSphereNonAlloc(foot, tuning.heatRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count && !exposed; i++) exposed = IsRiver(nearby[i]);
            Heat = Mathf.MoveTowards(Heat, exposed ? 1f : 0f, Time.deltaTime / (exposed ? tuning.heatTime : tuning.heatCoolTime));

            bool touched = Physics.Raycast(foot + Vector3.up * 0.15f, Vector3.down, out var hit, 0.49f, ~0, QueryTriggerInteraction.Ignore)
                           && IsRiver(hit.collider);
            if (touched || Heat >= 1f) Burn();
        }

        bool IsRiver(Collider c) => c != null && Array.IndexOf(rivers, c as MeshCollider) >= 0;

        void Burn()
        {
            HazardArmed = false;
            Heat = 0f;
            HeroBurned?.Invoke();
        }

        // Полоска жара из SummerLava: появляется, только когда уже ощутимо печёт.
        void OnGUI()
        {
            if (Heat < 0.32f) return;
            var r = new Rect(Screen.width * 0.5f - 62, Screen.height * 0.86f, 124, 5);
            var color = GUI.color;
            GUI.color = new Color(0.16f, 0.09f, 0.07f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            r.width *= Heat;
            GUI.color = Color.Lerp(new Color(1f, 0.65f, 0.22f), new Color(1f, 0.18f, 0.05f), Heat);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
        }
    }
}
