using System;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Мост между испытанием Байху и контроллером игрока.
    // Тигр знает о героине только то, что объявлено здесь. Новый контроллер игрока подключается так:
    // наследник HeroBridge на объекте игрока + ссылка на него в BaihuEncounter. Логику тигра трогать не нужно.
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

        protected void RaiseSwingStarted(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Body.forward;
            SwingStarted?.Invoke(direction.normalized);
        }
    }
}
