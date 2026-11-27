using UnityEngine;
using UnityEngine.Rendering;

namespace TanShadow.Spring
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SpringBirdFlock : MonoBehaviour
    {
        [SerializeField] GameObject birdModel;
        [SerializeField] Material birdMaterial;
        [SerializeField] SpringDragonEncounter encounter;
        [SerializeField] Transform player;

        [Header("Стая")]
        [SerializeField, Range(3, 12)] int birdCount = 7;
        [SerializeField, Range(0f, 1f), Tooltip("0 — тёмный силуэт, 1 — исходный светлый тон оперения.")]
        float birdTone = .48f;
        [SerializeField, Min(.1f)] float birdScale = 1.55f;
        [SerializeField, Min(.2f)] float wingBeatsPerSecond = 2f;

        [Header("Маршрут и интервалы")]
        [SerializeField] Transform[] routes;
        [SerializeField] float heightOffset;
        [SerializeField, Min(1f)] float flightLength = 154f;
        [SerializeField, Min(1f)] float flightDuration = 13f;
        [SerializeField, Min(0f)] float firstFlightDelay = 15f;
        [SerializeField] Vector2 intervalSeconds = new Vector2(70f, 110f);

        static readonly int ToneId = Shader.PropertyToID("_UnlitColor");
        static readonly Color DarkTone = new Color(.17f, .21f, .27f, 1f);

        sealed class Bird
        {
            public Transform root, leftWing, rightWing;
            public SkinnedMeshRenderer renderer;
            public Quaternion leftRest, rightRest;
            public Vector3 leftAxis, rightAxis, formation;
            public float phase, size;
        }

        GameObject flockRoot;
        Bird[] birds;
        Transform activeRoute;
        MaterialPropertyBlock toneBlock;
        float lastTone = -1f;
        float elapsed, untilFlight;
        bool flying;

        void OnEnable()
        {
            untilFlight = firstFlightDelay;
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PreviewTick;
#endif
            flying = false;
            if (flockRoot == null) return;
            flockRoot.SetActive(false);
            if (Application.IsPlaying(gameObject)) Destroy(flockRoot);
            else DestroyImmediate(flockRoot);
            flockRoot = null;
            birds = null;
        }

        void Update()
        {
            if (!Application.IsPlaying(gameObject)) return;
            if (encounter != null && encounter.isActiveAndEnabled &&
                (!encounter.DragonPresent || encounter.IsDefeated))
            {
                if (flying) FinishFlight();
                return;
            }

            if (flying)
            {
                TickFlight(Time.deltaTime);
                return;
            }

            untilFlight -= Time.deltaTime;
            if (untilFlight <= 0f) StartFlight();
        }

        void StartFlight(Transform route = null)
        {
            elapsed = 0f;
            activeRoute = route != null ? route : ClosestRoute(player != null ? player.position : transform.position);
            if (!BuildFlock())
            {
                FinishFlight();
                return;
            }

            flying = true;
            flockRoot.SetActive(true);
            Pose(0f);
        }

        Transform ClosestRoute(Vector3 observer)
        {
            Transform best = transform;
            float bestDistance = float.PositiveInfinity;
            if (routes == null) return best;

            foreach (var route in routes)
            {
                if (route == null) continue;
                Vector3 distance = route.position - observer;
                float planarDistance = distance.x * distance.x + distance.z * distance.z;
                if (planarDistance >= bestDistance) continue;
                bestDistance = planarDistance;
                best = route;
            }

            return best;
        }

        void TickFlight(float deltaTime)
        {
            elapsed += deltaTime;
            float progress = elapsed / Mathf.Max(1f, flightDuration);
            if (progress >= 1f) FinishFlight();
            else Pose(progress);
        }

        void FinishFlight()
        {
            flying = false;
            if (flockRoot != null) flockRoot.SetActive(false);
            float min = Mathf.Max(0f, intervalSeconds.x);
            untilFlight = Random.Range(min, Mathf.Max(min, intervalSeconds.y));
        }

        bool BuildFlock()
        {
            if (flockRoot != null) return birds != null;
            if (birdModel == null || birdMaterial == null) return false;

            bool preview = !Application.IsPlaying(gameObject);
            flockRoot = new GameObject("Flying birds");
            if (preview) flockRoot.hideFlags = HideFlags.HideAndDontSave;
            flockRoot.transform.SetParent(transform, false);
            birds = new Bird[birdCount];
            toneBlock ??= new MaterialPropertyBlock();
            lastTone = -1f;

            for (int i = 0; i < birds.Length; i++)
            {
                var mount = new GameObject($"Bird {i + 1}");
                if (preview) mount.hideFlags = HideFlags.HideAndDontSave;
                mount.transform.SetParent(flockRoot.transform, false);
                var model = Instantiate(birdModel, mount.transform, false);
                // The imported bird's tail points along +Z; the flight mount uses +Z as its heading.
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
                if (preview) model.hideFlags = HideFlags.HideAndDontSave;
                var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
                if (renderer == null)
                {
                    if (preview) DestroyImmediate(flockRoot);
                    else Destroy(flockRoot);
                    flockRoot = null;
                    birds = null;
                    return false;
                }

                renderer.sharedMaterial = birdMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                var bounds = renderer.localBounds;
                bounds.Expand(bounds.size.magnitude * .58f);
                renderer.localBounds = bounds;

                int row = (i + 1) / 2;
                float side = i % 2 == 0 ? 1f : -1f;
                var bird = new Bird
                {
                    root = mount.transform,
                    renderer = renderer,
                    formation = new Vector3(side * row * 4.2f, Mathf.Sin(i * 2.4f) * 1.35f, -row * 5.1f + Mathf.Sin(i * 2.1f) * 1.55f),
                    phase = i * 2.17f,
                    size = 1f + Mathf.Sin(i * 4.04f) * .14f
                };

                foreach (var bone in renderer.bones)
                {
                    if (bone.name == "Wing.L") bird.leftWing = bone;
                    if (bone.name == "Wing.R") bird.rightWing = bone;
                }

                CacheWing(bird.leftWing, mount.transform.forward, out bird.leftRest, out bird.leftAxis);
                CacheWing(bird.rightWing, mount.transform.forward, out bird.rightRest, out bird.rightAxis);
                birds[i] = bird;
            }

            return true;
        }

        static void CacheWing(Transform wing, Vector3 flightAxis, out Quaternion rest, out Vector3 axis)
        {
            rest = Quaternion.identity;
            axis = Vector3.forward;
            if (wing == null) return;
            rest = wing.localRotation;
            axis = wing.parent.InverseTransformDirection(flightAxis);
        }

        Vector3 FlightPoint(Transform route, float progress)
        {
            float arc = Mathf.Sin(Mathf.PI * progress);
            return route.position + route.right * (4f * arc) + Vector3.up * (heightOffset + 4.5f * arc)
                + route.forward * (flightLength * (progress - .5f));
        }

        void Pose(float progress)
        {
            Vector3 heading = FlightPoint(activeRoute, progress + .002f) - FlightPoint(activeRoute, progress - .002f);
            Quaternion orientation = Quaternion.LookRotation(heading, Vector3.up);
            Vector3 center = FlightPoint(activeRoute, progress);
            bool toneChanged = !Mathf.Approximately(lastTone, birdTone);
            if (toneChanged)
            {
                lastTone = birdTone;
                toneBlock.SetColor(ToneId, Color.Lerp(DarkTone, Color.white, birdTone));
            }

            foreach (var bird in birds)
            {
                float beat = elapsed * wingBeatsPerSecond * (1f + .07f * Mathf.Sin(bird.phase)) * Mathf.PI * 2f + bird.phase;
                float flutter = Mathf.Sin(beat);
                float glide = .66f + .34f * Mathf.Sin(elapsed * 1.75f + bird.phase);
                float amplitude = Mathf.Lerp(11f, 37f, glide);
                Vector3 offset = bird.formation + new Vector3(Mathf.Sin(elapsed * .81f + bird.phase) * .73f,
                    flutter * .19f + Mathf.Cos(elapsed * 1.7f + bird.phase) * .32f, 0f);
                bird.root.SetPositionAndRotation(center + orientation * offset, orientation * Quaternion.Euler(0f, 0f, -4f + Mathf.Sin(elapsed * .93f + bird.phase) * 7f));
                bird.root.localScale = Vector3.one * (birdScale * bird.size);
                if (bird.leftWing != null)
                    bird.leftWing.localRotation = Quaternion.AngleAxis(-flutter * amplitude, bird.leftAxis) * bird.leftRest;
                if (bird.rightWing != null)
                    bird.rightWing.localRotation = Quaternion.AngleAxis(flutter * amplitude, bird.rightAxis) * bird.rightRest;
                if (toneChanged) bird.renderer.SetPropertyBlock(toneBlock);
            }
        }

#if UNITY_EDITOR
        double lastPreviewTick;

        public void PreviewFlight(int routeIndex)
        {
            Transform route = routes != null && routeIndex >= 0 && routeIndex < routes.Length ? routes[routeIndex] : transform;
            if (Application.IsPlaying(gameObject))
            {
                if (encounter == null || !encounter.isActiveAndEnabled || (encounter.DragonPresent && !encounter.IsDefeated))
                    StartFlight(route);
                return;
            }

            StartFlight(route);
            if (!flying) return;
            elapsed = Mathf.Max(1f, flightDuration) * .34f;
            Pose(.34f);
            lastPreviewTick = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update -= PreviewTick;
            UnityEditor.EditorApplication.update += PreviewTick;
            var view = UnityEditor.SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.LookAt(FlightPoint(activeRoute, .49f), Quaternion.LookRotation(activeRoute.right + Vector3.up * .35f),
                    flightLength * .47f, false, true);
                view.Repaint();
            }
        }

        public void StopPreview()
        {
            UnityEditor.EditorApplication.update -= PreviewTick;
            FinishFlight();
        }

        void PreviewTick()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !flying)
            {
                StopPreview();
                return;
            }
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastPreviewTick);
            if (dt < 1f / 45f) return;
            lastPreviewTick = now;
            TickFlight(Mathf.Min(dt, .10f));
            UnityEditor.SceneView.RepaintAll();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.39f, .72f, .95f, .72f);
            if (routes == null || routes.Length == 0) DrawRoute(transform);
            else foreach (var route in routes)
                if (route != null) DrawRoute(route);
        }

        void DrawRoute(Transform route)
        {
            Vector3 previous = FlightPoint(route, 0f);
            for (int i = 1; i <= 24; i++)
            {
                Vector3 next = FlightPoint(route, i / 24f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif
    }
}
