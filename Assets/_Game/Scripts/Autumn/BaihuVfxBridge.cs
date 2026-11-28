using UnityEngine;
using UnityEngine.Events;

namespace TanShadow.Autumn
{
    [DisallowMultipleComponent]
    public sealed class BaihuVfxBridge : MonoBehaviour
    {
        [SerializeField] BaihuEncounter encounter;
        [SerializeField] UnityEvent<Vector3> rustled = new();
        [SerializeField] UnityEvent eyesShown = new();
        [SerializeField] UnityEvent scared = new();
        [SerializeField] UnityEvent<Vector3, Vector3> pounced = new();
        [SerializeField] UnityEvent<Vector3, Vector3> leaptAway = new();
        [SerializeField] UnityEvent<Vector3> landed = new();
        [SerializeField] UnityEvent<Vector3> impact = new();
        [SerializeField] UnityEvent<Vector3> monumentLanded = new();
        [SerializeField] UnityEvent stopped = new();
        [SerializeField] UnityEvent reset = new();

        void OnEnable()
        {
            if (!encounter) return;
            encounter.Rustled += OnRustled;
            encounter.EyesShown += OnEyes;
            encounter.Scared += OnScared;
            encounter.Pounced += OnPounced;
            encounter.TigerLeptAway += OnLeaptAway;
            encounter.PounceMissed += OnLanded;
            encounter.TigerFell += OnLanded;
            encounter.MonumentHit += OnImpact;
            encounter.MonumentFalling += OnMonumentFalling;
            encounter.MonumentLanded += OnMonumentLanded;
            encounter.HeroKilled += OnStopped;
            encounter.Defeated += OnStopped;
            encounter.EncounterReset += OnReset;
        }

        void OnDisable()
        {
            if (!encounter) return;
            encounter.Rustled -= OnRustled;
            encounter.EyesShown -= OnEyes;
            encounter.Scared -= OnScared;
            encounter.Pounced -= OnPounced;
            encounter.TigerLeptAway -= OnLeaptAway;
            encounter.PounceMissed -= OnLanded;
            encounter.TigerFell -= OnLanded;
            encounter.MonumentHit -= OnImpact;
            encounter.MonumentFalling -= OnMonumentFalling;
            encounter.MonumentLanded -= OnMonumentLanded;
            encounter.HeroKilled -= OnStopped;
            encounter.Defeated -= OnStopped;
            encounter.EncounterReset -= OnReset;
        }

        void OnRustled(Vector3 point) => rustled.Invoke(point);
        void OnEyes(Vector3 _) => eyesShown.Invoke();
        void OnScared(Vector3 _) => scared.Invoke();
        void OnPounced(Vector3 point) => pounced.Invoke(point, encounter.PresentationHeading);
        void OnLeaptAway(Vector3 point) => leaptAway.Invoke(point, encounter.PresentationHeading);
        void OnLanded(Vector3 point) => landed.Invoke(point);
        void OnImpact(int hits, int required, Vector3 point) => impact.Invoke(point);
        void OnMonumentFalling(Vector3 _)
        {
            if (!encounter.EyesOnlyInEyesPhase && encounter.peek && encounter.tigerBody && encounter.hero)
                encounter.peek.ShowPinned(encounter.tigerBody.position + Vector3.up * .42f, encounter.hero.Body.position);
        }
        void OnMonumentLanded(Vector3 point) => monumentLanded.Invoke(point);
        void OnStopped() => stopped.Invoke();
        void OnReset() => reset.Invoke();
    }
}
