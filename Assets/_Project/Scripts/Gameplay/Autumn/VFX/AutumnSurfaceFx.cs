using ShadowTan.Combat;
using ShadowTan.Player;
using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnSurfaceFx : MonoBehaviour
    {
        [SerializeField] FootstepEmitter footsteps;
        [SerializeField] ParticleSystem dust;
        [SerializeField] Terrain terrain;
        [SerializeField] TerrainLayer[] dryLayers;
        [SerializeField] SurfaceDefinition drySurface;
        [SerializeField] Collider stoneRamp;
        [SerializeField] SurfaceDefinition stoneSurface;
        [SerializeField] Collider[] dryColliders;

        TerrainCollider terrainCollider;
        Texture2D[] splatMaps;
        TerrainLayer[] terrainLayers;

        public FootstepEmitter Footsteps => footsteps;
        public bool Running { get; set; } = true;

        void Awake()
        {
            if (!terrain) return;
            terrainCollider = terrain.GetComponent<TerrainCollider>();
            terrainLayers = terrain.terrainData.terrainLayers;
            splatMaps = terrain.terrainData.alphamapTextures;
        }

        void OnEnable()
        {
            if (footsteps) footsteps.FootstepDetected += OnFootstep;
        }

        void OnDisable()
        {
            if (footsteps) footsteps.FootstepDetected -= OnFootstep;
        }

        public SurfaceDefinition Resolve(SurfaceDefinition tagged, RaycastHit hit)
        {
            if (tagged) return tagged;
            if (!hit.collider) return null;
            if (hit.collider == stoneRamp) return stoneSurface;
            if (dryColliders != null)
                foreach (var collider in dryColliders)
                    if (hit.collider == collider) return drySurface;
            if (hit.collider != terrainCollider || splatMaps == null || dryLayers == null) return null;

            var size = terrain.terrainData.size;
            var local = hit.point - terrain.transform.position;
            float weight = 0;
            for (int map = 0; map < splatMaps.Length; map++)
            {
                Color sample = splatMaps[map].GetPixelBilinear(local.x / size.x, local.z / size.z);
                for (int channel = 0; channel < 4 && 4 * map + channel < terrainLayers.Length; channel++)
                {
                    var layer = terrainLayers[4 * map + channel];
                    foreach (var dry in dryLayers)
                        if (layer == dry) { weight += sample[channel]; break; }
                }
            }
            return weight >= 0.65f ? drySurface : null;
        }

        void OnFootstep(SurfaceDefinition surface)
        {
            var hit = footsteps.LastContact;
            if (!Running || !drySurface || Resolve(surface, hit) != drySurface) return;
            AutumnParticles.Emit(dust, hit.point + hit.normal * 0.04f, -footsteps.transform.forward,
                Random.Range(4, 9), 0.32f, 0.19f, 0.42f, 0.28f);
        }
    }
}
