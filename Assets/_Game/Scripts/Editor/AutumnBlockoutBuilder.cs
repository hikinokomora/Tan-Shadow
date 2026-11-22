using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TanShadow.Autumn;

public static class AutumnBlockoutBuilder
{
    const string AssetsDir = "Assets/_Game/Art/AutumnBlockout/";
    const float Clearing = 28, ArenaZ = 430, ArenaRadius = 36, HalfTrail = 2.65f;
    static T Asset<T>(string name) where T : Object => AssetDatabase.LoadAssetAtPath<T>(AssetsDir + name);
    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }
    static void Remove(Transform parent, string name)
    {
        foreach (Transform child in parent.Cast<Transform>().Where(t=>t.name==name).ToArray())
            Object.DestroyImmediate(child.gameObject);
    }
    static Transform Model(Transform parent,string name,Mesh mesh,Material[] materials,Vector3 p,float angle)
    {
        var t=Group(name,parent);
        t.SetPositionAndRotation(p,Quaternion.Euler(0,angle,0));
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterials=materials;
        return t;
    }
    static Mesh MakeSand()
    {
        var vertices=new List<Vector3>(); var indices=new List<int>();
        void Sector(float z,float radius,float radians)
        {
            int origin=vertices.Count; vertices.Add(new Vector3(0,.018f,z));
            for(int i=0;i<=128;i++) {
                float a=radians*i/128;
                vertices.Add(new Vector3(radius*Mathf.Cos(a),.018f,z+radius*Mathf.Sin(a)));
                if(i>0)indices.AddRange(new[]{origin,origin+i+1,origin+i});
            }
        }
        Sector(0,Clearing+.5f,Mathf.PI);Sector(ArenaZ,ArenaRadius+.5f,2*Mathf.PI);
        var m=Asset<Mesh>("Autumn sand plan.asset");
        if(!m){m=new Mesh();AssetDatabase.CreateAsset(m,AssetsDir+"Autumn sand plan.asset");}
        m.Clear();m.SetVertices(vertices);m.SetTriangles(indices,0);m.RecalculateNormals();m.RecalculateBounds();EditorUtility.SetDirty(m);
        return m;
    }
    static Mesh Surface(string name, List<Vector3> vertices, List<int> indices)
    {
        var mesh=Asset<Mesh>(name+".asset");
        if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,AssetsDir+name+".asset");}
        mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);return mesh;
    }

    static void FinishApproach(Transform approach, Transform ramp)
    {
        var vertices=new List<Vector3>();var faces=new List<int>();
        Vector3 SurfacePoint(float angle,float t) => new Vector3(
            Mathf.Cos(angle)*Mathf.Lerp(4.6f,12,t),8*(1-t),Mathf.Sin(angle)*Mathf.Lerp(2.5f,24,t));
        void Tri(Vector3 a,Vector3 b,Vector3 c){
            int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);faces.AddRange(new[]{n,n+1,n+2});}
        for(int i=0;i<48;i++){
            float a=i*Mathf.PI/48,b=(i+1)*Mathf.PI/48;
            var p=SurfacePoint(a,0);var q=SurfacePoint(b,0);var r=SurfacePoint(a,1);var s=SurfacePoint(b,1);
            Tri(Vector3.up*8,q,p);Tri(p,q,r);Tri(q,s,r);
        }
        var continuous=Surface("Compact embankment",vertices,faces);ramp.localScale=Vector3.one;
        ramp.GetComponent<MeshFilter>().sharedMesh=continuous;ramp.GetComponent<MeshCollider>().sharedMesh=continuous;
        vertices.Clear();faces.Clear();
        var random=new System.Random(84311);
        float Sample()=> (float)random.NextDouble();
        for(int i=0;i<6800;i++){
            var p=SurfacePoint(Sample()*Mathf.PI,Mathf.Sqrt(Sample()));float r=Mathf.Lerp(.075f,.168f,Sample());
            float h=r*Mathf.Lerp(.68f,1.05f,Sample()),phase=Sample()*6.28f;
            Vector3 Ring(int j,float w,float lift){
                float a=j*Mathf.PI/3+phase;return p+new Vector3(Mathf.Cos(a)*r*w,h*lift,Mathf.Sin(a)*r*w*.87f);}
            for(int j=0;j<6;j++){
                var a=Ring(j,.96f,-.12f);var b=Ring(j+1,.96f,-.12f);
                var c=Ring(j,.74f,.70f);var d=Ring(j+1,.74f,.70f);
                Tri(a,c,b);Tri(b,c,d);Tri(c,p+Vector3.up*h,d);
            }
        }
        var pebbles=approach.Find("Rounded scree · visual surface");pebbles.localScale=Vector3.one;
        pebbles.GetComponent<MeshFilter>().sharedMesh=Surface("Compact rounded scree",vertices,faces);
    }

    static Mesh Reeds()
    {
        var vertices=new List<Vector3>();var faces=new List<int>();var random=new System.Random(156);
        float S()=> (float)random.NextDouble();
        for(int i=0;i<270;i++){
            var p=new Vector3((S()-.5f)*1.15f,0,(S()-.5f)*7);
            var tip=p+new Vector3((S()-.5f)*.8f,1.05f+S()*.84f,(S()-.5f)*.85f);
            var w=new Vector3(.025f+S()*.046f,0,(S()-.5f)*.1f);
            int n=vertices.Count;
            vertices.AddRange(new[]{p-w,p+w,tip,tip,p+w,p-w});
            faces.AddRange(new[]{n,n+1,n+2,n+3,n+4,n+5});
        }
        return Surface("Observation thicket",vertices,faces);
    }

    public static void Apply()
    {
        var sc=SceneManager.GetActiveScene();
        if(sc.path!="Assets/_Game/Scenes/Autumn_Baihu.unity" || Application.isPlaying)
            throw new System.InvalidOperationException("Open the Autumn scene in Edit Mode.");
        var trial=sc.GetRootGameObjects().Select(x=>x.GetComponent<AutumnTrial>()).First(x=>x);
        var root=trial.transform;
        var entrance=root.Find("01 · Concrete passage and shutter");
        entrance.localPosition=new Vector3(0,-42,0);
        var limits=root.Find("Walkable limits");limits.Find("Scene boundary").position=new Vector3(0,11,-20.1f);
        var approach=root.Find("02 · Stone embankment and semicircular clearing");
        var ramp=approach.GetComponentInChildren<MeshCollider>().transform;
        ramp.name="8m descent · compact stone embankment";
        ramp.localScale=new Vector3(12f/103,.16f,24f/103);
        approach.Find("Rounded scree · visual surface").localScale=ramp.localScale;
        FinishApproach(approach,ramp);
        // Full-height walkable portal sill spanning the rectangular mouth and the start of the slope.
        Remove(approach,"Portal landing");
        var landing=GameObject.CreatePrimitive(PrimitiveType.Cube);
        landing.name="Portal landing";landing.transform.SetParent(approach,false);
        landing.transform.localPosition=new Vector3(0,7.88f,.19f);landing.transform.localScale=new Vector3(6,.24f,.4f);
        landing.GetComponent<Renderer>().sharedMaterial=Asset<Material>("Scree bed.mat");
        trial.spawn.position=new Vector3(0,8.06f,-15);
        trial.hero.transform.position=trial.spawn.position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(trial.hero.transform);
        trial.view.transform.SetPositionAndRotation(new Vector3(0,10,-3),Quaternion.Euler(8,0,0));
        var live=root.Find("Living bamboo forest");
        var tiles=Enumerable.Range(0,6).Select(i=>Asset<Mesh>("BambooBank_"+i+".asset")).ToArray();
        var palette=new[]{"Ochre culms","Sunbleached culms","Olive culms","Bamboo nodes","Canopy","Dry grass"}.Select(x=>Asset<Material>(x+".mat")).ToArray();
        Remove(live,"Bamboo bank");Remove(limits,"Dense growth");Remove(root,"After victory · forest volumes");
        var floor=root.Find("Sand clearings").GetComponent<MeshFilter>();floor.sharedMesh=MakeSand();
        var arena=root.Find("04 · Sand arena and iron monument");arena.position=new Vector3(0,0,ArenaZ);
        for(int i=1;i<=4;i++)Remove(arena,"Footing fracture "+i);
        var monument=arena.GetComponentInChildren<BoxCollider>().transform.parent;
        monument.name="Iron monument";monument.localScale=new Vector3(2.65f,14f/4.8f,2.65f);
        float start=Mathf.Sqrt(Clearing*Clearing-HalfTrail*HalfTrail);
        float end=ArenaZ-Mathf.Sqrt(ArenaRadius*ArenaRadius-HalfTrail*HalfTrail);
        var strip=root.Find("Straight sandy trail");strip.position=new Vector3(0,.005f,(start+end)*.5f);strip.localScale=new Vector3(5.9f,.012f,end-start+.4f);
        int serial=0;
        void Bank(Vector3 p,float angle,float length)
        {
            // Open a few windows into the stalks so the face and shoulders read from the path.
            bool lookout=new[]{74f,182f,283f}.Select((z,i)=>Mathf.Abs(z-p.z)<5.8f && Mathf.Sign(p.x)==(i==1?1:-1)).Any(x=>x);
            Vector3 outwards=Quaternion.Euler(0,angle,0)*Vector3.right;
            var t=Model(live,"Bamboo bank",tiles[(serial++*5)%6],palette,p+(lookout?outwards*3.6f:Vector3.zero),angle);
            t.localScale=new Vector3(1,1,length/12);
            var wall=Group("Dense growth",limits);wall.SetPositionAndRotation(p+outwards*.85f+Vector3.up*2,Quaternion.Euler(0,angle,0));
            wall.gameObject.AddComponent<BoxCollider>().size=new Vector3(1.7f,5,length+.12f);
        }
        void Arc(float z,float r,float a,float b)
        {
            int n=Mathf.CeilToInt((b-a)*Mathf.Deg2Rad*r/11);
            float step=(b-a)*Mathf.Deg2Rad/n;
            for(int i=0;i<n;i++){float t=a*Mathf.Deg2Rad+(i+.5f)*step,ap=r*Mathf.Cos(step/2);
                Bank(new Vector3(ap*Mathf.Cos(t),0,z+ap*Mathf.Sin(t)),-t*Mathf.Rad2Deg,2*r*Mathf.Sin(step/2));}
        }
        float gap=Mathf.Asin(HalfTrail/Clearing)*Mathf.Rad2Deg;
        Arc(0,Clearing,0,90-gap);Arc(0,Clearing,90+gap,180);
        int strips=Mathf.CeilToInt((end-start)/12);float stepZ=(end-start)/strips;
        for(int i=0;i<strips;i++)foreach(int side in new[]{-1,1})
            Bank(new Vector3(side*HalfTrail,0,start+(i+.5f)*stepZ),side==1?0:180,stepZ);
        gap=Mathf.Asin(HalfTrail/ArenaRadius)*Mathf.Rad2Deg;Arc(ArenaZ,ArenaRadius,-90+gap,270-gap);
        // Reuse previously authored distant tree clusters. Keep their full mesh bounds outside the arena.
        foreach(string n in new[]{"Outer canopy · layered crowns","Distant canopy","Understorey · inner forest"}){
            var layer=live.Find(n);
            foreach(Transform t in layer.Cast<Transform>().ToArray()){
                var b=t.GetComponent<Renderer>().bounds;var q=b.ClosestPoint(new Vector3(0,b.center.y,ArenaZ));
                if(new Vector2(q.x,q.z-ArenaZ).magnitude < ArenaRadius+1)Object.DestroyImmediate(t.gameObject);
            }
        }
        var actors=trial.hero.transform.parent;Remove(actors,"Single hidden threat");
        if(trial.tiger)Object.DestroyImmediate(trial.tiger.gameObject);
        Remove(actors,"Observation positions");
        var positions=Group("Observation positions",actors);
        Transform Station(string n,Vector3 at,float yaw){var t=Group(n,positions);t.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));return t;}
        trial.observationPoints=new[]{
            Station("Trail · left thicket",new Vector3(-6.05f,0,74),139),
            Station("Trail · right thicket",new Vector3(6.05f,0,182),-139),
            Station("Trail · left bend of stems",new Vector3(-6.05f,0,283),139)};
        Remove(live,"Watchpoint reeds");var reeds=Group("Watchpoint reeds",live);var patch=Reeds();
        foreach(var station in trial.observationPoints){
            int side=station.position.x<0?-1:1;
            Model(reeds,"Dry grass screen",patch,new[]{palette[5]},new Vector3(side*3.15f,0,station.position.z-1.35f),side*9);
        }
        trial.arenaPoint=Station("Arena · scale reference",new Vector3(-16,0,ArenaZ-4),121);
        var model=Asset<GameObject>("BaihuScale.obj");
        var cat=(GameObject)PrefabUtility.InstantiatePrefab(model,actors);
        cat.name="Baihu · observing";trial.tiger=cat.transform;
        cat.transform.SetPositionAndRotation(trial.observationPoints[0].position,trial.observationPoints[0].rotation);
        foreach(var r in cat.GetComponentsInChildren<MeshRenderer>()){
            var mats=r.sharedMaterials;
            for(int i=0;i<mats.Length;i++){
                string name=mats[i]?mats[i].name:"Fur";
                mats[i]=Asset<Material>((name.Contains("Stripe")?"Basal fractures":name.Contains("Eye")?"Red eyes":"Porcelain pelt")+".mat");
            }
            r.sharedMaterials=mats;PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(cat.transform);
        EditorUtility.SetDirty(trial);EditorSceneManager.MarkSceneDirty(sc);
        AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(sc))throw new System.Exception("Save failed");
    }
}
