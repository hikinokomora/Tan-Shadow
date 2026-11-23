using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.MotionMatchingLab.Editor
{
    static class MotionLibraryBaker
    {
        internal const string Folder = "Assets/_Game/TestMotionmatching";
        const string Imports = Folder + "/animations";
        internal const string RigPath = Imports + "/_Rig/SKM_UEFN_Mannequin.fbx";
        internal const string DatabasePath = Folder + "/Data/MotionDatabase.asset";
        static readonly string[] Sources = { "Idle", "Walk", "Run", "Sprint", "Crouch", "Jump", "Traversal" };

        internal static MotionDatabase Bake(GameObject model)
        {
            var animationPaths = Sources.SelectMany(folder =>
                Directory.GetFiles(Imports + "/" + folder, "*.fbx", SearchOption.AllDirectories))
                .Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var clips = new List<MotionClip>(animationPaths.Length);
            var frames = new List<MotionFrame>(42000);

            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                Transform root = Bone(rig, "root");
                Transform hips = Bone(rig, "pelvis");
                Transform left = Bone(rig, "foot_l");
                Transform right = Bone(rig, "foot_r");
                Quaternion inverseBind = Quaternion.Inverse(root.localRotation);
                foreach (string path in animationPaths)
                {
                    string label = Path.GetFileNameWithoutExtension(path);
                    if (!Describe(path, label, out MotionKind kind, out Gait gait))
                        continue;
                    var asset = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                        .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));
                    if (asset == null || asset.length < .06f)
                        throw new InvalidOperationException("Missing a playable clip in " + path);

                    bool locomotion = kind == MotionKind.Ground || kind == MotionKind.Crouch;
                    int sampleCount = Mathf.CeilToInt(asset.length * (locomotion ? 16 : 30)) + 1;
                    var clip = new MotionClip
                    {
                        label = label, animation = asset, kind = kind, gait = gait,
                        sampleStep = asset.length / (sampleCount - 1),
                        root = new MotionRoot[sampleCount],
                        catchFromAir = path.Contains("/Traversal/Catch/", StringComparison.Ordinal),
                        heavy = label.Contains("_Heavy", StringComparison.Ordinal) || label.Contains("_Roll", StringComparison.Ordinal)
                    };
                    var poses = new MotionPose[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        asset.SampleAnimation(rig, clip.sampleStep * i);
                        float yaw = (root.localRotation * inverseBind).eulerAngles.y;
                        Vector3 origin = root.localPosition;
                        Quaternion toLocal = Quaternion.Euler(0f, -yaw, 0f);
                        clip.root[i] = new MotionRoot { position = origin, yaw = yaw };
                        poses[i] = new MotionPose
                        {
                            hips = toLocal * (rig.transform.InverseTransformPoint(hips.position) - origin),
                            leftFoot = toLocal * (rig.transform.InverseTransformPoint(left.position) - origin),
                            rightFoot = toLocal * (rig.transform.InverseTransformPoint(right.position) - origin)
                        };
                    }

                    clip.loop = label.Contains("_Loop", StringComparison.Ordinal)
                                && (kind == MotionKind.Fall
                                    || (locomotion && Mathf.Abs(Mathf.DeltaAngle(clip.root[0].yaw, clip.root[^1].yaw)) < 4f));
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int before = Mathf.Max(0, i - 1), after = Mathf.Min(sampleCount - 1, i + 1);
                        float interval = (after - before) * clip.sampleStep;
                        poses[i].leftVelocity = Vector3.ClampMagnitude((poses[after].leftFoot - poses[before].leftFoot) / interval, 15f);
                        poses[i].rightVelocity = Vector3.ClampMagnitude((poses[after].rightFoot - poses[before].rightFoot) / interval, 15f);
                    }
                    if (locomotion)
                        BakeFrames(clip, clips.Count, poses, frames);
                    else
                    {
                        clip.actionPoses = poses;
                        FindActionWindow(clip);
                    }
                    clips.Add(clip);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            int idle = clips.FindIndex(clip => clip.label == "M_Neutral_Stand_Idle_Loop");
            if (idle < 0 || frames.Count == 0 || !clips.Any(clip => clip.kind == MotionKind.Fall)
                || !clips.Any(clip => clip.kind == MotionKind.Crouch))
                throw new InvalidOperationException("Motion Matching Lab: required locomotion clips are missing.");

            var database = AssetDatabase.LoadAssetAtPath<MotionDatabase>(DatabasePath);
            if (!database)
            {
                database = ScriptableObject.CreateInstance<MotionDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }
            database.SetData(clips.ToArray(), frames.ToArray(), idle);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
            return database;
        }

        internal static Transform Bone(GameObject rig, string name)
        {
            Transform bone = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
            return bone ? bone : throw new InvalidOperationException("Mannequin bone missing: " + name);
        }

        static bool Describe(string path, string label, out MotionKind kind, out Gait gait)
        {
            gait = Gait.Idle;
            kind = MotionKind.Ground;
            if (path.Contains("/Traversal/", StringComparison.Ordinal))
            {
                if (label.Contains("_Hurdle_", StringComparison.Ordinal))
                    kind = MotionKind.Hurdle;
                else if (label.Contains("_Vault_", StringComparison.Ordinal))
                    kind = MotionKind.Vault;
                else if (label.Contains("_Mantle_", StringComparison.Ordinal))
                    kind = MotionKind.Mantle;
                else if (label.Contains("_Climb_", StringComparison.Ordinal) || label.Contains("_Cliff_", StringComparison.Ordinal))
                    kind = MotionKind.Climb;
                else
                    return false;
            }
            else if (path.Contains("/Jump/", StringComparison.Ordinal))
            {
                kind = label.Contains("_Land_", StringComparison.Ordinal) ? MotionKind.Land
                    : (label.Contains("_Start_", StringComparison.Ordinal) ? MotionKind.Jump : MotionKind.Fall);
            }
            else if (label.Contains("_Crouch_", StringComparison.Ordinal) || path.Contains("/Crouch/", StringComparison.Ordinal))
            {
                kind = MotionKind.Crouch;
                gait = Gait.Crouch;
            }
            else if (path.Contains("/Walk/", StringComparison.Ordinal))
                gait = Gait.Walk;
            else if (path.Contains("/Run/", StringComparison.Ordinal))
                gait = Gait.Run;
            else if (path.Contains("/Sprint/", StringComparison.Ordinal))
                gait = Gait.Sprint;
            return true;
        }

        static void BakeFrames(MotionClip clip, int index, MotionPose[] poses, List<MotionFrame> frames)
        {
            for (int i = 0; i < poses.Length - 1; i++)
            {
                float time = clip.sampleStep * i;
                if (!clip.loop && time > clip.Length - .16f && i > 0)
                    break;

                MotionRoot root = clip.root[i];
                MotionRoot near = clip.SampleRoot(time + MotionTrajectory.Near);
                MotionRoot mid = clip.SampleRoot(time + MotionTrajectory.Mid);
                MotionRoot far = clip.SampleRoot(time + MotionTrajectory.Far);
                Quaternion inverse = Quaternion.Euler(0f, -root.yaw, 0f);
                frames.Add(new MotionFrame
                {
                    clip = index,
                    time = time,
                    pose = poses[i],
                    velocity = Flat(inverse * clip.Velocity(time)),
                    trajectory = new MotionTrajectory
                    {
                        near = Flat(inverse * (near.position - root.position)),
                        mid = Flat(inverse * (mid.position - root.position)),
                        far = Flat(inverse * (far.position - root.position)),
                        facingNear = Flat(Quaternion.Euler(0f, near.yaw - root.yaw, 0f) * Vector3.forward),
                        facingFar = Flat(Quaternion.Euler(0f, far.yaw - root.yaw, 0f) * Vector3.forward)
                    }
                });
            }
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static void FindActionWindow(MotionClip clip)
        {
            MotionRoot[] keys = clip.root;
            float baseline = keys[0].position.y, top = keys[^1].position.y;
            int peak = 0;
            for (int i = 1; i < keys.Length; i++)
                if (keys[i].position.y > keys[peak].position.y)
                    peak = i;
            clip.peakHeight = keys[peak].position.y - baseline;

            int marker = 0;
            float duration = .55f;
            float TimeAt(int frame) => frame * clip.sampleStep;
            if (clip.kind == MotionKind.Jump)
            {
                marker = FirstY(keys, 1, baseline + .075f, true);
                duration = .34f;
            }
            else if (clip.kind == MotionKind.Land)
            {
                marker = FirstY(keys, 0, top + .055f, false);
                duration = .43f;
            }
            else if (clip.kind == MotionKind.Mantle || clip.kind == MotionKind.Climb)
            {
                int first = 0;
                if (clip.catchFromAir)
                {
                    for (int i = 1; i < keys.Length && keys[i].position.z < .05f; i++)
                        if (keys[i].position.y < keys[first].position.y)
                            first = i;
                }
                marker = FirstY(keys, first, top - .10f, true);
                duration = clip.kind == MotionKind.Climb ? .88f : .64f;
            }
            else if (clip.kind == MotionKind.Vault || clip.kind == MotionKind.Hurdle)
            {
                marker = peak;
                if (clip.catchFromAir)
                    marker = Array.FindIndex(keys, frame => frame.position.z >= 0f);
                int landing = FirstY(keys, Mathf.Clamp(marker + 2, 0, keys.Length - 1), baseline + .075f, false);
                duration = clip.catchFromAir ? .52f : Mathf.Clamp(TimeAt(landing - marker) + .09f, .28f, .78f);
            }

            clip.eventTime = TimeAt(Mathf.Clamp(marker, 0, keys.Length - 2));
            if (clip.kind >= MotionKind.Vault)
                clip.eventTime = Mathf.Clamp(clip.eventTime, .13f, clip.Length - .13f);
            clip.exitTime = Mathf.Min(clip.Length, clip.eventTime + duration);
        }

        static int FirstY(MotionRoot[] values, int start, float limit, bool rising)
        {
            for (int i = start; i < values.Length; i++)
                if (rising ? values[i].position.y >= limit : values[i].position.y <= limit)
                    return i;
            return start;
        }
    }
}
