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
        [SerializeField] Material[] crackStages;
        [SerializeField] Mesh damagedShaft;
        [Tooltip("Ударов до падения (должно совпадать с BaihuTuning.requiredHits)")]
        [Min(1)] public int stages = 4;
        [Tooltip("Насколько основание проседает с каждым ударом, м")]
        [Min(0)] public float sinkPerHit = 0.04f;
        [Tooltip("Наклон столба с каждым ударом, °")]
        [Min(0)] public float tiltPerHit = 0.8f;

        public int Hits { get; private set; }
        public bool HasFallen { get; private set; }

        MeshFilter shaftVisual;
        MeshRenderer shaftRenderer;
        Mesh originalShaft;
        Material originalMaterial;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 pivot;
        Vector3 fallAxis;
        Quaternion fallStart;
        Vector3 fallStartPosition;

        void Awake()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            if (shaftCollider != null)
            {
                shaftVisual = shaftCollider.GetComponent<MeshFilter>();
                shaftRenderer = shaftCollider.GetComponent<MeshRenderer>();
                if (shaftVisual != null) originalShaft = shaftVisual.sharedMesh;
                if (shaftRenderer != null) originalMaterial = shaftRenderer.sharedMaterial;
            }
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
            if (shaftRenderer != null)
                shaftRenderer.sharedMaterial = crackStages is { Length: > 0 }
                    ? crackStages[Mathf.Min(Hits, crackStages.Length - 1)]
                    : originalMaterial;
            if (shaftVisual != null)
                shaftVisual.sharedMesh = Hits >= stages && damagedShaft != null ? damagedShaft : originalShaft;
        }
    }
}
