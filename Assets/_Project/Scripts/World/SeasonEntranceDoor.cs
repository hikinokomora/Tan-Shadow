using System;
using System.Collections;
using UnityEngine;

namespace ShadowTan.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SeasonEntranceDoor : MonoBehaviour
    {
        [SerializeField] Transform slab;
        [SerializeField] Transform entrant;
        [SerializeField] Vector3 closedPosition;
        [SerializeField] float rise = 5.6f;
        [SerializeField] float openingSeconds = 1f;
        [SerializeField] float closingSeconds = 0.2f;
        [SerializeField] bool openOnStart;
        [SerializeField] ParticleSystem closeDust;

        Coroutine motion;
        bool locked;
        bool victory;

        public bool IsLocked => locked;
        public bool IsOpen { get; private set; }
        public event Action OnDoorClosed;

        void Start()
        {
            if (openOnStart) Open();
        }

        void OnDisable()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
        }

        public void Open()
        {
            if (locked || IsOpen || slab == null || !isActiveAndEnabled || motion != null) return;
            motion = StartCoroutine(Move(closedPosition + Vector3.up * rise, openingSeconds, true));
        }

        public void Close()
        {
            if (locked || victory || slab == null || !isActiveAndEnabled || (!IsOpen && motion == null)) return;
            if (motion != null) StopCoroutine(motion);
            locked = true;
            IsOpen = false;
            motion = StartCoroutine(Move(closedPosition, closingSeconds, false));
        }

        public void UnlockAfterVictory()
        {
            victory = true;
            locked = false;
            if (motion != null)
            {
                StopCoroutine(motion);
                motion = null;
            }

            Open();
        }

        void OnTriggerEnter(Collider other)
        {
            if (entrant != null && (other.transform == entrant || other.transform.IsChildOf(entrant)))
                Close();
        }

        IEnumerator Move(Vector3 target, float seconds, bool opening)
        {
            Vector3 start = slab.localPosition;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                float t = elapsed / seconds;
                slab.localPosition = Vector3.LerpUnclamped(start, target, opening ? Mathf.SmoothStep(0f, 1f, t) : t * t);
                yield return null;
                elapsed += Time.deltaTime;
            }

            slab.localPosition = target;
            IsOpen = opening;
            motion = null;
            if (!opening)
            {
                if (closeDust != null) closeDust.Play();
                OnDoorClosed?.Invoke();
            }
        }
    }
}
