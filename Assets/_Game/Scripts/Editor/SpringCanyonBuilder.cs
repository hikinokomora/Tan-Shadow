using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using TanShadow.Spring;

public static class SpringCanyonBuilder
{
    const string Dir="Assets/_Game/Art/SpringBlockout";
    const string ScenePath="Assets/_Game/Scenes/Spring_Qinglong.unity";
    const int Length=690;
    const int DressedLength=Length-18;
    static Material[] rock;
    static Material soil, egg, shelf, concrete, water, moss, bloom, pink;

    static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
    // Plan of the ravine, traced from the approach towards the far end.
    static readonly Vector2[] Plan={
        new Vector2(0,0),new Vector2(0,4),new Vector2(-5,17),new Vector2(-18,43),
        new Vector2(-20,57),new Vector2(-13,66),new Vector2(6,73),new Vector2(22,91),
        new Vector2(31,110),new Vector2(28,126),new Vector2(10,138),new Vector2(-4,152),
        new Vector2(-20,185),new Vector2(-26,204),new Vector2(-25,211),new Vector2(-14,229),
        new Vector2(1,250),new Vector2(18,278),new Vector2(25,298),new Vector2(27,317)
    };
    static readonly Vector3[] path=MakePath();
    static Vector3[] MakePath(){
        const int samples=32;
        var points=new List<Vector2>{Plan[0]};var length=new List<float>{0};
        for(int i=0;i<Plan.Length-1;i++){
            Vector2 a=Plan[Mathf.Max(0,i-1)],b=Plan[i],c=Plan[i+1],d=Plan[Mathf.Min(Plan.Length-1,i+2)];
            for(int j=1;j<=samples;j++){
                float t=j/(float)samples;
                var q=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
                length.Add(length[length.Count-1]+Vector2.Distance(points[points.Count-1],q));points.Add(q);
            }
        }
        var line=new Vector3[Length+1];float total=length[length.Count-1];int ix=1;
        for(int i=0;i<=Length;i++){
            float distance=i*total/Length;
            while(ix<length.Count-1 && length[ix]<distance)ix++;
            var v=Vector2.Lerp(points[ix-1],points[ix],Mathf.InverseLerp(length[ix-1],length[ix],distance))*Length/total;
            line[i]=V(v.x,0,v.y);
        }
        return line;
    }
    static Vector3 Center(float s){
        s=Mathf.Clamp(s,0,Length);
        int i=Mathf.Min(Mathf.FloorToInt(s),Length-1);
        return Vector3.Lerp(path[i],path[i+1],s-i);
    }
    static Vector3 Across(float s) => Vector3.Cross(Vector3.up,Center(s+.5f)-Center(s-.5f)).normalized;
    static Vector3 P(float s,float x,float y=0) => Center(s)+Across(s)*x+Vector3.up*y;
    static float Width(float s,int side){
        float natural=(5.18f+.47f*Mathf.Sin(s*.057f+side*.8f)+.27f*Mathf.Sin(s*.123f+side))*2;
        float d=Mathf.Max(0,Length-s);
        const float radius=1.6f;
        // A tangent circular fillet closes the two 45-degree faces without a hard corner.
        if(d<radius*(1-.70710678f))return Mathf.Sqrt(Mathf.Max(0,2*radius*d-d*d));
        float wedge=d+radius*(Mathf.Sqrt(2)-1);
        float blend=Mathf.Max(0,2-Mathf.Abs(natural-wedge));
        return Mathf.Min(natural,wedge)-blend*blend/8;
    }
    static float Rim(float s,int side)=>8f*(14.4f+1.25f*Mathf.Sin(s*.028f+side)+.55f*Mathf.Sin(s*.082f+side*.4f));
    static Transform Group(string name,Transform parent=null){
        var o=new GameObject(name);o.transform.SetParent(parent,false);return o.transform;
    }
    static Material Mat(string name,string hex,float smooth=.18f){
        var existing=AssetDatabase.LoadAssetAtPath<Material>(Dir+"/"+name+".mat");if(existing)return existing;
        var m=new Material(Shader.Find("HDRP/Lit"));ColorUtility.TryParseHtmlString("#"+hex,out var c);
        m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);m.enableInstancing=true;AssetDatabase.CreateAsset(m,Dir+"/"+name+".mat");return m;
    }
    class Surface
    {
        public List<Vector3> v=new List<Vector3>(); readonly List<int>[] sub;
        public Surface(int slots=1){sub=new List<int>[slots];for(int i=0;i<slots;i++)sub[i]=new List<int>();}
        public int Pt(Vector3 p){v.Add(p);return v.Count-1;}
        public void Tri(int a,int b,int c,int slot=0){sub[slot].Add(a);sub[slot].Add(b);sub[slot].Add(c);}
        public void Quad(int a,int b,int c,int d,int slot=0){Tri(a,b,c,slot);Tri(a,c,d,slot);}
        public Mesh Bake(string name){
            string assetPath=Dir+"/"+name+".asset";
            var m=AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            bool exists=m!=null;
            if(exists)m.Clear();else m=new Mesh{name=name};
            m.indexFormat=v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            m.SetVertices(v);m.subMeshCount=sub.Length;
            for(int i=0;i<sub.Length;i++)m.SetTriangles(sub[i],i);m.RecalculateNormals();m.RecalculateBounds();
            if(exists)EditorUtility.SetDirty(m);else AssetDatabase.CreateAsset(m,assetPath);return m;
        }
    }
    static Transform Model(Transform par,string name,Mesh mesh,Material[] mats,bool collider){
        var t=par.Find(name);
        if(!t)t=Group(name,par);
        var filter=t.GetComponent<MeshFilter>(); if(!filter)filter=t.gameObject.AddComponent<MeshFilter>();
        var renderer=t.GetComponent<MeshRenderer>(); if(!renderer)renderer=t.gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh=mesh;renderer.sharedMaterials=mats;
        if(collider){
            var shape=t.GetComponent<MeshCollider>(); if(!shape)shape=t.gameObject.AddComponent<MeshCollider>();
            shape.sharedMesh=null;shape.sharedMesh=mesh;
        }
        return t;
    }
    static Transform Box(Transform par,string name,Vector3 p,Vector3 size,Material mat){
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(par,false);
        o.transform.localPosition=p;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=mat;return o.transform;
    }
    static void Wall(Transform par,int side){
        var g=new Surface(5);float[] levels={-.025f,.09f,.23f,.39f,.58f,.78f,1f};
        // Above the shelter band, the inner face only recedes towards the rim.
        var stations=new List<float>();
        for(float s=0;s<Length-18;s+=1.5f)stations.Add(s);
        for(int j=0;j<=144;j++)stations.Add(Length-18+j*.125f);
        for(int i=0;i<stations.Count;i++){
            float s=stations[i],w=Width(s,side);
            float vertical=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Length-20,Length-12,s));
            float h=Mathf.Lerp(Rim(s,side),.5f*(Rim(s,1)+Rim(s,-1)),vertical);
            for(int k=0;k<levels.Length;k++){
                float y=h*levels[k]+(k>0&&k<6?.33f*Mathf.Sin(s*.28f+k):0);
                float setback=k<3?.08f*k:.24f+(k-3)*.33f;
                g.Pt(P(s,side*(w+setback*(1-vertical)),y));
            }
            g.Pt(P(s,side*(w+4.5f),h-.3f));
            if(i==0)continue;
            for(int j=0;j<7;j++){
                int a=(i-1)*8+j,b=i*8+j;
                int tone=j==6?4:new[]{0,1,0,2,1,3}[j];
                if(side<0)g.Quad(a,a+1,b+1,b,tone);else g.Quad(b,b+1,a+1,a,tone);
            }
        }
        string label=side<0?"West rock face":"East rock face";Model(par,label,g.Bake(label),rock,true);
    }
    static Mesh Egg(){
        var g=new Surface();const int n=32,rows=20;
        int bottom=g.Pt(Vector3.down);
        for(int i=1;i<rows;i++){
            float a=Mathf.PI*i/rows,h=-Mathf.Cos(a);
            // Full belly low down, narrower shoulder and rounded crown.
            float radius=Mathf.Sin(a)*(1-.24f*h);
            for(int j=0;j<n;j++){float t=2*Mathf.PI*j/n;g.Pt(V(radius*Mathf.Cos(t),h,radius*Mathf.Sin(t)));}
        }
        int top=g.Pt(Vector3.up);
        for(int j=0;j<n;j++){int next=(j+1)%n;g.Tri(bottom,1+j,1+next);
            for(int i=0;i<rows-2;i++){int a=1+i*n+j,b=1+i*n+next;g.Quad(a,a+n,b+n,b);}
            g.Tri(1+(rows-2)*n+j,top,1+(rows-2)*n+next);
        }return g.Bake("Smooth asymmetric egg");
    }
    static void Ledge(Transform par,float s,int side,float span,int id,float height=2.45f,float depth=1){
        var g=new Surface(2);
        // Convex shelves are embedded at the heel and taper towards the lip.
        Vector2[] footprint={new Vector2(-.95f,-.51f),new Vector2(.58f,-.46f),new Vector2(2.05f,-.22f),
            new Vector2(2.55f,.02f),new Vector2(2.1f,.32f),new Vector2(.57f,.51f),new Vector2(-.95f,.51f)};
        float clearance=height+.16f*Mathf.Sin(id*2);
        for(int k=0;k<3;k++)foreach(var uv in footprint){
            float u01=Mathf.InverseLerp(-.95f,2.55f,uv.x);
            float tip=uv.x;
            float along=uv.y;
            switch(id%4){
                case 0: tip*=.65f+.65f*(uv.y+.5f);along+=u01*.20f;break;
                case 1: tip*=1.03f-.38f*Mathf.Abs(uv.y);along-=u01*.24f;break;
                case 2: tip*=.74f+.25f*Mathf.Cos(uv.y*6);break;
                case 3: tip+=.52f*Mathf.Sin(uv.y*7)*u01;along+=u01*.11f;break;
            }
            float inwards=tip, u=k==1?inwards:inwards*.89f-.10f;
            float thickness=.73f+(id%5)*.32f;
            float y=clearance+(k==0?0:k==1?.44f:1)*thickness-.17f*inwards+along*(id%3-.9f);
            g.Pt(P(s+along*span,side*(Width(s+along*span,side)+(height>10?.38f:0)-u*depth),y));
        }
        int n=footprint.Length;
        for(int i=0;i<n;i++){int j=(i+1)%n;
            for(int k=0;k<2;k++){int a=k*n+i,b=k*n+j;
                if(side>0)g.Quad(a,b,b+n,a+n);else g.Quad(a,a+n,b+n,b);}
        }
        for(int i=1;i<n-1;i++){
            if(side>0){g.Tri(0,i+1,i,1);g.Tri(2*n,2*n+i,2*n+i+1);}
            else{g.Tri(0,i,i+1,1);g.Tri(2*n,2*n+i+1,2*n+i);}
        }
        // Orient closed hull faces consistently outwards.
        string name="Shelf "+id.ToString("00");var m=g.Bake(name);
        Vector3 center=m.bounds.center;for(int slot=0;slot<m.subMeshCount;slot++){
            var ix=m.GetTriangles(slot);for(int i=0;i<ix.Length;i+=3){
                var a=g.v[ix[i]];var b=g.v[ix[i+1]];var c=g.v[ix[i+2]];
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3-center)<0){int t=ix[i+1];ix[i+1]=ix[i+2];ix[i+2]=t;}
            }m.SetTriangles(ix,slot);
        }m.RecalculateNormals();EditorUtility.SetDirty(m);
        Model(par,name,m,new[]{shelf,rock[0]},true);
    }
    static void Floor(Transform par){
        var g=new Surface(3);for(int z=0;z<=Length;z++){
            for(int x=-7;x<=7;x++){float h=.035f*Mathf.Sin(x*.75f+z*.22f)*Mathf.Sin(z*.23f);
                float side=x<0?-1:1;
                float half=Width(z,(int)side);
                g.Pt(P(z,x/7f*(half+.04f*Mathf.Clamp01(Length-z)),h));}
            if(z>0)for(int j=0;j<14;j++){
                int a=(z-1)*15+j;
                int material=z<Length-18?0:(j+z/3)%5<2?2:0;
                if(z==Length)g.Tri(a,a+15,a+1,material);else g.Quad(a,a+15,a+16,a+1,material);}
        }
        Model(par,"Continuous canyon bed",g.Bake("Canyon bed"),new[]{soil,rock[0],rock[2]},true);
    }
    static void PoolsAndBanks(Transform par){
        var pools=new Surface();var flowers=new Surface(3);var rnd=new System.Random(991);
        float F()=> (float)rnd.NextDouble();int idx=0;
        for(float s=4;s<DressedLength-2;s+=9.7f+2.6f*Mathf.Sin(idx*1.82f)){
            float x=2.6f*Mathf.Sin(idx++*2.6f);int c=pools.Pt(P(s,x,.065f));
            for(int j=0;j<32;j++){
                float a=j*Mathf.PI/16,r=1+.13f*Mathf.Sin(3*a+idx)+.07f*Mathf.Sin(5*a);
                pools.Pt(P(s+Mathf.Sin(a)*3.4f*r,x+Mathf.Cos(a)*2.45f*r,.065f));}
            for(int j=0;j<32;j++)pools.Tri(c,c+1+(j+1)%32,c+1+j);
        }
        for(int i=0;i<460;i++){
            float s=2+F()*(DressedLength-4);int side=F()<.5f?-1:1;float x=side*(4.3f+F()*4.7f);float spread=.35f+F()*.72f;
            int mid=flowers.Pt(P(s,x,.17f));int n=12;
            for(int k=0;k<n;k++){float a=k*2*Mathf.PI/n;flowers.Pt(P(s+Mathf.Sin(a)*spread*1.7f,x+Mathf.Cos(a)*spread,.035f));}
            for(int k=0;k<n;k++)flowers.Tri(mid,mid+1+(k+1)%n,mid+1+k);
            for(int j=0;j<16;j++){float a=F()*6.28f,r=F()*spread;var p=P(s+Mathf.Sin(a)*r*1.6f,x+Mathf.Cos(a)*r,.19f+(1-r/spread)*.045f);float size=.055f+F()*.068f;
                int core=flowers.Pt(p);int mat=F()<.3f?2:1;
                for(int k=0;k<8;k++){float ang=k*Mathf.PI/4;float l=size*(k%2==0?1:.42f);flowers.Pt(p+V(Mathf.Cos(ang)*l,.04f,Mathf.Sin(ang)*l));}
                for(int k=0;k<8;k++)flowers.Tri(core,core+1+(k+1)%8,core+1+k,mat);
            }
        }
        Model(par,"Ankle-deep pools",pools.Bake("Shallow pool patches"),new[]{water},false);
        Model(par,"Flowering islets",flowers.Bake("Flower banks"),new[]{moss,bloom,pink},false);
    }
    static void Sun(Transform parent,string name,Color c,float lux,Vector3 rotation,bool shadow){
        var t=Group(name,parent);var l=t.gameObject.AddComponent<Light>();l.type=LightType.Directional;t.gameObject.AddComponent<HDAdditionalLightData>();
        l.lightUnit=LightUnit.Lux;l.intensity=lux;l.color=c;l.shadows=shadow?LightShadows.Soft:LightShadows.None;t.rotation=Quaternion.Euler(rotation);
    }
    public static void Build(){
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))throw new System.InvalidOperationException("Spring scene already exists");
        if(!AssetDatabase.IsValidFolder(Dir))AssetDatabase.CreateFolder("Assets/_Game/Art","SpringBlockout");
        var sc=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(sc);
        var root=Group("Spring · Qinglong");
        soil=Mat("Slate bed","697A82");rock=new[]{Mat("Deep blue slate","455B6D"),Mat("Indigo strata","576E81"),Mat("Grey blue strata","657A87"),Mat("Upper shale","788C97"),Mat("Cliff crest","8A999E")};
        shelf=Mat("Shelter lip","657780");egg=Mat("Smooth riverstone","9AA4A6",.42f);water=Mat("Shallow silver water","719AA1",.84f);
        moss=Mat("Spring ground cover","82937D");bloom=Mat("Blush flowers","E2B0BE");pink=Mat("Heather flowers","BA89BD");concrete=Mat("Cast concrete","969998");
        var tunnel=Group("01 · Concrete approach",root);
        Box(tunnel,"Floor",V(0,-.25f,-7),V(5,.5f,14),concrete);
        foreach(int side in new[]{-1,1}){
            Box(tunnel,"Side wall",V(side*2.6f,2.3f,-7),V(.45f,4.6f,14),concrete);
            Box(tunnel,"Portal cheek",V(side*4.35f,6.7f,-.6f),V(3.9f,13.4f,1.2f),rock[1]);}
        Box(tunnel,"Ceiling",V(0,4.8f,-7),V(5.7f,.5f,14),concrete);
        Box(tunnel,"Lintel",V(0,8.9f,-.65f),V(5,8.6f,1.3f),rock[1]);
        Box(tunnel,"Hub-side slab",V(0,2.35f,-14),V(5,4.7f,.5f),concrete);
        var canyon=Group("02 · Continuous bed and cliffs",root);Floor(canyon);Wall(canyon,-1);Wall(canyon,1);
        var end=Box(canyon,"Headwall",P(90,0,6.7f),V(15,14.2f,1.7f),rock[1]);end.rotation=Quaternion.Euler(0,-6,0);
        var shelves=Group("03 · Wall shelters",root);float[] station={6,16,28,38,48,60,72,84};
        for(int i=0;i<station.Length;i++)Ledge(shelves,station[i],i%2==0?-1:1,4.3f+(i%3)*.75f,i+1);
        var stones=Group("04 · Freestanding tilted boulders",root);var shape=Egg();
        float[] spacing={11,22,33,43,54,66,78};float[] tilt={31,27,35,24,32,29,34};
        for(int i=0;i<spacing.Length;i++){
            float s=spacing[i];int side=i%2==0?1:-1;var t=Model(stones,"Egg boulder "+(i+1).ToString("00"),shape,new[]{egg},false);
            t.localScale=V(1.08f+.09f*(i%3),1.84f+.14f*(i%2),1.12f+.08f*(i%2));
            t.rotation=Quaternion.Euler(-tilt[i],0,0);
            float bottom=100;foreach(var p in shape.vertices)bottom=Mathf.Min(bottom,t.TransformVector(p).y);
            t.position=P(s,side*2.2f,-bottom-.19f);t.gameObject.AddComponent<MeshCollider>().sharedMesh=shape;
        }
        PoolsAndBanks(Group("05 · Shallows and spring banks",root));
        var guide=Group("06 · Dragon flight reference points (editor)",root);
        for(int s=0;s<=90;s+=5){var t=Group("Upper third · "+s+"m",guide);t.position=P(s,0,Mathf.Min(Rim(s,-1),Rim(s,1))*2/3);}
        var rig=Group("Walkthrough",root);rig.position=V(0,.07f,-7);
        var body=rig.gameObject.AddComponent<CharacterController>();body.height=1.75f;body.radius=.32f;body.center=Vector3.up*.875f;body.stepOffset=.25f;
        var eyes=Group("Main Camera",rig);eyes.tag="MainCamera";eyes.localPosition=Vector3.up*1.62f;
        var cam=eyes.gameObject.AddComponent<Camera>();cam.fieldOfView=68;cam.nearClipPlane=.08f;cam.farClipPlane=320;
        var hd=eyes.gameObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=new Color(.70f,.84f,.97f);
        eyes.gameObject.AddComponent<AudioListener>();rig.gameObject.AddComponent<SpringWalkthrough>().eyes=eyes;
        var lights=Group("Daylight",root);Sun(lights,"High morning light",new Color(1,.92f,.84f),8600,V(69,-28,0),true);
        Sun(lights,"Canyon bounce",new Color(.68f,.83f,1),2600,V(32,135,0),false);
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Dir+"/Spring daylight.asset");
        var ex=profile.Add<Exposure>();ex.mode.Override(ExposureMode.Fixed);ex.fixedExposure.Override(10);AssetDatabase.AddObjectToAsset(ex,profile);
        var blur=profile.Add<MotionBlur>();blur.intensity.Override(0);AssetDatabase.AddObjectToAsset(blur,profile);EditorUtility.SetDirty(profile);
        var volume=lights.gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=30;volume.sharedProfile=profile;
        AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(sc,ScenePath))throw new System.Exception("Save failed");
    }
    static void OrientBoulder(Transform stone, float station)
    {
        float tilt = Vector3.Angle(Vector3.up, stone.up);
        // A negative pitch leans towards local -Z, back along the player's route.
        stone.rotation = Quaternion.Euler(-tilt, 0, 0);
    }

    static void AddCanyonDetail(Transform root)
    {
        var detail = Group("07 · Rock fragments, water edges and spring growth", root);
        var rocks = root.GetChild(1).GetChild(1).GetComponent<MeshRenderer>().sharedMaterials;
        var banks = root.GetChild(4).GetChild(1).GetComponent<MeshRenderer>().sharedMaterials;
        var soilMaterial = root.GetChild(1).GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        var rng = new System.Random(25118);
        float Pick(float a, float b) => Mathf.Lerp(a,b,(float)rng.NextDouble());
        var talus = new Surface(3);
        var growth = new Surface(3);
        var gravel = new Surface(2);
        void Stone(Surface mesh, Vector3 center, float radius, float height, int slot)
        {
            int n = mesh.v.Count;
            float twist = Pick(0,6.28f);
            for(int j=0;j<6;j++){
                float a=twist+j*Mathf.PI/3;float r=radius*Pick(.81f,1.2f);
                mesh.Pt(center+V(Mathf.Cos(a)*r,-.025f,Mathf.Sin(a)*r));
            }
            int peak=mesh.Pt(center+V(radius*Pick(-.3f,.3f),height,radius*Pick(-.3f,.3f)));
            for(int j=0;j<6;j++)mesh.Tri(n+j,peak,n+(j+1)%6,slot);
        }
        for (int side=-1;side<=1;side+=2)
        {
            for (int i=0;i<138;i++)
            {
                float s=2.4f+i*4.81f+Pick(-.75f,.65f);
                float width=Width(s,side);
                float size=Pick(.25f,.8f);
                Stone(talus,P(s,side*(width-.26f)),size,Pick(.15f,.70f),i%3);
                for(int j=0;j<11;j++){
                    float localS=s+Pick(-1.3f,1.3f);float x=side*(Width(localS,side)-Pick(.35f,1.2f));
                    float pebble=Pick(.045f,.17f);
                    Stone(gravel,P(localS,x,.024f),pebble,pebble*.48f,j%2);
                }
            }
            for (int i=0;i<220;i++)
            {
                float s=2+Pick(0,DressedLength-4);
                for(int j=0;j<9;j++){
                    float local=s+Pick(-.4f,.4f),x=side*(Width(local,side)-Pick(.62f,1.45f));
                    var p=P(local,x,.02f);
                    int n=growth.v.Count;float height=Pick(.12f,.35f);float leaf=Pick(.025f,.06f);
                    growth.Pt(p-V(leaf,0,0));growth.Pt(p+V(leaf,0,0));growth.Pt(p+V(Pick(-.09f,.09f),height,Pick(-.1f,.1f)));
                    growth.Tri(n,n+1,n+2);growth.Tri(n+2,n+1,n);
                    if(j%3!=0)continue;
                    int top=growth.Pt(growth.v[n+2]);float radius=Pick(.066f,.11f);
                    for(int k=0;k<10;k++){
                        float a=k*Mathf.PI/5;float r=radius*(k%2==0?1:.52f);
                        growth.Pt(growth.v[top]+V(Mathf.Cos(a)*r,.023f,Mathf.Sin(a)*r));
                    }
                    for(int k=0;k<10;k++)growth.Tri(top,top+1+(k+1)%10,top+1+k,1+i%2);
                }
            }
        }
        Model(detail,"Slope debris at cliff bases",talus.Bake("Spring talus"),new[]{rocks[0],rocks[1],rocks[2]},false);
        Model(detail,"Pebble patches along the banks",gravel.Bake("Bank gravel"),new[]{soilMaterial,rocks[2]},false);
        Model(detail,"Upright spring stems and flowers",growth.Bake("Bank stems"),banks,false);

        var pools=root.GetChild(4).GetChild(0).GetComponent<MeshFilter>().sharedMesh.vertices;
        var waterEdge=new Surface();
        for(int first=0;first<pools.Length;first+=33){
            var center=pools[first];int n=waterEdge.v.Count;
            for(int j=1;j<=32;j++){
                var radial=pools[first+j]-center;
                waterEdge.Pt(center+radial*.98f-Vector3.up*.007f);
                waterEdge.Pt(center+radial*1.065f-Vector3.up*.018f);
            }
            for(int j=0;j<32;j++){
                int a=n+2*j,b=n+2*((j+1)%32);waterEdge.Quad(a,b,b+1,a+1);
            }
        }
        Model(detail,"Wet stone along pool edges",waterEdge.Bake("Pool margins"),new[]{rocks[2]},false);

        // Shadowed seams and inset joints keep the portal readable from inside the tunnel.

        for(int i=0;i<5;i++)
        {
            float z=-12+i*2.45f;
            foreach(int side in new[]{-1,1}){
                var seam=Box(detail,"Formwork joint",V(side*2.37f,2.3f,z),V(.018f,4.5f,.027f),rocks[2]);
                Object.DestroyImmediate(seam.GetComponent<Collider>());
            }
        }
    }

    public static void RefineOpenScene()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath || Application.isPlaying)throw new System.InvalidOperationException("Open Spring in Edit Mode.");
        var root=scene.GetRootGameObjects()[0].transform;
        var previous=root.GetComponentInChildren<SpringWalkthrough>();
        if(!previous)throw new System.InvalidOperationException("Spring already has a character.");
        var camera=previous.GetComponentInChildren<Camera>();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/Player.prefab");
        if(!camera || !prefab)throw new System.InvalidOperationException("Camera or player is missing.");
        float[] stations={11,22,33,43,54,66,78};
        var stones=root.GetChild(3);
        for(int i=0;i<stations.Length;i++)OrientBoulder(stones.GetChild(i),stations[i]);

        var player=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
        player.transform.SetPositionAndRotation(previous.transform.position,previous.transform.rotation);
        camera.transform.SetParent(root,true);
        camera.transform.SetPositionAndRotation(player.transform.position+V(0,2.35f,-4.03f),Quaternion.Euler(12,0,0));
        var follow=camera.gameObject.AddComponent<SpringFollowCamera>();
        follow.player=player.transform;
        var controller=player.GetComponent<TanShadow.Player.PlayerController>();
        controller.cameraTransform=camera.transform;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        Object.DestroyImmediate(previous.gameObject);

        AddCanyonDetail(root);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new System.InvalidOperationException("Spring save failed.");
    }

    static void AddRockVegetation(Transform root, Transform shelves)
    {
        var foliage = Group("08 · Shelf trees and cliff vegetation",root);
        var trunks=Mat("Young tree bark","645044");
        var leaf=Mat("Cliff foliage","52685C");
        var young=Mat("Young spring leaves","8C9C69");
        var flower=Mat("Spring tree blossoms","BB7E9D");
        var trees=new Surface(4);
        var wallGreenery=new Surface(3);
        var random=new System.Random(251219);
        float Pick(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());

        void Branch(Surface mesh,Vector3 a,Vector3 b,float low,float high,int material)
        {
            var axis=(b-a).normalized;
            var u=Vector3.Cross(axis,Vector3.forward).normalized;
            var v=Vector3.Cross(axis,u);
            int start=mesh.v.Count;
            for(int j=0;j<6;j++){
                float angle=j*Mathf.PI/3f;var d=u*Mathf.Cos(angle)+v*Mathf.Sin(angle);
                mesh.Pt(a+d*low);mesh.Pt(b+d*high);
            }
            for(int j=0;j<6;j++){
                int p=start+j*2,q=start+((j+1)%6)*2;mesh.Quad(p,q,q+1,p+1,material);
            }
        }
        void Crown(Surface mesh,Vector3 center,float radius,float height,int material)
        {
            int n=mesh.v.Count;
            mesh.Pt(center+Vector3.up*height);
            mesh.Pt(center-Vector3.up*height*.62f);
            for(int j=0;j<8;j++){
                float a=j*Mathf.PI/4f;float r=radius*Pick(.86f,1.12f);
                mesh.Pt(center+V(Mathf.Cos(a)*r,Pick(-.24f,.18f)*height,Mathf.Sin(a)*r));
            }
            for(int j=0;j<8;j++){
                int a=n+2+j,b=n+2+(j+1)%8;mesh.Tri(n,b,a,material);mesh.Tri(n+1,a,b,material);
            }
        }

        int shelfNumber=0;
        foreach(Transform shelfObject in shelves){
            var vertices=shelfObject.GetComponent<MeshFilter>().sharedMesh.vertices;
            // Top ring indices come from Ledge; points 3 and 4 are at the exposed lip.
            var top=Vector3.Lerp(vertices[17],vertices[18],.45f);
            var planting=Vector3.Lerp(top,(vertices[14]+vertices[20])*.5f,.30f);
            planting.y-=.18f;
            float height=Pick(1.15f,2.45f);
            var tip=planting+V(Pick(-.34f,.34f),height,Pick(-.24f,.24f));
            var split=Vector3.Lerp(planting,tip,.58f);
            Branch(trees,planting,split,.115f,.07f,0);
            Branch(trees,split,tip,.074f,.024f,0);
            int inflorescence=shelfNumber++%4==0?3:2;
            Crown(trees,tip,.62f+height*.18f,.55f,inflorescence);
            for(int j=0;j<3;j++){
                float a=j*2.09f;
                var end=tip+V(Mathf.Cos(a)*height*.37f,-Pick(.17f,.46f),Mathf.Sin(a)*height*.37f);
                Branch(trees,split,end,.045f,.015f,0);Crown(trees,end,Pick(.49f,.76f),.39f,j==1?1:inflorescence);
            }
        }

        for(int side=-1;side<=1;side+=2)
        for(int t=0;t<203;t++){
            float s=3+t*3.25f+Pick(-.35f,.38f);
            float y=Pick(4,Rim(s,side)*.49f);
            var outwards=Across(s)*side;
            var cap=P(s,side*(Width(s,side)+(y>Rim(s,side)*.39f?.40f:.07f)),y);
            float lineLength=Pick(.7f,3.5f);
            for(int j=0;j<3;j++){
                var start=cap+V(0,.08f*j,Pick(-.29f,.29f));
                var end=start-outwards*Pick(.16f,.58f)-Vector3.up*lineLength*Pick(.6f,1);
                Branch(wallGreenery,start,end,.031f,.014f,0);
                for(int i=0;i<4;i++){
                    var at=Vector3.Lerp(start,end,(i+.35f)/4)-outwards*.13f;
                    Crown(wallGreenery,at,Pick(.12f,.35f),Pick(.12f,.25f),i%3==0?2:1);
                }
            }
        }
        Model(foliage,"Small trees on shelves",trees.Bake("Shelf trees"),new[]{trunks,leaf,young,flower},false);
        Model(foliage,"Trailing growth on rock faces",wallGreenery.Bake("Cliff vines"),new[]{trunks,leaf,young},false);
    }

    public static void RefreshEscarpment()
    {
        var scene=SceneManager.GetActiveScene();
        var canyon=scene.GetRootGameObjects()[0].transform.GetChild(1);
        soil=canyon.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        rock=canyon.GetChild(1).GetComponent<MeshRenderer>().sharedMaterials;
        Floor(canyon);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void ExpandOpenScene()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath || Application.isPlaying)throw new System.InvalidOperationException("Open Spring in Edit Mode.");
        var root=scene.GetRootGameObjects()[0].transform;
        var canyon=root.GetChild(1);
        var canyonBed=canyon.GetChild(0);
        soil=canyonBed.GetComponent<MeshRenderer>().sharedMaterial;
        rock=canyon.GetChild(1).GetComponent<MeshRenderer>().sharedMaterials;
        var shelves=root.GetChild(2);shelf=shelves.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        var pools=root.GetChild(4);
        water=pools.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        var banks=pools.GetChild(1).GetComponent<MeshRenderer>().sharedMaterials;
        moss=banks[0];bloom=banks[1];pink=banks[2];

        Floor(canyon);Wall(canyon,-1);Wall(canyon,1);
        var headwall=canyon.Find("Headwall");if(headwall)Object.DestroyImmediate(headwall.gameObject);
        var block=root.GetChild(0);
        foreach(Transform piece in block){
            if(piece.name!="Portal cheek")continue;
            float side=Mathf.Sign(piece.localPosition.x);
            piece.localScale=V(10.3f,104f,2f);
            piece.localPosition=V(side*7.55f,51.7f,-.8f);
        }
        var lintel=block.Find("Lintel");lintel.localPosition=V(0,53.75f,-.70f);lintel.localScale=V(5,98.3f,1.3f);

        while(shelves.childCount>0)Object.DestroyImmediate(shelves.GetChild(shelves.childCount-1).gameObject);
        float[] stops={14,54,99,147,204,232,287,335,391,419,483,535,572,617,658};
        for(int i=0;i<stops.Length;i++){
            int side=i%5==1||i%5==3||i%5==4?1:-1;
            float height=i%4==0?3.3f:i%4==1?11f:i%4==2?24f:39f;
            Ledge(shelves,stops[i],side,4f+(i*7%11),i+1,height+.6f*Mathf.Sin(i*1.91f),.84f+(i*3%7)*.29f);
        }
        var stones=root.GetChild(3);var template=stones.GetChild(0);
        float station=9;
        for(int i=0;station<DressedLength-7;i++){
            var stone=i<stones.childCount?stones.GetChild(i):Object.Instantiate(template,stones,false);
            stone.name="Egg boulder "+(i+1).ToString("00");
            float tilt=27+(i*7)%12;
            stone.localScale=V(1.68f+.14f*(i%3),3.02f+.23f*(i%2),1.72f+.15f*(i%2));
            stone.rotation=Quaternion.LookRotation(Center(station+1)-Center(station-1))*Quaternion.Euler(-tilt,0,0);
            float bottom=float.PositiveInfinity;
            foreach(var vertex in stone.GetComponent<MeshFilter>().sharedMesh.vertices)
                bottom=Mathf.Min(bottom,stone.TransformVector(vertex).y);
            stone.position=P(station,(i%2==0?1:-1)*(3.25f+i%3*.57f),-bottom-.29f);
            station+=17+(i*7)%10;
        }
        PoolsAndBanks(pools);
        var oldDetail=root.Find("07 · Rock fragments, water edges and spring growth");
        if(oldDetail)Object.DestroyImmediate(oldDetail.gameObject);
        AddCanyonDetail(root);
        var oldTrees=root.Find("08 · Shelf trees and cliff vegetation");if(oldTrees)Object.DestroyImmediate(oldTrees.gameObject);
        AddRockVegetation(root,shelves);

        var guide=root.GetChild(5);
        while(guide.childCount>0)Object.DestroyImmediate(guide.GetChild(guide.childCount-1).gameObject);
        for(int s=0;s<=Length;s+=15){
            string name="Upper third · "+s+"m";
            var waypoint=guide.Find(name);
            if(!waypoint)waypoint=Group(name,guide);
            waypoint.position=P(s,0,Mathf.Min(Rim(s,-1),Rim(s,1))*2/3);
        }
        var lights=root.Find("Daylight");
        lights.Find("High morning light").rotation=Quaternion.Euler(76,-22,0);
        var fill=lights.Find("Canyon bounce").GetComponent<Light>();fill.intensity=3400f;
        var cam=root.GetComponentInChildren<Camera>();cam.farClipPlane=930;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new System.InvalidOperationException("Spring save failed.");
    }

}

