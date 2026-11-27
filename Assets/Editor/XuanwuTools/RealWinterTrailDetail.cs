using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XuanwuTools
{
    internal static partial class RealWinterBuilder
    {
        private const string TrailDetailFolder = Refined + "/Trail detail";

        private sealed class RaisedTrail
        {
            internal readonly Station[] Points;
            internal readonly float[] Distances;

            internal RaisedTrail(Station[] points)
            {
                Points=points;
                Distances=new float[points.Length];
                for(int i=1;i<points.Length;i++) Distances[i]=Distances[i-1]+Vector2.Distance(points[i-1].Point,points[i].Point);
            }

            internal void Project(Vector2 p,out float distance,out float across,out float along,out float width,out Vector2 side)
            {
                float best=float.PositiveInfinity;
                across=along=width=0;
                side=Vector2.zero;
                for(int i=1;i<Points.Length;i++)
                {
                    Station a=Points[i-1],b=Points[i];
                    Vector2 step=b.Point-a.Point;
                    float t=Mathf.Clamp01(Vector2.Dot(p-a.Point,step)/step.sqrMagnitude);
                    Vector2 offset=p-a.Point-step*t;
                    if(offset.sqrMagnitude>=best) continue;
                    best=offset.sqrMagnitude;
                    side=new Vector2(step.y,-step.x).normalized;
                    across=Vector2.Dot(offset,side);
                    along=Mathf.Lerp(Distances[i-1],Distances[i],t);
                    width=Mathf.Lerp(a.Width,b.Width,t);
                }
                distance=Mathf.Sqrt(best);
            }
        }

        [MenuItem("Tools/Xuanwu/Detail real_winter raised trail")]
        public static string DetailRaisedTrail()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            if(AssetDatabase.IsValidFolder(TrailDetailFolder))
                throw new InvalidOperationException("The raised trail revision is already present.");
            GameObject[] roots=scene.GetRootGameObjects();
            Terrain[] tiles=roots.Select(g=>g.GetComponent<Terrain>()).Where(t=>t).ToArray();
            var main=tiles.Single(t=>t.name=="Terrain");
            var skyline=roots.Single(g=>g.name=="Горные гребни за обрывом").GetComponentInChildren<Terrain>();
            var shell=roots.Single(g=>g.name.StartsWith("06 · ",StringComparison.Ordinal));
            var turtle=roots.Single(g=>g.name=="Сюаньу · Каменная черепаха");
            if(!skyline || !AssetDatabase.GetAssetPath(main.terrainData).StartsWith(Refined+"/",StringComparison.Ordinal) ||
                !AssetDatabase.GetAssetPath(skyline.terrainData).StartsWith(Refined+"/",StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the refined real_winter scene and its dedicated TerrainData.");
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            layers=main.terrainData.terrainLayers;
            AssetDatabase.CreateFolder(Refined,"Trail detail");
            var raised=new RaisedTrail(Slope.Skip(5).Concat(HeadAndNeck.Skip(1).Take(6)).ToArray());
            var oldTiles=tiles.ToDictionary(t=>t,t=>t.terrainData);

            int sculpted=BreakUpRaisedBanks(main,raised);
            float previousSkyline=LowerSkyline(skyline,out float currentSkyline);
            ReseatDecoration(roots,tiles,oldTiles);
            ReseatTurtleParts(turtle.transform,main,oldTiles[main]);
            var mesh=shell.GetComponent<MeshFilter>().sharedMesh;
            var shellCollider=shell.GetComponent<MeshCollider>();
            shellCollider.sharedMesh=null;
            shellCollider.sharedMesh=mesh;

            var bush=roots.Single(g=>g.name.StartsWith("Кустарник",StringComparison.Ordinal)).transform;
            var stone=roots.Single(g=>g.name.StartsWith("Валуны",StringComparison.Ordinal)).transform;
            var trees=roots.Single(g=>g.name.StartsWith("Ели",StringComparison.Ordinal)).transform;
            AddShoulderDetails(tiles,raised,bush,stone,out int bushesAdded,out int rocksAdded);
            Physics.SyncTransforms();
            var tunnel=roots.Single(g=>g.name.StartsWith("01 · ",StringComparison.Ordinal));
            int removed=RemoveHiddenDetail(scene,tiles,shellCollider,turtle.transform,tunnel.transform,new[]{trees,bush,stone},out int eyePositions);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the raised trail revision.");
            return FormattableString.Invariant($"Sculpted cells {sculpted}; highest skyline {previousSkyline:F1} -> {currentSkyline:F1}; added low shrubs {bushesAdded}, edge rocks {rocksAdded}; visibility viewpoints {eyePositions}; hidden decorations removed {removed}; remaining firs {trees.childCount}, shrubs {bush.childCount}, rocks {stone.childCount}.");
        }

        private static float SampleGrid(float[,] heights,Vector2 location,Vector3 origin,Vector3 size,int n)
        {
            float x=Mathf.Clamp01((location.x-origin.x)/size.x)*(n-1);
            float z=Mathf.Clamp01((location.y-origin.z)/size.z)*(n-1);
            int ix=Mathf.Min(n-2,(int)x),iz=Mathf.Min(n-2,(int)z);
            return Mathf.Lerp(Mathf.Lerp(heights[iz,ix],heights[iz,ix+1],x-ix),
                Mathf.Lerp(heights[iz+1,ix],heights[iz+1,ix+1],x-ix),z-iz)*size.y+origin.y;
        }

        private static int BreakUpRaisedBanks(Terrain terrain,RaisedTrail raised)
        {
            TerrainData original=terrain.terrainData;
            int n=original.heightmapResolution;
            Vector3 o=terrain.transform.position,size=original.size;
            float[,] old=original.GetHeights(0,0,n,n),heights=(float[,])old.Clone();
            int count=0;
            for(int z=0;z<n;z++)
            {
                float wz=o.z+z*size.z/(n-1);
                if(wz<458 || wz>750) continue;
                for(int x=0;x<n;x++)
                {
                    float wx=o.x+x*size.x/(n-1);
                    if(wx< -500 || wx> -55) continue;
                    var p=new Vector2(wx,wz);
                    float shell=Smooth(1.02f,1.14f,((p-ShellCenter)/ShellRadii).magnitude);
                    if(shell<=0) continue;
                    raised.Project(p,out float distance,out float across,out float along,out float width,out Vector2 side);
                    if(distance<7 || distance>width+56) continue;
                    float endMask=Smooth(2,31,along)*(1-Smooth(raised.Distances.Last()-44,raised.Distances.Last(),along));
                    if(endMask<=0) continue;
                    float margin=Smooth(7,16,distance)*(1-Smooth(width+37,width+56,distance))*endMask*shell;
                    float sign=Mathf.Sign(across);
                    float phase=sign>0 ? 1.8f : 5f;

                    // Push alternating ribs away from the trail and cut gullies between them.
                    float broad=Mathf.Sin(along*.082f+phase+distance*.027f);
                    float narrow=Mathf.Sin(along*.197f+distance*.104f-phase);
                    float warp=(broad*8.8f+narrow*3.2f)*margin;
                    float warped=SampleGrid(old,p+side*sign*warp,o,size,n);
                    float relief=warped-(old[z,x]*size.y+o.y);
                    float buttress=Smooth(12,23,distance)*(1-Smooth(width+14,width+37,distance));
                    float flutes=Mathf.Pow(.5f+.5f*Mathf.Cos(along*.117f+distance*.05f+phase),2.7f);
                    float ragged=(Noise(wx,wz,.11f)-.5f)*3.8f+(Noise(wx,wz,.045f)-.5f)*6.2f;
                    relief+=(10.2f*flutes-4.8f+ragged)*buttress*margin;

                    // Small irregular snow lips close to the crest. The center seven units are unchanged.
                    float lipCenter=width*.43f+2.7f*broad;
                    float lip=(1-Smooth(1.5f,7.2f,Mathf.Abs(distance-lipCenter)))*
                        Mathf.Pow(.5f+.5f*Mathf.Sin(along*.193f+phase),1.5f);
                    relief+=lip*3.1f*endMask*shell*Smooth(7,12,distance);
                    float floor=SampleGrid(old,p-side*across,o,size,n);
                    float wallY=old[z,x]*size.y+o.y+relief;
                    relief+=TalusLift(wallY,floor,distance,along,width,sign,endMask*shell,p);
                    if(Mathf.Abs(relief)<.04f) continue;
                    heights[z,x]=Mathf.Clamp01(old[z,x]+relief/size.y);
                    count++;
                }
            }
            TerrainData tile=Object.Instantiate(original);
            tile.name="Terrain with eroded raised trail";
            tile.SetHeights(0,0,heights);
            AssetDatabase.CreateAsset(tile,TrailDetailFolder+"/"+tile.name+".asset");
            terrain.terrainData=tile;
            terrain.GetComponent<TerrainCollider>().terrainData=tile;
            terrain.Flush();
            Paint(terrain);
            return count;
        }

        private static float TalusLift(float wallY,float trailY,float distance,float along,float width,float sign,float mask,Vector2 p)
        {
            float angle=along*.097f+sign*2.4f+distance*.012f;
            float rib=Mathf.Pow(.5f+.5f*Mathf.Cos(angle),2.1f);
            float inner=Mathf.Clamp(width*.39f,10.2f,15.8f)+2.7f*Mathf.Sin(along*.122f-sign);
            float run=Mathf.Max(0,distance-inner);
            float fall=run*Mathf.Lerp(3.9f,1.63f,rib)+run*run*.0105f;
            float fracture=(Noise(p.x,p.y,.083f)-.5f)*8.2f;
            float top=trailY-3.9f-fall+fracture*Smooth(2,11,run);
            float feather=Smooth(8.5f,19.5f,distance)*(1-Smooth(width+35,width+67,distance));
            return Mathf.Max(0,top-wallY)*mask*feather*Mathf.Lerp(.44f,1,rib);
        }

        public static string FinishRaisedBanks()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open real_winter as the active edit scene.");
            GameObject[] roots=scene.GetRootGameObjects();
            Terrain[] tiles=roots.Select(g=>g.GetComponent<Terrain>()).Where(t=>t).ToArray();
            Terrain main=tiles.Single(t=>t.name=="Terrain");
            TerrainData data=main.terrainData;
            if(!AssetDatabase.GetAssetPath(data).StartsWith(TrailDetailFolder+"/",StringComparison.Ordinal))
                throw new InvalidOperationException("The dedicated raised-trail terrain is missing.");
            var woods=roots.Single(x=>x.name.StartsWith("Ели",StringComparison.Ordinal)).transform;
            var plants=roots.Single(x=>x.name.StartsWith("Кустарник",StringComparison.Ordinal)).transform;
            var stones=roots.Single(x=>x.name.StartsWith("Валуны",StringComparison.Ordinal)).transform;
            var shell=roots.Single(x=>x.name.StartsWith("06 · ",StringComparison.Ordinal)).GetComponent<MeshCollider>();
            var turtle=roots.Single(x=>x.name=="Сюаньу · Каменная черепаха").transform;
            var tunnel=roots.Single(x=>x.name.StartsWith("01 · ",StringComparison.Ordinal)).transform;
            ravine=ReadRavine();
            toolNoise=ReadTerrainToolsNoise();
            var line=new RaisedTrail(Slope.Skip(5).Concat(HeadAndNeck.Skip(1).Take(6)).ToArray());
            var oldTiles=tiles.ToDictionary(t=>t,t=>t.terrainData);
            TerrainData snapshot=Object.Instantiate(data);
            oldTiles[main]=snapshot;
            try
            {
                int n=data.heightmapResolution;
                Vector3 o=main.transform.position,size=data.size;
                float[,] old=data.GetHeights(0,0,n,n),newHeights=(float[,])old.Clone();
                for(int z=0;z<n;z++)
                {
                    float wz=o.z+z*size.z/(n-1);
                    if(wz<462 || wz>764) continue;
                    for(int x=0;x<n;x++)
                    {
                        float wx=o.x+x*size.x/(n-1);
                        if(wx< -499 || wx> -52) continue;
                        var p=new Vector2(wx,wz);
                        float shellGuard=Smooth(1.022f,1.16f,((p-ShellCenter)/ShellRadii).magnitude);
                        if(shellGuard<=0) continue;
                        line.Project(p,out float d,out float across,out float along,out float width,out Vector2 nrm);
                        if(d<8.5f || d>width+67) continue;
                        float mask=Smooth(9,35,along)*(1-Smooth(line.Distances.Last()-50,line.Distances.Last()-5,along))*shellGuard;
                        float y=SampleGrid(old,p-nrm*across,o,size,n);
                        float lift=TalusLift(old[z,x]*size.y+o.y,y,d,along,width,Mathf.Sign(across),mask,p);
                        newHeights[z,x]=Mathf.Clamp01(old[z,x]+lift/size.y);
                    }
                }
                data.SetHeights(0,0,newHeights);
                main.Flush();
                Paint(main);
                ReseatDecoration(roots,tiles,oldTiles);
                ReseatTurtleParts(turtle,main,snapshot);
                Mesh mesh=shell.sharedMesh;
                shell.sharedMesh=null;
                shell.sharedMesh=mesh;
                Physics.SyncTransforms();
                int hidden=RemoveHiddenDetail(scene,tiles,shell,turtle,tunnel,new[]{woods,plants,stones},out _);
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the final shoulders.");
                return $"Additional terrain ribs applied; newly occluded props removed {hidden}; remaining firs {woods.childCount}, shrubs {plants.childCount}, rocks {stones.childCount}.";
            }
            finally { Object.DestroyImmediate(snapshot); }
        }

        private static float LowerSkyline(Terrain terrain,out float finalHeight)
        {
            TerrainData original=terrain.terrainData;
            int n=original.heightmapResolution;
            float[,] old=original.GetHeights(0,0,n,n),heights=new float[n,n];
            float high=0,final=0;
            for(int z=0;z<n;z++)
            for(int x=0;x<n;x++)
            {
                float h=old[z,x];
                high=Mathf.Max(high,h);
                int a=Mathf.Max(0,x-4),b=Mathf.Min(n-1,x+4),c=Mathf.Max(0,z-4),d=Mathf.Min(n-1,z+4);
                float surround=(old[z,a]+old[z,b]+old[c,x]+old[d,x])*.25f;
                float broad=Mathf.Lerp(h,surround,.30f*Smooth(.28f,.72f,h));
                float reduced=broad*(1-.31f*Smooth(.10f,.73f,broad));
                heights[z,x]=reduced;
                final=Mathf.Max(final,reduced);
            }
            TerrainData data=Object.Instantiate(original);
            data.name="Low alpine skyline";
            data.SetHeights(0,0,heights);
            AssetDatabase.CreateAsset(data,TrailDetailFolder+"/"+data.name+".asset");
            terrain.terrainData=data;
            PaintSkyline(terrain);
            finalHeight=terrain.transform.position.y+final*data.size.y;
            return terrain.transform.position.y+high*data.size.y;
        }

        private static void ReseatTurtleParts(Transform turtle,Terrain main,TerrainData previous)
        {
            foreach(Transform part in turtle)
            {
                if(!part.name.Contains("лапа") && !part.name.StartsWith("05",StringComparison.Ordinal)) continue;
                Vector3 position=part.position;
                var p=new Vector2(position.x,position.z);
                position.y+=SampleTile(main,main.terrainData,p)-SampleTile(main,previous,p);
                part.position=position;
            }
        }

        private readonly struct TrailMark
        {
            public readonly Vector2 Point, Across;
            public readonly float Width, Length;

            public TrailMark(Vector2 point,Vector2 across,float width,float length)
            {
                Point=point; Across=across; Width=width; Length=length;
            }
        }

        private static IEnumerable<TrailMark> AtIntervals(Station[] line,float step)
        {
            float arc=0,remainder=0;
            for(int i=1;i<line.Length;i++)
            {
                Station a=line[i-1],b=line[i];
                Vector2 delta=b.Point-a.Point;
                float d=delta.magnitude;
                if(d<.01f) continue;
                var normal=new Vector2(delta.y,-delta.x)/d;
                for(;remainder<d;remainder+=step)
                {
                    float t=remainder/d;
                    yield return new TrailMark(a.Point+delta*t,normal,Mathf.Lerp(a.Width,b.Width,t),arc+remainder);
                }
                arc+=d;
                remainder-=d;
            }
        }

        private static void AddShoulderDetails(Terrain[] tiles,RaisedTrail raised,Transform bushRoot,Transform rockRoot,
            out int bushesAdded,out int rocksAdded)
        {
            var shrubs=new[]{AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_A.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/BushDry_B.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/Heather_B.prefab")};
            var rocks=new[]{AssetDatabase.LoadAssetAtPath<Mesh>(Organic+"/Frost boulder A.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>(Organic+"/Frost boulder B.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>(Organic+"/Frost boulder C.asset")};
            var stone=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Exposed shell slate.mat");
            var cap=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Shell and drift snow.mat");
            if(shrubs.Any(x=>!x)||rocks.Any(x=>!x)||!stone||!cap) throw new IOException("Local shoulder props are missing.");
            var rng=new System.Random(37590);
            float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
            var used=new List<Vector2>();
            foreach(Transform child in bushRoot) used.Add(new Vector2(child.position.x,child.position.z));
            bool Surface(Vector2 p,float limit,out float y)
            {
                y=0;
                Terrain tile=TileAt(tiles,p);
                if(!tile || ((p-ShellCenter)/ShellRadii).sqrMagnitude<1.02f) return false;
                Vector3 o=tile.transform.position,size=tile.terrainData.size;
                if(tile.terrainData.GetSteepness((p.x-o.x)/size.x,(p.y-o.z)/size.z)>limit) return false;
                y=SampleTile(tile,tile.terrainData,p);
                return true;
            }

            rocksAdded=0;
            foreach(TrailMark mark in AtIntervals(raised.Points,12.5f))
            {
                if(mark.Length<23 || mark.Length>raised.Distances.Last()-25) continue;
                Terrain center=TileAt(tiles,mark.Point);
                if(!center) continue;
                float centerY=SampleTile(center,center.terrainData,mark.Point);
                float outer=mark.Width+12;
                Terrain left=TileAt(tiles,mark.Point-mark.Across*outer);
                Terrain right=TileAt(tiles,mark.Point+mark.Across*outer);
                if(!left||!right||centerY-SampleTile(left,left.terrainData,mark.Point-mark.Across*outer)<11 ||
                   centerY-SampleTile(right,right.terrainData,mark.Point+mark.Across*outer)<11) continue;
                for(int side=-1;side<=1;side+=2)
                {
                    Vector2 p=mark.Point+mark.Across*side*(Mathf.Clamp(mark.Width*.37f,10,15)+R(-1.3f,1.5f));
                    if(R(0,1)<.14f || !Surface(p,44,out float y)) continue;
                    var rock=BodyPart(rockRoot,"Снежная глыба · Кромка высокой тропы",rocks[rng.Next(rocks.Length)],
                        new Vector3(p.x,y-.38f,p.y),new Vector3(R(1.7f,3.1f),R(.66f,1.55f),R(1.45f,2.7f)),stone,cap);
                    rock.localRotation=Quaternion.Euler(R(-8,8),R(0,360),R(-12,12));
                    rocksAdded++;
                }
            }

            bushesAdded=0;
            foreach(Station[] line in new[]{ravine,PawExit,Slope,HeadAndNeck})
            foreach(TrailMark mark in AtIntervals(line,6.7f))
            {
                if(mark.Point.y< -12) continue;
                for(int side=-1;side<=1;side+=2)
                {
                    if(R(0,1)<.26f) continue;
                    float d=Mathf.Clamp(mark.Width*.41f,3.4f,8.9f)+R(-.55f,1.25f);
                    Vector2 p=mark.Point+mark.Across*side*d;
                    // Leave a 6.2 m central lane even in the narrow ravine.
                    if(d<3.1f || used.Any(v=>(v-p).sqrMagnitude<2.05f*2.05f) || !Surface(p,28,out float y)) continue;
                    PlacePrefab(shrubs[rng.Next(shrubs.Length)],bushRoot,p,y-.13f,Quaternion.Euler(0,R(0,360),0),R(.75f,1.92f));
                    used.Add(p);
                    bushesAdded++;
                }
            }
        }

        private readonly struct Occluder
        {
            public readonly Collider Collider;
            public readonly Bounds Bounds;

            public Occluder(Collider collider)
            {
                Collider=collider;
                Bounds=collider.bounds;
            }
        }

        private static int RemoveHiddenDetail(Scene scene,Terrain[] tiles,MeshCollider shell,Transform turtle,Transform tunnel,
            Transform[] decorationGroups,out int eyePositions)
        {
            var terrainColliders=tiles.Select(t=>t.GetComponent<TerrainCollider>()).ToArray();
            Collider slab=tunnel.Find("Cube").GetComponent<BoxCollider>();
            var floorColliders=terrainColliders.Cast<Collider>().Concat(new[]{(Collider)shell,slab}).ToArray();
            var viewpoints=new List<Vector3>();
            var player=scene.GetRootGameObjects().Single(g=>g.name=="Player").GetComponent<CharacterController>();
            float eyeHeight=player.height*player.transform.lossyScale.y*.78f;
            foreach(Station[] line in new[]{Entrance,ravine,PawExit,Slope,HeadAndNeck})
            foreach(TrailMark mark in AtIntervals(line,6.2f))
            {
                float offset=Mathf.Min(7.2f,mark.Width*.31f);
                for(int side=-1;side<=1;side++)
                {
                    Vector2 p=mark.Point+mark.Across*offset*side;
                    var ray=new Ray(new Vector3(p.x,650,p.y),Vector3.down);
                    float floor=float.NegativeInfinity;
                    foreach(Collider col in floorColliders)
                        if(col.Raycast(ray,out RaycastHit hit,850)) floor=Mathf.Max(floor,hit.point.y);
                    if(!float.IsNegativeInfinity(floor)) viewpoints.Add(new Vector3(p.x,floor+eyeHeight,p.y));
                }
            }
            eyePositions=viewpoints.Count;
            if(eyePositions<100) throw new InvalidOperationException("Visibility bake found too few positions on the trail.");

            // Visual-only large parts of the turtle also hide decorations. Use temporary
            // collision proxies during this pass and remove them before saving the scene.
            GameObject proxies=Root(scene,"Temporary visibility occluders");
            var removed=new List<GameObject>();
            try
            {
                foreach(MeshFilter filter in turtle.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer=filter.GetComponent<MeshRenderer>();
                    if(!filter.sharedMesh || !renderer || renderer.bounds.size.magnitude<26) continue;
                    var proxy=new GameObject(filter.name);
                    proxy.transform.SetParent(proxies.transform,false);
                    proxy.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);
                    proxy.transform.localScale=filter.transform.lossyScale;
                    proxy.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
                }
                Physics.SyncTransforms();
                var occluders=terrainColliders.Cast<Collider>().Concat(new[]{shell})
                    .Concat(tunnel.GetComponentsInChildren<BoxCollider>()).Concat(proxies.GetComponentsInChildren<MeshCollider>())
                    .Where(c=>c.enabled && c.gameObject.activeInHierarchy).Select(c=>new Occluder(c)).ToArray();
                var distances=new float[viewpoints.Count];
                var order=new int[viewpoints.Count];
                float far=scene.GetRootGameObjects().Single(g=>g.name=="Main Camera").GetComponent<Camera>().farClipPlane;

                foreach(Transform group in decorationGroups)
                foreach(Transform item in group)
                {
                    Renderer[] renderers=item.GetComponentsInChildren<Renderer>();
                    if(renderers.Length==0) continue;
                    Bounds b=renderers[0].bounds;
                    foreach(Renderer renderer in renderers) b.Encapsulate(renderer.bounds);
                    Vector3 center=b.center;
                    for(int i=0;i<viewpoints.Count;i++)
                    {
                        order[i]=i;
                        distances[i]=(viewpoints[i]-center).sqrMagnitude;
                    }
                    Array.Sort(distances,order);
                    bool visible=false;
                    for(int i=0;i<order.Length && !visible;i++)
                    {
                        if(distances[i]>far*far) break;
                        Vector3 eye=viewpoints[order[i]];
                        Vector3 side=Vector3.Cross(Vector3.up,(center-eye).normalized)*Mathf.Min(b.extents.x,b.extents.z)*.60f;
                        visible=HasView(eye,center,occluders) || HasView(eye,center+Vector3.up*(b.extents.y*.82f),occluders) ||
                            HasView(eye,center+side,occluders) || HasView(eye,center-side,occluders);
                    }
                    if(!visible) removed.Add(item.gameObject);
                }
            }
            finally { Object.DestroyImmediate(proxies); }
            foreach(GameObject item in removed) Object.DestroyImmediate(item);
            return removed.Count;
        }

        private static bool HasView(Vector3 eye,Vector3 target,Occluder[] occluders)
        {
            Vector3 delta=target-eye;
            float distance=delta.magnitude-.11f;
            if(distance<=0) return true;
            var ray=new Ray(eye,delta.normalized);
            foreach(Occluder item in occluders)
                if(item.Bounds.IntersectRay(ray,out float entry) && entry<distance && item.Collider.Raycast(ray,out _,distance))
                    return false;
            return true;
        }
    }
}
