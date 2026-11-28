using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    public static class AutumnArenaPolish
    {
        const string Location = "Assets/_Project/Art/Environment/Autumn/Location";
        const string Output = Location + "/Generated/ArenaPort";
        const string Vfx = Location + "/Generated/VFX";
        static readonly Vector3 Center = new(70, 0, 188);

        [MenuItem("ShadowTan/Setup/Art/Polish Autumn Arena")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != AutumnVfxSetup.ArtScene)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode.");
            var level = scene.GetRootGameObjects().Single(g => g.name == "[Level]").transform;
            var terrain = level.GetComponentInChildren<Terrain>();
            if (!terrain) throw new InvalidOperationException("Missing AutumnTerrain.");
            var props = level.Find("Greybox/Props");
            var root = AutumnVfxSetup.FindOrCreate(props, "Arena Polish");
            var north = level.Find("Atmosphere/BambooBackdrop_Black/Arena_North");
            if (north && north.TryGetComponent<Renderer>(out var panel))
            {
                Undo.RecordObject(panel, "Reveal arena depth");
                panel.enabled = false;
            }
            AutumnBaihuPort.EnsureOutput();

            BuildForest(root, terrain);
            BuildFloor(root, terrain);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void BuildForest(Transform parent, Terrain terrain)
        {
            var root = AutumnVfxSetup.FindOrCreate(parent, "Forest depth");
            var material = ForestMaterial();
            var random = new System.Random(186270);
            for (int band = 0; band < 2; band++)
            {
                bool middle = band == 0;
                float row = band * .5f;
                for (int sector = 0; sector < 3; sector++)
                {
                    var mesh = new MeshWriter(1);
                    float start = sector * 120f;
                    int count = middle ? 108 : 78;
                    for (int i = 0; i < count; i++)
                    {
                        float heading = start + (i + Between(random, .04f, .96f)) * 120f / count;
                        float radius = middle ? Between(random, 60f, 90f) : Between(random, 140f, 214f);
                        Vector3 foot = AtAngle(heading, radius);
                        if (foot.z < 158f && Mathf.Abs(foot.x - Center.x) < 23f) continue;
                        foot.y = OnGround(terrain, foot) - .12f;
                        float ridge = .77f + .23f * Mathf.PerlinNoise(heading * .055f, band * 3.3f);
                        float height = ridge * (middle ? Between(random, 17f, 38f) : Between(random, 23f, 44f));
                        float diameter = middle ? Between(random, .11f, .25f) : Between(random, .15f, .32f);
                        Vector3 lean = new Vector3(Between(random, -2.1f, 2.1f), height, Between(random, -2.1f, 2.1f));
                        mesh.Culm(foot, foot + lean, diameter, random.Next(8), row, .25f);
                        for (int cluster = 0; cluster < (middle ? 3 : 2); cluster++)
                        {
                            Vector3 tip = foot + lean * Between(random, .48f + cluster * .13f, .69f + cluster * .12f);
                            float width = middle ? Between(random, 2.2f, 5.0f) : Between(random, 3.1f, 6.7f);
                            float yaw = Between(random, 0f, 180f);
                            int tile = random.Next(4);
                            mesh.Leaves(tip, width, width * .54f, yaw, tile, row + .25f, .25f);
                            if (middle) mesh.Leaves(tip, width * .73f, width * .58f, yaw + 83f, tile, row + .25f, .25f);
                        }
                    }
                    string label = (middle ? "Forest mid " : "Forest far ") + sector.ToString("D2");
                    Install(root, label, mesh, new[] { material });
                }
            }

            var baseMesh = new MeshWriter(1);
            const int segments = 36;
            var rings = new[] { 96f, 158f, 198f, 246f };
            var opacity = new[] { .74f, .68f, .49f, 0f };
            for (int band = 0; band < rings.Length - 1; band++)
            for (int i = 0; i < segments; i++)
            {
                float a = 360f * i / segments, b = 360f * (i + 1) / segments;
                Vector3 innerA = AtAngle(a, rings[band]), innerB = AtAngle(b, rings[band]);
                Vector3 outerA = AtAngle(a, rings[band + 1]), outerB = AtAngle(b, rings[band + 1]);
                innerA.y = innerB.y = outerA.y = outerB.y = -.195f;
                baseMesh.FadeGround(innerA, innerB, outerA, outerB,
                    new Color(.35f, .275f, .14f, opacity[band]), opacity[band + 1]);
            }
            var source = SourceMaterial(Vfx + "/M_Autumn_Dust.mat");
            var foundation = Tint("Forest_floor", source, Color.white, "_Color");
            foundation.shader = source.shader;
            foundation.SetTexture("_MainTex", null);
            EditorUtility.SetDirty(foundation);
            Install(root, "Forest floor beyond terrain", baseMesh, new[] { foundation });
        }

        static Material ForestMaterial()
        {
            string texturePath = Output + "/T_ArenaForest.asset";
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!atlas)
            {
                const int width = 1024, row = 128;
                var culm = AssetDatabase.LoadAssetAtPath<Texture2D>(Location + "/Generated/Pass2/T_Autumn_CulmAtlas_Gold.png");
                var leaves = AssetDatabase.LoadAssetAtPath<Texture2D>(Location + "/Generated/Pass2/T_Autumn_LeafAtlas_Gold.png");
                if (!culm || !leaves) throw new InvalidOperationException("Missing warm bamboo atlas.");
                atlas = new Texture2D(width, row * 4, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
                var colors = new[] { new Color(.82f, .73f, .57f), new Color(.78f, .73f, .56f),
                    new Color(.73f, .69f, .56f), new Color(.70f, .66f, .52f) };
                var previous = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(width, row, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var read = new Texture2D(width, row, TextureFormat.RGBA32, false);
                try
                {
                    for (int i = 0; i < colors.Length; i++)
                    {
                        Graphics.Blit(i % 2 == 0 ? culm : leaves, rt);
                        RenderTexture.active = rt;
                        read.ReadPixels(new Rect(0, 0, width, row), 0, 0);
                        var pixels = read.GetPixels();
                        for (int j = 0; j < pixels.Length; j++)
                        {
                            float alpha = pixels[j].a;
                            pixels[j] *= colors[i];
                            pixels[j].a = alpha;
                        }
                        atlas.SetPixels(0, i * row, width, row, pixels);
                    }
                    atlas.Apply(true, true);
                    AssetDatabase.CreateAsset(atlas, texturePath);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.DestroyImmediate(read);
                }
            }
            string path = Output + "/M_ArenaForest.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("HDRP/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_UnlitColorMap", atlas);
            material.SetColor("_UnlitColor", Color.white);
            material.SetFloat("_AlphaCutoffEnable", 1);
            material.SetFloat("_AlphaCutoff", .46f);
            material.SetFloat("_DoubleSidedEnable", 1);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildFloor(Transform root, Terrain terrain)
        {
            var shapes = new[] { "Rounded", "Flat", "Long" }
                .Select(n => AssetDatabase.LoadAssetAtPath<Mesh>($"{Vfx}/Chip_{n}.asset")).ToArray();
            if (shapes.Any(m => !m)) throw new InvalidOperationException("Missing existing low-poly chip meshes.");
            var rock = SourceMaterial(Vfx + "/M_Autumn_StoneDebris.mat");
            var darkStone = Tint("Arena_Pebble_dark", rock, new Color(.26f, .22f, .16f));
            var lightStone = Tint("Arena_Pebble_warm", rock, new Color(.42f, .33f, .23f));
            var metal = SourceMaterial(Vfx + "/M_Autumn_MonumentChips.mat");
            var random = new System.Random(526188);
            var stones = new MeshWriter(3);
            for (int i = 0; i < 64; i++)
            {
                float a = AwayFromEntrance(Between(random, -180, 180));
                float r = 6f + 14f * Mathf.Sqrt((float)random.NextDouble());
                Vector3 p = AtAngle(a, r);
                p.y = OnGround(terrain, p) + .016f;
                var s = Vector3.one * Between(random, .11f, .34f);
                s.y *= Between(random, .45f, .85f);
                stones.Append(shapes[i % 3], p, Quaternion.Euler(0, a, 0), s, i % 4 == 0 ? 1 : 0);
            }

            var collisionRoot = AutumnVfxSetup.FindOrCreate(root, "Rim stone colliders");
            for (int i = 0; i < 10; i++)
            {
                float a = AwayFromEntrance(-162f + 36f * i + Between(random, -9f, 9f));
                Vector3 p = AtAngle(a, Between(random, 25.6f, 26.15f));
                p.y = OnGround(terrain, p) + .17f;
                var size = new Vector3(Between(random, .75f, 1.15f), Between(random, .58f, .99f), Between(random, .7f, 1f));
                var rotation = Quaternion.Euler(0, a, 0);
                stones.Append(shapes[i % 2], p, rotation, size, i % 3 == 0 ? 1 : 0);
                var t = AutumnVfxSetup.FindOrCreate(collisionRoot, $"Rim stone {i:00}");
                t.SetPositionAndRotation(p + Vector3.down * .035f, rotation);
                t.gameObject.layer = LayerMask.NameToLayer("Wall");
                var collider = AutumnVfxSetup.Component<BoxCollider>(t);
                collider.size = new Vector3(size.x * .66f, size.y * .50f, size.z * .61f);
            }
            for (int i = 0; i < 12; i++)
            {
                float a = Between(random, 0, 360);
                Vector3 p = AtAngle(a, Between(random, 3.1f, 4.75f));
                p.y = OnGround(terrain, p) + .025f;
                stones.Append(shapes[i % 3], p, Quaternion.Euler(0, a, 0), Vector3.one * Between(random, .055f, .14f), i < 7 ? 2 : 0);
            }
            Install(root, "Arena ground stones and fragments", stones, new[] { darkStone, lightStone, metal });

            var marks = new MeshWriter(1);
            var sandMaterial = Tint("Arena_Weathered_Sand", SourceMaterial(Vfx + "/M_Autumn_Dust.mat"), Color.white, "_Color");
            for (int i = 0; i < 26; i++)
            {
                Vector3 p = AtAngle(AwayFromEntrance(Between(random, -180, 180)), Between(random, 5.6f, 25.3f));
                Color tint = i % 3 == 0 ? new Color(.78f, .58f, .39f, .15f)
                    : new Color(.34f, .24f, .145f, .19f);
                Mark(marks, terrain, p, Between(random, 1.6f, 4.0f), Between(random, .7f, 2.25f), Between(random, 0f, 180f), tint);
            }
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = AtAngle(30f + 124f * i, 3.7f);
                Mark(marks, terrain, p, 2.25f, 1.18f, i * 41f, new Color(.24f, .25f, .135f, .21f));
            }
            for (int cluster = 0; cluster < 8; cluster++)
            {
                float a = AwayFromEntrance(Between(random, -180f, 180f));
                Vector3 p = AtAngle(a, Between(random, 18f, 24.4f));
                for (int i = 0; i < 3; i++)
                {
                    Vector3 step = p + Quaternion.Euler(0, a, 0) * new Vector3((i % 2 == 0 ? -.12f : .12f), 0, .3f * i);
                    Mark(marks, terrain, step, .13f, Between(random, .24f, .43f), a + 12f, new Color(.22f, .16f, .095f, .53f));
                }
            }
            Install(root, "Sand tones moss and tracks", marks, new[] { sandMaterial });

            var litter = new MeshWriter(1);
            var leafMat = SourceMaterial(Location + "/Materials/ForestBake_Leaves.mat");
            for (int i = 0; i < 84; i++)
            {
                float a = AwayFromEntrance(Between(random, -180, 180));
                Vector3 p = AtAngle(a, Between(random, 24.4f, 27f));
                p.y = OnGround(terrain, p) + .033f;
                litter.LeafOnGround(p, Between(random, .21f, .44f), Between(random, 0f, 360f), random.Next(4));
            }
            Install(root, "Arena rim leaf litter", litter, new[] { leafMat });

            var culms = new MeshWriter(1);
            for (int i = 0; i < 14; i++)
            {
                float a = AwayFromEntrance(Between(random, -180, 180));
                Vector3 p = AtAngle(a, Between(random, 24.6f, 27f));
                p.y = OnGround(terrain, p) + .049f;
                var direction = Quaternion.Euler(0, a + 55f, 0) * Vector3.forward;
                culms.Culm(p, p + direction * Between(random, .35f, .9f) + Vector3.up * .02f, .055f, random.Next(8));
            }
            Install(root, "Arena rim fallen bamboo", culms, new[] { SourceMaterial(Location + "/Materials/ForestBake_Culm.mat") });
        }

        static void Mark(MeshWriter mesh, Terrain terrain, Vector3 point, float width, float length, float yaw, Color tint)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 Corner(float x, float z)
            {
                Vector3 p = point + q * new Vector3(x * width, 0, z * length);
                p.y = OnGround(terrain, p) + .032f;
                return p;
            }
            mesh.GroundQuad(Corner(-.5f, -.5f), Corner(-.5f, .5f), Corner(.5f, -.5f), Corner(.5f, .5f), tint);
        }

        static Vector3 AtAngle(float angle, float radius)
        {
            float a = angle * Mathf.Deg2Rad;
            return Center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
        }

        static float AwayFromEntrance(float angle)
        {
            float relative = Mathf.DeltaAngle(-90f, angle);
            return Mathf.Abs(relative) < 14f ? -90f + Mathf.Sign(relative == 0 ? 1f : relative) * 16f : angle;
        }

        static float OnGround(Terrain terrain, Vector3 p)
        {
            Vector3 q = p - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return q.x < 0 || q.z < 0 || q.x > size.x || q.z > size.z
                ? -.21f : terrain.SampleHeight(p) + terrain.transform.position.y;
        }

        static float Between(System.Random r, float a, float b) => Mathf.Lerp(a, b, (float)r.NextDouble());

        static Material SourceMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            return material ? material : throw new InvalidOperationException($"Missing source {path}.");
        }

        static Material Tint(string name, Material source, Color tint, string property = "_BaseColor")
        {
            string path = $"{Output}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            material.SetColor(property, tint);
            if (material.shader.name == "HDRP/Lit") HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Install(Transform parent, string name, MeshWriter data, Material[] materials)
        {
            string path = $"{Output}/SM_{name.Replace(' ', '_')}.asset";
            var target = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!target) { target = new Mesh { name = name }; AssetDatabase.CreateAsset(target, path); }
            data.Write(target);
            EditorUtility.SetDirty(target);
            var t = AutumnVfxSetup.FindOrCreate(parent, name);
            var filter = AutumnVfxSetup.Component<MeshFilter>(t);
            var renderer = AutumnVfxSetup.Component<MeshRenderer>(t);
            filter.sharedMesh = target;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = !parent.name.Equals("Forest depth", StringComparison.Ordinal);
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        sealed class MeshWriter
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector2> uv = new();
            readonly List<Color> colors = new();
            readonly List<int>[] triangles;

            public MeshWriter(int slots) => triangles = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int slot, Vector2 from, Vector2 to, Color color)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(from); uv.Add(new Vector2(from.x, to.y)); uv.Add(new Vector2(to.x, from.y)); uv.Add(to);
                for (int j = 0; j < 4; j++) colors.Add(color);
                var t = triangles[slot];
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i + 2); t.Add(i + 1); t.Add(i + 3);
            }

            public void GroundQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
                => Quad(a, b, c, d, 0, Vector2.zero, Vector2.one, color);

            public void FadeGround(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color inner, float outer)
            {
                GroundQuad(a, b, c, d, inner);
                inner.a = outer;
                colors[colors.Count - 2] = inner;
                colors[colors.Count - 1] = inner;
            }

            public void Culm(Vector3 foot, Vector3 top, float width, int tile, float row = 0f, float height = 1f)
            {
                var axis = (top - foot).normalized;
                var right = Vector3.Cross(Mathf.Abs(axis.y) > .8f ? Vector3.forward : Vector3.up, axis).normalized * width * .5f;
                var up = Vector3.Cross(axis, right);
                var corners = new[] { -right + up, right + up, right - up, -right - up };
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 4];
                    Quad(foot + a, top + a * .67f, foot + b, top + b * .67f, 0,
                        new Vector2((tile + .025f) / 8f, row + height * .02f), new Vector2((tile + .965f) / 8f, row + height * .98f), Color.white);
                }
            }

            public void Leaves(Vector3 center, float width, float height, float yaw, int tile, float row, float rowHeight)
            {
                Vector3 across = Quaternion.Euler(0, yaw, 0) * Vector3.right * width * .5f;
                Vector3 vertical = Vector3.up * height * .5f;
                Quad(center - across - vertical, center - across + vertical, center + across - vertical, center + across + vertical,
                    0, new Vector2((tile + .012f) * .25f, row + rowHeight * .025f),
                    new Vector2((tile + .988f) * .25f, row + rowHeight * .975f), Color.white);
            }

            public void LeafOnGround(Vector3 center, float size, float yaw, int tile)
            {
                var rotation = Quaternion.Euler(0, yaw, 0);
                Vector3 across = rotation * Vector3.right * size;
                Vector3 vertical = rotation * Vector3.forward * size * .55f;
                Quad(center - across - vertical, center - across + vertical, center + across - vertical, center + across + vertical,
                    0, new Vector2((tile + .012f) * .25f, .012f), new Vector2((tile + .988f) * .25f, .988f), Color.white);
            }

            public void Append(Mesh shape, Vector3 center, Quaternion rotation, Vector3 scale, int slot)
            {
                int i = vertices.Count;
                foreach (var v in shape.vertices)
                {
                    vertices.Add(center + rotation * Vector3.Scale(v, scale));
                    uv.Add(Vector2.zero);
                    colors.Add(Color.white);
                }
                foreach (int index in shape.triangles) triangles[slot].Add(i + index);
            }

            public void Write(Mesh mesh)
            {
                mesh.Clear();
                mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetColors(colors);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }
    }
}
