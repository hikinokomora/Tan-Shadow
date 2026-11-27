using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XuanwuTools
{
    internal static partial class RealWinterBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/real_winter.unity";
        private const string Folder = "Assets/XuanwuGenerated/RealWinter";
        private const string Pack = "Assets/TerrainSampleAssets/";
        private const string Fresh = "Assets/XuanwuGenerated/Fresh44/";
        private const int Resolution = 1025;
        private static readonly Vector2 HeartCenter = new Vector2(-496.705f, 344.270f);
        private static readonly Vector2 ShellCenter = new Vector2(-506f, 370f);
        private static readonly Vector2 ShellRadii = new Vector2(219f, 215f);

        private readonly struct Station
        {
            public readonly Vector2 Point;
            public readonly float Height;
            public readonly float Width;
            public Station(float x, float z, float height, float width)
            {
                Point = new Vector2(x, z);
                Height = height;
                Width = width;
            }
        }

        // Width is a half width. The wide zone and the head bowl merge into narrower passages.
        private static readonly Station[] PawExit = {
            new(-265, 96, 41.7f, 13), new(-237, 127, 41.5f, 17), new(-205, 154, 43, 21),
            new(-180, 178, 47, 21), new(-152, 202, 52, 22), new(-124, 230, 59, 25)
        };
        private static readonly Station[] Slope = {
            new(-124, 230, 59, 26), new(-112, 285, 69, 46), new(-123, 350, 86, 55),
            new(-137, 418, 108, 55), new(-150, 486, 130, 50), new(-152, 542, 151, 44),
            new(-172, 592, 178, 34), new(-219, 636, 200, 22)
        };
        private static readonly Station[] HeadAndNeck = {
            new(-219, 636, 200, 22), new(-254, 654, 188, 30), new(-301, 659, 184, 37),
            new(-341, 637, 193, 34), new(-367, 607, 208, 25), new(-389, 576, 223, 22),
            new(-424, 540, 247, 23), new(-454, 488, 273, 25), new(-477, 434, 298, 27),
            new(-492, 390, 323, 23), new(-497, 362, 333, 18)
        };

        private static Station[] ravine;
        private static Color[] toolNoise;
        private static TerrainLayer[] layers;
        private static Material snowMesh, rockMesh, bark, needles;

        [MenuItem("Tools/Xuanwu/Build real_winter")]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            if (AssetDatabase.IsValidFolder(Folder))
                throw new InvalidOperationException("RealWinter assets already exist. Open the saved scene to edit them.");

            var roots = scene.GetRootGameObjects();
            var terrains = roots.Select(o => o.GetComponent<Terrain>()).Where(t => t).ToArray();
            var main = terrains.Single(t => t.name == "Terrain");
            var heart = roots.Single(o => o.name == "Heart");
            if (terrains.Length != 3 || terrains.Any(t => !t.terrainData))
                throw new InvalidOperationException("Expected three usable Terrain tiles copied from Winter_new.");
            ravine = ReadRavine();
            toolNoise = ReadTerrainToolsNoise();
            if (!AssetDatabase.CreateFolder("Assets/XuanwuGenerated", "RealWinter").Any())
                throw new InvalidOperationException("Could not create RealWinter output folder.");
            CreateMaterials();

            for (int i = 0; i < terrains.Length; i++)
                Sculpt(terrains[i], terrains[i] == main, i);
            AddShell(main, heart.transform, scene);
            foreach (Terrain terrain in terrains) Paint(terrain);
            AddLandscape(main, scene);
            ConfigureScene(roots, scene);
            FinishRelief(terrains, main);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save real_winter.");
        }

        private static Station[] ReadRavine()
        {
            const string path = "XuanwuReports/Fresh44/ravine-centerline.csv";
            if (!File.Exists(path)) throw new IOException("Measured ravine centerline is missing: " + path);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var stations = new List<Station> { new(-528.1f, -37.7f, 56.4f, 5f) };
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i += 16)
            {
                string[] fields = lines[i].Split(',');
                if (fields.Length < 4) continue;
                float along = float.Parse(fields[0], culture);
                float halfWidth = Mathf.Lerp(4.5f, 13f, Mathf.InverseLerp(265, 334, along));
                stations.Add(new Station(float.Parse(fields[1], culture), float.Parse(fields[3], culture),
                    float.Parse(fields[2], culture), halfWidth));
            }
            return stations.ToArray();
        }

        private static Color[] ReadTerrainToolsNoise()
        {
            Type settings = Type.GetType("UnityEditor.TerrainTools.NoiseSettings, Unity.TerrainTools.Editor", true);
            Type noise = Type.GetType("UnityEditor.TerrainTools.NoiseUtils, Unity.TerrainTools.Editor", true);
            var configuration = ScriptableObject.CreateInstance(settings);
            var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                settings.GetMethod("Reset").Invoke(configuration, null);
                noise.GetMethod("Blit2D", new[] { settings, typeof(RenderTexture) }).Invoke(null, new object[] { configuration, target });
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                return texture.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(configuration);
            }
        }

        private static TerrainLayer Layer(string source, string name, float tile, Color tint, float smoothness)
        {
            var original = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pack + "TerrainLayers/" + source + "_TerrainLayer.terrainlayer");
            if (!original) throw new IOException("Terrain Asset Pack layer missing: " + source);
            var layer = Object.Instantiate(original);
            layer.name = name;
            layer.tileSize = Vector2.one * tile;
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1);
            layer.smoothness = smoothness;
            layer.normalScale = source == "Snow" ? .42f : .85f;
            AssetDatabase.CreateAsset(layer, Folder + "/" + name + ".terrainlayer");
            return layer;
        }

        private static Material Lit(string name, Color color, float smoothness, TerrainLayer layer = null)
        {
            var material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (layer)
            {
                material.SetTexture("_BaseColorMap", layer.diffuseTexture);
                material.SetTexture("_NormalMap", layer.normalMapTexture);
                material.SetFloat("_NormalScale", layer.normalScale);
                material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            }
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
            return material;
        }

        private static void CreateMaterials()
        {
            layers = new[] {
                Layer("Snow", "Alpine powder", 9, new Color(.88f, .94f, 1), .19f),
                Layer("Snow", "Wind packed snow", 17, new Color(.66f, .76f, .86f), .32f),
                Layer("Rock", "Blue slate", 16, new Color(.32f, .41f, .52f), .18f)
            };
            snowMesh = Lit("Shell and drift snow", new Color(.86f,.92f,.99f), .24f, layers[0]);
            rockMesh = Lit("Exposed shell slate", new Color(.33f,.40f,.48f), .19f, layers[2]);
            bark = Lit("Cold timber", new Color(.126f,.091f,.077f), .17f);
            needles = Lit("Alpine needles", new Color(.12f,.23f,.21f), .17f);
        }

        private static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a,b,value));

        private static float TextureNoise(float x, float z)
        {
            float u = Mathf.Repeat(x / 1670 + .61f, 1) * 255;
            float v = Mathf.Repeat(z / 1670 + .35f, 1) * 255;
            int col = Mathf.Min(254, (int)u), row = Mathf.Min(254, (int)v);
            float a = Mathf.Lerp(toolNoise[row * 256 + col].r, toolNoise[row * 256 + col + 1].r, u - col);
            float b = Mathf.Lerp(toolNoise[(row + 1) * 256 + col].r, toolNoise[(row + 1) * 256 + col + 1].r, u - col);
            return Mathf.Clamp(Mathf.Lerp(a, b, v - row), -1, 1);
        }

        private static float Route(Vector2 point, Station[] line, out float height, out float width, out float right)
        {
            float best = float.PositiveInfinity;
            height = 0; width = 0; right = 0;
            for (int i = 1; i < line.Length; i++)
            {
                Station a = line[i-1], b = line[i];
                Vector2 step = b.Point - a.Point;
                float f = Mathf.Clamp01(Vector2.Dot(point - a.Point, step) / step.sqrMagnitude);
                Vector2 d = point - (a.Point + step * f);
                if (d.sqrMagnitude >= best) continue;
                best = d.sqrMagnitude;
                height = Mathf.Lerp(a.Height, b.Height, f);
                width = Mathf.Lerp(a.Width, b.Width, f);
                right = Vector2.Dot(d, new Vector2(step.y, -step.x).normalized);
            }
            return Mathf.Sqrt(best);
        }

        private static float Grade(float y, Vector2 p, Station[] route, float shoulder, float crossfall)
        {
            float distance = Route(p, route, out float height, out float width, out float right);
            float grade = height - Mathf.Clamp(right,-width,width)*crossfall;
            return Mathf.Lerp(y, grade, 1 - Smooth(width*.5f, width+shoulder, distance));
        }

        private static float ShapeMain(float y, Vector2 p, float noise)
        {
            float distance = Route(p, ravine, out float height, out float width, out float right);
            if (p.y > -35) y = Mathf.Lerp(y,height,1-Smooth(width*.65f,width+8,distance));
            y = Grade(y,p,PawExit,14,.025f);
            y = Grade(y,p,Slope,26,.21f);
            // Keep the cliff on the right of the ascent. It opens into the lower alpine basin.
            if (p.y > 248 && p.y < 567 && p.x > -104)
            {
                float edge = 1 - Smooth(21, 64, p.x + 104);
                float fade = Smooth(248,285,p.y)*(1-Smooth(519,567,p.y));
                y -= fade * (1-edge) * (12+10*Smooth(285,480,p.y));
            }
            y = Grade(y,p,HeadAndNeck,18,.02f);
            return y + noise * Smooth(4,18,distance) * .52f;
        }

        private static void Sculpt(Terrain terrain, bool main, int index)
        {
            TerrainData original = terrain.terrainData;
            var data = Object.Instantiate(original);
            data.name = "Winter terrain " + index;
            AssetDatabase.CreateAsset(data, Folder + "/" + data.name + ".asset");
            terrain.terrainData = data;
            terrain.GetComponent<TerrainCollider>().terrainData = data;
            // The greybox has non-unit transform scale. Unity Terrain uses TerrainData.size;
            // normalize the copied transforms so render, sampling and colliders agree.
            terrain.transform.localScale = Vector3.one;
            data.heightmapResolution = Resolution;
            data.terrainLayers = layers;
            data.treeInstances = Array.Empty<TreeInstance>();
            Vector3 o = terrain.transform.position;
            Vector3 size = original.size;
            data.size = size;
            int oldRes = original.heightmapResolution;
            float[,] old = original.GetHeights(0,0,oldRes,oldRes);
            float[,] heights = new float[Resolution,Resolution];
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                float u = x / (Resolution-1f), v = z / (Resolution-1f);
                int bx = Mathf.Min(oldRes-2, (int)(u*(oldRes-1))), bz = Mathf.Min(oldRes-2, (int)(v*(oldRes-1)));
                float fx = u*(oldRes-1)-bx, fz = v*(oldRes-1)-bz;
                float y = Mathf.Lerp(Mathf.Lerp(old[bz,bx],old[bz,bx+1],fx),Mathf.Lerp(old[bz+1,bx],old[bz+1,bx+1],fx),fz)*size.y + o.y;
                var p = new Vector2(o.x+u*size.x,o.z+v*size.z);
                float n = TextureNoise(p.x,p.y);
                y = main ? ShapeMain(y,p,n) : y + n * .52f;
                // Identical border heights retain neighbouring tiles without a seam.
                if (x==0 || z==0 || x==Resolution-1 || z==Resolution-1)
                    y = Mathf.Lerp(Mathf.Lerp(old[bz,bx],old[bz,bx+1],fx),Mathf.Lerp(old[bz+1,bx],old[bz+1,bx+1],fx),fz)*size.y + o.y;
                heights[z,x] = Mathf.Clamp01((y-o.y)/size.y);
            }
            data.SetHeights(0,0,heights);
            terrain.heightmapPixelError = 3;
            terrain.Flush();
            AssetDatabase.SaveAssetIfDirty(data);
        }

        private static void Paint(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            int res = data.alphamapResolution;
            Vector3 o = terrain.transform.position, size = data.size;
            var alpha = new float[res,res,3];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = (x+.5f)/res, v = (z+.5f)/res;
                float wx = o.x+u*size.x, wz = o.z+v*size.z;
                float noise = TextureNoise(wx,wz);
                float steepness = data.GetSteepness(u,v);
                float rock = Smooth(26,59,steepness + noise*9) * .93f;
                float wind = Mathf.Lerp(.13f,.58f,Smooth(.25f,.77f,Mathf.PerlinNoise(wx*.018f+23,wz*.02f+13))) * (1-rock);
                alpha[z,x,0] = 1-rock-wind;
                alpha[z,x,1] = wind;
                alpha[z,x,2] = rock;
            }
            data.SetAlphamaps(0,0,alpha);
            AssetDatabase.SaveAssetIfDirty(data);
        }

        private static GameObject Root(Scene scene, string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static void AddShell(Terrain terrain, Transform heart, Scene scene)
        {
            TerrainData data = terrain.terrainData;
            Vector3 o = terrain.transform.position, size = data.size;
            int resolution = data.heightmapResolution;
            float dx = size.x/(resolution-1), dz = size.z/(resolution-1);
            float[,] original = data.GetHeights(0,0,resolution,resolution);
            bool[,] holes = data.GetHoles(0,0,data.holesResolution,data.holesResolution);
            Vector3 pivot = new Vector3(HeartCenter.x, terrain.SampleHeight(heart.position)+o.y, HeartCenter.y);
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var snow = new List<int>(); var slate = new List<int>();
            var mapped = new Dictionary<int,int>();
            int Vertex(int x, int z)
            {
                int key = z*resolution+x;
                if (mapped.TryGetValue(key, out int id)) return id;
                float wx = o.x+x*dx, wz = o.z+z*dz;
                float distance = Vector2.Distance(new Vector2(wx,wz),HeartCenter);
                float y = original[z,x] * size.y + o.y;
                y = Mathf.Lerp(pivot.y-6.5f,y,Smooth(6,18,distance));
                y += 1.3f * Smooth(10,17,distance) * (1-Smooth(18,25,distance));
                id = vertices.Count; mapped.Add(key,id);
                vertices.Add(new Vector3(wx,y,wz)-pivot);
                uv.Add(new Vector2(wx/10,wz/10));
                return id;
            }
            for (int z = 0; z < resolution-1; z++)
            for (int x = 0; x < resolution-1; x++)
            {
                Vector2 p = new Vector2(o.x+(x+.5f)*dx,o.z+(z+.5f)*dz);
                var q = (p-ShellCenter)/ShellRadii;
                float radius = .99f + .022f*Mathf.Sin(9*Mathf.Atan2(q.y,q.x));
                if (q.sqrMagnitude > radius*radius) continue;
                holes[z,x] = false;
                int a=Vertex(x,z), b=Vertex(x,z+1), c=Vertex(x+1,z+1), d=Vertex(x+1,z);
                float tilt = Vector3.Angle(Vector3.up,Vector3.Cross(vertices[b]-vertices[a],vertices[d]-vertices[a]));
                bool bare = tilt > 39f && q.sqrMagnitude > .77f;
                (bare ? slate : snow).AddRange(new[] {a,b,c,a,c,d});
            }
            var mesh = new Mesh { name="Snow shell and heart crater", indexFormat=IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.subMeshCount=2;
            mesh.SetTriangles(snow,0); mesh.SetTriangles(slate,1); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Folder+"/Snow shell.asset");
            var shell = Root(scene,"06 · Панцирь и кратер сердца");
            shell.transform.position = pivot;
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            shell.AddComponent<MeshRenderer>().sharedMaterials = new[] {snowMesh,rockMesh};
            shell.AddComponent<MeshCollider>().sharedMesh = mesh;
            data.SetHoles(0,0,holes);
            terrain.Flush();
            AssetDatabase.SaveAssetIfDirty(data);
            Bounds heartBounds = heart.GetComponentInChildren<Renderer>().bounds;
            heart.position += Vector3.up * (pivot.y-6.5f-heartBounds.min.y+.35f);
            heart.SetParent(shell.transform,true);
        }

        private static bool OnRoute(Vector2 p, float margin)
        {
            foreach (Station[] line in new[] { ravine, PawExit, Slope, HeadAndNeck })
            {
                float d = Route(p,line,out _,out float width,out _);
                if (d < width*.72f+margin) return true;
            }
            return ((p-ShellCenter)/ShellRadii).sqrMagnitude < 1.03f;
        }

        private static float Ground(Terrain terrain, Vector2 p) =>
            terrain.SampleHeight(new Vector3(p.x,0,p.y)) + terrain.transform.position.y;

        private static void AddLandscape(Terrain terrain, Scene scene)
        {
            var rocks = Root(scene,"02–05 · Пласты синего сланца");
            var vegetation = Root(scene,"02–05 · Редкие ели и зимний подлесок");
            var mountains = Root(scene,"Горные гребни за обрывом");
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(Fresh+"Rock Face 01 Fresh.prefab");
            var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(Fresh+"Boulder 01 Fresh.prefab");
            var bush = AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_A.prefab");
            if (!wall || !boulder || !bush) throw new IOException("Missing winter scene dressing assets.");
            var rng = new System.Random(4863);
            float R(float a,float b) => Mathf.Lerp(a,b,(float)rng.NextDouble());

            // Seat rock faces on the outside of measured ravine shoulders; preserve every cross-section.
            for (int i = 2; i < ravine.Length-2; i += 2)
            {
                Vector2 axis = (ravine[i+1].Point-ravine[i-1].Point).normalized;
                Vector2 right = new Vector2(axis.y,-axis.x);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 p = ravine[i].Point + right * (ravine[i].Width+R(10,17))*side;
                    if (OnRoute(p,7)) continue;
                    PlacePrefab(wall,rocks.transform,p,Ground(terrain,p)-R(4,9),
                        Quaternion.LookRotation(new Vector3(-right.x*side,0,-right.y*side))*Quaternion.Euler(0,R(-17,17),0),R(18,32));
                }
            }

            var bands = new (float a,float b,float c,float d,int count)[] {
                (-276,-195,137,254,5), (-109,-39,266,465,9), (-165,-67,462,596,8),
                (-251,-177,525,626,6), (-445,-345,647,720,7), (-333,-277,590,681,6)
            };
            Mesh[] trees = { Conifer(18), Conifer(57), Conifer(104) };
            foreach (var band in bands)
            {
                var planted = new List<Vector2>();
                for (int j = 0; j < band.count*5 && planted.Count < band.count; j++)
                {
                    Vector2 p = new Vector2(R(band.a,band.b),R(band.c,band.d));
                    if (OnRoute(p,7) || planted.Any(a => Vector2.Distance(a,p)<12)) continue;
                    Vector3 uv = new Vector3(p.x,0,p.y)-terrain.transform.position;
                    if (terrain.terrainData.GetSteepness(uv.x/terrain.terrainData.size.x,uv.z/terrain.terrainData.size.z)>36) continue;
                    float y = Ground(terrain,p);
                    if (j%4==0)
                    {
                        MeshObject("Снежная ель",trees[j%trees.Length],new[] {bark,needles,snowMesh},vegetation.transform,
                            new Vector3(p.x,y-.35f,p.y),Quaternion.Euler(0,R(0,360),0),R(13,25));
                    }
                    else if (j%3==0)
                        PlacePrefab(boulder,rocks.transform,p,y-R(.65f,1.6f),Quaternion.Euler(0,R(0,360),0),R(4,10));
                    else
                        PlacePrefab(bush,vegetation.transform,p,y-.12f,Quaternion.Euler(0,R(0,360),0),R(1.6f,3.5f));
                    planted.Add(p);
                }
            }
            AddMountainRange(mountains.transform,terrain.materialTemplate);
        }

        private static void PlacePrefab(GameObject prefab, Transform parent, Vector2 p, float ground, Quaternion rotation, float extent)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            obj.transform.SetPositionAndRotation(new Vector3(p.x,ground,p.y),rotation);
            Renderer[] all = obj.GetComponentsInChildren<Renderer>();
            if (all.Length == 0) return;
            Bounds bounds = all[0].bounds;
            foreach (Renderer renderer in all) bounds.Encapsulate(renderer.bounds);
            float maximum = Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            if (maximum > .001f) obj.transform.localScale *= extent/maximum;
            Bounds resized = all[0].bounds;
            foreach (Renderer renderer in all) resized.Encapsulate(renderer.bounds);
            obj.transform.position += new Vector3(p.x-resized.center.x,ground-resized.min.y,p.y-resized.center.z);
            foreach (Collider collider in obj.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        private sealed class MeshDraft
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector2> UV = new();
            public readonly List<int>[] Parts;
            public MeshDraft(int count) => Parts = Enumerable.Range(0,count).Select(_ => new List<int>()).ToArray();
            public int Point(Vector3 v)
            {
                int i = Vertices.Count;
                Vertices.Add(v);
                UV.Add(new Vector2(v.x+v.y*.17f,v.z+v.y*.33f));
                return i;
            }
            public void Triangle(int sub,int a,int b,int c)
            {
                Parts[sub].Add(a); Parts[sub].Add(b); Parts[sub].Add(c);
            }
            public Mesh Save(string name)
            {
                var mesh = new Mesh { name=name };
                mesh.SetVertices(Vertices); mesh.SetUVs(0,UV); mesh.subMeshCount=Parts.Length;
                for(int i=0;i<Parts.Length;i++) mesh.SetTriangles(Parts[i],i);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,Folder+"/"+name+".asset");
                return mesh;
            }
        }

        private static void Branch(MeshDraft draft, int part, Vector3 a, Vector3 b, float radius, float tip)
        {
            Vector3 forward = (b-a).normalized;
            Vector3 right = Vector3.Cross(forward,Mathf.Abs(forward.y)>.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(forward,right);
            int first = draft.Vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                float angle = i*Mathf.PI/3;
                Vector3 radial = right*Mathf.Cos(angle)+up*Mathf.Sin(angle);
                draft.Point(a+radial*radius);
                draft.Point(b+radial*tip);
            }
            for (int i = 0; i < 6; i++)
            {
                int x = first+i*2, y = first+(i+1)%6*2;
                draft.Triangle(part,x,y,y+1); draft.Triangle(part,x,y+1,x+1);
            }
        }

        private static Mesh Conifer(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a,float b) => Mathf.Lerp(a,b,(float)rng.NextDouble());
            var d = new MeshDraft(3);
            Branch(d,0,Vector3.zero,Vector3.up,.026f,.003f);
            for(int layer=0;layer<13;layer++)
            {
                float h = .18f+layer*.056f;
                float sweep = (1-h)*R(.27f,.39f);
                int branches = h<.66f ? 9 : 6;
                for(int k=0;k<branches;k++)
                {
                    float angle=(k+layer*.381f)*Mathf.PI*2/branches+R(-.15f,.15f);
                    Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    Vector3 across=Vector3.Cross(Vector3.up,radial);
                    Vector3 basePoint=Vector3.up*h;
                    Vector3 end=basePoint+radial*sweep*R(.8f,1.2f)+Vector3.up*R(-.025f,.045f);
                    Branch(d,0,basePoint,end,.008f,.001f);
                    for(int twig=0;twig<5;twig++)
                    {
                        float f=.19f+twig*.155f;
                        Vector3 mid=Vector3.Lerp(basePoint,end,f);
                        float breadth=sweep*(1-f)*R(.18f,.4f);
                        Vector3 front=end-mid;
                        int a=d.Point(mid-front*.16f), b=d.Point(mid+across*breadth+Vector3.down*.017f);
                        int c=d.Point(mid+front*.53f+Vector3.up*.025f), e=d.Point(mid-across*breadth+Vector3.down*.017f);
                        int ridge=d.Point(mid+Vector3.up*.034f);
                        d.Triangle(1,a,ridge,b); d.Triangle(1,b,ridge,c); d.Triangle(1,c,ridge,e); d.Triangle(1,e,ridge,a);
                        d.Triangle(1,a,b,e); d.Triangle(1,b,c,e);
                        if (R(0,1)>.34f)
                        {
                            int snowA=d.Point(Vector3.Lerp(d.Vertices[a],d.Vertices[ridge],.4f)+Vector3.up*.012f);
                            int snowB=d.Point(Vector3.Lerp(d.Vertices[b],d.Vertices[ridge],.24f)+Vector3.up*.012f);
                            int snowC=d.Point(Vector3.Lerp(d.Vertices[c],d.Vertices[ridge],.21f)+Vector3.up*.012f);
                            int snowE=d.Point(Vector3.Lerp(d.Vertices[e],d.Vertices[ridge],.22f)+Vector3.up*.012f);
                            int snowRidge=d.Point(d.Vertices[ridge]+Vector3.up*.016f);
                            d.Triangle(2,snowA,snowRidge,snowB); d.Triangle(2,snowB,snowRidge,snowC);
                            d.Triangle(2,snowC,snowRidge,snowE); d.Triangle(2,snowE,snowRidge,snowA);
                        }
                    }
                }
            }
            return d.Save("Alpine fir " + seed);
        }

        private static void MeshObject(string name, Mesh mesh, Material[] materials, Transform parent, Vector3 position, Quaternion rotation, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(position,rotation);
            go.transform.localScale = Vector3.one*scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=materials;
        }

        private static void AddMountainRange(Transform parent, Material terrainMaterial)
        {
            AddMountain(parent,"Ridge01",new Vector3(-28,-76,274),new Vector3(720,325,650),terrainMaterial);
            AddMountain(parent,"Mountain02",new Vector3(400,-76,295),new Vector3(960,555,920),terrainMaterial);
            AddMountain(parent,"Mountain08",new Vector3(-454,-55,791),new Vector3(940,460,814),terrainMaterial);
        }

        private static void AddMountain(Transform parent,string source,Vector3 position,Vector3 size,Material terrainMaterial)
        {
            const int resolution=257;
            var heightmap=AssetDatabase.LoadAssetAtPath<Texture2D>(Pack+"Textures/Heightmaps/"+source+"_Heightmap.tif");
            if (!heightmap) throw new IOException("Terrain Asset Pack heightmap missing: "+source);
            var target=RenderTexture.GetTemporary(resolution,resolution,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image=new Texture2D(resolution,resolution,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;
            Color[] pixels;
            try
            {
                Graphics.Blit(heightmap,target);
                RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,resolution,resolution),0,0);
                pixels=image.GetPixels();
            }
            finally
            {
                RenderTexture.active=previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
            float low=pixels.Min(p=>p.r), high=pixels.Max(p=>p.r);
            var heights=new float[resolution,resolution];
            for(int z=0;z<resolution;z++)
            for(int x=0;x<resolution;x++)
                heights[z,x]=Mathf.Pow(Mathf.InverseLerp(low,high,pixels[z*resolution+x].r),.84f);
            var data=new TerrainData { name="Alpine "+source,heightmapResolution=resolution,size=size,alphamapResolution=256,terrainLayers=layers };
            data.SetHeights(0,0,heights);
            AssetDatabase.CreateAsset(data,Folder+"/"+data.name+".asset");
            GameObject go=Terrain.CreateTerrainGameObject(data);
            go.name="Горный фон · "+source;
            go.transform.SetParent(parent,false);
            go.transform.position=position;
            var terrain=go.GetComponent<Terrain>();
            terrain.allowAutoConnect=false;
            terrain.materialTemplate=terrainMaterial;
            terrain.shadowCastingMode=ShadowCastingMode.Off;
            terrain.heightmapPixelError=6;
            Object.DestroyImmediate(go.GetComponent<TerrainCollider>());
            Paint(terrain);
        }

        private static readonly Station[] Entrance = {
            new(-548,-79,57.72f,5),new(-536,-54,57.74f,5),new(-524.7f,-30.8f,57.78f,5.8f),
            new(-520.5f,-22,55.8f,6),new(-514.3993f,-9.91161f,53.10f,6)
        };

        private static void FinishRelief(Terrain[] terrains,Terrain main,bool softenCliffs=true)
        {
            foreach(Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;
                int n=data.heightmapResolution;
                float[,] heights=data.GetHeights(0,0,n,n);
                Vector3 o=terrain.transform.position,size=data.size;
                for(int z=0;z<n;z++)
                {
                    float wz=o.z+z*size.z/(n-1);
                    if(wz< -93 || wz>3) continue;
                    for(int x=0;x<n;x++)
                    {
                        float wx=o.x+x*size.x/(n-1);
                        if(wx< -565 || wx> -496) continue;
                        Vector2 p=new Vector2(wx,wz);
                        float distance=Route(p,Entrance,out float y,out float width,out _);
                        if(distance>=width+6) continue;
                        float previous=heights[z,x]*size.y+o.y;
                        float blend=1-Smooth(width,width+6,distance);
                        blend*=Smooth(-93,-79,wz)*(1-Smooth(-13,3,wz));
                        heights[z,x]=(Mathf.Lerp(previous,y,blend)-o.y)/size.y;
                    }
                }
                if(softenCliffs && terrain==main) SoftCliffFaces(heights,n,o,size);
                data.SetHeights(0,0,heights);
                terrain.Flush();
                Paint(terrain);
            }
        }

        [MenuItem("Tools/Xuanwu/Align real_winter entrance")]
        public static void AlignEntrance()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            var terrains=scene.GetRootGameObjects().Select(o=>o.GetComponent<Terrain>()).Where(t=>t).ToArray();
            if(terrains.Length!=3 || terrains.Any(t=>!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(Folder+"/")))
                throw new InvalidOperationException("Expected three dedicated real_winter terrain assets.");
            toolNoise=ReadTerrainToolsNoise();
            FinishRelief(terrains,terrains.Single(t=>t.name=="Terrain"),false);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the entrance.");
        }

        private static void SoftCliffFaces(float[,] heights,int n,Vector3 origin,Vector3 size)
        {
            var before=(float[,])heights.Clone();
            for(int z=5;z<n-5;z++)
            for(int x=5;x<n-5;x++)
            {
                var p=new Vector2(origin.x+x*size.x/(n-1),origin.z+z*size.z/(n-1));
                if(p.x <= -755 || p.x>= -16 || p.y< -4 || p.y>785 || OnRoute(p,6)) continue;
                if(((p-ShellCenter)/ShellRadii).sqrMagnitude<1.12f) continue;
                float four=(before[z-4,x]+before[z+4,x]+before[z,x-4]+before[z,x+4])*.25f;
                float diagonal=(before[z-3,x-3]+before[z+3,x-3]+before[z-3,x+3]+before[z+3,x+3])*.25f;
                heights[z,x]=Mathf.Lerp(before[z,x],(four+diagonal)*.5f,.64f);
            }
        }

        [MenuItem("Tools/Xuanwu/Finish real_winter terrain")]
        public static void FinishExisting()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            var roots=scene.GetRootGameObjects();
            var terrains=roots.Select(o=>o.GetComponent<Terrain>()).Where(t=>t).ToArray();
            if(terrains.Length!=3 || terrains.Any(t=>!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(Folder+"/")))
                throw new InvalidOperationException("Expected three dedicated real_winter terrain assets.");
            var main=terrains.Single(t=>t.name=="Terrain");
            var skyline=roots.Single(o=>o.name=="Горные гребни за обрывом").transform;
            if(skyline.GetComponentInChildren<Terrain>()) throw new InvalidOperationException("Terrain refinement was already applied.");
            layers=main.terrainData.terrainLayers;
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            var prior= new List<GameObject>();
            foreach(Transform child in skyline) prior.Add(child.gameObject);
            AddMountainRange(skyline,main.materialTemplate);
            foreach(GameObject child in prior) Object.DestroyImmediate(child);
            FinishRelief(terrains,main);

            // The generation pass used an exposed material inside the crater. Cover that ring with snow.
            var shell=roots.Single(o=>o.name.StartsWith("06 · ")).GetComponent<MeshFilter>().sharedMesh;
            var shellVertices=shell.vertices;
            var cap=new List<int>(shell.GetTriangles(0));
            var stone=new List<int>();
            int[] originalStone=shell.GetTriangles(1);
            for(int i=0;i<originalStone.Length;i+=3)
            {
                Vector3 centroid=(shellVertices[originalStone[i]]+shellVertices[originalStone[i+1]]+shellVertices[originalStone[i+2]])/3;
                var target=centroid.x*centroid.x+centroid.z*centroid.z<24*24 ? cap : stone;
                target.Add(originalStone[i]); target.Add(originalStone[i+1]); target.Add(originalStone[i+2]);
            }
            shell.SetTriangles(cap,0); shell.SetTriangles(stone,1);
            EditorUtility.SetDirty(shell);
            AssetDatabase.SaveAssetIfDirty(shell);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save terrain refinement.");
            foreach(int seed in new[] {18,42,103}) AssetDatabase.DeleteAsset(Folder+"/Alpine ridge "+seed+".asset");
        }

        private static void ConfigureScene(GameObject[] roots,Scene scene)
        {
            GameObject tunnel=roots.Single(o=>o.name=="GameObject");
            GameObject player=roots.Single(o=>o.name=="Player");
            GameObject debugSpawn=roots.Single(o=>o.name=="Player (1)");
            Camera camera=roots.Single(o=>o.name=="Main Camera").GetComponent<Camera>();
            Bounds floor=tunnel.transform.Find("Cube").GetComponent<Renderer>().bounds;
            Vector3 exit=new Vector3(-528.1f,floor.max.y,-37.7f);
            Vector3 axis=exit-floor.center; axis.y=0; axis.Normalize();
            Vector3 spawn=floor.center-axis*21;
            spawn.y=floor.max.y+.15f;
            player.transform.SetPositionAndRotation(spawn,Quaternion.LookRotation(axis));
            debugSpawn.SetActive(false);
            tunnel.name="01 · Стартовый тоннель";
            var cameraTarget=new GameObject("Winter camera target").transform;
            cameraTarget.SetParent(player.transform,false);
            cameraTarget.localPosition=Vector3.up*(1.55f-1.55f/player.transform.lossyScale.y);
            camera.farClipPlane=1600;
            camera.transform.SetPositionAndRotation(spawn+Vector3.up*7-axis*6,Quaternion.LookRotation(axis));
            camera.gameObject.AddComponent<TanShadow.Spring.SpringFollowCamera>().player=cameraTarget;
            var controller=player.GetComponent<TanShadow.Player.PlayerController>();
            if (controller) controller.cameraTransform=camera.transform;

            var volume=roots.Single(o=>o.name=="Daylight Volume").GetComponent<Volume>();
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name="Real winter daylight";
            AssetDatabase.CreateAsset(profile,Folder+"/Real winter daylight.asset");
            foreach (VolumeComponent original in volume.sharedProfile.components)
            {
                VolumeComponent copy=Object.Instantiate(original);
                copy.name=original.name;
                AssetDatabase.AddObjectToAsset(copy,profile);
                profile.components.Add(copy);
            }
            if (profile.TryGet(out Fog fog))
            {
                fog.enabled.Override(true);
                fog.meanFreePath.Override(1040f);
                fog.maxFogDistance.Override(1350f);
            }
            if (profile.TryGet(out Exposure exposure)) exposure.fixedExposure.Override(13.25f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            volume.sharedProfile=profile;

            // Keep the user's dirty greybox loaded. Its visibility is changed only in Scene View.
            for (int i=0;i<SceneManager.sceneCount;i++)
            {
                Scene other=SceneManager.GetSceneAt(i);
                if (other==scene || other.path!="Assets/_Game/Scenes/Winter_new.unity") continue;
                SceneVisibilityManager.instance.Hide(other.GetRootGameObjects(),true);
            }
            Selection.activeObject=scene.GetRootGameObjects().Single(o=>o.name=="Terrain");
            SceneView.lastActiveSceneView?.LookAt(new Vector3(-411,190,364),Quaternion.Euler(62,-20,0),625,false);
        }
    }
}
