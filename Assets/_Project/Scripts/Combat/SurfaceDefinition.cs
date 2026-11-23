using ShadowTan.Core.Contracts;
using ShadowTan.Core.Data;
using UnityEngine;

namespace ShadowTan.Combat
{
    // Поверхность мира: решает, вонзается ли меч, и хранит звуки/эффекты для этой поверхности (§10.4).
    // Звуковые и VFX-поля появятся вместе с модулями Audio/VFX; контрактам нужны только имя и acceptsStab.
    [CreateAssetMenu(fileName = "Surface_", menuName = "ShadowTan/Surface")]
    public class SurfaceDefinition : ScriptableObject, ISurface
    {
        [Tooltip("Для отладки и инспектора")]
        public string displayName;

        [Tooltip("Меч вонзается (да) или отскакивает (нет)")]
        [Tbd("ARCHITECTURE §10.4; GDD-00 §13")]
        public bool acceptsStab;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public bool AcceptsStab => acceptsStab;
    }
}
