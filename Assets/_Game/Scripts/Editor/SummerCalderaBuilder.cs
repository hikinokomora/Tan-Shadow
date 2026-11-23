using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using TanShadow.Player;
using TanShadow.Spring;
using TanShadow.Summer;

public static class SummerCalderaBuilder
{
    const string Art = "Assets/_Game/Art/SummerBlockout";
    const string Scenes = "Assets/_Game/Scenes/Summer_Zhuque_";
    const string Dry = Scenes + "01_Dry.unity", Flood = Scenes + "02_Lava.unity";
    const float Rad = Mathf.Deg2Rad;
    const int Rows = 30;
    static readonly float[] mouths = {9, 83, 141, 224, 294};
    static Material ash, dune, basalt, shale, cliff, moss, concrete, joint, bird, amber, red, garnet, magma;
    static readonly Mesh[] stones = new Mesh[7];
    static System.Random rng = new System.Random(493160);
    static float Rand(float a,float b) => Mathf.Lerp(a,b,(float)rng.NextDouble());
    static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);
    static float Sin(float angle) => Mathf.Sin(angle*Rad);
    static float Cos(float angle) => Mathf.Cos(angle*Rad);
    static Vector3 Polar(float a,float r,float y) => V(r*Cos(a),y,r*Sin(a));
    static float OriginalInner(float a) => 20 + 1.8f*Sin(3*a+21) + .8f*Sin(6*a-9);
    static float Inner(float a) => 4 * OriginalInner(a);
    static float Outer(float a) => Inner(a) - OriginalInner(a) + 51 + 2.3f*Sin(2*a+9) + 1.1f*Sin(5*a);
    static float Ground(float x,float z) => .32f*Sin(6*x+2*z)+.21f*Sin(9*z-3*x)+.18f*Mathf.PerlinNoise(x*.14f+80,z*.14f+80);
    static Vector3 Surface(float a,float t) {
        var p=Polar(a,Mathf.Lerp(Inner(a),Outer(a),t),0);p.y=Ground(p.x,p.z);return p;
    }
    static float Axis(int k,float t) => mouths[k]+(5.3f*Sin(t*310+k*83)+3f*Sin(t*600+k*51))*Mathf.Sin(t*Mathf.PI)*.44f;
    static float Half(int k,float t) {
        float a=Axis(k,t),r=Mathf.Lerp(Inner(a),Outer(a),t);
        return (4.15f+.8f*Sin(k*71+t*360)+.4f*Sin(t*845+k*30))/r/Rad;
    }
    static Vector3 Channel(int k,float t,float across) => Surface(Axis(k,t)+across*Half(k,t),t);
    static Transform Group(Transform p,string n) {
        var t=new GameObject(n).transform;t.SetParent(p,false);return t;
    }
    static Material Mat(string name,string hex,float emission=0) {
        ColorUtility.TryParseHtmlString("#"+hex,out var c);
        var m=new Material(Shader.Find("HDRP/Lit"));m.name=name;m.SetColor("_BaseColor",c);
        m.SetFloat("_Smoothness",.18f);m.SetFloat("_DoubleSidedEnable",1);
        if(emission>0)m.SetColor("_EmissiveColor",c.linear*emission);
        HDMaterial.ValidateMaterial(m);AssetDatabase.CreateAsset(m,Art+"/"+name+".mat");return m;
    }
    static void Palette() {
        ash=Mat("Ash sand","555458");dune=Mat("Ash drift","515055");basalt=Mat("Basalt","444144");
        shale=Mat("Weathered lava stone","514F52");cliff=Mat("Cinder escarpment","48474A");
        moss=Mat("Olive crest moss","505C44");concrete=Mat("Formed concrete","99958F");joint=Mat("Concrete recess","625F5E");
        bird=Mat("Vermilion plumage","D96B2B");amber=Mat("Golden mantle","E9A448");red=Mat("Scarlet train","BA3631");garnet=Mat("Deep scarlet","862F34");
        magma=Mat("Molten basalt","EE5625",820);
    }

    sealed class Shape {
        readonly List<Vector3> v=new List<Vector3>();
        readonly List<int>[] f;
        public Shape(int materials=1) {
            f=new List<int>[materials];for(int i=0;i<materials;i++)f[i]=new List<int>();
        }
        public void Tri(Vector3 a,Vector3 b,Vector3 c,int slot=0) {
            int i=v.Count;v.Add(a);v.Add(b);v.Add(c);f[slot].Add(i);f[slot].Add(i+1);f[slot].Add(i+2);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot=0) {Tri(a,b,c,slot);Tri(a,c,d,slot);}
        public void Up(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot=0) {
            if(Vector3.Cross(b-a,c-a).y>=0)Quad(a,b,c,d,slot);else Quad(d,c,b,a,slot);
        }
        public Mesh Save(string name) {
            string path=Art+"/"+name+".asset";
            var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=m!=null;
            if(exists)m.Clear();else m=new Mesh{name=name};
            m.indexFormat=v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            m.SetVertices(v);m.subMeshCount=f.Length;for(int i=0;i<f.Length;i++)m.SetTriangles(f[i],i);
            m.RecalculateNormals();m.RecalculateBounds();if(exists)EditorUtility.SetDirty(m);else AssetDatabase.CreateAsset(m,path);return m;
        }
    }
    static Transform Instance(Transform p,string n,Mesh m,Material[] mats,bool solid) {
        var t=Group(p,n);t.gameObject.AddComponent<MeshFilter>().sharedMesh=m;t.gameObject.AddComponent<MeshRenderer>().sharedMaterials=mats;
        if(solid)t.gameObject.AddComponent<MeshCollider>().sharedMesh=m;return t;
    }
    static Transform Bake(Transform p,string n,Shape s,Material[] mats,bool solid=true) => Instance(p,n,s.Save(n),mats,solid);
    static Transform Box(Transform p,string n,Vector3 pos,Vector3 size,Material mat,bool solid=true) {
        var t=GameObject.CreatePrimitive(PrimitiveType.Cube).transform;t.name=n;t.SetParent(p,false);t.localPosition=pos;t.localScale=size;
        t.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)UnityEngine.Object.DestroyImmediate(t.GetComponent<Collider>());return t;
    }

    // Each ash sector and its neighbouring channels use identical edge samples in both scenes.
    static Transform Floor(Transform parent) {
        var dry=Group(parent,"Dry ash across the five future channels");
        for(int k=0;k<mouths.Length;k++) {
            int next=(k+1)%mouths.Length;
            Vector3 Fan(float t,float u) {
                float a=Axis(k,t)+Half(k,t),b=Axis(next,t)-Half(next,t)+(next==0?360:0);
                return Surface(Mathf.Lerp(a,b,u),t);
            }
            var fan=new Shape(2);var fill=new Shape(2);
            for(int j=0;j<Rows;j++) {
                float t=j/(float)Rows,s=(j+1f)/Rows;
                for(int i=0;i<72;i++) {
                    float u=i/72f,w=(i+1)/72f;var a=Fan(t,u);
                    fan.Up(a,Fan(s,u),Fan(s,w),Fan(t,w),Patch(a));
                }
                for(int i=0;i<6;i++){
                    float u=i/3f-1,w=(i+1)/3f-1;var a=Channel(k,t,u);
                    fill.Up(a,Channel(k,s,u),Channel(k,s,w),Channel(k,t,w),Patch(a));
                }
            }
            Bake(parent,"Ash fan "+(k+1),fan,new[]{ash,dune});Bake(dry,"Dry bed "+(k+1),fill,new[]{ash,dune});
        }
        return dry;
    }
    static int Patch(Vector3 p) => Mathf.PerlinNoise(73+p.x*.075f,51+p.z*.08f)>.64f?1:0;

    // The outer rim closes over a rectangular mouth, keeping the portal open at trail height.
    static Vector3 RimPoint(float a,int row,bool center) {
        float foot=center?Inner(a):Outer(a);
        float relief=center?17.2f+3*Sin(2*a+33)+1.2f*Sin(5*a):20+4*Sin(3*a)+2.3f*Sin(7*a+21);
        float wrinkle=Sin(19*a+4)+.55f*Sin(37*a);
        float[] heights=center?new[]{0,.1f,.38f,.7f,1,.98f,.52f,.38f}:new[]{0,.1f,.38f,.71f,1,.95f,.43f,.04f};
        float[] offsets=center?new[]{0f,-1.1f,-3.3f,-5.9f,-9.4f,-10.8f,-13.4f,-16f}:new[]{0f,1.35f,3.8f,7.1f,11.6f,15.1f,23.4f,31.2f};
        float r=foot+4*(offsets[row]+(row==0?0:wrinkle*(center?.40f:.14f)));
        if(center && row>=5)r=Mathf.Max(1.2f,r);
        var p=Polar(a,r,0);p.y=Ground(p.x,p.z)+4*relief*heights[row]+(row==0?-.13f:.24f*wrinkle);
        return p;
    }
    static void Rims(Transform p) {
        foreach(bool center in new[]{false,true}) {
            var s=new Shape(4);
            const int N=240;
            for(int i=0;i<N;i++) {
                float a=i*360f/N,b=(i+1)*360f/N,mid=(a+b)*.5f;
                for(int j=0;j<7;j++) {
                    var v=RimPoint(a,j,center);var w=RimPoint(b,j,center);
                    var x=RimPoint(b,j+1,center);var y=RimPoint(a,j+1,center);
                    int slot=(j==4 && Sin(3*mid+19)+.6f*Sin(7*mid)>-.3f)?3:
                        (j==0||j==1)?0:(Sin(mid*13+j*19)+Sin(mid*5-j*12)>.77f?2:1);
                    // Remove a narrow wedge under the ridge; the roof and cheeks fill it below.
                    if(!center && Mathf.Abs(mid-180)<1.5f && j==0){
                        float ceiling=Ground(PortalFront,0)+5.22f;
                        v=Vector3.Lerp(v,y,Mathf.InverseLerp(v.y,y.y,ceiling));
                        w=Vector3.Lerp(w,x,Mathf.InverseLerp(w.y,x.y,ceiling));
                    }
                    if(!center)slot=j>=4?1:0;
                    s.Up(v,w,x,y,slot);
                }
                if(center)s.Tri(RimPoint(a,7,true),V(0,22f,0),RimPoint(b,7,true),1);
            }
            Bake(p,center?"Central scoria cone and recessed crater":"Outer caldera rim",s,center?new[]{shale,cliff,basalt,moss}:new[]{Sand(),Sand(),Sand(),Sand()});
        }
    }
    static Material Sand() {
        string path=Art+"/Caldera sand.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        return material?material:Mat("Caldera sand","9B8975");
    }
    static float PortalFront => -Outer(180)+1.35f;
    static void Portal(Transform p) {
        float front=PortalFront,back=front-36.8f,mid=(front+back)*.5f;
        float y=Ground(front,0);
        Box(p,"Passage floor",V(mid,y-.24f,0),V(front-back,.5f,5.85f),concrete);
        Box(p,"Passage soffit",V(mid,y+4.95f,0),V(front-back,.7f,6.25f),concrete);
        Box(p,"Far end",V(back,y+2.28f,0),V(.55f,4.8f,6.1f),concrete);
        foreach(int side in new[]{-1,1}){
            Box(p,"Cast sidewall",V(mid,y+2.26f,side*2.91f),V(front-back,4.6f,.42f),concrete);
            Box(p,"Mouth jamb",V(front,y+2.4f,side*3),V(1.14f,4.8f,.6f),joint);
            for(int j=0;j<8;j++)Box(p,"Board form joint",V(front-1-j*4.4f,y+2.28f,side*2.697f),V(.045f,4.56f,.012f),joint,false);
        }
        Box(p,"Lintel",V(front,y+4.92f,0),V(1.14f,.65f,6.6f),joint);
    }
    static void StoneKit() {
        for(int k=0;k<stones.Length;k++){
            int n=10+k%3;var levels=new Vector3[5,n];var s=new Shape(2);
            for(int i=0;i<n;i++){
                float a=i*360f/n;float jitter=1+.08f*Sin(3*a+k*39)+.045f*Sin(5*a-k*51);
                for(int j=0;j<5;j++){
                    float[] width={.78f,.99f,1,.83f,.52f};float[] h={-.08f,.19f,.49f,.78f,1};
                    var v=Polar(a+Sin(j*65+k*38)*3,width[j]*jitter,h[j]);
                    v.x+=.07f*j/4*Sin(k*55);v.z+=.07f*j/4;
                    v.y+=(j>0?.045f:0)*Sin(a*2+k*47+j*13);
                    levels[j,i]=v;
                }
            }
            for(int i=0;i<n;i++){
                int e=(i+1)%n;
                for(int j=0;j<4;j++)s.Quad(levels[j,i],levels[j+1,i],levels[j+1,e],levels[j,e],j==0?1:0);
                s.Tri(levels[4,e],levels[4,i],V(.04f,1.01f,.03f));
                s.Tri(levels[0,i],levels[0,e],Vector3.down*.083f,1);
            }
            stones[k]=s.Save("Weathered block "+(k+1));
        }
    }
    static Transform Rock(Transform p,string n,Vector3 pos,Vector3 scale,int model,float yaw,bool solid,Material m=null) {
        var t=Instance(p,n,stones[model%stones.Length],new[]{m?m:shale,basalt},solid);
        t.localPosition=pos;t.localScale=scale;t.localRotation=Quaternion.Euler(0,yaw,0);return t;
    }
    static void Dressing(Transform p) {
        rng=new System.Random(493160);
        float[] angles={168,193,254,272,325,343,39,58,105,122,208,19,72,157,247,305,178,200,215,231,262,282,315,333,353,27,49,68,96,113,131,152};
        for(int i=0;i<angles.Length;i++){
            var pos=Surface(angles[i],i==0?.48f:i==14?.23f:(i%3==0?.26f:i%3==1?.66f:.51f));
            float w=i==0?4.15f:Rand(3.45f,5.6f),h=i==0?3.4f:Rand(3.7f,6.2f);
            bool nearWater=false;
            float station=i==0?.48f:i==14?.23f:(i%3==0?.26f:i%3==1?.66f:.51f);
            for(int k=0;k<mouths.Length;k++)
                if(Mathf.Abs(Mathf.DeltaAngle(angles[i],Axis(k,station)))*Rad*pos.magnitude < w+7f)nearWater=true;
            if(nearWater || i!=0 && Mathf.Abs(Mathf.DeltaAngle(angles[i],180))<5)continue;
            Rock(p,i==0?"First perch":"Cover and perch "+(i+1),pos-Vector3.up*.12f,V(w,h,w*.82f),i,i*47,true,i%3==1?basalt:shale);
            if(i%3!=0)Rock(p,"Basal fragment "+i,pos+Polar(angles[i]+37,w*.85f,-.12f),V(1.35f,1.04f,1.12f),i+1,i*17,true);
        }
        var gravel=Group(p,"Cinder and scree clusters");
        for(int i=0;i<67;i++){
            float a=i*137.508f%360,t=(i%3==0?Rand(.79f,.91f):i%3==1?Rand(.09f,.2f):Rand(.25f,.76f));
            bool inRiver=false;
            for(int k=0;k<mouths.Length;k++)if(Mathf.Abs(Mathf.DeltaAngle(Axis(k,t),a))<Half(k,t)+3)inRiver=true;
            if(inRiver || Mathf.Abs(Mathf.DeltaAngle(a,180))<6)continue;
            var pos=Surface(a,t);
            for(int j=0;j<4;j++){
                var pt=pos+V(Rand(-1.3f,1.3f),0,Rand(-1.3f,1.3f));pt.y=Ground(pt.x,pt.z)-.03f;
                float w=Rand(.16f,.57f);Rock(gravel,"Scoria fragment",pt,V(w,w*Rand(.28f,.7f),w*Rand(.7f,1.3f)),i+j,j*57,false,i%2==0?basalt:shale);
            }
        }
    }
    // Closed, tapered lofts for the beak, crest, flight feathers and long tail.
    static Shape Loft(Vector3[] path,float[] width,float[] depth,Vector3 side,int sides=8) {
        var s=new Shape();var rings=new Vector3[path.Length,sides];
        for(int j=0;j<path.Length;j++){
            var forward=(path[Mathf.Min(j+1,path.Length-1)]-path[Mathf.Max(0,j-1)]).normalized;
            var x=Vector3.ProjectOnPlane(side,forward).normalized;var y=Vector3.Cross(forward,x).normalized;
            for(int i=0;i<sides;i++)rings[j,i]=path[j]+x*(Cos(i*360f/sides)*width[j])+y*(Sin(i*360f/sides)*depth[j]);
        }
        for(int i=0;i<sides;i++){
            int e=(i+1)%sides,last=path.Length-1;
            for(int j=0;j<last;j++)s.Quad(rings[j,i],rings[j,e],rings[j+1,e],rings[j+1,i]);
            s.Tri(path[0],rings[0,e],rings[0,i]);s.Tri(path[last],rings[last,i],rings[last,e]);
        }return s;
    }
    static void Bird(Transform p) {
        var perch=Surface(168,.48f);p.position=perch+Vector3.up*3.28f;p.rotation=Quaternion.Euler(0,153,0);
        void Feather(string n,Vector3[] path,float[] w,float[] d,Material m){
            Bake(p,n,Loft(path,w,d,Vector3.right),new[]{m},false);
        }
        Feather("Breast and back",new[]{V(0,1.1f,-1.3f),V(0,1.24f,-.55f),V(0,1.38f,.27f),V(0,1.67f,.75f),V(0,1.94f,.78f)},
            new[]{.12f,.49f,.56f,.43f,.26f},new[]{.12f,.43f,.65f,.45f,.25f},bird);
        Feather("Raised neck and head",new[]{V(0,1.5f,.59f),V(0,1.92f,.76f),V(0,2.45f,.73f),V(0,2.72f,.9f),V(0,2.67f,1.16f)},
            new[]{.31f,.26f,.18f,.255f,.17f},new[]{.33f,.24f,.2f,.24f,.13f},amber);
        Feather("Curved beak",new[]{V(0,2.68f,1.06f),V(0,2.63f,1.34f),V(0,2.52f,1.52f)},new[]{.17f,.09f,.009f},new[]{.12f,.075f,.008f},basalt);
        for(int i=-1;i<=1;i++)
            Feather("Swept crest "+(i+2),new[]{V(i*.07f,2.76f,.88f),V(i*.15f,3.02f,.6f),V(i*.22f,3.23f-.12f*Mathf.Abs(i),.24f)},
                new[]{.075f,.083f,.006f},new[]{.07f,.05f,.005f},bird);
        foreach(int side in new[]{-1,1}){
            Feather("Tarsus "+side,new[]{V(side*.31f,.05f,.05f),V(side*.30f,.42f,-.06f),V(side*.37f,.9f,.15f)},
                new[]{.08f,.07f,.145f},new[]{.09f,.075f,.15f},basalt);
            for(int toe=-1;toe<=1;toe++)
                Feather("Claw "+side+" "+toe,new[]{V(side*.31f,.09f,.03f),V(side*.31f+toe*.11f,.06f,.25f),V(side*.31f+toe*.18f,-.02f,.46f)},
                    new[]{.053f,.045f,.008f},new[]{.05f,.04f,.01f},amber);
            for(int i=0;i<6;i++){
                float t=i/5f;
                Feather("Folded primary "+side+" "+i,new[]{V(side*(.31f+.12f*t),1.67f+.12f*t,.45f-.15f*t),V(side*(.54f+.14f*t),1.45f+.22f*t,-.52f),V(side*(.42f+.31f*t),.91f+.46f*t,-1.85f-.43f*t)},
                    new[]{.105f,.19f,.01f},new[]{.12f,.13f,.014f},i==4?amber:bird);
            }
            var eye=GameObject.CreatePrimitive(PrimitiveType.Sphere);eye.name="Eye";eye.transform.SetParent(p,false);
            eye.transform.localPosition=V(side*.196f,2.715f,1.014f);eye.transform.localScale=V(.073f,.073f,.073f);
            eye.GetComponent<Renderer>().sharedMaterial=basalt;UnityEngine.Object.DestroyImmediate(eye.GetComponent<Collider>());
        }
        for(int i=-2;i<=2;i++){
            float e=Mathf.Abs(i)*.26f;
            Feather("Trailing tail plume "+(i+3),new[]{V(i*.12f,1.27f,-.92f),V(i*.23f,1.08f,-2),V(i*.33f,.43f,-3),V(i*.41f,-1.28f,-3.72f),V(i*.48f,-2.96f+e,-4.38f),V(i*.58f,-3.06f+e,-5.37f+.32f*e)},
                new[]{.13f,.225f,.235f,.2f,.15f,.012f},new[]{.09f,.10f,.10f,.075f,.055f,.011f},i%2==0?red:garnet);
        }
    }
    static void CrossingStones(Transform root) {
        for(int k=0;k<mouths.Length;k++){
            var g=Group(root,"Crossing "+(k+1));float station=k%2==0?.53f:.46f;
            for(int j=-2;j<=2;j++) {
                float u=j*.41f,t=station+.016f*Sin(j*68+k*41);
                var pt=Channel(k,t,u);pt.y-=.47f;
                Rock(g,"Foothold "+(j+3),pt,V(.86f+.05f*Cos(j*56),.78f+.055f*Sin(k*61+j*87),.87f),k+j+2,j*32+k*49,true,basalt);
            }
        }
    }
    static void Daylight(Transform p) {
        void Sun(string n,float lux,Color color,Vector3 angles,bool shadow) {
            var t=Group(p,n);var l=t.gameObject.AddComponent<Light>();l.type=LightType.Directional;
            t.gameObject.AddComponent<HDAdditionalLightData>();l.lightUnit=LightUnit.Lux;l.intensity=lux;l.color=color;
            l.shadows=shadow?LightShadows.Soft:LightShadows.None;t.rotation=Quaternion.Euler(angles);
        }
        Sun("Afternoon sun",13700,new Color(1,.94f,.86f),V(49,-28,0),true);
        Sun("Sky fill",1950,new Color(.77f,.84f,1),V(31,138,0),false);
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Art+"/Caldera daylight.asset");
        T Add<T>() where T:VolumeComponent {var component=profile.Add<T>();AssetDatabase.AddObjectToAsset(component,profile);return component;}
        var env=Add<VisualEnvironment>();env.skyType.Override((int)SkyType.Gradient);
        var sky=Add<GradientSky>();sky.top.Override(new Color(.29f,.45f,.62f));sky.middle.Override(new Color(.94f,.77f,.60f));
        sky.bottom.Override(new Color(.37f,.33f,.31f));sky.exposure.Override(10.35f);sky.gradientDiffusion.Override(2.15f);
        var exposure=Add<Exposure>();exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(10.5f);
        var fog=Add<Fog>();fog.enabled.Override(true);fog.colorMode.Override(FogColorMode.ConstantColor);
        fog.color.Override(new Color(.64f,.53f,.48f)*350);fog.meanFreePath.Override(175);fog.baseHeight.Override(1);fog.maximumHeight.Override(78);
        Add<MotionBlur>().intensity.Override(0);
        var vol=p.gameObject.AddComponent<Volume>();vol.isGlobal=true;vol.priority=30;vol.sharedProfile=profile;EditorUtility.SetDirty(profile);
    }
    static void PlayerRig(Transform root) {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/Player.prefab");
        var player=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
        player.transform.SetPositionAndRotation(V(PortalFront-8.8f,Ground(PortalFront,0)+.08f,0),Quaternion.Euler(0,90,0));
        var rig=Group(root,"Main Camera");rig.tag="MainCamera";
        rig.SetPositionAndRotation(player.transform.position+V(-4,2.38f,0),Quaternion.Euler(12,90,0));
        var cam=rig.gameObject.AddComponent<Camera>();cam.fieldOfView=67;cam.nearClipPlane=.08f;cam.farClipPlane=790;
        rig.gameObject.AddComponent<HDAdditionalCameraData>().clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
        rig.gameObject.AddComponent<AudioListener>();rig.gameObject.AddComponent<SpringFollowCamera>().player=player.transform;
        var controller=player.GetComponent<PlayerController>();controller.cameraTransform=rig;controller.jumpSpeed=6.8f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
    }
    static Vector3 Liquid(int k,float t,float u) => Channel(k,t,u)+Vector3.down*.58f;
    static MeshCollider River(Transform parent,int k) {
        var flow=new Shape();var cut=new Shape(2);
        for(int j=0;j<Rows;j++){
            float t=j/(float)Rows,s=(j+1f)/Rows;
            foreach(int d in new[]{-1,1}){
                var a=Channel(k,t,d);var b=Channel(k,s,d);
                var c=Liquid(k,s,d*.87f);var e=Liquid(k,t,d*.87f);
                cut.Up(a,b,c,e,j%8==0?1:0);
            }
            for(int i=0;i<12;i++){
                float u=Mathf.Lerp(-.87f,.87f,i/12f),w=Mathf.Lerp(-.87f,.87f,(i+1f)/12);
                flow.Up(Liquid(k,t,u),Liquid(k,s,u),Liquid(k,s,w),Liquid(k,t,w));
            }

        }
        foreach(float t in new[]{0f,1f})
            for(int i=0;i<12;i++){
                float u=Mathf.Lerp(-.87f,.87f,i/12f),w=Mathf.Lerp(-.87f,.87f,(i+1f)/12);
                cut.Quad(Channel(k,t,u),Channel(k,t,w),Liquid(k,t,w),Liquid(k,t,u),0);
            }
        // A narrow cascade on the cone feeds the broad current on the valley floor.
        Vector3 Source(int j,float sign) {
            float a=mouths[k]+j*1.9f*Sin(k*61+37)+sign*Mathf.Lerp(Half(k,0)*.76f,2.1f,j/3f);
            if(j==0)return Liquid(k,0,sign*.76f)+Vector3.up*.055f;
            var q=RimPoint(a,j,true);return q+Polar(a,.5f,.16f);
        }
        for(int j=0;j<3;j++)flow.Up(Source(j,-1),Source(j+1,-1),Source(j+1,1),Source(j,1));
        Bake(parent,"Cut basalt banks "+(k+1),cut,new[]{basalt,shale});
        var river=Bake(parent,"Molten river "+(k+1),flow,new[]{magma});
        RiverUV(river.GetComponent<MeshFilter>().sharedMesh,k);
        return river.GetComponent<MeshCollider>();
    }
    static Material SteamMaterial() {
        var tex=new Texture2D(32,32,TextureFormat.RGBA32,false);tex.name="Steam falloff";tex.wrapMode=TextureWrapMode.Clamp;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            float radius=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
            float a=Mathf.Pow(Mathf.Clamp01(1-radius),2)*.20f;
            tex.SetPixel(x,y,new Color(1,1,1,a));
        }
        tex.Apply();AssetDatabase.CreateAsset(tex,Art+"/Steam falloff.asset");
        var mat=new Material(Shader.Find("HDRP/Unlit"));mat.SetFloat("_SurfaceType",1);mat.SetColor("_UnlitColor",new Color(.68f,.62f,.55f));
        mat.SetTexture("_UnlitColorMap",tex);mat.SetColor("_EmissiveColor",new Color(.65f,.55f,.47f)*380);
        HDMaterial.ValidateMaterial(mat);AssetDatabase.CreateAsset(mat,Art+"/Fumarole mist.mat");return mat;
    }
    static void Heat(Transform parent,int k,Material steam) {
        for(int i=0;i<3;i++){
            var t=Group(parent,"Reflected heat");t.position=Liquid(k,.16f+i*.31f,.15f)+Vector3.up*.85f;
            var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;t.gameObject.AddComponent<HDAdditionalLightData>();
            l.lightUnit=LightUnit.Lumen;l.intensity=3800;l.range=6.8f;l.color=new Color(1,.32f,.096f);l.shadows=LightShadows.None;
        }
        var vent=Group(parent,"Fumarole");vent.position=Channel(k,.17f,-.25f);
        var ps=vent.gameObject.AddComponent<ParticleSystem>();var main=ps.main;
        main.startLifetime=new ParticleSystem.MinMaxCurve(2.2f,3.7f);main.startSize=new ParticleSystem.MinMaxCurve(.75f,1.7f);
        main.startSpeed=.4f;main.maxParticles=28;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.54f;
        var emission=ps.emission;emission.rateOverTime=3;
        var wind=ps.velocityOverLifetime;wind.enabled=true;wind.space=ParticleSystemSimulationSpace.World;wind.x=.28f;wind.y=.64f;wind.z=.12f;
        var growth=ps.sizeOverLifetime;growth.enabled=true;growth.size=new ParticleSystem.MinMaxCurve(2,AnimationCurve.Linear(0,.36f,1,1));
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=steam;
    }

    static void Encrust(Material m) {
        const int n=256;
        var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/Cooling surface.asset");
        if(!tex) {
            tex=new Texture2D(n,n,TextureFormat.RGB24,true);tex.name="Cooling surface";tex.wrapMode=TextureWrapMode.Repeat;
            float Tile(float x,float y,int cells,int seed){
                int ix=Mathf.FloorToInt(x*cells),iy=Mathf.FloorToInt(y*cells);
                float xf=Mathf.Repeat(x*cells,1),yf=Mathf.Repeat(y*cells,1);
                float Hash(int a,int b){
                    unchecked{uint h=(uint)(((a%cells+cells)%cells)*73856093 ^ ((b%cells+cells)%cells)*19349663 ^ seed);
                    h^=h>>16;h*=0x7feb352du;h^=h>>15;return (h&65535)/65535f;}
                }
                xf=xf*xf*(3-2*xf);yf=yf*yf*(3-2*yf);
                return Mathf.Lerp(Mathf.Lerp(Hash(ix,iy),Hash(ix+1,iy),xf),Mathf.Lerp(Hash(ix,iy+1),Hash(ix+1,iy+1),xf),yf);
            }
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                float u=x/(float)n,v=y/(float)n;
                float a=u+.14f*Tile(u,v,4,977)+.03f*Tile(u,v,13,239);
                float b=v+.12f*Tile(u,v,4,417)+.034f*Tile(u,v,14,523);
                float field=.64f*Tile(a,b,5,119)+.24f*Tile(a,b,11,217)+.12f*Tile(a,b,29,937);
                float seam=Mathf.Clamp01(1-Mathf.Abs(field-.49f)*13f);
                float flicker=.67f+.33f*Tile(a,b,8,439);
                var col=Color.Lerp(new Color(.17f,.043f,.034f),new Color(.90f,.267f,.062f),Mathf.InverseLerp(.25f,.58f,field));
                col=Color.Lerp(col,new Color(1,.69f,.21f),seam*seam*flicker);
                tex.SetPixel(x,y,col);
            }
            tex.Apply();AssetDatabase.CreateAsset(tex,Art+"/Cooling surface.asset");
        }
        m.SetTexture("_BaseColorMap",tex);m.SetColor("_BaseColor",Color.white);
        m.SetTexture("_EmissiveColorMap",tex);m.SetColor("_EmissiveColor",Color.white*1060);
        HDMaterial.ValidateMaterial(m);EditorUtility.SetDirty(m);
    }
    static void RiverUV(Mesh mesh,int k) {
        var uv=new List<Vector2>();var cross=Polar(mouths[k]+90,1,0);
        foreach(var p in mesh.vertices)uv.Add(new Vector2(Vector3.Dot(p,cross)/6.2f,new Vector2(p.x,p.z).magnitude/11.6f-p.y*.06f));
        mesh.SetUVs(0,uv);mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
    }

    public static void ResizeOpenScene() {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying || (scene.path!=Dry && scene.path!=Flood))throw new InvalidOperationException("Open a Summer scene in Edit Mode.");
        var root=scene.GetRootGameObjects()[0].transform;
        Material Load(string n)=>AssetDatabase.LoadAssetAtPath<Material>(Art+"/"+n+".mat");
        ash=Load("Ash sand");dune=Load("Ash drift");basalt=Load("Basalt");shale=Load("Weathered lava stone");cliff=Load("Cinder escarpment");
        moss=Load("Olive crest moss");concrete=Load("Formed concrete");joint=Load("Concrete recess");magma=Load("Molten basalt");
        for(int i=0;i<stones.Length;i++)stones[i]=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/Weathered block "+(i+1)+".asset");
        void Clear(Transform t){while(t.childCount>0)UnityEngine.Object.DestroyImmediate(t.GetChild(t.childCount-1).gameObject);}
        var terrain=root.GetChild(0);Clear(terrain);
        var dry=Floor(terrain);Rims(terrain);if(scene.path==Flood)UnityEngine.Object.DestroyImmediate(dry.gameObject);
        Clear(root.GetChild(1));Portal(root.GetChild(1));Clear(root.GetChild(2));Dressing(root.GetChild(2));
        Clear(root.GetChild(3));CrossingStones(root.GetChild(3));
        root.GetChild(4).position=Surface(168,.48f)+Vector3.up*3.28f;
        var player=root.GetComponentInChildren<PlayerController>();
        root.GetComponentInChildren<Camera>().farClipPlane=790;
        var spawn=V(PortalFront-8.8f,Ground(PortalFront,0)+.08f,0);
        player.cameraTransform.position=spawn+V(-4,2.38f,0);player.transform.position=spawn;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        if(scene.path==Flood){
            var hazard=root.GetComponentInChildren<SummerLava>();var steam=Load("Fumarole mist");
            for(int k=0;k<mouths.Length;k++){
                var river=hazard.transform.GetChild(k);Clear(river);hazard.rivers[k]=River(river,k);Heat(river,k,steam);
            }
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Summer save failed");
    }

    public static void Build() {
        if(EditorApplication.isPlaying || AssetDatabase.LoadAssetAtPath<SceneAsset>(Dry) || AssetDatabase.LoadAssetAtPath<SceneAsset>(Flood))
            throw new InvalidOperationException("Build requires Edit mode and two new scene paths.");
        if(!AssetDatabase.IsValidFolder(Art))AssetDatabase.CreateFolder("Assets/_Game/Art","SummerBlockout");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);var root=Group(null,"Summer - Zhuque");
        Palette();StoneKit();var terrain=Group(root,"01 - Ash ring and volcanic rim");
        Floor(terrain);Rims(terrain);Portal(Group(root,"02 - Concrete passage"));
        Dressing(Group(root,"03 - Basalt cover and perches"));CrossingStones(Group(root,"04 - Crossing stones"));
        Bird(Group(root,"05 - Perched Zhuque - scale proxy"));Daylight(Group(root,"06 - Caldera daylight"));PlayerRig(root);
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene,Dry) || !AssetDatabase.CopyAsset(Dry,Flood))throw new InvalidOperationException("Could not save both summer scenes.");
        EditorSceneManager.CloseScene(scene,true);
        var late=EditorSceneManager.OpenScene(Flood,OpenSceneMode.Additive);SceneManager.SetActiveScene(late);
        root=late.GetRootGameObjects()[0].transform;
        UnityEngine.Object.DestroyImmediate(root.Find("01 - Ash ring and volcanic rim/Dry ash across the five future channels").gameObject);
        var lava=Group(root,"07 - Open lava rivers and heat");var volumes=new MeshCollider[mouths.Length];var steam=SteamMaterial();Encrust(magma);
        for(int k=0;k<mouths.Length;k++){var river=Group(lava,"Lava crossing "+(k+1));volumes[k]=River(river,k);Heat(river,k,steam);}
        var hazard=lava.gameObject.AddComponent<SummerLava>();hazard.player=root.GetComponentInChildren<PlayerController>().GetComponent<CharacterController>();hazard.rivers=volumes;
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(late);
        if(!EditorSceneManager.SaveScene(late))throw new InvalidOperationException("Could not save active rivers.");
        EditorSceneManager.CloseScene(late,true);
    }
}



