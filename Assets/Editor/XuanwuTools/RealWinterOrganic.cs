using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XuanwuTools
{
    internal static partial class RealWinterBuilder
    {
        private const string Organic = Folder + "/Organic";
        private const int StampResolution = 129;
        private static float[][] heightStamps;

        private readonly struct MountainStamp
        {
            public readonly Vector2 Center, Size;
            public readonly float Height, Angle;
            public readonly int Map;

            public MountainStamp(int map, float x, float z, float width, float depth, float height, float angle)
            {
                Map=map; Center=new Vector2(x,z); Size=new Vector2(width,depth); Height=height; Angle=angle*Mathf.Deg2Rad;
            }

            public float Evaluate(Vector2 p)
            {
                Vector2 d=p-Center;
                if (d.sqrMagnitude>Size.sqrMagnitude*.25f) return 0;
                float s=Mathf.Sin(Angle), c=Mathf.Cos(Angle);
                Vector2 uv=new Vector2((c*d.x+s*d.y)/Size.x,(-s*d.x+c*d.y)/Size.y)+Vector2.one*.5f;
                if (uv.x<=0 || uv.x>=1 || uv.y<=0 || uv.y>=1) return 0;
                float x=uv.x*(StampResolution-1), z=uv.y*(StampResolution-1);
                int ix=(int)x, iz=(int)z;
                float[] h=heightStamps[Map];
                float value=Mathf.Lerp(Mathf.Lerp(h[iz*StampResolution+ix],h[iz*StampResolution+ix+1],x-ix),
                    Mathf.Lerp(h[(iz+1)*StampResolution+ix],h[(iz+1)*StampResolution+ix+1],x-ix),z-iz);
                float border=Mathf.Min(Mathf.Min(uv.x,1-uv.x),Mathf.Min(uv.y,1-uv.y));
                return Mathf.Pow(value,.93f)*Height*Smooth(.01f,.19f,border);
            }
        }

        // World-space composition. The three 1000 m tiles sample the same field, including their shared edges.
        private static readonly MountainStamp[] Masses={
            new(1,-720,52,455,480,214,-15), new(0,-532,166,345,355,225,40),
            new(2,-248,274,307,625,273,20), new(1,-264,413,275,480,237,10),
            new(0,-495,702,426,391,297,20), new(1,-319,790,464,393,288,-24),
            new(0,-674,495,530,510,281,30), new(2,-689,731,510,550,333,-25),
            new(2,-363,742,490,300,232,-15), new(0,-308,-120,556,445,184,19),
            new(1,-702,-289,540,500,212,-5), new(2,61,642,426,820,126,42),
            new(1,372,550,725,640,188,-15)
        };

        [MenuItem("Tools/Xuanwu/Replace real_winter with organic terrain and turtle")]
        public static string RebuildOrganic()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("real_winter must be the active edit scene.");
            if(AssetDatabase.IsValidFolder(Organic)) throw new InvalidOperationException("The organic revision already exists.");
            GameObject[] roots=scene.GetRootGameObjects();
            Terrain[] terrain=roots.Select(x=>x.GetComponent<Terrain>()).Where(x=>x).ToArray();
            if(terrain.Length!=3 || terrain.Any(t=>!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(Folder+"/")))
                throw new InvalidOperationException("Expected three dedicated real_winter terrain tiles.");
            Terrain main=terrain.Single(t=>t.name=="Terrain");
            GameObject oldShell=roots.Single(x=>x.name.StartsWith("06 · ",StringComparison.Ordinal));
            Transform heart=oldShell.transform.Find("Heart");
            if(!heart) throw new InvalidOperationException("The original heart anchor is missing.");
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            heightStamps=new[]{ReadStamp("Mountain02"),ReadStamp("Mountain08"),ReadStamp("Ridge01")};
            layers=main.terrainData.terrainLayers;
            snowMesh=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shell and drift snow.mat");
            rockMesh=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Exposed shell slate.mat");
            bark=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Cold timber.mat");
            needles=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Alpine needles.mat");
            AssetDatabase.CreateFolder(Folder,"Organic");

            for(int i=0;i<terrain.Length;i++) NewTile(terrain[i],i);
            CreateTurtleShell(main,heart,oldShell,scene);
            return FinishOrganic(scene,terrain,main);
        }

        public static string ResumeOrganic()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("real_winter must be the active edit scene.");
            var roots=scene.GetRootGameObjects();
            Terrain[] terrain=roots.Select(x=>x.GetComponent<Terrain>()).Where(x=>x).ToArray();
            if(terrain.Length!=3 || terrain.Any(x=>!AssetDatabase.GetAssetPath(x.terrainData).StartsWith(Organic+"/")))
                throw new InvalidOperationException("The new tiles are not all assigned to real_winter.");
            var shell=roots.FirstOrDefault(x=>x.name.StartsWith("06 · ",StringComparison.Ordinal));
            if(!shell || !shell.transform.Find("Heart") || !AssetDatabase.GetAssetPath(shell.GetComponent<MeshFilter>().sharedMesh).StartsWith(Organic+"/"))
                throw new InvalidOperationException("The replacement shell is not assigned to real_winter.");
            if(roots.Any(x=>x.name=="Сюаньу · Каменная черепаха"))
                throw new InvalidOperationException("The turtle assembly is already present.");
            Terrain main=terrain.Single(t=>t.name=="Terrain");
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            layers=main.terrainData.terrainLayers;
            snowMesh=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shell and drift snow.mat");
            rockMesh=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Exposed shell slate.mat");
            bark=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Cold timber.mat");
            needles=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Alpine needles.mat");
            return FinishOrganic(scene,terrain,main);
        }

        private static string FinishOrganic(Scene scene,Terrain[] terrain,Terrain main)
        {
            foreach(Terrain t in terrain) Paint(t);

            foreach(GameObject root in scene.GetRootGameObjects())
                if(root.name=="Paw" || root.name=="head" || root.name=="WinterSnake" ||
                    root.name.StartsWith("02–05 · ",StringComparison.Ordinal)) Object.DestroyImmediate(root);

            BuildTurtleBody(main,scene);
            string inventory=DressOrganic(terrain,scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the rebuilt real_winter.");
            EditorUtility.UnloadUnusedAssetsImmediate();
            return inventory;
        }

        private static float[] ReadStamp(string name)
        {
            Texture2D source=AssetDatabase.LoadAssetAtPath<Texture2D>(Pack+"Textures/Heightmaps/"+name+"_Heightmap.tif");
            if(!source) throw new IOException("Heightmap missing: "+name);
            var rt=RenderTexture.GetTemporary(StampResolution,StampResolution,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var sample=new Texture2D(StampResolution,StampResolution,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;
            try
            {
                Graphics.Blit(source,rt);
                RenderTexture.active=rt;
                sample.ReadPixels(new Rect(0,0,StampResolution,StampResolution),0,0);
                Color[] colors=sample.GetPixels();
                float min=colors.Min(p=>p.r), max=colors.Max(p=>p.r);
                return colors.Select(p=>Mathf.InverseLerp(min,max,p.r)).ToArray();
            }
            finally
            {
                RenderTexture.active=previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(sample);
            }
        }

        private static float Noise(float x,float z,float scale) => Mathf.PerlinNoise(x*scale+79.3f,z*scale+104.7f);

        private static float TurtleDome(Vector2 p)
        {
            Vector2 q=(p-ShellCenter)/ShellRadii;
            float r=q.magnitude;
            float baseY=186+31*Smooth(-1f,.85f,q.y);
            float shape=baseY+(340-baseY)*Mathf.Pow(Mathf.Clamp01(1-r*r),.81f);
            // Broad curved scute seams sit in the snow without raised hexagonal blocks.
            float angle=Mathf.Atan2(q.y,q.x);
            float bandA=Mathf.Abs(r-.49f-.018f*Mathf.Sin(angle*4));
            float bandB=Mathf.Abs(r-.77f-.022f*Mathf.Cos(angle*5));
            float seam=1-Smooth(.002f,.018f,Mathf.Min(bandA,bandB));
            return shape-seam*.9f+1.1f*(Noise(p.x,p.y,.048f)-.5f);
        }

        private static float OrganicHeight(Vector2 p, bool legacyCuts = false)
        {
            float roll=Noise(p.x,p.y,.0043f)*28+Noise(p.x,p.y,.014f)*10+Noise(p.x,p.y,.045f)*2.9f;
            float highest=0, other=0;
            foreach(var mass in Masses)
            {
                float value=mass.Evaluate(p);
                if(value>=highest) { other=highest; highest=value; }
                else other=Mathf.Max(other,value);
            }
            float y=6+roll+highest+other*.12f;
            float shellRadius=((p-ShellCenter)/ShellRadii).magnitude;
            if(shellRadius<1.23f) y=Mathf.Lerp(y,TurtleDome(p),1-Smooth(.98f,1.23f,shellRadius));
            if(legacyCuts ? p.x> -600 && p.x< -220 && p.y> -48 && p.y<150
                          : p.x> -607 && p.x< -181 && p.y> -109 && p.y<185)
            {
                float d=Route(p,ravine,out float grade,out float width,out _);
                if(d<width+54)
                {
                    float shoulder=width+2f*Mathf.Sin(p.x*.051f+p.y*.044f);
                    float relief=Smooth(shoulder,shoulder+31,d)*(30+57*Noise(p.x,p.y,.014f));
                    y=Mathf.Lerp(y,grade+relief,1-Smooth(width+35,width+54,d));
                }
            }
            if(legacyCuts ? p.x> -320 && p.x<20 && p.y>177 && p.y<687
                          : p.x> -328 && p.x<24 && p.y>133 && p.y<739)
            {
                float d=Route(p,Slope,out float grade,out float width,out float right);
                if(d<width+60)
                {
                    float shelf=grade-Mathf.Clamp(right,-width*.63f,width)*.20f;
                    if(right>width*.63f)
                        shelf-=Smooth(width*.63f,width+22+3*Mathf.Sin(p.y*.071f),right)*(30+grade*.24f);
                    else if(right< -width*.55f)
                        shelf+=Smooth(width*.55f,width+26,-right)*(42+75*Noise(p.x,p.y,.012f));
                    y=Mathf.Lerp(y,shelf,1-Smooth(width*.8f,width+60,d));
                }
            }
            if(legacyCuts ? p.x> -610 && p.x< -78 && p.y>70 && p.y<710
                          : p.x> -381 && p.x< -70 && p.y>45 && p.y<281)
                y=Grade(y,p,PawExit,18,.028f);
            if(legacyCuts ? p.x> -610 && p.x< -78 && p.y>70 && p.y<710
                          : p.x> -560 && p.x< -164 && p.y>318 && p.y<731)
            {
                y=Grade(y,p,HeadAndNeck,23,.018f);
            }
            if(legacyCuts ? p.x> -589 && p.x< -480 && p.y> -105 && p.y<21
                          : p.x> -575 && p.x< -494 && p.y> -104 && p.y<12)
                y=Grade(y,p,Entrance,11,0);
            float detail=(TextureNoise(p.x,p.y)-.5f)*.65f;
            return legacyCuts ? OpenTurtleChambers(y+detail,p,true) : OpenTurtleChambers(y,p)+detail;
        }

        private static float OpenTurtleChambers(float elevation,Vector2 p,bool legacyCuts=false)
        {
            float shell=((p-ShellCenter)/ShellRadii).sqrMagnitude;
            if(shell<1.025f) return elevation;
            float original=elevation;
            Vector2 head=(p-new Vector2(-357,674))/new Vector2(117,109);
            if(head.sqrMagnitude<1)
            {
                float basin=185+(p.x+357)*.046f+(p.y-674)*.044f+8*Noise(p.x,p.y,.021f);
                elevation=Mathf.Lerp(elevation,Mathf.Min(elevation,basin),1-Smooth(.61f,1,head.magnitude));
                float grade=Grade(elevation,p,HeadAndNeck,16,.018f);
                elevation=legacyCuts ? grade : Mathf.Lerp(elevation,grade,1-Smooth(.74f,1,head.magnitude));
            }
            Vector2 paw=(p-new Vector2(-299,106))/new Vector2(87,73);
            if(paw.sqrMagnitude<1)
            {
                float shelf=47+(p.y-106)*.085f+Noise(p.x,p.y,.038f)*8;
                elevation=Mathf.Lerp(elevation,Mathf.Min(elevation,shelf),1-Smooth(.52f,1,paw.magnitude));
                float d=Route(p,ravine,out float grade,out float width,out _);
                float local=legacyCuts ? 1 : 1-Smooth(.73f,1,paw.magnitude);
                if(d<width+7) elevation=Mathf.Lerp(elevation,grade,(1-Smooth(width*.64f,width+7,d))*local);
                elevation=Mathf.Lerp(elevation,Grade(elevation,p,PawExit,10,.028f),local);
            }
            return legacyCuts ? elevation : Mathf.Lerp(original,elevation,Smooth(1.025f,1.17f,shell));
        }

        private static void NewTile(Terrain terrain,int index)
        {
            // Deliberately take only bounds from the previous asset. Every height is generated above.
            Vector3 origin=terrain.transform.position, size=terrain.terrainData.size;
            var data=new TerrainData {
                name="New alpine terrain "+index,heightmapResolution=Resolution,alphamapResolution=512,
                baseMapResolution=512,size=size,terrainLayers=layers
            };
            var heights=new float[Resolution,Resolution];
            var xValues=new float[Resolution];
            float stepX=size.x/(Resolution-1), stepZ=size.z/(Resolution-1);
            for(int x=0;x<Resolution;x++) xValues[x]=origin.x+x*stepX;
            for(int z=0;z<Resolution;z++)
            for(int x=0;x<Resolution;x++)
                heights[z,x]=Mathf.Clamp01((OrganicHeight(new Vector2(xValues[x],origin.z+z*stepZ))-origin.y)/size.y);
            data.SetHeights(0,0,heights);
            AssetDatabase.CreateAsset(data,Organic+"/"+data.name+".asset");
            terrain.terrainData=data;
            terrain.GetComponent<TerrainCollider>().terrainData=data;
            terrain.Flush();
        }

        private static void CreateTurtleShell(Terrain terrain,Transform heart,GameObject oldShell,Scene scene)
        {
            // AddShell uses the newly sculpted heightfield and cuts matching Terrain holes.
            // Keep the old asset as a recoverable copy, using a different filename for the new mesh.
            string oldPath=Folder+"/Snow shell.asset";
            string backupPath=Folder+"/Previous snow shell.asset";
            if(AssetDatabase.LoadAssetAtPath<Mesh>(backupPath)) throw new IOException("Shell backup path is occupied.");
            string error=AssetDatabase.MoveAsset(oldPath,backupPath);
            if(error.Length!=0) throw new IOException(error);
            AddShell(terrain,heart,scene);
            error=AssetDatabase.MoveAsset(oldPath,Organic+"/Turtle shell and crater.asset");
            if(error.Length!=0) throw new IOException(error);
            Object.DestroyImmediate(oldShell);
            var shell=heart.parent.GetComponent<MeshCollider>();
            Mesh mesh=heart.parent.GetComponent<MeshFilter>().sharedMesh;
            shell.sharedMesh=null;
            shell.sharedMesh=mesh;
        }

        private static Material TurtleMaterial(string name,Color color,float shine)
        {
            var m=new Material(Shader.Find("HDRP/Lit")){name=name,enableInstancing=true};
            m.SetColor("_BaseColor",color);
            m.SetFloat("_Smoothness",shine);
            if(name=="Glacial turtle skin") ApplyTurtleSkinNormal(m);
            AssetDatabase.CreateAsset(m,Organic+"/"+name+".mat");
            return m;
        }

        private static void ApplyTurtleSkinNormal(Material skin)
        {
            TerrainLayer rock=AssetDatabase.LoadAssetAtPath<TerrainLayer>(Folder+"/Blue slate.terrainlayer");
            skin.SetTexture("_NormalMap",rock.normalMapTexture);
            skin.SetFloat("_NormalScale",.58f);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(skin);
        }

        private static Mesh OrganicMesh(string name,float relief, float snowLine, int seed, int rings=21, int sectors=36,
            bool continuousCap=false, Mesh replacement=null)
        {
            var meshData=new MeshDraft(2);
            int bottom=meshData.Point(Vector3.down), top=meshData.Point(Vector3.up);
            int seamRow=Mathf.Clamp(Mathf.RoundToInt(Mathf.Acos(-Mathf.Clamp(snowLine,-.99f,.99f))*rings/Mathf.PI),2,rings-2);
            for(int row=1;row<rings;row++)
            {
                for(int column=0;column<sectors;column++)
                {
                    float longitude=column/(float)sectors*Mathf.PI*2;
                    float latitude=row/(float)rings*Mathf.PI;
                    if(continuousCap)
                    {
                        float seam=snowLine+.082f*Mathf.Sin(longitude*3+.6f)+.044f*Mathf.Cos(longitude*7+seed);
                        float boundary=Mathf.Acos(-Mathf.Clamp(seam,-.97f,.97f));
                        latitude=row<=seamRow ? boundary*row/seamRow :
                            Mathf.Lerp(boundary,Mathf.PI,(row-seamRow)/(float)(rings-seamRow));
                    }
                    Vector3 p=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),-Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                    float f=1+relief*(Mathf.PerlinNoise((p.x+p.y*.3f)*3.6f+seed,p.z*4.7f+p.y*3.3f+35)-.5f);
                    meshData.Point(p*f);
                    meshData.UV[meshData.UV.Count-1]=new Vector2(column/(float)sectors,row/(float)rings)*4;
                }
            }
            void Face(int a,int b,int c,int row)
            {
                Vector3 m=(meshData.Vertices[a]+meshData.Vertices[b]+meshData.Vertices[c])/3;
                Vector3 normal=Vector3.Cross(meshData.Vertices[b]-meshData.Vertices[a],meshData.Vertices[c]-meshData.Vertices[a]);
                if(Vector3.Dot(m,normal)<0) (b,c)=(c,b);
                bool snow=continuousCap ? row>=seamRow : m.y>snowLine+.17f*Noise(m.x*30+seed,m.z*30,.45f);
                int material=snow ? 1:0;
                meshData.Triangle(material,a,b,c);
            }
            for(int c=0;c<sectors;c++)
            {
                int next=(c+1)%sectors;
                Face(bottom,2+c,2+next,0);
                for(int row=1;row<rings-1;row++)
                {
                    int a=2+(row-1)*sectors+c,b=2+(row-1)*sectors+next;
                    Face(a,b,a+sectors,row); Face(b,b+sectors,a+sectors,row);
                }
                Face(top,2+(rings-2)*sectors+c,2+(rings-2)*sectors+next,rings-1);
            }
            if(replacement)
            {
                replacement.Clear();
                replacement.SetVertices(meshData.Vertices);
                replacement.SetUVs(0,meshData.UV);
                replacement.subMeshCount=2;
                for(int part=0;part<2;part++) replacement.SetTriangles(meshData.Parts[part],part);
                replacement.RecalculateNormals();
                replacement.RecalculateTangents();
                replacement.RecalculateBounds();
                EditorUtility.SetDirty(replacement);
                AssetDatabase.SaveAssetIfDirty(replacement);
                return replacement;
            }
            var mesh=meshData.Save(name);
            string error=AssetDatabase.MoveAsset(Folder+"/"+name+".asset",Organic+"/"+name+".asset");
            if(error.Length!=0) throw new IOException(error);
            return mesh;
        }

        private static Transform BodyPart(Transform parent,string name,Mesh shape,Vector3 center,Vector3 radius,Material skin,Material cap)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.transform.localPosition=center;
            go.transform.localScale=radius;
            go.AddComponent<MeshFilter>().sharedMesh=shape;
            go.AddComponent<MeshRenderer>().sharedMaterials=new[]{skin,cap};
            return go.transform;
        }

        private static void BuildTurtleBody(Terrain terrain,Scene scene)
        {
            var turtle=Root(scene,"Сюаньу · Каменная черепаха").transform;
            Material skin=TurtleMaterial("Glacial turtle skin",new Color(.22f,.30f,.34f),.28f);
            Material pale=TurtleMaterial("Glacial turtle beak and claws",new Color(.36f,.38f,.36f),.28f);
            Material seam=TurtleMaterial("Weathered shell edge",new Color(.19f,.26f,.29f),.21f);
            Material iris=TurtleMaterial("Turtle eye amber",new Color(.51f,.36f,.11f),.39f);
            Material pupil=TurtleMaterial("Turtle eye pupil",new Color(.022f,.031f,.03f),.56f);
            Mesh tissue=OrganicMesh("Weathered reptile form",.16f,.59f,39,37,64,true);
            Mesh armored=OrganicMesh("Snow on reptile scales",.25f,.47f,81,37,64,true);
            Mesh smooth=OrganicMesh("Turtle beak and eye form",.035f,2,12,13,24);

            BodyPart(turtle,"Пластрон под панцирем",tissue,new Vector3(ShellCenter.x,172,ShellCenter.y),
                new Vector3(218,30,208),seam,seam);

            var head=new GameObject("05 · Голова черепахи").transform;
            head.SetParent(turtle,false);
            BodyPart(head,"Череп · округлый профиль",armored,new Vector3(0,4,-7),new Vector3(45,34,54),skin,snowMesh);
            BodyPart(head,"Верхняя часть клюва",tissue,new Vector3(0,-5,32),new Vector3(31,17,32),pale,pale);
            BodyPart(head,"Короткий загнутый кончик клюва",smooth,new Vector3(0,-13,58),new Vector3(18,12,8),pale,pale);
            BodyPart(head,"Нижняя челюсть",tissue,new Vector3(0,-19,25),new Vector3(29,11,34),skin,skin);
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 eyeAt=new Vector3(side*41,11,12);
                BodyPart(head,"Надглазничный валик",tissue,eyeAt,new Vector3(9,9,13),skin,skin);
                BodyPart(head,"Глаз черепахи",smooth,eyeAt+new Vector3(side*7,0,1),new Vector3(3.2f,5.6f,8),iris,iris);
                BodyPart(head,"Зрачок",smooth,eyeAt+new Vector3(side*9.4f,.2f,1.8f),new Vector3(1.9f,4.9f,3.1f),pupil,pupil);
                BodyPart(head,"Ноздря",smooth,new Vector3(side*12,1,60),new Vector3(3.6f,2.3f,1.7f),pupil,pupil);
            }
            var neck=BodyPart(turtle,"Шея к панцирю",armored,Vector3.zero,Vector3.one,skin,snowMesh);
            SeatTurtleHead(terrain,head,neck);

            Vector2[] feet={new(-309,99),new(-651,172),new(-259,441),new(-663,529)};
            for(int i=0;i<feet.Length;i++)
            {
                Vector2 at=feet[i];
                Vector2 facing=(at-ShellCenter).normalized;
                float y=Ground(terrain,at);
                var foot=new GameObject(i==0 ? "03 · Передняя лапа черепахи" : "Опорная лапа черепахи "+(i+1)).transform;
                foot.SetParent(turtle,false);
                foot.SetPositionAndRotation(new Vector3(at.x,y+7,at.y),Quaternion.LookRotation(new Vector3(facing.x,0,facing.y)));
                BodyPart(foot,"Плечо",armored,new Vector3(0,14,-29),new Vector3(38,28,56),skin,snowMesh);
                BodyPart(foot,"Массивная стопа",armored,new Vector3(0,0,13),new Vector3(38,19,35),skin,snowMesh);
                for(int toe=0;toe<4;toe++)
                {
                    float x=(toe-1.5f)*15;
                    float z=42-3.2f*Mathf.Abs(toe-1.5f);
                    BodyPart(foot,"Палец",tissue,new Vector3(x,-2,z),new Vector3(9,8,18),skin,snowMesh);
                    BodyPart(foot,"Короткий коготь",smooth,new Vector3(x,-3,z+13),new Vector3(6,5,9),pale,pale);
                }
            }
        }

        private static void SeatTurtleHead(Terrain terrain,Transform head,Transform neck)
        {
            // Keep the beak beside the chamber's walking line; it points away from the shell.
            Vector2 position=new Vector2(-389,671);
            Vector2 axis=(position-ShellCenter).normalized;
            head.SetPositionAndRotation(new Vector3(position.x,Ground(terrain,position)+18,position.y),
                Quaternion.LookRotation(new Vector3(axis.x,0,axis.y)));
            Vector3 neckFrom=new Vector3(-438,214,521);
            Vector3 neckTo=head.position+new Vector3(-axis.x*22,-15,-axis.y*22);
            neck.SetPositionAndRotation((neckFrom+neckTo)*.5f,Quaternion.LookRotation(neckTo-neckFrom));
            neck.localScale=new Vector3(32,24,Vector3.Distance(neckFrom,neckTo)*.64f);
        }

        public static void FinishTurtlePlacement()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("real_winter must be the active edit scene.");
            GameObject[] roots=scene.GetRootGameObjects();
            Terrain terrain=roots.Single(x=>x.name=="Terrain").GetComponent<Terrain>();
            if(!AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(Organic+"/"))
                throw new InvalidOperationException("The new real_winter terrain is not assigned.");
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            TerrainData data=terrain.terrainData;
            int n=data.heightmapResolution;
            Vector3 o=terrain.transform.position,size=data.size;
            float[,] heights=data.GetHeights(0,0,n,n);
            for(int z=0;z<n;z++)
            {
                float wz=o.z+z*size.z/(n-1);
                if((wz<33 || wz>179) && (wz<565 || wz>783)) continue;
                for(int x=0;x<n;x++)
                {
                    float wx=o.x+x*size.x/(n-1);
                    if(wx< -474 || wx> -212) continue;
                    float y=heights[z,x]*size.y+o.y;
                    heights[z,x]=Mathf.Clamp01((OpenTurtleChambers(y,new Vector2(wx,wz))-o.y)/size.y);
                }
            }
            data.SetHeights(0,0,heights);
            terrain.Flush();
            Paint(terrain);
            Transform turtle=roots.Single(x=>x.name=="Сюаньу · Каменная черепаха").transform;
            SeatTurtleHead(terrain,turtle.Find("05 · Голова черепахи"),turtle.Find("Шея к панцирю"));
            Transform paw=turtle.Find("03 · Передняя лапа черепахи");
            Vector3 pawPosition=paw.position;
            pawPosition.y=Ground(terrain,new Vector2(pawPosition.x,pawPosition.z))+7;
            paw.position=pawPosition;
            var decorations=roots.Where(x=>x.name.StartsWith("Ели",StringComparison.Ordinal) ||
                x.name.StartsWith("Кустарник",StringComparison.Ordinal) || x.name.StartsWith("Валуны",StringComparison.Ordinal));
            foreach(GameObject group in decorations)
            foreach(Transform item in group.transform)
            {
                Vector3 pos=item.position;
                Vector2 horizontal=new Vector2(pos.x,pos.z);
                bool affected=Vector2.Distance(horizontal,new Vector2(-357,674))<118 || Vector2.Distance(horizontal,new Vector2(-299,106))<88;
                if(!affected) continue;
                pos.y=Ground(terrain,horizontal)-.45f;
                item.position=pos;
            }
            var shell=roots.Single(x=>x.name.StartsWith("06 · ",StringComparison.Ordinal)).GetComponent<MeshCollider>();
            var shellMesh=shell.sharedMesh;
            shell.sharedMesh=null;
            shell.sharedMesh=shellMesh;
            Material skin=AssetDatabase.LoadAssetAtPath<Material>(Organic+"/Glacial turtle skin.mat");
            ApplyTurtleSkinNormal(skin);
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssetIfDirty(skin);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save turtle placement.");
        }

        private static string DressOrganic(Terrain[] terrains,Scene scene)
        {
            Mesh[] trees=new[]{18,57,104}.Select(n=>AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Alpine fir "+n+".asset")).ToArray();
            Mesh[] stones={OrganicMesh("Frost boulder A",.58f,.10f,18,9,15),OrganicMesh("Frost boulder B",.64f,.18f,54,9,15),
                OrganicMesh("Frost boulder C",.49f,.23f,97,9,15)};
            GameObject[] shrubs={AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_A.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_B.prefab")};
            if(trees.Any(t=>!t)||shrubs.Any(t=>!t)) throw new IOException("Local fir or shrub assets are missing.");
            Transform firRoot=Root(scene,"Ели · Альпийские группы").transform;
            Transform bushRoot=Root(scene,"Кустарник · По краям снежных троп").transform;
            Transform rockRoot=Root(scene,"Валуны · Отдельные камни в снегу").transform;
            var rng=new System.Random(6942);
            float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
            Terrain At(Vector2 p)
            {
                foreach(Terrain t in terrains)
                {
                    Vector3 o=t.transform.position, s=t.terrainData.size;
                    if(p.x>=o.x && p.x<=o.x+s.x && p.y>=o.z && p.y<=o.z+s.z) return t;
                }
                return null;
            }
            bool Spot(Vector2 p,float margin,float slope,out float y)
            {
                y=0;
                if(OnRoute(p,margin) || (p.y<-23 && p.x> -588 && p.x < -480) || Vector2.Distance(p,new Vector2(-389,671))<49) return false;
                Terrain t=At(p);
                if(!t) return false;
                Vector3 o=t.transform.position, s=t.terrainData.size;
                if(t.terrainData.GetSteepness((p.x-o.x)/s.x,(p.y-o.z)/s.z)>slope) return false;
                y=Ground(t,p);
                return y<302;
            }

            int treeCount=0,bushCount=0,stoneCount=0;
            // Several contiguous pockets and clear ridge lines rather than a uniform forest across the route.
            for(float z=-123;z<=736;z+=18)
            for(float x=-738;x<=45;x+=18)
            {
                Vector2 p=new Vector2(x+R(-6,6),z+R(-6,6));
                float group=Noise(p.x,p.y,.011f);
                if(group>.51f && R(0,1)>.19f && Spot(p,4,33,out float y))
                {
                    int variant=rng.Next(trees.Length);
                    MeshObject("Снежная ель",trees[variant],new[]{bark,needles,snowMesh},firRoot,
                        new Vector3(p.x,y-.28f,p.y),Quaternion.Euler(0,R(0,360),0),R(10,21));
                    treeCount++;
                }
            }
            for(float z=-100;z<=736;z+=11)
            for(float x=-701;x<=46;x+=11)
            {
                Vector2 p=new Vector2(x+R(-4,4),z+R(-4,4));
                if(R(0,1)<.2f && Noise(p.x,p.y,.024f)>.34f && Spot(p,2,36,out float y))
                {
                    PlacePrefab(shrubs[rng.Next(shrubs.Length)],bushRoot,p,y-.26f,Quaternion.Euler(0,R(0,360),0),R(1.4f,4.7f));
                    bushCount++;
                }
            }
            for(float z=-122;z<=736;z+=15)
            for(float x=-729;x<=63;x+=15)
            {
                Vector2 p=new Vector2(x+R(-5,5),z+R(-5,5));
                if(R(0,1)>.19f || !Spot(p,2.5f,44,out float y)) continue;
                float r=R(.9f,4.4f)*R(.62f,1.2f);
                var stone=BodyPart(rockRoot,"Снежный валун",stones[rng.Next(stones.Length)],
                    new Vector3(p.x,y-r*.22f,p.y),new Vector3(r*R(.8f,1.5f),r*R(.39f,.84f),r*R(.7f,1.5f)),rockMesh,snowMesh);
                stone.localRotation=Quaternion.Euler(R(-13,13),R(0,360),R(-11,11));
                stoneCount++;
            }
            return $"New TerrainData: {terrains.Length}; firs: {treeCount}; shrubs: {bushCount}; individual boulders: {stoneCount}";
        }
    }
}
