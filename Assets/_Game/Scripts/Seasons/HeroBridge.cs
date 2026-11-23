using System;
using UnityEngine;

namespace TanShadow.Seasons
{
    // Мост между сезонным испытанием и контроллером игрока.
    // Колосс знает о героине только то, что объявлено здесь. Новый контроллер игрока подключается так:
    // наследник HeroBridge на объекте игрока + ссылка на него в испытании. Логику колоссов трогать не нужно.
    public abstract class HeroBridge : MonoBehaviour
    {
        // Удар мечом начался. Аргумент — горизонтальное направление взмаха (куда смотрит героиня).
        // По нему тигр решает, был ли это отпугивающий взмах в его сторону.
        public event Action<Vector3> SwingStarted;

        // Корневой объект героини (позиция, поворот).
        public virtual Transform Body => transform;

        // Точка на уровне груди: в неё целится прыжок тигра и от неё меряется дистанция смерти.
        public virtual Vector3 AimPoint => Body.position + Vector3.up * 1.1f;

        // Героиня сейчас уходит кувырком/рывком. Только для отладки и звука: от прыжка спасает
        // не флаг, а то, что героиня физически ушла с траектории.
        public abstract bool IsDodging { get; }

        // Включить или выключить управление (на время чёрного/белого экрана и финала).
        public abstract void SetInputEnabled(bool enabled);

        // Мгновенно переставить героиню (возрождение под заливкой).
        public abstract void Teleport(Vector3 position, Quaternion rotation);

        // Кнопку удара нажали в этом кадре. Работает и при выключенном управлении:
        // летом героиня висит на хвосте Чжуцюэ и бьёт по перьям этой кнопкой.
        public abstract bool AttackPressed { get; }

        // Героиню несёт колосс: управление и физика тела выключены, позицию каждый кадр ставит испытание
        // через Body. false — отпустить: героиня падает под своей гравитацией.
        public abstract void SetCarried(bool carried);

        protected void RaiseSwingStarted(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Body.forward;
            SwingStarted?.Invoke(direction.normalized);
        }
    }
}
