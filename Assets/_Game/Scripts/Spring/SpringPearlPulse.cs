using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TanShadow.Spring
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class SpringPearlPulse : MonoBehaviour
    {
        public enum Stage { Intact, FirstCracks, BranchingCracks, Fractured, Shattered }

        [Serializable]
        struct CrackMaps
        {
            public Texture2D albedo;
            public Texture2D emissive;
        }

        enum Burst { None, Hit, Shatter }

        const int LargeShardCount = 14;
        const int SmallShardCount = 42;
        const int ExtraLargeShardCount = 10;
        const int ShardCount = LargeShardCount + SmallShardCount + ExtraLargeShardCount;

        [Header("Поверхность")]
        [SerializeField] Stage startingStage;
        [SerializeField, Min(.1f)] float period = 2.4f;
        [SerializeField] CrackMaps[] cracks = new CrackMaps[3];

        [Header("Аура и частицы")]
        [SerializeField] Material auraMaterial;
        [SerializeField, Range(0f, 1f)] float auraOpacity = .58f;
        [SerializeField] Material flashMaterial;
        [SerializeField] Material ringMaterial;
        [SerializeField] Material sparkMaterial;
        [SerializeField] Material shardMaterial;
        [SerializeField] Mesh shardMesh;

        static readonly int AlbedoId = Shader.PropertyToID("_BaseColorMap");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissiveColorMap");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissiveColor");
        static readonly int EmissionTransformId = Shader.PropertyToID("_EmissiveColorMap_ST");
        static readonly Color Ember = new Color(1f, .18f, .055f);
        static readonly Color Gold = new Color(1f, .71f, .26f);

        MeshRenderer pearlRenderer;
        Material surface;
        MaterialPropertyBlock properties;
        GameObject effectsRoot;
        ParticleSystem aura, flash, ring, sparks, shards;
        readonly ParticleSystem.Particle[] card = new ParticleSystem.Particle[1];
        readonly ParticleSystem.Particle[] sparkParticles = new ParticleSystem.Particle[16];
        readonly ParticleSystem.Particle[] shardParticles = new ParticleSystem.Particle[ShardCount];
        readonly Debris[] sparkPaths = new Debris[16];
        readonly Debris[] shardPaths = new Debris[ShardCount];

        struct Debris
        {
            public Vector3 velocity;
            public Vector3 spin;
            public float size, lifetime, hue;
        }

        Stage stage, stageBeforeBurst;
        Burst burst;
        float elapsed, burstAge;
        Vector3 impactPoint;

        public Stage PreviewStage => stage;

        void OnEnable()
        {
            pearlRenderer = GetComponent<MeshRenderer>();
            surface = pearlRenderer.sharedMaterial;
            properties ??= new MaterialPropertyBlock();
            stage = startingStage;
            elapsed = 0f;
            if (!gameObject.scene.IsValid()) return;

#if UNITY_EDITOR
            if (!Application.IsPlaying(gameObject))
            {
                lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
                UnityEditor.EditorApplication.update += PreviewInEditor;
            }
#endif
            DrawFrame(0f);
        }

        void Update()
        {
            if (Application.IsPlaying(gameObject)) DrawFrame(Time.deltaTime);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PreviewInEditor;
#endif
            if (pearlRenderer != null)
            {
                pearlRenderer.forceRenderingOff = false;
                pearlRenderer.SetPropertyBlock(null);
            }
            burst = Burst.None;
            if (effectsRoot == null) return;
            effectsRoot.SetActive(false);
            if (Application.IsPlaying(gameObject)) Destroy(effectsRoot);
            else DestroyImmediate(effectsRoot);
            effectsRoot = null;
            aura = flash = ring = sparks = shards = null;
        }

#if UNITY_EDITOR
        public float EditorPreviewSpeed { get; set; } = 1f;
        double lastEditorTime;

        void PreviewInEditor()
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float step = Mathf.Clamp((float)(now - lastEditorTime), 0f, .05f);
            lastEditorTime = now;
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEditor.Selection.activeGameObject != gameObject && burst == Burst.None) return;
            DrawFrame(step * EditorPreviewSpeed);
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        public void SetStage(Stage value)
        {
            stage = value;
            burst = Burst.None;
            DrawFrame(0f);
        }

        public void PlayHit(Vector3 point, Vector3 normal)
        {
            if (stage == Stage.Shattered) return;
            StartBurst(Burst.Hit, point, normal);
        }

        public void PlayShatter(Vector3 point, Vector3 normal)
        {
            stageBeforeBurst = Stage.Fractured;
            stage = Stage.Shattered;
            StartBurst(Burst.Shatter, point, normal);
        }

        void StartBurst(Burst kind, Vector3 point, Vector3 normal)
        {
            burst = kind;
            burstAge = 0;
            impactPoint = point;
            float radius = Radius;
            var random = new System.Random(kind == Burst.Hit ? 381 : 924);
            bool final = kind == Burst.Shatter;
            FillPaths(sparkPaths, random, normal, radius, final ? 4.8f : 3.1f, final ? .90f : .68f, false);
            if (final)
            {
                FillPaths(shardPaths, random, normal, radius, 3.5f, 1.18f, true);
                int extraLargeStart = LargeShardCount + SmallShardCount;
                for (int i = LargeShardCount; i < extraLargeStart; i++)
                {
                    shardPaths[i].size *= .46f;
                    shardPaths[i].velocity *= 1.18f;
                }
                for (int i = extraLargeStart; i < shardPaths.Length; i++)
                {
                    shardPaths[i].size = radius * Pick(random, .34f, .46f);
                    shardPaths[i].velocity *= 1.26f;
                }
            }
            DrawFrame(0f);
        }

        static void FillPaths(Debris[] paths, System.Random random, Vector3 normal, float radius,
            float speed, float lifetime, bool fragment)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Vector3 direction = new Vector3(Pick(random, -1f, 1f), Pick(random, -.65f, 1f), Pick(random, -1f, 1f)).normalized;
                direction = (direction + normal * (fragment ? .24f : .82f)).normalized;
                paths[i] = new Debris
                {
                    velocity = direction * (radius * speed * Pick(random, .56f, 1.6f)),
                    spin = new Vector3(Pick(random, -490f, 490f), Pick(random, -490f, 490f), Pick(random, -490f, 490f)),
                    size = radius * (fragment ? Pick(random, .12f, .31f) : Pick(random, .045f, .135f)),
                    lifetime = lifetime * Pick(random, .52f, 1f),
                    hue = Pick(random, 0f, 1f)
                };
            }
        }

        static float Pick(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        float Radius
        {
            get
            {
                Vector3 size = pearlRenderer.bounds.extents;
                return Mathf.Max(.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            }
        }

        void DrawFrame(float step)
        {
            if (surface == null || !surface.HasProperty(EmissionColorId)) return;
            float duration = Mathf.Max(.1f, period);
            elapsed = (elapsed + step) % duration;

            bool final = burst == Burst.Shatter;
            if (burst != Burst.None)
            {
                burstAge += step;
                if (burstAge >= (final ? 1.22f : .72f))
                    burst = Burst.None;
            }

            bool windingUp = burst == Burst.Shatter && burstAge < .105f;
            pearlRenderer.forceRenderingOff = stage == Stage.Shattered && !windingUp;
            float radiance = 1f - .27f * Mathf.Cos(elapsed * 2f * Mathf.PI / duration);
            float boost = burst == Burst.Hit ? 4.1f * Falloff(burstAge, .18f, 2.15f) : 0f;
            if (windingUp) boost = Mathf.Lerp(1.25f, 6.4f, burstAge / .105f);
            if (!pearlRenderer.forceRenderingOff)
                DrawSurface(windingUp ? stageBeforeBurst : stage, radiance, boost);

            float radius = Radius;
            Vector3 center = pearlRenderer.bounds.center;
            if (!pearlRenderer.forceRenderingOff && auraMaterial != null)
            {
                if (aura == null) aura = CreateParticles("Aura", auraMaterial, 1);
                var tint = Color.Lerp(Ember, new Color(1f, .22f, .15f), radiance - .65f);
                tint.a = auraOpacity * (.68f + .25f * radiance);
                float rotation = 8f * Mathf.Sin(elapsed * 2f * Mathf.PI / duration);
                DrawCard(aura, center, radius * (2.94f + .17f * radiance), tint, rotation);
            }
            else Clear(aura);

            if (burst == Burst.None)
            {
                Clear(flash); Clear(ring); Clear(sparks); Clear(shards);
                return;
            }

            float age = final ? Mathf.Max(0f, burstAge - .075f) : burstAge;
            Vector3 origin = final ? center : impactPoint;
            DrawImpact(age, radius, origin, final);
        }

        void DrawSurface(Stage appearance, float radiance, float boost)
        {
            properties.Clear();
            int crackIndex = (int)appearance - 1;
            if (crackIndex >= 0 && crackIndex < cracks.Length)
            {
                if (cracks[crackIndex].albedo != null) properties.SetTexture(AlbedoId, cracks[crackIndex].albedo);
                if (cracks[crackIndex].emissive != null) properties.SetTexture(EmissionMapId, cracks[crackIndex].emissive);
            }

            float phase = elapsed * 2f * Mathf.PI / Mathf.Max(.1f, period);
            float drift = appearance == Stage.Intact ? 1f : .16f;
            Vector2 tiling = surface.GetTextureScale("_EmissiveColorMap");
            Vector2 offset = surface.GetTextureOffset("_EmissiveColorMap");
            properties.SetColor(EmissionColorId, surface.GetColor(EmissionColorId) * (radiance + boost));
            properties.SetVector(EmissionTransformId, new Vector4(tiling.x, tiling.y,
                offset.x + .012f * drift * Mathf.Sin(phase), offset.y + .004f * drift * Mathf.Sin(2f * phase)));
            pearlRenderer.SetPropertyBlock(properties);
        }

        void DrawImpact(float age, float radius, Vector3 origin, bool final)
        {
            float ringLife = final ? .56f : .27f;
            float flashLife = final ? .42f : .19f;
            bool visible = !final || burstAge >= .05f;
            Vector3 cardOrigin = final ? Vector3.Lerp(origin, impactPoint, .48f) : impactPoint;
            if (flashMaterial != null && visible && age < flashLife)
            {
                if (flash == null) flash = CreateParticles("Flash", flashMaterial, 1);
                Color tint = Color.Lerp(Gold, Ember, Mathf.Clamp01(age / flashLife) * .8f);
                tint.a = Falloff(age, flashLife, 2.2f);
                DrawCard(flash, cardOrigin, radius * (final ? 2.65f + age * 10f : .68f + age * 10f), tint, 18f - age * 90f);
            }
            else Clear(flash);

            if (ringMaterial != null && visible && age < ringLife)
            {
                if (ring == null) ring = CreateParticles("Shock ring", ringMaterial, 1);
                Color tint = Color.Lerp(Gold, Ember, age / ringLife);
                tint.a = (final ? .72f : .53f) * Falloff(age, ringLife, 1.4f);
                DrawCard(ring, cardOrigin, radius * ((final ? 1.4f : .3f) + age * (final ? 10f : 7f)), tint, age * -46f);
            }
            else Clear(ring);

            if (sparkMaterial != null && visible)
            {
                if (sparks == null) sparks = CreateParticles("Sparks", sparkMaterial, sparkParticles.Length, stretched: true);
                DrawDebris(sparks, sparkParticles, sparkPaths, final ? 16 : 12, age, cardOrigin, radius, false);
            }
            if (final && shardMesh != null && shardMaterial != null && burstAge >= .105f)
            {
                if (shards == null) shards = CreateParticles("Shards", shardMaterial, shardParticles.Length, mesh: shardMesh);
                DrawDebris(shards, shardParticles, shardPaths, shardParticles.Length, burstAge - .105f, origin, radius, true);
            }
            else Clear(shards);
        }

        static float Falloff(float age, float duration, float sharpness) =>
            Mathf.Pow(1f - Mathf.Clamp01(age / duration), sharpness);

        ParticleSystem CreateParticles(string label, Material material, int count, Mesh mesh = null, bool stretched = false)
        {
            if (effectsRoot == null)
            {
                effectsRoot = new GameObject("Pearl VFX Preview") { hideFlags = HideFlags.HideAndDontSave };
                effectsRoot.transform.SetParent(transform, false);
            }

            var owner = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
            owner.transform.SetParent(effectsRoot.transform, false);
            var system = owner.AddComponent<ParticleSystem>();
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = count;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.maxParticleSize = 1f;
            renderer.alignment = ParticleSystemRenderSpace.View;
            if (mesh != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.alignment = ParticleSystemRenderSpace.World;
                renderer.mesh = mesh;
            }
            else if (stretched)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = .10f;
                renderer.lengthScale = 2.15f;
            }

            system.Play(false);
            system.Pause(false);
            return system;
        }

        void DrawCard(ParticleSystem system, Vector3 point, float size, Color tint, float rotation)
        {
            card[0] = new ParticleSystem.Particle
            {
                position = point, startSize = size, rotation = rotation, startColor = tint,
                startLifetime = 2f, remainingLifetime = 1f, randomSeed = 1
            };
            system.SetParticles(card, 1);
        }

        static void DrawDebris(ParticleSystem system, ParticleSystem.Particle[] particles, Debris[] paths, int count,
            float age, Vector3 origin, float radius, bool fragment)
        {
            Vector3 gravity = Vector3.down * (fragment ? 6.3f : 2.25f) * radius;
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                Debris path = paths[i];
                if (age >= path.lifetime) continue;
                float progress = age / path.lifetime;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, progress);
                Color tint = Color.Lerp(Gold, Ember, progress * .76f + path.hue * .22f);
                tint.a = fade;
                particles[alive++] = new ParticleSystem.Particle
                {
                    position = origin + path.velocity * (age + .031f) + gravity * (.5f * age * age),
                    velocity = path.velocity + gravity * age,
                    rotation3D = path.spin * (age + .15f),
                    startSize = path.size * (fragment ? Mathf.Sqrt(fade) : 1f),
                    startColor = tint,
                    startLifetime = path.lifetime,
                    remainingLifetime = path.lifetime - age,
                    randomSeed = (uint)i + 1
                };
            }
            system.SetParticles(particles, alive);
        }

        static void Clear(ParticleSystem system)
        {
            if (system != null && system.particleCount != 0) system.Clear(false);
        }
    }
}
