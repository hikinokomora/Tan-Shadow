using UnityEngine;

namespace TanShadow.Summer
{
    // Local traversal preview for the flooded copy of the arena.
    public sealed class SummerLava : MonoBehaviour
    {
        public CharacterController player;
        public MeshCollider[] rivers;
        [Min(1)] public float heatDuration = 8f;
        readonly Collider[] nearby = new Collider[64];
        Vector3 start;
        float heat;

        void Start() => start = player.transform.position;

        void LateUpdate()
        {
            var foot = player.bounds.center - Vector3.up * (player.bounds.extents.y - .12f);
            bool exposed = false;
            int count = Physics.OverlapSphereNonAlloc(foot, 1.35f, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (System.Array.IndexOf(rivers, nearby[i] as MeshCollider) >= 0) { exposed = true; break; }
            heat = Mathf.MoveTowards(heat, exposed ? 1f : 0f, Time.deltaTime / (exposed ? heatDuration : 2f));
            bool touched = Physics.Raycast(foot + Vector3.up * .15f, Vector3.down, out var hit, .49f, 1,
                QueryTriggerInteraction.Ignore) && System.Array.IndexOf(rivers, hit.collider as MeshCollider) >= 0;
            if (touched || heat >= 1 || player.transform.position.y < -8)
            {
                player.enabled = false;
                player.transform.position = start;
                player.enabled = true;
                heat = 0;
            }
        }

        void OnGUI()
        {
            if (heat < .32f) return;
            var r = new Rect(Screen.width * .5f - 62, Screen.height * .86f, 124, 5);
            var color = GUI.color;
            GUI.color = new Color(.16f, .09f, .07f, .9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            r.width *= heat;
            GUI.color = Color.Lerp(new Color(1f,.65f,.22f), new Color(1f,.18f,.05f),heat);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
        }
    }
}
