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
        private const string Refined = Organic + "/Refined";

        private readonly struct Summit
        {
            private readonly Vector2 center, radii;
            private readonly float height, angle, ridgePhase;

            public Summit(float x,float z,float rx,float rz,float elevation,float bearing,float phase)
            {
                center=new Vector2(x,z); radii=new Vector2(rx,rz);
                height=elevation; angle=bearing*Mathf.Deg2Rad; ridgePhase=phase;
            }

            public float Elevation(Vector2 point)
            {
                Vector2 d=point-center;
                if(d.sqrMagnitude>radii.sqrMagnitude) return 0;
                float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
                var q=new Vector2((d.x*c+d.y*s)/radii.x,(-d.x*s+d.y*c)/radii.y);
                float radius=q.magnitude;
                if(radius>=1.2f) return 0;
                float direction=Mathf.Atan2(q.y,q.x);
                float spur=.83f+.22f*Mathf.Pow(.5f+.5f*Mathf.Cos(direction*3+ridgePhase+radius*1.2f),2.4f);
                spur+=.11f*Mathf.Pow(.5f+.5f*Mathf.Cos(direction*5-ridgePhase*.36f-radius*2),3);
                float face=Mathf.Clamp01(1-radius/spur);
                // A nonzero gradient all the way to the crest keeps the summit pointed.
                float flank=.5f+.5f*Mathf.Cos(direction*3+ridgePhase+radius*2.1f);
                float couloir=.5f+.5f*Mathf.Cos(direction*5-ridgePhase*.44f-radius*2.6f);
                float faces=1+(.16f*flank-.20f*couloir)*Smooth(.015f,.24f,radius)*Smooth(.06f,.34f,face);
                return height*(.58f*Mathf.Pow(face,.91f)+.42f*face*face)*faces;
            }
        }

        private static readonly Summit[] Skyline={
            new Summit(430,729,402,411,690,22,1.3f),
            new Summit(947,706,395,357,550,-31,2.2f),
            new Summit(227,722,229,344,287,-8,2.7f),
            new Summit(386,927,283,333,337,32,-1.4f),
            new Summit(754,910,309,365,289,-35,.9f),
            new Summit(1113,965,287,287,250,17,-.8f),
            new Summit(323,1204,263,273,240,6,2.3f),
            new Summit(156,1049,261,288,204,-18,.16f)
        };

        [MenuItem("Tools/Xuanwu/Refine real_winter cliffs and skyline")]
        public static string RefineCliffsAndSkyline()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            if(AssetDatabase.IsValidFolder(Refined)) throw new InvalidOperationException("The refined terrain is already present.");
            var roots=scene.GetRootGameObjects();
            var terrains=roots.Select(g=>g.GetComponent<Terrain>()).Where(t=>t).ToArray();
            if(terrains.Length!=3 || terrains.Any(t=>!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(Organic+"/",StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected three organic terrain tiles in the working scene.");
            var main=terrains.Single(t=>t.name=="Terrain");
            var sky=roots.Single(g=>g.name=="Горные гребни за обрывом").transform;
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            heightStamps=new[]{ReadStamp("Mountain02"),ReadStamp("Mountain08"),ReadStamp("Ridge01")};
            layers=main.terrainData.terrainLayers;
            AssetDatabase.CreateFolder(Organic,"Refined");

            var oldTiles=terrains.ToDictionary(t=>t,t=>t.terrainData);
            int resolved=0;
            for(int i=0;i<terrains.Length;i++)
                resolved+=RepairTile(terrains[i],i);

            var previousSky=new List<GameObject>();
            foreach(Transform child in sky) previousSky.Add(child.gameObject);
            SculptSkyline(sky,main.materialTemplate);
            foreach(GameObject child in previousSky) Object.DestroyImmediate(child);

            ReseatDecoration(roots,terrains,oldTiles);
            var shell=roots.Single(g=>g.name.StartsWith("06 · ",StringComparison.Ordinal)).GetComponent<MeshCollider>();
            Mesh shellMesh=shell.sharedMesh;
            shell.sharedMesh=null;
            shell.sharedMesh=shellMesh;
            int trees=MorePlants(roots,terrains,out int shrubs);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save refined real_winter.");
            return $"Refined height samples: {resolved}; new firs: {trees}; new shrubs: {shrubs}; skyline: two summits over six lower spurs.";
        }

        private static int RepairTile(Terrain terrain,int index)
        {
            TerrainData old=terrain.terrainData;
            Vector3 origin=terrain.transform.position, size=old.size;
            int n=old.heightmapResolution;
            float dx=size.x/(n-1),dz=size.z/(n-1);
            float[,] heights=old.GetHeights(0,0,n,n);
            float[,] weights=new float[n,n];
            int minX=n, minZ=n, maxX=-1, maxZ=-1;
            int count=0;
            for(int z=0;z<n;z++)
            {
                float wz=origin.z+z*dz;
                if(wz< -119 || wz>800) continue;
                for(int x=0;x<n;x++)
                {
                    float wx=origin.x+x*dx;
                    if(wx< -641 || wx> 38) continue;
                    var p=new Vector2(wx,wz);
                    float radius=((p-ShellCenter)/ShellRadii).magnitude;
                    // The separate shell has to meet the copied heightfield on the exact original vertices.
                    float shellGuard=Smooth(1.031f,1.12f,radius);
                    if(shellGuard<=0) continue;
                    float fade=(1-Smooth(755f,800f,wz))*Smooth(-119f,-94f,wz)*Smooth(-641f,-612f,wx)*(1-Smooth(20f,38f,wx));
                    float previous=OrganicHeight(p,true), corrected=OrganicHeight(p);
                    float delta=(corrected-previous)*shellGuard*fade*(1-NeckProtection(p));
                    if(Mathf.Abs(delta)>.04f) count++;
                    heights[z,x]=Mathf.Clamp01(heights[z,x]+delta/size.y);

                    // Only the banks near paths and chambers need the slope limiter.
                    // Leave the route floor, shell ring, and distant faces at their existing height.
                    float bank=BankRepairWeight(p)*shellGuard*fade;
                    if(bank<=.001f) continue;
                    weights[z,x]=bank;
                    minX=Mathf.Min(minX,x); maxX=Mathf.Max(maxX,x);
                    minZ=Mathf.Min(minZ,z); maxZ=Mathf.Max(maxZ,z);
                }
            }
            if(minX<=maxX && minZ<=maxZ)
                count+=SoftenBanks(heights,weights,n,minX,maxX,minZ,maxZ,dx,dz,size.y);
            if(count==0) return 0;

            TerrainData data=Object.Instantiate(old);
            data.name="Alpine terrain refined "+index;
            data.SetHeights(0,0,heights);
            AssetDatabase.CreateAsset(data,Refined+"/"+data.name+".asset");
            terrain.terrainData=data;
            terrain.GetComponent<TerrainCollider>().terrainData=data;
            terrain.Flush();
            Paint(terrain);
            return count;
        }

        private static float BankRepairWeight(Vector2 p)
        {
            float bank=0;
            void Consider(Station[] route,float reach)
            {
                float d=Route(p,route,out _,out float width,out _);
                float shoulder=Mathf.Max(3.5f,width*.44f);
                float strength=Smooth(shoulder,shoulder+6,d)*(1-Smooth(reach*.71f,reach,d));
                bank=Mathf.Max(bank,strength);
                if(d<shoulder) bank=-1;
            }
            if(p.x < -201 && p.y<193) Consider(ravine,82);
            if(bank<0) return 0;
            if(p.x> -382 && p.x< -47 && p.y>20 && p.y<326) Consider(PawExit,87);
            if(bank<0) return 0;
            if(p.x> -335 && p.y>131 && p.y<741) Consider(Slope,116);
            if(bank<0) return 0;
            if(p.x< -126 && p.y>275) Consider(HeadAndNeck,98);
            if(bank<0) return 0;
            if(p.x< -480 && p.y<33) Consider(Entrance,55);
            return Mathf.Max(0,bank);
        }

        private static int SoftenBanks(float[,] heights,float[,] weights,int n,int left,int right,int bottom,int top,float dx,float dz,float range)
        {
            var bounded=(float[,])heights.Clone();
            float capX=2.45f*dx/range, capZ=2.45f*dz/range;
            float capDiagonal=2.45f*Mathf.Sqrt(dx*dx+dz*dz)/range;
            left=Mathf.Max(1,left); right=Mathf.Min(n-2,right);
            bottom=Mathf.Max(1,bottom); top=Mathf.Min(n-2,top);
            // Sweep the lowest neighbouring slopes through the banks, rounding the abrupt top of a cut.
            for(int pass=0;pass<4;pass++)
            {
                int sx=(pass&1)==0?1:-1, sz=(pass&2)==0?1:-1;
                for(int z=sz>0?bottom:top; z>=bottom && z<=top; z+=sz)
                for(int x=sx>0?left:right; x>=left && x<=right; x+=sx)
                {
                    if(weights[z,x]<=.001f) continue;
                    float a=Mathf.Min(bounded[z,x-1],bounded[z,x+1])+capX;
                    float b=Mathf.Min(bounded[z-1,x],bounded[z+1,x])+capZ;
                    float c=Mathf.Min(bounded[z-1,x-1],bounded[z-1,x+1]);
                    float d=Mathf.Min(bounded[z+1,x-1],bounded[z+1,x+1]);
                    bounded[z,x]=Mathf.Min(bounded[z,x],Mathf.Min(Mathf.Min(a,b),Mathf.Min(c,d)+capDiagonal));
                }
            }
            int changed=0;
            for(int z=bottom;z<=top;z++)
            for(int x=left;x<=right;x++)
            {
                float original=heights[z,x], lowered=bounded[z,x];
                if(original-lowered<.1f/range || weights[z,x]<=.001f) continue;
                heights[z,x]=Mathf.Lerp(original,lowered,weights[z,x]);
                changed++;
            }
            return changed;
        }

        private static float NeckProtection(Vector2 p)
        {
            if(p.x> -171 || p.x< -543 || p.y<316 || p.y>713) return 0;
            float d=Route(p,HeadAndNeck,out _,out float width,out _);
            return 1-Smooth(Mathf.Max(5,width*.34f),Mathf.Max(13,width*.66f),d);
        }

        private static float[,] SkylineHeights(Vector3 origin,Vector3 size,int n)
        {
            var heights=new float[n,n];
            for(int z=0;z<n;z++)
            for(int x=0;x<n;x++)
            {
                float u=x/(n-1f), v=z/(n-1f);
                var p=new Vector2(origin.x+u*size.x,origin.z+v*size.z);
                float top=0, support=0;
                foreach(Summit summit in Skyline)
                {
                    float y=summit.Elevation(p);
                    if(y>top) { support=top; top=y; }
                    else support=Mathf.Max(support,y);
                }
                float erosion=(Noise(p.x,p.y,.017f)-.5f)*35+(Noise(p.x,p.y,.065f)-.5f)*12;
                float jagged=(TextureNoise(p.x,p.y)-.49f)*74;
                float footing=10+13*Noise(p.x,p.y,.0043f);
                float detailWeight=Smooth(27,137,top)*(1-.50f*Smooth(480,660,top));
                float envelope=Smooth(0,.085f,Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v)));
                heights[z,x]=Mathf.Clamp01(envelope*(footing+top+support*.09f+(erosion+jagged)*detailWeight)/size.y);
            }
            return heights;
        }

        private static void SculptSkyline(Transform parent,Material material)
        {
            const int n=513;
            var origin=new Vector3(-25,-66,280);
            var size=new Vector3(1325,760,1260);
            var data=new TerrainData {name="Twin alpine skyline",heightmapResolution=n,alphamapResolution=512,baseMapResolution=512,size=size,terrainLayers=layers};
            data.SetHeights(0,0,SkylineHeights(origin,size,n));
            AssetDatabase.CreateAsset(data,Refined+"/"+data.name+".asset");
            GameObject go=Terrain.CreateTerrainGameObject(data);
            go.name="Альпийский массив · Главная вершина, соседний пик и отроги";
            go.transform.SetParent(parent,false);
            go.transform.position=origin;
            var terrain=go.GetComponent<Terrain>();
            terrain.allowAutoConnect=false;
            terrain.materialTemplate=material;
            terrain.shadowCastingMode=ShadowCastingMode.Off;
            terrain.heightmapPixelError=3;
            Object.DestroyImmediate(go.GetComponent<TerrainCollider>());
            PaintSkyline(terrain);
        }

        private static void PaintSkyline(Terrain terrain)
        {
            TerrainData data=terrain.terrainData;
            int n=data.alphamapResolution;
            var alpha=new float[n,n,3];
            for(int z=0;z<n;z++)
            for(int x=0;x<n;x++)
            {
                float u=(x+.5f)/n,v=(z+.5f)/n;
                float slope=data.GetSteepness(u,v);
                float height=data.GetInterpolatedHeight(u,v);
                float rock=Smooth(29,61,slope+(Noise(x,z,.048f)-.5f)*18)*Mathf.Lerp(.92f,.75f,Smooth(285,610,height));
                float packed=(.13f+.28f*Noise(x,z,.039f))*(1-rock);
                alpha[z,x,0]=1-rock-packed; alpha[z,x,1]=packed; alpha[z,x,2]=rock;
            }
            data.SetAlphamaps(0,0,alpha);
            AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
        }

        public static string FinishCliffsAndSkyline()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            GameObject[] roots=scene.GetRootGameObjects();
            var main=roots.Single(g=>g.name=="Terrain").GetComponent<Terrain>();
            TerrainData data=main.terrainData;
            if(!AssetDatabase.GetAssetPath(data).StartsWith(Refined+"/",StringComparison.Ordinal))
                throw new InvalidOperationException("The refined working terrain is missing.");
            toolNoise=ReadTerrainToolsNoise();
            layers=data.terrainLayers;
            TerrainData old=AssetDatabase.LoadAssetAtPath<TerrainData>(Organic+"/New alpine terrain 0.asset");
            var skyline=roots.Single(g=>g.name=="Горные гребни за обрывом").GetComponentInChildren<Terrain>();
            if(!old || !skyline) throw new InvalidOperationException("Missing the organic baseline or the skyline.");
            skyline.terrainData.SetHeights(0,0,SkylineHeights(skyline.transform.position,skyline.terrainData.size,skyline.terrainData.heightmapResolution));
            PaintSkyline(skyline);

            int n=data.heightmapResolution;
            Vector3 origin=main.transform.position,size=data.size;
            float[,] heights=data.GetHeights(0,0,n,n), previous=old.GetHeights(0,0,n,n);
            for(int z=0;z<n;z++)
            {
                float wz=origin.z+z*size.z/(n-1);
                if(wz<316 || wz>713) continue;
                for(int x=0;x<n;x++)
                {
                    float wx=origin.x+x*size.x/(n-1);
                    if(wx> -171 || wx< -543) continue;
                    float keep=NeckProtection(new Vector2(wx,wz));
                    if(keep>0) heights[z,x]=Mathf.Lerp(heights[z,x],previous[z,x],keep);
                }
            }
            data.SetHeights(0,0,heights);
            main.Flush();
            Paint(main);
            var skin=AssetDatabase.LoadAssetAtPath<Mesh>(Organic+"/Weathered reptile form.asset");
            var armor=AssetDatabase.LoadAssetAtPath<Mesh>(Organic+"/Snow on reptile scales.asset");
            if(!skin || !armor) throw new InvalidOperationException("The turtle's snowcap meshes are missing.");
            OrganicMesh(skin.name,.16f,.59f,39,37,64,true,skin);
            OrganicMesh(armor.name,.25f,.47f,81,37,64,true,armor);

            var shell=roots.Single(g=>g.name.StartsWith("06 · ",StringComparison.Ordinal)).GetComponent<MeshCollider>();
            Mesh shellMesh=shell.sharedMesh;
            shell.sharedMesh=null;
            shell.sharedMesh=shellMesh;
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the final cliff edits.");
            return "Restored the walking line at the neck, separated the two summits, and rebuilt the turtle snowcaps.";
        }

        private static Terrain TileAt(Terrain[] tiles,Vector2 p)
        {
            foreach(Terrain tile in tiles)
            {
                Vector3 o=tile.transform.position,s=tile.terrainData.size;
                if(p.x>=o.x && p.x<=o.x+s.x && p.y>=o.z && p.y<=o.z+s.z) return tile;
            }
            return null;
        }

        private static float SampleTile(Terrain tile,TerrainData data,Vector2 p)
        {
            Vector3 o=tile.transform.position;
            return o.y+data.GetInterpolatedHeight((p.x-o.x)/data.size.x,(p.y-o.z)/data.size.z);
        }

        private static void ReseatDecoration(GameObject[] roots,Terrain[] tiles,Dictionary<Terrain,TerrainData> oldTiles)
        {
            foreach(GameObject root in roots)
            {
                if(!root || (!root.name.StartsWith("Ели",StringComparison.Ordinal) &&
                    !root.name.StartsWith("Кустарник",StringComparison.Ordinal) &&
                    !root.name.StartsWith("Валуны",StringComparison.Ordinal))) continue;
                foreach(Transform item in root.transform)
                {
                    Vector3 position=item.position;
                    var p=new Vector2(position.x,position.z);
                    Terrain tile=TileAt(tiles,p);
                    if(!tile || tile.terrainData==oldTiles[tile]) continue;
                    position.y+=SampleTile(tile,tile.terrainData,p)-SampleTile(tile,oldTiles[tile],p);
                    item.position=position;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                }
            }
        }

        private static int MorePlants(GameObject[] roots,Terrain[] tiles,out int shrubsAdded)
        {
            var firRoot=roots.Single(g=>g.name.StartsWith("Ели",StringComparison.Ordinal)).transform;
            var bushRoot=roots.Single(g=>g.name.StartsWith("Кустарник",StringComparison.Ordinal)).transform;
            var stoneRoot=roots.Single(g=>g.name.StartsWith("Валуны",StringComparison.Ordinal)).transform;
            var turtle=roots.Single(g=>g.name=="Сюаньу · Каменная черепаха").transform;
            var poses=new List<Vector2>();
            foreach(Transform part in turtle)
                if(part.name.Contains("лапа") || part.name.StartsWith("05")) poses.Add(new Vector2(part.position.x,part.position.z));
            var firPositions=new List<Vector2>();
            foreach(Transform t in firRoot) firPositions.Add(new Vector2(t.position.x,t.position.z));
            var bushPositions=new List<Vector2>();
            foreach(Transform t in bushRoot) bushPositions.Add(new Vector2(t.position.x,t.position.z));
            var rockPositions=new List<Vector2>();
            foreach(Transform t in stoneRoot) rockPositions.Add(new Vector2(t.position.x,t.position.z));
            Mesh[] trees=new[]{18,57,104}.Select(n=>AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Alpine fir "+n+".asset")).ToArray();
            var firMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Cold timber.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Alpine needles.mat"),AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shell and drift snow.mat")};
            GameObject[] shrubs={AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_A.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_B.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/Heather_B.prefab")};
            if(trees.Any(t=>!t)||firMaterials.Any(t=>!t)||shrubs.Any(t=>!t)) throw new IOException("Local plants are missing.");

            var random=new System.Random(14729);
            float R(float lo,float hi)=>Mathf.Lerp(lo,hi,(float)random.NextDouble());
            bool IsFree(List<Vector2> used,Vector2 p,float distance)
            {
                float d2=distance*distance;
                foreach(Vector2 other in used) if((other-p).sqrMagnitude<d2) return false;
                return true;
            }
            float TrailDistance(Vector2 p)
            {
                float near=float.PositiveInfinity;
                foreach(Station[] line in new[]{ravine,PawExit,Slope,HeadAndNeck})
                    near=Mathf.Min(near,Route(p,line,out _,out float width,out _)-width*.54f);
                return near;
            }
            bool CanGrow(Vector2 p,float margin,float maxSlope,out float height)
            {
                height=0;
                if(OnRoute(p,margin) || !IsFree(poses,p,60) || !IsFree(rockPositions,p,3.9f)) return false;
                if(p.x> -593 && p.x < -482 && p.y< -19) return false;
                Terrain tile=TileAt(tiles,p);
                if(!tile) return false;
                Vector3 o=tile.transform.position,s=tile.terrainData.size;
                if(tile.terrainData.GetSteepness((p.x-o.x)/s.x,(p.y-o.z)/s.z)>maxSlope) return false;
                height=SampleTile(tile,tile.terrainData,p);
                return height<292;
            }

            var candidates=new List<Vector2>();
            for(float z=-84;z<=748;z+=10)
            for(float x=-727;x<=42;x+=10)
                candidates.Add(new Vector2(x+R(-4.7f,4.7f),z+R(-4.7f,4.7f)));
            for(int i=candidates.Count-1;i>0;i--)
            {
                int j=random.Next(i+1);
                (candidates[i],candidates[j])=(candidates[j],candidates[i]);
            }

            int count=0;
            foreach(Vector2 p in candidates)
            {
                if(count>=175) break;
                float d=TrailDistance(p);
                if(d<5 || d>156 || Noise(p.x,p.y,.018f)<.43f || !IsFree(firPositions,p,10) ||
                    !CanGrow(p,2.2f,33,out float y)) continue;
                MeshObject("Ель · Группа у тропы",trees[random.Next(trees.Length)],firMaterials,firRoot,
                    new Vector3(p.x,y-.31f,p.y),Quaternion.Euler(0,R(0,360),0),R(9,18));
                firPositions.Add(p);
                count++;
            }

            shrubsAdded=0;
            foreach(Vector2 p in candidates)
            {
                if(shrubsAdded>=280) break;
                float d=TrailDistance(p);
                if(d<1 || d>115 || Noise(p.x-17,p.y,.029f)<.36f || !IsFree(bushPositions,p,5) ||
                    !IsFree(firPositions,p,2.7f) || !CanGrow(p,.65f,41,out float y)) continue;
                PlacePrefab(shrubs[random.Next(shrubs.Length)],bushRoot,p,y-.27f,Quaternion.Euler(0,R(0,360),0),R(1.25f,3.95f));
                bushPositions.Add(p);
                shrubsAdded++;
            }
            return count;
        }
    }
}
