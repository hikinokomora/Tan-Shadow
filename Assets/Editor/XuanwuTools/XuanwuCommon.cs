// Copy only into Assets/Editor/XuanwuTools. Unity 6 / no package dependencies.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace XuanwuTools {
 [Serializable] public class ProtectedArea { public Vector3 center, size; }
 [Serializable] public class Route { public string scenePath; public Vector3[] points; public float halfWidth, shoulder, step, maxSlope, maxStep, radius, height, castUp, castDown; public int groundMask, obstacleMask; public ProtectedArea[] protectedAreas; }
 [Serializable] public class Placement { public string id, prefab; public Vector3 position, euler, scale=Vector3.one; }
 [Serializable] public class Layout { public string scenePath, rootName; public Placement[] placements; }
 internal static class Common {
  public const string Menu="Tools/Xuanwu/";
  public static T Read<T>() where T:class { string p=EditorUtility.OpenFilePanel("Choose config", "", "json"); if(string.IsNullOrEmpty(p)) throw new OperationCanceledException(); var x=JsonUtility.FromJson<T>(File.ReadAllText(p)); if(x==null)throw new Exception("Invalid JSON"); return x; }
  public static void Run(Action a) {try{a();}catch(OperationCanceledException){}catch(Exception e){Debug.LogError("Xuanwu: "+e.Message);}}
  public static void SceneGuard(string path){var s=SceneManager.GetActiveScene(); if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount!=1 || string.IsNullOrEmpty(path)|| s.path!=path||!path.StartsWith("Assets/")||!path.EndsWith(".unity")||!Path.GetFileNameWithoutExtension(path).Contains("Xuanwu_Work"))throw new Exception("Open explicit Xuanwu_Work copy; config scenePath must match. No play mode.");}
  public static bool Confirm(string title,string desc)=>EditorUtility.DisplayDialog(title,desc,"Apply","Cancel");
  public static void Report(string name,string data){string dir=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"XuanwuReports"); Directory.CreateDirectory(dir); string p=Path.Combine(dir,name+"_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+".txt"); File.WriteAllText(p,data); Debug.Log("Xuanwu report: "+p);}
  public static IEnumerable<T> SceneObjects<T>() where T:Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<T>(true));
  public static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
  public static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
  public static void RouteCheck(Route r){if(r.protectedAreas==null||r.protectedAreas.Any(a=>a==null||!Finite(a.center)||!Finite(a.size)||a.size.x<=0||a.size.z<=0)||!Finite(r.maxSlope)||!Finite(r.maxStep)||!Finite(r.radius)||!Finite(r.height)||!Finite(r.castUp)||!Finite(r.castDown)||r.points==null||r.points.Length<2||r.points.Any(p=>!Finite(p))||r.points.Length>500||!Finite(r.halfWidth)||!Finite(r.shoulder)||r.halfWidth<=0||r.shoulder<0||!Finite(r.step)||r.step<.1f||r.step>2||r.maxSlope<=0||r.maxSlope>=89||r.maxStep<0||r.radius<=0||r.height<2*r.radius||r.castUp<=0||r.castDown<=0||r.groundMask==0||r.obstacleMask==0)throw new Exception("Fill route/controller/layer metrics; invalid config"); for(int i=1;i<r.points.Length;i++){Vector3 d=r.points[i]-r.points[i-1]; if(new Vector2(d.x,d.z).sqrMagnitude<.0001f)throw new Exception("Duplicate horizontal route point");} }
  public static float Nearest(Vector3 p,Vector3[] a,out float y){float best=float.PositiveInfinity;y=0;for(int i=1;i<a.Length;i++){var v=new Vector2(a[i].x-a[i-1].x,a[i].z-a[i-1].z);var w=new Vector2(p.x-a[i-1].x,p.z-a[i-1].z);float t=Mathf.Clamp01(Vector2.Dot(w,v)/v.sqrMagnitude);float d=(w-v*t).magnitude;if(d<best){best=d;y=Mathf.Lerp(a[i-1].y,a[i].y,t);}}return best;}
  public static string PathOf(Component c)=>AnimationUtility.CalculateTransformPath(c.transform,null);
  public static bool Protected(Vector3 p,ProtectedArea[] bounds)=>bounds!=null&&bounds.Any(a=>Mathf.Abs(p.x-a.center.x)<=a.size.x*.5f&&Mathf.Abs(p.z-a.center.z)<=a.size.z*.5f);
  public static Terrain SelectedTerrain(){var t=Selection.activeGameObject?Selection.activeGameObject.GetComponent<Terrain>():null;if(t==null||t.terrainData==null||t.gameObject.scene!=SceneManager.GetActiveScene())throw new Exception("Select Terrain in active scene");return t;}
  public static TerrainData CloneData(Terrain t){const string dir="Assets/XuanwuGenerated";if(!AssetDatabase.IsValidFolder(dir))AssetDatabase.CreateFolder("Assets","XuanwuGenerated");var d=UnityEngine.Object.Instantiate(t.terrainData);string path=AssetDatabase.GenerateUniqueAssetPath(dir+"/TerrainData.asset");AssetDatabase.CreateAsset(d,path);Undo.RecordObject(t,"Assign copied terrain data");t.terrainData=d;var c=t.GetComponent<TerrainCollider>();if(c){Undo.RecordObject(c,"Assign copied terrain collider");c.terrainData=d;}EditorSceneManager.MarkSceneDirty(t.gameObject.scene);return d;}
 }
}
