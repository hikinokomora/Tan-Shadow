using System.Collections.Generic;
using UnityEngine;

namespace ShadowTan.Core.Contracts
{
    // Запросы чужих модулей о героине. Регистрируется самой героиней при появлении (§8.2).
    public interface IPlayerQuery : IService
    {
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        Vector3 Velocity { get; }
        IReadOnlyList<Vector3> VisibilityPoints { get; }
        string StateName { get; }
        bool IsHanging { get; }
        GrabMode GrabMode { get; }
        string Zone { get; set; }
        bool IsInCover(IDetectionSource source);
    }

    // Источник обнаружения глазами колосса (реализуется модулем Detection).
    public interface IDetectionSource
    {
        bool IsPresent { get; }
        Vector3 EyePosition { get; }
    }
}
