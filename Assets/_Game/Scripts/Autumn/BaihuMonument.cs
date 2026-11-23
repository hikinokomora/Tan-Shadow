using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Железный столб на арене. Изначально основание в трещинах; каждый удар тигра добавляет трещины
    // и чуть «осыпает» основание. На последнем ударе столб падает на оглушённого тигра.
    // Трещины на грейбоксе — тонкие тёмные пластины на гранях основания (материал «Basal fractures»).
    public sealed class BaihuMonument : MonoBehaviour
    {
        [Tooltip("Коллайдер ствола: по нему считается попадание тигра")]
        public BoxCollider shaftCollider;
        [Tooltip("Материал трещин")]
        public Material crackMaterial;
        [Tooltip("Трещины, видимые с самого начала (подсказка «основание ослаблено»)")]
        [Min(0)] public int initialCracks = 4;
        [Tooltip("Сколько трещин добавляет каждый удар")]
        [Min(0)] public int cracksPerHit = 6;
        [Tooltip("Ударов до падения (должно совпадать с BaihuTuning.requiredHits)")]
        [Min(1)] public int stages = 4;
        [Tooltip("Высота зоны трещин от основания, доля высоты столба")]
        [Range(0.05f, 1f)] public float crackZone = 0.28f;
        [Tooltip("Насколько основание проседает с каждым ударом, м")]
        [Min(0)] public float sinkPerHit = 0.04f;
        [Tooltip("Наклон столба с каждым ударом, °")]
        [Min(0)] public float tiltPerHit = 0.8f;

        public int Hits { get; private set; }
        public bool HasFallen { get; private set; }

        readonly List<GameObject> cracks = new List<GameObject>();
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 pivot;
        Vector3 fallAxis;
        Quaternion fallStart;
        Vector3 fallStartPosition;
        bool built;

        void Awake()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            BuildCracks();
            ResetMonument();
        }

        public Bounds HitBounds => shaftCollider != null ? shaftCollider.bounds : new Bounds(transform.position, Vector3.one * 3f);

        // Ближайшая точка ствола у земли: туда тигр врезается и там лежит оглушённый.
        public Vector3 BasePointToward(Vector3 from)
        {
            var b = HitBounds;
            Vector3 p = b.ClosestPoint(new Vector3(from.x, b.min.y + 0.5f, from.z));
            p.y = b.min.y;
            return p;
        }

        public void ApplyHit(Vector3 tigerPosition)
        {
            if (HasFallen) return;
            Hits = Mathf.Min(Hits + 1, stages);
            ShowCracks();
            // Столб чуть проседает и клонится в сторону, откуда бил тигр.
            Vector3 away = transform.position - tigerPosition;
            away.y = 0f;
            Vector3 axis = away.sqrMagnitude > 0.001f ? Vector3.Cross(Vector3.up, away.normalized) : Vector3.right;
            transform.SetPositionAndRotation(
                restPosition + Vector3.down * sinkPerHit * Hits,
                Quaternion.AngleAxis(tiltPerHit * Hits, axis) * restRotation);
        }

        // Подготовить падение в сторону точки (туда, где лежит тигр).
        public void BeginFall(Vector3 toward)
        {
            var b = HitBounds;
            Vector3 dir = toward - b.center;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            pivot = new Vector3(b.center.x, b.min.y, b.center.z) + dir * b.extents.x;
            fallAxis = Vector3.Cross(Vector3.up, dir);
            fallStart = transform.rotation;
            fallStartPosition = transform.position;
        }

        // t от 0 до 1. Падение с ускорением, как у тяжёлого тела.
        public void SetFall(float t)
        {
            float eased = t * t;
            var rotation = Quaternion.AngleAxis(88f * eased, fallAxis);
            transform.SetPositionAndRotation(pivot + rotation * (fallStartPosition - pivot), rotation * fallStart);
            if (t >= 1f) HasFallen = true;
        }

        public void ResetMonument()
        {
            Hits = 0;
            HasFallen = false;
            transform.SetPositionAndRotation(restPosition, restRotation);
            ShowCracks();
        }

        void ShowCracks()
        {
            int visible = initialCracks + cracksPerHit * Hits;
            for (int i = 0; i < cracks.Count; i++) cracks[i].SetActive(i < visible);
        }

        void BuildCracks()
        {
            if (built || shaftCollider == null) return;
            built = true;
            var root = new GameObject("Cracks").transform;
            root.SetParent(shaftCollider.transform, false);

            // Работаем в локальных координатах коллайдера, чтобы трещины ехали вместе со столбом.
            Vector3 size = shaftCollider.size;
            Vector3 center = shaftCollider.center;
            var random = new System.Random(4127);
            float R() => (float)random.NextDouble();
            int total = initialCracks + cracksPerHit * stages;
            for (int i = 0; i < total; i++)
            {
                int face = random.Next(4);
                Vector3 normal = face == 0 ? Vector3.right : face == 1 ? Vector3.left : face == 2 ? Vector3.forward : Vector3.back;
                Vector3 tangent = Vector3.Cross(Vector3.up, normal);
                float faceHalf = Mathf.Abs(Vector3.Dot(size * 0.5f, tangent));
                float depthHalf = Mathf.Abs(Vector3.Dot(size * 0.5f, normal));
                // Поздние трещины длиннее и поднимаются выше: основание «расходится».
                float growth = (float)i / total;
                float height = size.y * crackZone * Mathf.Lerp(0.35f, 1f, growth);
                float y = center.y - size.y * 0.5f + R() * height;
                float along = (R() * 2f - 1f) * faceHalf * 0.9f;
                var crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(crack.GetComponent<Collider>());
                crack.name = "Crack " + i;
                crack.transform.SetParent(root, false);
                crack.transform.localPosition = center + normal * (depthHalf + 0.005f) + tangent * along + Vector3.up * (y - center.y);
                crack.transform.localRotation = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Mathf.Lerp(-70f, 70f, R()));
                // Масштаб задаётся в локальных единицах коллайдера, поэтому делим на его масштаб, чтобы трещины не растягивались.
                Vector3 parentScale = shaftCollider.transform.lossyScale;
                float s = Mathf.Max(0.0001f, Mathf.Min(parentScale.x, parentScale.z));
                crack.transform.localScale = new Vector3(
                    Mathf.Lerp(0.03f, 0.07f, R()) / s,
                    Mathf.Lerp(0.4f, 1.4f, growth * 0.6f + R() * 0.4f) / Mathf.Max(0.0001f, parentScale.y),
                    0.01f / s);
                if (crackMaterial != null) crack.GetComponent<Renderer>().sharedMaterial = crackMaterial;
                cracks.Add(crack);
            }
        }
    }
}
