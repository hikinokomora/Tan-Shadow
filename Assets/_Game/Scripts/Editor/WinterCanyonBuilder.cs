using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using TanShadow.Player;
using TanShadow.Spring;
using TanShadow.Winter;

public static class WinterCanyonBuilder
{
    const string ScenePath = "Assets/_Game/Scenes/Winter_Xuanwu.unity";
    const string AssetsDir = "Assets/_Game/Art/WinterBlockout";
    static readonly Vector2 ShellRadii = new Vector2(24, 31);
    static readonly Vector3 ShellCenter = new Vector3(0, 0, 66);
    const int Subdivisions=48, TrackColumns=12;
    const float WinterJumpSpeed = 6.8f;
    const float LandscapeScale=4f;
    const float PawStation=6.75f;
    const float SummitRise=5.4f;
    static readonly Vector2Int[] CrevasseRows = { new Vector2Int(116,124), new Vector2Int(196,207) };
    static readonly Vector3[] TrackKnots = {
        new Vector3(28,1.7f,65), new Vector3(28,1.72f,67.45f), new Vector3(28.25f,1.82f,69.6f),
        new Vector3(28.85f,1.93f,72), new Vector3(29.2f,2.10f,74.2f), new Vector3(28.4f,2.24f,76.35f),
        new Vector3(27.35f,2.40f,78.8f), new Vector3(26.7f,2.55f,81), new Vector3(26.15f,2.78f,83.1f),
        new Vector3(25.65f,3.15f,85.2f), new Vector3(24.9f,3.72f,87.4f), new Vector3(23.6f,4.02f,89.6f),
        new Vector3(21.9f,3.79f,91.65f), new Vector3(19.8f,3.48f,94), new Vector3(16.9f,3.3f,96.5f),
        new Vector3(13.35f,3.45f,99.3f), new Vector3(8.3f,3.9f,101.7f), new Vector3(2,4.35f,103),
        new Vector3(.25f,5.45f,100.9f), new Vector3(0,6.9f,98), new Vector3(0,7.9f,95.6f)
    };
    static readonly float[] HalfWidths = {2.7f,3.2f,2.5f,3.25f,3.15f,4.9f,6.9f,4.35f,3.55f,4.4f,5.3f,4.7f,3.65f,4.7f,5,4.7f,4.9f,3.6f,2.25f,2.25f,2.6f};
    static Material snow, windpack, shadedSnow, rock, shale, darkRock, ice, shell, shellSeam, skin, scales, obsidian, concrete, heart, heartLight;
    static System.Random random;
    static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);
    static float Sample(float low,float high) => Mathf.Lerp(low,high,(float)random.NextDouble());

    sealed class Geometry
    {
        public readonly List<Vector3> points = new List<Vector3>();
        readonly List<int>[] faces;
        public Geometry(int count = 1)
        {
            faces=new List<int>[count];
            for(int i=0;i<count;i++)faces[i]=new List<int>();
        }
        public int Add(Vector3 p){points.Add(p);return points.Count-1;}
        public void Tri(int a,int b,int c,int material=0){faces[material].Add(a);faces[material].Add(b);faces[material].Add(c);}
        public void Quad(int a,int b,int c,int d,int material=0){Tri(a,b,c,material);Tri(a,c,d,material);}
        public void Face(Vector3 a,Vector3 b,Vector3 c,int material=0){int n=Add(a);Add(b);Add(c);Tri(n,n+1,n+2,material);}
        public Mesh Bake(string name)
        {
            string path=AssetsDir+"/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool existing=mesh!=null;
            if(existing)mesh.Clear();else mesh=new Mesh{name=name};
            mesh.indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(points);
            mesh.subMeshCount=faces.Length;
            for(int i=0;i<faces.Length;i++)mesh.SetTriangles(faces[i],i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if(existing)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }
    }
    static Transform Group(string name,Transform parent=null)
    {
        var t=new GameObject(name).transform;
        t.SetParent(parent,false);
        return t;
    }
    static Material Material(string name,string hex,float roughness=.84f)
    {
        ColorUtility.TryParseHtmlString("#"+hex,out var color);
        var material=new Material(Shader.Find("HDRP/Lit"));
        material.SetColor("_BaseColor",color);
        material.SetFloat("_Smoothness",1-roughness);
        AssetDatabase.CreateAsset(material,AssetsDir+"/"+name+".mat");
        return material;
    }
    static Transform Model(Transform parent,string name,Geometry geometry,Material[] palette,bool collision=false)
    {
        var t=parent.Find(name);
        if(!t)t=Group(name,parent);
        var mesh=geometry.Bake(name);
        var filter=t.GetComponent<MeshFilter>();if(!filter)filter=t.gameObject.AddComponent<MeshFilter>();
        var renderer=t.GetComponent<MeshRenderer>();if(!renderer)renderer=t.gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh=mesh;renderer.sharedMaterials=palette;
        if(collision){
            var collider=t.GetComponent<MeshCollider>();if(!collider)collider=t.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh=null;collider.sharedMesh=mesh;
        }
        return t;
    }
    static Transform Block(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool solid=true)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name=name;g.transform.SetParent(parent,false);
        g.transform.localPosition=position;g.transform.localScale=size;
        g.GetComponent<Renderer>().sharedMaterial=material;
        if(!solid)Object.DestroyImmediate(g.GetComponent<Collider>());
        return g.transform;
    }
    static void Palette()
    {
        snow=Material("Powder snow","D9E4F0");
        windpack=Material("Wind packed snow","BECEE0",.62f);
        shadedSnow=Material("Blue glacial snow","9EAFC3",.74f);
        rock=Material("Ash blue gneiss","46556A");
        shale=Material("Slate seams","647186");
        darkRock=Material("Deep rock fractures","354155");
        ice=Material("Compressed glacial ice","657F9A",.43f);
        shell=Material("Ancient carapace","53616C");
        shellSeam=Material("Carapace rim joints","394A53");
        skin=Material("Turtle skin","5E6D71");
        scales=Material("Raised scales","7C8887");
        obsidian=Material("Eyelids and mouth","343D44");
        concrete=Material("Entrance concrete","8B99A6");
        heart=Material("Exposed heart","A33C48",.55f);
        heartLight=Material("Heart red accent","F26069",.46f);
        heartLight.SetColor("_EmissiveColor",new Color(.95f,.061f,.083f)*40);
        EditorUtility.SetDirty(heartLight);
    }
    static Vector3 Track(float s)
    {
        int i=Mathf.Min(Mathf.FloorToInt(s),TrackKnots.Length-2);
        float t=s-i;Vector3 a=TrackKnots[Mathf.Max(0,i-1)],b=TrackKnots[i];
        Vector3 c=TrackKnots[i+1],d=TrackKnots[Mathf.Min(i+2,TrackKnots.Length-1)];
        return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*(t*t)+(-a+3*b-3*c+d)*(t*t*t));
    }
    static Vector3 Side(float s)
    {
        var direction=Track(Mathf.Min(20,s+.03f))-Track(Mathf.Max(0,s-.03f));
        return Vector3.Cross(Vector3.up,direction).normalized;
    }
    static float OriginalHalfWidth(float s)
    {
        int i=Mathf.Min(Mathf.FloorToInt(s),HalfWidths.Length-2);
        return Mathf.Lerp(HalfWidths[i],HalfWidths[i+1],s-i)+.16f*Mathf.Sin(s*9)*Mathf.Sin(s*.3f);
    }
    static float Width(float s) => OriginalHalfWidth(s)/LandscapeScale;
    static float Mound(float s)
    {
        if(s<=7.8f || s>=14.5f)return 0;
        float wave=Mathf.Sin(Mathf.PI*(s-7.8f)/6.7f);
        return wave*wave;
    }

    static Vector3 Span(float s,float across)
    {
        float mound=Mound(s);
        float outer=Mathf.Max(0,across);
        float spread=Width(s)*across+outer*mound*(5.4f+.35f*Mathf.Sin(1.9f*s));
        // Crest by the left cliff, then a convex fall line to a scalloped cornice on the right.
        float crossfall=mound*(.32f+.08f*outer*Mathf.Sin(s*2.7f+across*7)+.09f*Mathf.Sin(s*2.4f+across*3)*across
            - (2.85f+.20f*Mathf.Sin(1.46f*s))*outer*outer);
        return Track(s)+Side(s)*spread+Vector3.up*crossfall;
    }

    static void Passage(Transform parent)
    {
        const float length=8;
        Block(parent,"Tunnel floor",V(0,-.32f,-length/2),V(5.1f,.64f,length),concrete);
        Block(parent,"Soffit",V(0,4.72f,-length/2),V(6.2f,.7f,length),concrete);
        foreach(int side in new[]{-1,1})
        {
            Block(parent,"Tunnel side",V(side*2.72f,2.2f,-length/2),V(.58f,4.4f,length),concrete);
            for(int i=0;i<3;i++)Block(parent,"Concrete joint",V(side*2.417f,2.18f,-1.2f-i*2.5f),V(.045f,4.35f,.055f),shale,false);
            Block(parent,"Portal pier",V(side*2.76f,2.35f,-.21f),V(.62f,4.7f,.78f),shale);
        }
        Block(parent,"Lintel",V(0,4.56f,-.2f),V(6.2f,.56f,.78f),shale);
        Block(parent,"Crossroads slab",V(0,2.1f,-length),V(5.16f,4.4f,.48f),concrete);
        var lamp=Group("Indirect portal fill",parent);lamp.localPosition=V(0,3.65f,-2.8f);
        var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Point;light.range=16;
        light.intensity=750;light.color=new Color(.74f,.85f,1);
    }

    static bool IsGap(float row)
    {
        foreach(var gap in CrevasseRows)
            if(row>gap.x && row<gap.y)return true;
        return false;
    }
    static void WalkingSurface(Transform group)
    {
        var terrain=new Geometry(4);
        int rows=(TrackKnots.Length-1)*Subdivisions;
        for(int i=0;i<=rows;i++)
        {
            float s=i/(float)Subdivisions;
            for(int j=0;j<=TrackColumns;j++)
                terrain.Add(Span(s,2*j/(float)TrackColumns-1));
            if(i==0 || IsGap(i-.5f))continue;
            for(int j=0;j<TrackColumns;j++){
                int a=(i-1)*(TrackColumns+1)+j,b=a+TrackColumns+1;
                int tone=s>18.15f&&s<19.6f&&j>0&&j<8?3:Mound(s)>.05f?(j>8 && Mathf.Sin(s*2.6f+j*.31f)>.76f?1:0):j==0||j==TrackColumns-1?2:0;
                terrain.Quad(a,b,b+1,a+1,tone);
            }
        }
        Model(group,"Walkable snow from portal to shell",terrain,new[]{snow,windpack,shadedSnow,skin},true);
        CrevasseFaces(group);
    }

    static void CrevasseFaces(Transform group)
    {
        var faces=new Geometry(4);
        foreach(var gap in CrevasseRows)
        {
            for(int side=-1;side<=1;side+=2)
            {
                float station=(side<0?gap.x:gap.y)/(float)Subdivisions;
                Vector3 inward=(Track((gap.x+gap.y)/(2f*Subdivisions))-Track(station)).normalized;
                for(int j=0;j<=TrackColumns;j++)
                {
                    float across=2*j/(float)TrackColumns-1;
                    var edge=Span(station,across);
                    int n=faces.Add(edge);
                    faces.Add(edge+inward*.09f-Vector3.up*(.34f+.09f*Mathf.Sin(j*1.2f)));
                    faces.Add(edge+inward*(.14f*Mathf.Sin(j*.9f))-Vector3.up*(1.5f+.2f*Mathf.Cos(j)));
                    faces.Add(edge-inward*.22f-Vector3.up*(4.1f+.48f*Mathf.Sin(j*.85f)));
                    faces.Add(edge+inward*.12f-Vector3.up*(11f+.6f*Mathf.Cos(j)));
                    if(j==0)continue;
                    for(int k=0;k<4;k++){
                        int tone=k==0?0:k==2?2:k==3?3:1;
                        int a=n-5+k,b=n+k;
                        if(side<0)faces.Quad(a,a+1,b+1,b,tone);
                        else faces.Quad(a,b,b+1,a+1,tone);
                    }
                }
            }
        }
        Model(group,"Two transverse glacial crevasses",faces,new[]{windpack,rock,ice,darkRock},true);
    }

    static float WallOpening(float s,float first,float last,float transition)
    {
        return Mathf.SmoothStep(0,1,Mathf.InverseLerp(first-transition,first,s))*
               (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(last,last+transition,s)));
    }
    static float OpenAmount(float s) => Mathf.Max(WallOpening(s,1.3f,2.9f,.4f),WallOpening(s,8.5f,13.8f,.7f));
    static bool OpenEdge(float s) => OpenAmount(s)>.99f;

    static void Cliff(Transform group,int side)
    {
        var geology=new Geometry(5);int rows=20*Subdivisions;
        for(int row=0;row<rows;row++)
        {
            float s0=row/(float)Subdivisions,s1=(row+1f)/Subdivisions;
            if(side<0 && s0>=16.75f)break;
            bool open=side==1&&OpenEdge((s0+s1)*.5f);
            Vector3[] Profile(float s)
            {
                Vector3 edge=Span(s,side),outward=Side(s)*side;
                float wall=6.3f+2.6f*Mathf.Sin(s*.6f)+2.1f*Mathf.Cos(s*1.15f);
                if(s>4&&s<8.2f)wall+=4.9f;
                if(s>8.2f&&s<12.9f)wall+=3.8f;
                if(s>13&&s<18.8f)wall+=3.6f;
                if(side<0){
                    float headWindow=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(15.25f,16.75f,s));
                    wall*=Mathf.Lerp(.025f,1,headWindow);
                    edge+=outward*(1-headWindow)*3.2f;
                }
                float join=Mathf.Sin(s*12.3f)+.65f*Mathf.Cos(s*6.6f);
                if(side>0){
                    wall*=1-OpenAmount(s);
                    float headRoom=Mathf.SmoothStep(0,1,Mathf.InverseLerp(16.25f,17.65f,s))*
                        (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(19.45f,20,s)));
                    var foot=edge-outward*.018f-Vector3.up*.05f;
                    var recess=outward*headRoom*4.4f;
                    var bank=new[]{foot,edge+outward*(.6f+join*.25f)+recess+Vector3.up*(wall*.17f),
                        edge+outward*(1.0f+join*.38f)+recess+Vector3.up*(wall*.38f),
                        edge+outward*(.65f+join*.45f)+recess+Vector3.up*(wall*.68f),
                        edge+outward*(1.3f+join*.5f)+recess+Vector3.up*wall,
                        edge+outward*(1.9f+join*.45f)+recess+Vector3.up*(wall+.45f*(1-OpenAmount(s))),
                        edge+outward*Mathf.Lerp(5.7f,.02f,OpenAmount(s))+recess+Vector3.up*(wall+(1.25f+join*.50f)*(1-OpenAmount(s)))};
                    float exposure=OpenAmount(s);
                    for(int k=1;k<bank.Length;k++){
                        Vector3 d=bank[k]-edge;
                        bank[k]=edge+V(d.x*(1-exposure),d.y,d.z*(1-exposure));
                    }
                    return bank;
                }
                // Carve a pocket out of the inner cliff. The horizontal return from
                // the recessed wall to the eave forms the underside of the overhang.
                float bay=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.31f,1.03f,Mathf.Abs(s-PawStation)));
                float recessDepth=9.6f*bay;
                float soffit=Mathf.Lerp(wall*.38f,4.2f+.22f*join,bay);
                return new[]{
                    edge+outward*(recessDepth-.018f)-Vector3.up*.05f,
                    edge+outward*(recessDepth+.6f+join*.25f)+Vector3.up*Mathf.Lerp(wall*.17f,2.45f,bay),
                    edge+outward*(recessDepth+1f+join*.32f)+Vector3.up*soffit,
                    edge+outward*Mathf.Lerp(.65f+join*.45f,-.16f+.24f*join,bay)+Vector3.up*Mathf.Lerp(wall*.68f,soffit+.47f,bay),
                    edge+outward*(1.3f+join*.5f)+Vector3.up*wall,
                    edge+outward*(1.9f+join*.45f)+Vector3.up*(wall+.45f),
                    edge+outward*5.7f+Vector3.up*(wall+1.25f+join*.50f)};

            }
            if(side>0){
                // Continue the snow surface into an undercut, rocky abyss wall.
                Vector3 Lip(float s,int layer){
                    var edge=Span(s,1);var across=Side(s);
                    float fluting=Mathf.Sin(4.7f*s)+.29f*Mathf.Sin(13.1f*s);
                    switch(layer){
                        case 0:return edge;
                        case 1:return edge+across*(.22f+.10f*fluting)-Vector3.up*.28f;
                        case 2:return edge+across*(.16f+.21f*fluting)-Vector3.up*1.05f;
                        case 3:return edge+across*(.45f+.61f*fluting)-Vector3.up*5.8f;
                        case 4:return edge+across*(2.4f+fluting)-Vector3.up*17;
                        default:return edge+across*(4.3f+fluting)-Vector3.up*34;
                    }
                }
                int cut=geology.points.Count;
                for(int k=0;k<=5;k++){geology.Add(Lip(s0,k));geology.Add(Lip(s1,k));}
                for(int k=0;k<5;k++){int n=cut+2*k;geology.Quad(n,n+2,n+3,n+1,k==0?4:k==1?2:k==3?1:0);}
                if(open)continue;
            }
            var a=Profile(s0);var b=Profile(s1);
            int first=geology.points.Count;
            for(int i=0;i<7;i++){geology.Add(a[i]);geology.Add(b[i]);}
            for(int layer=0;layer<6;layer++){
                
                int tone=layer>=4?4:layer==2?1:layer==0&&Mathf.Sin(s0*2.7f)>.3f?2:0;
                int n=first+layer*2;
                if(side==-1)geology.Quad(n,n+2,n+3,n+1,tone);
                else geology.Quad(n,n+1,n+3,n+2,tone);
            }
        }
        Model(group,side<0?"Inner cliff and snowy cornices":"Outer cliff, fissures and void",geology,new[]{rock,shale,darkRock,ice,windpack},true);
    }

    static void Stone(Geometry g,Vector3 position,Vector3 scale,float angle,int rockTone,int snowTone=-1)
    {
        int n=g.points.Count;var rot=Quaternion.Euler(0,angle,0);
        for(int ring=0;ring<2;ring++)
        for(int j=0;j<7;j++){
            float a=2*Mathf.PI*j/7f;float f=Sample(.78f,1.12f)*(ring==0?1:.68f);
            g.Add(position+rot*Vector3.Scale(V(Mathf.Cos(a)*f,ring==0?Sample(-.13f,.09f):Sample(.50f,.88f),Mathf.Sin(a)*f),scale));
        }
        g.Add(position+rot*Vector3.Scale(V(Sample(-.27f,.27f),1,Sample(-.30f,.30f)),scale));
        for(int j=0;j<7;j++){
            int k=(j+1)%7;g.Quad(n+j,n+j+7,n+k+7,n+k,rockTone);
            g.Tri(n+14,n+k+7,n+j+7,snowTone<0?rockTone:snowTone);
            if(j>0 && j<6)g.Tri(n,n+j,n+k,rockTone);
        }
    }
    static void SetDressing(Transform group)
    {
        var debris = new Geometry(3);var arch = new Geometry(3);
        for(int i=0;i<77;i++){
            float s=Sample(.35f,17.3f);int side=i%2==0?-1:1;
            if((side==1&&OpenEdge(s))||(side<0&&(Mathf.Abs(s-PawStation)<1.1f||s>15.75f))||IsGap(s*Subdivisions))continue;
            Vector3 p=Span(s,side)+Side(s)*side*Sample(.025f,.58f);
            Stone(debris,p,V(Sample(.18f,.91f),Sample(.12f,.65f),Sample(.24f,1.25f)),Sample(0,360),i%2,i%3==0?2:-1);
        }
        for(int i=0;i<34;i++){
            float s=Sample(.1f,3.25f);
            Vector3 p=Span(s,1)+Side(s)*Sample(1.1f,4.2f)-Vector3.up*Sample(4.8f,19f);
            Stone(debris,p,V(Sample(.4f,2.1f),Sample(.4f,2.9f),Sample(.7f,2.8f)),Sample(0,360),0,i%4==0?2:-1);
        }
        foreach(float s in new[]{1.70f,2.0f,3.32f}){
            Vector3 c=Track(s);
            Stone(arch,c+Side(s)*-.95f+Vector3.up*7.9f,V(4.5f,4.4f,2.6f),Sample(-30,36),0,2);
            Stone(arch,c+Side(s)*3.85f+Vector3.up*10.4f,V(4.2f,3.8f,2.8f),Sample(0,30),1,2);
        }
        // Broken strata immediately below the downhill snow lip.
        for(int i=0;i<42;i++){
            float s=8.7f+i*.12f;
            Vector3 p=Span(s,1)+Side(s)*Sample(.15f,.9f)-Vector3.up*Sample(1.4f,4.2f);
            Stone(debris,p,V(Sample(.48f,1.32f),Sample(.74f,1.72f),Sample(.36f,.86f)),Sample(-35,35),i%2,i%3==0?2:-1);
        }
        Model(group,"Rock feet and glacial rubble",debris,new[]{rock,shale,windpack});
        Model(group,"Broken overhead rock and narrow skylight",arch,new[]{darkRock,rock,shadedSnow});
    }
    static void Oval(Geometry g, Vector3 center,Vector3 size,Quaternion rotation,int tone,int cap=-1,int resolution=18)
    {
        int first=g.points.Count;
        const int rings=10;
        for(int k=0;k<=rings;k++)
        {
            float angle=Mathf.PI*k/rings, h=-Mathf.Cos(angle), r=Mathf.Sin(angle);
            for(int j=0;j<resolution;j++){
                float a=2*Mathf.PI*j/resolution;
                g.Add(center+rotation*Vector3.Scale(V(Mathf.Cos(a)*r,h,Mathf.Sin(a)*r),size));
            }
        }
        for(int k=0;k<rings;k++)
        for(int j=0;j<resolution;j++){
            int p=first+k*resolution+j,q=first+k*resolution+(j+1)%resolution;
            g.Quad(p,p+resolution,q+resolution,q,cap>=0&&k>=7?cap:tone);
        }
    }
    static void Line(Geometry geometry,Vector3 a,Vector3 b,float radius,int material)
    {
        var diff=b-a;if(diff.sqrMagnitude<.001f)return;
        Oval(geometry,(a+b)/2,V(radius,diff.magnitude*.61f,radius),
            Quaternion.FromToRotation(Vector3.up,diff.normalized),material,-1,8);
    }
    static void TurtlePaw(Transform group)
    {
        var g=new Geometry(4);
        var outward=Side(PawStation);
        var turn=Quaternion.LookRotation(outward,Vector3.up)*Quaternion.Euler(3,0,0);
        var offset=Track(PawStation)-outward*(OriginalHalfWidth(PawStation)+.52f);
        offset.y-=.10f;
        void Piece(Vector3 p,Vector3 size,int material,int snowCap=-1){
            Oval(g,offset+turn*p,size,turn,material,snowCap);
        }
        Piece(V(0,1.14f,-3.1f),V(2.25f,1.62f,4.2f),0,3);
        Piece(V(0,.46f,.15f),V(2.40f,.98f,2.31f),0,3);
        for(int i=0;i<4;i++){
            float x=(i-1.5f)*1.09f;
            Piece(V(x,.23f,1.95f-Mathf.Abs(x)*.30f),V(.54f,.56f,1.39f),1);
            Piece(V(x,.28f,3.14f-Mathf.Abs(x)*.30f),V(.27f,.29f,.59f),2);
        }
        for(int j=0;j<24;j++){
            float x=Sample(-1.9f,1.9f),z=Sample(-1.8f,2);
            Piece(V(x,1.37f-.10f*x*x-.10f*z*z,z),V(.29f,.15f,.34f),j%6==0?3:1);
        }
        var old=group.Find("Resting foreleg and claws");
        if(old)old.name="Right foreleg and claws";
        Model(group,"Right foreleg and claws",g,new[]{skin,scales,obsidian,shadedSnow},true);
    }

    static void ForelegChamberFloor(Transform group)
    {
        var ground=new Geometry(3);
        const int rows=72, cols=6;
        for(int i=0;i<=rows;i++){
            float s=Mathf.Lerp(PawStation-1.05f,PawStation+1.05f,i/(float)rows);
            var edge=Span(s,-1);var inward=-Side(s);
            float pocket=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.31f,1.05f,Mathf.Abs(s-PawStation)));
            for(int j=0;j<=cols;j++){
                float u=j/(float)cols;
                var p=edge+inward*(Mathf.Lerp(-.12f,1.8f+10f*pocket,u));
                p.y=Mathf.Lerp(edge.y-.032f,Track(PawStation).y-.33f+.055f*Mathf.Sin(s*4+u*5),u*pocket);
                ground.Add(p);
            }
            if(i==0)continue;
            for(int j=0;j<cols;j++){
                int a=(i-1)*(cols+1)+j,b=a+cols+1;
                ground.Quad(a,a+1,b+1,b,j==cols-1?2:j==cols-2?1:0);
            }
        }
        Model(group,"Snow and stone beneath the right foreleg",ground,new[]{snow,windpack,rock},true);
    }


    static void TurtleHead(Transform group)
    {
        var g=new Geometry(4);
        // The broad skull emerges at the turn. The exposed ridge above it carries the walkable trail.
        Oval(g,V(-1.18f,4.36f,100.15f),V(2.04f,1.38f,2.85f),Quaternion.Euler(13,-12,-4),0,3,26);
        Oval(g,V(-1.73f,4.10f,102.03f),V(1.64f,.78f,1.49f),Quaternion.Euler(0,-16,0),0,-1,24);
        Oval(g,V(-1.62f,3.74f,101.64f),V(1.66f,.58f,1.75f),Quaternion.Euler(0,-16,0),1);
        Oval(g,V(-1.75f,4.15f,103.05f),V(.77f,.56f,.63f),Quaternion.Euler(24,-10,0),0);

        for(int side=-1;side<=1;side+=2){
            float baseX=-1.33f+side*1.77f;
            var eye=V(baseX,4.87f,101.19f);
            Oval(g,eye,V(.42f,.36f,.67f),Quaternion.Euler(-7,side*15,side*16),1);
            Line(g,eye+V(side*.35f,.01f,-.45f),eye+V(side*.48f,-.080f,.13f),.092f,2);
            Line(g,eye+V(side*.48f,-.080f,.13f),eye+V(side*.35f,.02f,.49f),.074f,2);
            var a=V(-1.6f+side*1.66f,4.02f,100.79f);
            var b=V(-1.61f+side*1.56f,4.00f,102.03f);
            var c=V(-1.71f+side*.82f,3.94f,103.40f);
            Line(g,a,b,.071f,2);Line(g,b,c,.065f,2);
        }
        for(int i=0;i<24;i++){
            float t=Sample(-1,1),z=Sample(98.25f,101f);
            var p=V(-1.22f+t*1.78f,5.16f-.54f*t*t+(.1f*(z-99)),z);
            Oval(g,p,V(Sample(.17f,.40f),.12f,Sample(.19f,.46f)),Quaternion.Euler(0,Sample(0,180),0),1);
        }
        for(int i=0;i<g.points.Count;i++)
        {
            var point=g.points[i];
            float influence=Mathf.SmoothStep(0,1,Mathf.InverseLerp(97.1f,101.3f,point.z));
            g.points[i]=point+V(-2.38f,1.08f,.77f)*influence+Vector3.up*.33f;
        }
        Oval(g,V(-.7f,5.09f,97.6f),V(2.45f,1.71f,3.67f),Quaternion.Euler(19,0,0),0,3,24);
        Model(group,"Sleeping snapping turtle head and neck",g,new[]{skin,scales,obsidian,shadedSnow});
    }

    static float ShellY(float a,float r)
    {
        float crown=1-r*r;
        float crest=7.35f+9.75f*crown+SummitRise*crown*crown;
        float texture=.27f*Mathf.Sin(a*5+r*20)*r*(1-r*.57f)+.20f*Mathf.Sin(a*9-r*13)*r;
        return crest+texture;
    }
    static Vector3 ShellPoint(float a,float r,float height)
    {
        float shape=1+.024f*Mathf.Sin(3*a)+.012f*Mathf.Sin(8*a+.5f);
        return ShellCenter+V(Mathf.Cos(a)*r*ShellRadii.x*shape,height,Mathf.Sin(a)*r*ShellRadii.y*shape);
    }
    static void Carapace(Transform group)
    {
        const int Segments=128, Rings=40;
        var surface = new Geometry(4);
        int center=surface.Add(ShellCenter+Vector3.up*(16.8f+SummitRise));
        for(int ring=1;ring<=Rings;ring++)
        {
            float r=ring/(float)Rings;
            for(int j=0;j<Segments;j++){
                float a=j*2*Mathf.PI/Segments;
                float hollow=.34f*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.07f,.17f,r)));
                surface.Add(ShellPoint(a,r,ShellY(a,r)-hollow));
            }
            for(int j=0;j<Segments;j++){
                int k=(j+1)%Segments,o=1+(ring-1)*Segments;
                int tone=ring<=3?3:ring>=Rings-1?2:(ring+(int)(4*Mathf.Sin(j*.14f)))%12<2?1:0;
                if(ring==1)surface.Tri(center,o+k,o+j,tone);
                else surface.Quad(o+j-Segments,o+k-Segments,o+k,o+j,tone);
            }
        }
        Model(group,"Continuous snow dome and exposed summit patch",surface,new[]{snow,windpack,shadedSnow,shell},true);
        var wall=new Geometry();
        for(int i=0;i<Segments;i++){
            float a=2*Mathf.PI*i/Segments,b=2*Mathf.PI*(i+1)/Segments;
            float h=ShellY(a,1),k=ShellY(b,1);
            wall.Face(ShellPoint(a,.78f,-15),ShellPoint(a,1,h),ShellPoint(b,1,k),0);
            wall.Face(ShellPoint(a,.78f,-15),ShellPoint(b,1,k),ShellPoint(b,.78f,-15),0);
        }
        Model(group,"Falling edge and underside of shell",wall,new[]{shell},true);
        var rim=new Geometry(2);
        int plates=27;
        for(int i=0;i<plates;i++)
        {
            float a=i*2*Mathf.PI/plates,b=(i+1)*2*Mathf.PI/plates;
            int first=rim.points.Count;
            for(int j=0;j<=7;j++){
                float t=j/7f, x=Mathf.Lerp(a+.012f,b-.012f,t);
                float bulge=Mathf.Sin(Mathf.PI*t),h=ShellY(x,1);
                // Leave a flush passage at the neck.
                float lift=Mathf.Abs(Mathf.DeltaAngle(x*Mathf.Rad2Deg,90))<9?.04f:.16f;
                rim.Add(ShellPoint(x,.96f,h+lift));
                rim.Add(ShellPoint(x,1.008f,h-.10f+bulge*.17f));
                rim.Add(ShellPoint(x,1.005f,h-.69f+bulge*.11f));
                rim.Add(ShellPoint(x,.97f,h-1.05f));
                if(j==0)continue;
                int o=first+(j-1)*4;
                for(int layer=0;layer<3;layer++)rim.Quad(o+layer,o+layer+4,o+layer+5,o+layer+1,layer==0?1:0);
            }
        }
        Model(group,"Segmented outer carapace rim",rim,new[]{shellSeam,shell});

    }


    static void Summit(Transform group)
    {
        var g=new Geometry(2);
        var c=ShellCenter+Vector3.up*(17.28f+SummitRise);
        Oval(g,c+V(-.28f,-.12f,.04f),V(.49f,.91f,.67f),Quaternion.Euler(11,-22,23),0);
        Oval(g,c+V(.28f,.13f,.11f),V(.48f,.67f,.60f),Quaternion.Euler(-19,19,-24),0);
        Oval(g,c+V(.06f,-.46f,-.04f),V(.52f,.76f,.49f),Quaternion.Euler(20,0,-13),0);
        Line(g,c+V(-.46f,.47f,.18f),c+V(-.39f,1.02f,.25f),.19f,1);
        Line(g,c+V(.36f,.49f,.01f),c+V(.43f,1.09f,-.07f),.17f,1);
        Model(group,"Red heart scale proxy",g,new[]{heart,heartLight});
    }

    static void Mountains(Transform group)
    {
        // Continuous foothills share a valley floor. The ridge line breaks into snow
        // summits separated by saddles, including to the north of the open slope.
        void Range(string name,float start,float crest,float end,float altitude,int seed){
            const int around=240, bands=18;
            var g=new Geometry(3);
            for(int i=0;i<around;i++){
                float a=2*Mathf.PI*i/around;
                for(int j=0;j<=bands;j++){
                    float u=j/(float)bands;
                    float ridge=.51f+.08f*Mathf.Sin(4*a+seed)+.045f*Mathf.Sin(9*a+seed*.7f);
                    float r=Mathf.Lerp(start,end,u)+(crest-(start+end)*.5f)*Mathf.Sin(Mathf.PI*u);
                    float x=r*Mathf.Cos(a),z=r*Mathf.Sin(a);
                    float n=Mathf.PerlinNoise(30+seed+.060f*x,23+.060f*z);
                    float silhouette=.50f+.20f*Mathf.Sin(5*a+seed)+.14f*Mathf.Cos(9*a-.4f*seed)+.24f*n;
                    float flank=Mathf.Clamp01(u<ridge?u/ridge:(1-u)/(1-ridge));
                    float height=-44+altitude*silhouette*Mathf.Pow(flank,.82f);
                    height+=flank*(1-flank)*(12*n+6*Mathf.Sin(14*a+u*6));
                    g.Add(ShellCenter+V(x,height,z));
                }
            }
            for(int i=0;i<around;i++)for(int j=0;j<bands;j++){
                int a=i*(bands+1)+j,b=((i+1)%around)*(bands+1)+j;
                Vector3 normal=Vector3.Cross(g.points[b]-g.points[a],g.points[b+1]-g.points[a]).normalized;
                int tone=normal.y>.68f?2:normal.y>.49f?1:0;
                g.Quad(a,b,b+1,a+1,tone);
            }
            Model(group,name,g,new[]{rock,ice,windpack});
        }
        Range("Peaks below the open ledge",42,69,101,65,13);
        Range("Far ranges in fog",100,133,181,89,41);
    }

    static void WinterSky(VolumeProfile profile)
    {
        T Setting<T>() where T:VolumeComponent{
            if(profile.TryGet(out T setting))return setting;
            setting=profile.Add<T>();AssetDatabase.AddObjectToAsset(setting,profile);return setting;
        }
        var environment=Setting<VisualEnvironment>();environment.skyType.Override((int)SkyType.Gradient);
        var sky=Setting<GradientSky>();
        sky.top.Override(new Color(.32f,.52f,.77f));sky.middle.Override(new Color(.78f,.86f,.92f));
        sky.bottom.Override(new Color(.56f,.65f,.74f));sky.gradientDiffusion.Override(2.2f);
        sky.exposure.Override(10.2f);
        EditorUtility.SetDirty(environment);EditorUtility.SetDirty(sky);EditorUtility.SetDirty(profile);
    }

    static Volume Lighting(Transform group)
    {
        void Sun(string name,float intensity,Color color,Vector3 angles,bool shadows){
            var t=Group(name,group);var l=t.gameObject.AddComponent<Light>();
            l.type=LightType.Directional;t.gameObject.AddComponent<HDAdditionalLightData>();
            l.lightUnit=LightUnit.Lux;l.intensity=intensity;l.color=color;l.shadows=shadows?LightShadows.Soft:LightShadows.None;
            t.rotation=Quaternion.Euler(angles);
        }
        Sun("Overcast key",7710,new Color(.86f,.92f,1),V(48,-47,0),true);
        Sun("Snow bounce",2800,new Color(.71f,.83f,1),V(38,132,0),false);
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile,AssetsDir+"/Winter atmosphere.asset");
        var exposure=profile.Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(10);
        AssetDatabase.AddObjectToAsset(exposure,profile);
        var fog=profile.Add<Fog>();
        fog.enabled.Override(true);fog.colorMode.Override(FogColorMode.ConstantColor);
        fog.color.Override(new Color(.62f,.72f,.84f)*400);fog.meanFreePath.Override(118);
        fog.baseHeight.Override(9);fog.maximumHeight.Override(100);
        AssetDatabase.AddObjectToAsset(fog,profile);
        var blur=profile.Add<MotionBlur>();blur.intensity.Override(0);AssetDatabase.AddObjectToAsset(blur,profile);
        EditorUtility.SetDirty(profile);
        var volume=group.gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=30;volume.sharedProfile=profile;
        return volume;
    }

    static ParticleSystem Flurries(Transform weather)
    {
        var particles=Group("Wind-driven snow near the camera",weather).gameObject.AddComponent<ParticleSystem>();
        var main=particles.main;
        main.loop=true;main.startLifetime=new ParticleSystem.MinMaxCurve(.65f,1.75f);
        main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(.022f,.077f);
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=1650;
        var emission=particles.emission;emission.rateOverTime=0;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=V(26,11,24);
        var forces=particles.velocityOverLifetime;forces.enabled=true;forces.space=ParticleSystemSimulationSpace.World;
        forces.x=new ParticleSystem.MinMaxCurve(7,16);forces.y=new ParticleSystem.MinMaxCurve(-5,-1.5f);
        forces.z=new ParticleSystem.MinMaxCurve(-4,-.8f);
        var renderer=particles.GetComponent<ParticleSystemRenderer>();ConfigureSnow(particles);
        var snowParticle=new Material(Shader.Find("HDRP/Unlit"));
        snowParticle.SetColor("_UnlitColor",new Color(.72f,.84f,1)*1.65f);
        AssetDatabase.CreateAsset(snowParticle,AssetsDir+"/Wind snow.mat");
        renderer.sharedMaterial=snowParticle;
        return particles;
    }
    static void ConfigureSnow(ParticleSystem particles)
    {
        var main=particles.main;
        main.startSize=new ParticleSystem.MinMaxCurve(.025f,.077f);
        var renderer=particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode=ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize=.021f;
        if(renderer.sharedMaterial){
            renderer.sharedMaterial.SetColor("_UnlitColor",new Color(.72f,.84f,1)*1.65f);
            EditorUtility.SetDirty(renderer.sharedMaterial);
        }
    }

    static void PreviewPlayer(Transform root, Volume volume, ParticleSystem snowFx)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/Player.prefab");
        var player=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
        player.transform.position=V(-13,.08f,-7.3f);
        var rig=Group("Main Camera",root);rig.tag="MainCamera";
        rig.SetPositionAndRotation(V(-13,2.34f,-11.32f),Quaternion.Euler(12,0,0));
        var camera=rig.gameObject.AddComponent<Camera>();camera.fieldOfView=67;camera.farClipPlane=430;camera.nearClipPlane=.08f;
        var hd=rig.gameObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR=new Color(.73f,.83f,.94f);
        rig.gameObject.AddComponent<AudioListener>();
        rig.gameObject.AddComponent<SpringFollowCamera>().player=player.transform;
        var controller=player.GetComponent<PlayerController>();controller.cameraTransform=rig;controller.jumpSpeed=WinterJumpSpeed;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);

        var test=root.gameObject.AddComponent<WinterWalkthrough>();
        test.player=player.GetComponent<CharacterController>();test.view=camera;test.atmosphere=volume;test.windSnow=snowFx;
        test.shellCenter=Group("Carapace center",root);test.shellCenter.position=ShellCenter;test.shellRadii=ShellRadii;
    }

    static void MakeRockSurfacesTwoSided(Transform root)
    {
        var surfaceMaterials=new HashSet<Material>();
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
        foreach(var material in renderer.sharedMaterials)
        {
            if(material && AssetDatabase.GetAssetPath(material).StartsWith(AssetsDir+"/",System.StringComparison.Ordinal)
                && material.HasProperty("_DoubleSidedEnable"))
                surfaceMaterials.Add(material);
        }
        foreach(var material in surfaceMaterials)
        {
            material.SetFloat("_DoubleSidedEnable",1f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }
    }

    static void ScaleLandscape(Transform root)
    {
        // Place the passage and spawn in the same frame as the start of the rebuilt ravine.
        var shift=V(39,0,0);
        for(int i=1;i<=6;i++){
            var group=root.GetChild(i);group.localScale=Vector3.one*LandscapeScale;
            group.localPosition=shift;
        }
        var walk=root.GetComponent<WinterWalkthrough>();
        var entrance=root.GetChild(0);
        entrance.localPosition=shift+Track(0)*LandscapeScale;
        entrance.localRotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(Track(.02f)-Track(0),Vector3.up));
        var player=walk.player.transform;
        player.SetPositionAndRotation(entrance.TransformPoint(V(0,.08f,-3.5f)),entrance.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        walk.view.transform.SetPositionAndRotation(player.position+entrance.rotation*V(0,2.34f,-3.65f),entrance.rotation*Quaternion.Euler(12,0,0));
        walk.shellCenter.position=shift+ShellCenter*LandscapeScale;
        walk.shellRadii=ShellRadii*LandscapeScale;walk.landscapeScale=LandscapeScale;
        walk.view.farClipPlane=1250;
        walk.view.GetComponent<HDAdditionalCameraData>().clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
        WinterSky(walk.atmosphere.sharedProfile);
    }

    public static void RefineOpenScene()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath || EditorApplication.isPlaying)throw new System.InvalidOperationException("Open Winter in Edit Mode.");
        var root=scene.GetRootGameObjects()[0].transform;
        Material Existing(string name)=>AssetDatabase.LoadAssetAtPath<Material>(AssetsDir+"/"+name+".mat");
        snow=Existing("Powder snow");windpack=Existing("Wind packed snow");shadedSnow=Existing("Blue glacial snow");
        rock=Existing("Ash blue gneiss");shale=Existing("Slate seams");darkRock=Existing("Deep rock fractures");ice=Existing("Compressed glacial ice");
        skin=Existing("Turtle skin");scales=Existing("Raised scales");obsidian=Existing("Eyelids and mouth");
        shell=Existing("Ancient carapace");shellSeam=Existing("Carapace rim joints");
        random=new System.Random(63412);
        var entrance=root.GetChild(0);
        while(entrance.childCount>0)Object.DestroyImmediate(entrance.GetChild(entrance.childCount-1).gameObject);
        concrete=Existing("Entrance concrete");Passage(entrance);
        WalkingSurface(root.GetChild(1));
        Cliff(root.GetChild(1),-1);
        Cliff(root.GetChild(1),1);
        SetDressing(root.GetChild(1));
        Mountains(root.GetChild(6));
        ForelegChamberFloor(root.GetChild(1));
        TurtlePaw(root.GetChild(2));
        TurtleHead(root.GetChild(3));
        var cap=root.GetChild(4);
        foreach(string n in new[]{"Trackside carapace plates","Snow bank at the base of the carapace"}){
            var obsolete=cap.Find(n);if(obsolete)Object.DestroyImmediate(obsolete.gameObject);
        }
        Carapace(cap);
        heart=Existing("Exposed heart");heartLight=Existing("Heart red accent");Summit(root.GetChild(5));
        ScaleLandscape(root);
        var fx=root.GetComponentInChildren<ParticleSystem>();ConfigureSnow(fx);
        var controller=root.GetComponentInChildren<PlayerController>();
        controller.jumpSpeed=WinterJumpSpeed;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        MakeRockSurfacesTwoSided(root);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Build()
    {
        if(EditorApplication.isPlaying||AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            throw new System.InvalidOperationException("Winter must be created in Edit Mode, and the scene already exists or Play is active.");
        if(!AssetDatabase.IsValidFolder(AssetsDir))AssetDatabase.CreateFolder("Assets/_Game/Art","WinterBlockout");
        random=new System.Random(63412);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root=Group("Winter · Xuanwu");
        Palette();
        Passage(Group("01 · Concrete passage",root));
        var route=Group("02 · Ravines, chambers and open snow ledge",root);
        WalkingSurface(route);Cliff(route,-1);Cliff(route,1);SetDressing(route);
        ForelegChamberFloor(route);
        TurtlePaw(Group("03 · Foreleg chamber",root));
        TurtleHead(Group("04 · Sleeping head and neck crossing",root));
        Carapace(Group("05 · Snow-covered carapace",root));
        Summit(Group("06 · Heart on the summit",root));
        Mountains(Group("07 · Distant mountains",root));
        var weather=Group("08 · Winter weather",root);
        var volume=Lighting(weather);var particles=Flurries(weather);
        PreviewPlayer(root,volume,particles);
        ScaleLandscape(root);
        MakeRockSurfacesTwoSided(root);
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new System.InvalidOperationException("Winter scene save failed.");
    }
}

