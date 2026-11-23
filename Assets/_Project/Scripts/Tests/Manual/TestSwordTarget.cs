using ShadowTan.Core.Contracts;
using UnityEngine;

namespace ShadowTan.Tests.Manual
{
    // Только для песочницы Sandbox_Player.
    public sealed class TestSwordTarget : MonoBehaviour, ISwordTarget
    {
        public HitResult OnSwordHit(in HitInfo hit)
        {
            Debug.Log($"{name}: HitInfo Action={hit.Action}, Point={hit.Point}, Normal={hit.Normal}, " +
                      $"Direction={hit.Direction}, WhileHanging={hit.WhileHanging}, Surface={hit.Surface?.DisplayName}",
                      this);
            return HitResult.Counted;
        }
    }
}
