using UnityEngine;

namespace ShadowTan.Combat
{
    // Ссылка с коллайдера на ассет поверхности. Без тега используется Surface_Default (§10.4).
    public class SurfaceTag : MonoBehaviour
    {
        [SerializeField] SurfaceDefinition surface;

        public SurfaceDefinition Surface => surface;

        public void SetSurface(SurfaceDefinition value) => surface = value;
    }
}
