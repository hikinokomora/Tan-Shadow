using UnityEngine;

namespace ShadowTan.Core.Contracts
{
    public enum SwordActionType
    {
        Swing,
        Stab
    }

    public enum HitResult
    {
        Ignored,
        Reaction,
        Counted,
        Deflected
    }

    // Общие поля поверхности нужны контрактам и событиям; конкретный ассет живёт в Combat.
    public interface ISurface
    {
        string DisplayName { get; }
        bool AcceptsStab { get; }
    }

    public readonly struct HitInfo
    {
        public readonly SwordActionType Action;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 Direction;
        public readonly bool WhileHanging;
        public readonly ISurface Surface;

        public HitInfo(SwordActionType action, Vector3 point, Vector3 normal, Vector3 direction,
            bool whileHanging, ISurface surface)
        {
            Action = action;
            Point = point;
            Normal = normal;
            Direction = direction;
            WhileHanging = whileHanging;
            Surface = surface;
        }
    }

    // Цель сама решает, что значит удар (ADR-006): меч только доставляет попадание.
    public interface ISwordTarget
    {
        HitResult OnSwordHit(in HitInfo hit);
    }

    // То, что пугается направленного взмаха (сейчас — тигр).
    public interface IScareable
    {
        void OnScareSwing(Vector3 swingDirection, Vector3 playerPosition);
    }
}
