using UnityEngine;

namespace TanShadow.Characters
{
    // Тот, кого героиня защищает (губернатор Лю Чанъи). Не боец: его нельзя захватить и случайно ударить.
    public class Protectee : MonoBehaviour
    {
        [Tooltip("Нефрит на шее — сюда приходится полночный удар")]
        public Transform jade;
        [Tooltip("Визуальная часть, которая падает при смерти")]
        public Transform visualRoot;

        public bool IsDead { get; private set; }

        public void Kill()
        {
            IsDead = true;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(-80f, 0f, 0f);
        }

        public void Revive()
        {
            IsDead = false;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        }
    }
}
