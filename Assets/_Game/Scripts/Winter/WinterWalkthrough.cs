using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TanShadow.Winter
{
    public sealed class WinterWalkthrough : MonoBehaviour
    {
        public CharacterController player;
        public Camera view;
        public Volume atmosphere;
        public ParticleSystem windSnow;
        public Transform shellCenter;
        public Vector2 shellRadii;
        [Min(1)] public float landscapeScale=1f;
        Vector3 entry;
        Quaternion entryRotation;
        Fog fog;
        GradientSky sky;

        void Start()
        {
            entry = player.transform.position;
            entryRotation = player.transform.rotation;
            atmosphere.profile.TryGet(out fog);
            atmosphere.profile.TryGet(out sky);
        }

        void LateUpdate()
        {
            if (player.transform.position.y < -37f)
            {
                player.enabled = false;
                player.transform.SetPositionAndRotation(entry, entryRotation);
                player.enabled = true;
            }
            Vector3 delta = player.transform.position - shellCenter.position;
            float radius = new Vector2(delta.x / shellRadii.x, delta.z / shellRadii.y).magnitude;
            float strength = Mathf.InverseLerp(1.035f, .92f, radius) *
                Mathf.InverseLerp(5.5f*landscapeScale, 7.3f*landscapeScale, player.transform.position.y);
            if (fog)
            {
                fog.meanFreePath.Override(Mathf.Lerp(118f*Mathf.Sqrt(landscapeScale), 42f*Mathf.Sqrt(landscapeScale), strength));
                fog.color.Override(Color.Lerp(new Color(.62f,.72f,.84f), new Color(.44f,.55f,.67f),strength)*400f);
            }
            if (sky) sky.exposure.Override(Mathf.Lerp(10.2f,9.3f,strength));
            windSnow.transform.position = view.transform.position + view.transform.forward*8f + new Vector3(-7, 7, 0);
            var emission = windSnow.emission;
            emission.rateOverTime = Mathf.Lerp(0f, 740f, strength);
        }
    }
}
