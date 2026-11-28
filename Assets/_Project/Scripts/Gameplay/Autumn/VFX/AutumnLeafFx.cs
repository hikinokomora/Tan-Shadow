using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnLeafFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem leaves;
        [SerializeField] Transform observer;
        [SerializeField] Vector2 xRange = new(61f, 79f);
        [SerializeField] Vector2 zRange = new(9f, 160f);

        public bool Running { get; set; } = true;
        float nextLeaf;

        void Update()
        {
            if (!Running || !observer || Time.time < nextLeaf) return;
            nextLeaf = Time.time + Random.Range(1.05f, 2.2f);
            Vector3 position = observer.position;
            if (position.x < xRange.x || position.x > xRange.y || position.z < zRange.x || position.z > zRange.y)
                return;
            var side = Random.value < 0.5f ? -1f : 1f;
            position += new Vector3(side * Random.Range(0.8f, 2f), Random.Range(3.4f, 5.2f), Random.Range(3f, 12f));
            AutumnParticles.Emit(leaves, position, new Vector3(side * 0.55f, -1.2f, 0.2f),
                1, 0.62f, 0.14f, 4.2f, 0.22f);
        }

        public void Leap(Vector3 point, Vector3 direction)
        {
            AutumnParticles.Emit(leaves, point + Vector3.up * 1.15f, direction + Vector3.up * 0.38f,
                Random.Range(5, 10), 2.15f, 0.15f, 1.2f, 0.56f);
        }
    }
}
