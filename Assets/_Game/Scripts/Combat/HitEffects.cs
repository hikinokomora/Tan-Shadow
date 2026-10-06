using UnityEngine;

namespace TanShadow.Combat
{
    // Искры и вспышка света в точке контакта клинков. Один на сцену.
    public class HitEffects : MonoBehaviour
    {
        public ParticleSystem deflectSparks;
        public ParticleSystem blockSparks;
        [Tooltip("Точечный свет для вспышки; держим выключенным")]
        public Light flash;
        [Tooltip("Яркость вспышки на дефлекте и на блоке")]
        [Min(0)] public float deflectFlashIntensity = 40000f;
        [Min(0)] public float blockFlashIntensity = 8000f;
        [Min(0.01f)] public float flashTime = 0.1f;
        [Tooltip("Высота точки контакта над ногами защищающегося, м")]
        public float contactHeight = 1.3f;
        [Tooltip("Насколько точка контакта вынесена к атакующему, м")]
        public float contactForward = 0.45f;

        float flashPeak;
        float flashLeft;

        [Min(0)] public float finisherFlashIntensity = 60000f;

        void OnEnable()
        {
            Combatant.AnyHit += OnHit;
            Combatant.AnyFinisher += OnFinisher;
        }

        void OnDisable()
        {
            Combatant.AnyHit -= OnHit;
            Combatant.AnyFinisher -= OnFinisher;
        }

        void OnFinisher(Combatant attacker, Combatant victim)
        {
            Vector3 toAttacker = attacker.transform.position - victim.transform.position;
            toAttacker.y = 0f;
            Vector3 point = victim.transform.position + Vector3.up * contactHeight + toAttacker.normalized * contactForward;
            Spawn(deflectSparks, point, Quaternion.LookRotation(Vector3.up));
            Flash(point, finisherFlashIntensity);
        }

        void Awake()
        {
            if (flash != null) flash.enabled = false;
        }

        void OnHit(HitInfo hit)
        {
            Vector3 toAttacker = hit.attacker.transform.position - hit.defender.transform.position;
            toAttacker.y = 0f;
            toAttacker = toAttacker.sqrMagnitude > 0.001f ? toAttacker.normalized : hit.defender.transform.forward;
            Vector3 point = hit.defender.transform.position + Vector3.up * contactHeight + toAttacker * contactForward;
            var rotation = Quaternion.LookRotation(toAttacker);

            switch (hit.result)
            {
                case HitResult.Deflected:
                    Spawn(deflectSparks, point, rotation);
                    Flash(point, deflectFlashIntensity);
                    break;
                case HitResult.Blocked:
                    Spawn(blockSparks, point, rotation);
                    Flash(point, blockFlashIntensity);
                    break;
            }
        }

        static void Spawn(ParticleSystem prefab, Vector3 point, Quaternion rotation)
        {
            if (prefab != null) Instantiate(prefab, point, rotation); // stopAction = Destroy в префабе
        }

        void Flash(Vector3 point, float intensity)
        {
            if (flash == null || intensity <= 0f) return;
            flash.transform.position = point;
            flashPeak = intensity;
            flashLeft = flashTime;
            flash.intensity = intensity;
            flash.enabled = true;
        }

        void Update()
        {
            if (flash == null || !flash.enabled) return;
            flashLeft -= Time.deltaTime;
            if (flashLeft <= 0f)
            {
                flash.enabled = false;
                return;
            }
            float t = flashLeft / flashTime;
            flash.intensity = flashPeak * t * t;
        }
    }
}
