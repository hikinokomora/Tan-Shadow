using ShadowTan.Core.Contracts;
using UnityEngine;

namespace ShadowTan.Core.Events
{
    public readonly struct SwordHit
    {
        public readonly ISwordTarget Target;
        public readonly HitResult Result;
        public readonly ISurface Surface;
        public readonly Vector3 Point;

        public SwordHit(ISwordTarget target, HitResult result, ISurface surface, Vector3 point)
        {
            Target = target;
            Result = result;
            Surface = surface;
            Point = point;
        }
    }

    public readonly struct SwordDeflected
    {
        public readonly ISurface Surface;
        public readonly Vector3 Point;

        public SwordDeflected(ISurface surface, Vector3 point)
        {
            Surface = surface;
            Point = point;
        }
    }

    public readonly struct SwordStuck
    {
        public readonly ISurface Surface;
        public readonly Vector3 Point;

        public SwordStuck(ISurface surface, Vector3 point)
        {
            Surface = surface;
            Point = point;
        }
    }
}
