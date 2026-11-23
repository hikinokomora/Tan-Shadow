using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.MotionMatchingLab.Editor
{
    static class AerialRollBaker
    {
        const string AssetPath = MotionLibraryBaker.Folder + "/Data/Aerial Roll.anim";
        const string LaunchClip = "M_Neutral_Jump_F_Start_Stand_Rfoot";
        const string RollClip = "M_Neutral_Jump_F_Land_Roll_Rfoot";
        const string LandingClip = "M_Neutral_Jump_F_Land_Stand_Light_Rfoot";

        sealed class PoseKey
        {
            public float time;
            public Vector3[] positions;
            public Quaternion[] rotations;
        }

        internal static AnimationClip Bake(GameObject model, MotionDatabase data)
        {
            MotionClip idle = data.Clips[data.IdleClip];
            MotionClip launch = Find(data, LaunchClip);
            MotionClip tuck = Find(data, RollClip);
            MotionClip landing = Find(data, LandingClip);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                Transform skeleton = MotionLibraryBaker.Bone(rig, "root");
                Transform pelvis = MotionLibraryBaker.Bone(rig, "pelvis");
                Vector3 modelPosition = rig.transform.localPosition, rootPosition = skeleton.localPosition;
                Quaternion modelRotation = rig.transform.localRotation, rootRotation = skeleton.localRotation;
                Transform[] bones = skeleton.GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
                var keys = new List<PoseKey>(12);

                void Sample(MotionClip source, float time)
                {
                    source.animation.SampleAnimation(rig, Mathf.Clamp(time, 0f, source.Length));
                    rig.transform.SetLocalPositionAndRotation(modelPosition, modelRotation);
                    skeleton.SetLocalPositionAndRotation(rootPosition, rootRotation);
                }

                void Key(float time)
                {
                    keys.Add(new PoseKey
                    {
                        time = time,
                        positions = bones.Select(bone => bone.localPosition).ToArray(),
                        rotations = bones.Select(bone => bone.localRotation).ToArray()
                    });
                }

                Sample(idle, 0f);
                Vector3 pelvisNeutral = pelvis.position;
                Quaternion pelvisUpright = pelvis.rotation;
                Key(0f);

                Sample(launch, launch.eventTime - .12f);
                Key(.052f);
                Sample(launch, launch.eventTime - .055f);
                Key(MotionMatchingPlayer.DodgeTakeoff);
                Sample(launch, launch.eventTime + .09f);
                Key(.196f);

                Sample(tuck, tuck.eventTime + .43f);
                SetTuck(rig, pelvis, pelvisNeutral, pelvisUpright, .82f);
                Key(.355f);
                Sample(tuck, tuck.eventTime + .47f);
                SetTuck(rig, pelvis, pelvisNeutral, pelvisUpright, 1f);
                Key(.505f);
                Sample(tuck, tuck.eventTime + .51f);
                SetTuck(rig, pelvis, pelvisNeutral, pelvisUpright, .76f);
                Key(.64f);

                Sample(landing, landing.eventTime - .095f);
                Key(.775f);
                Sample(landing, landing.eventTime + .012f);
                Key(MotionMatchingPlayer.DodgeTouchdown);
                Sample(landing, landing.eventTime + .105f);
                Key(.925f);
                Sample(idle, 0f);
                Key(1f);

                return WriteClip(rig.transform, bones, keys);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static MotionClip Find(MotionDatabase data, string name) =>
            data.Clips.First(clip => clip.label == name);

        static void SetTuck(GameObject rig, Transform pelvis, Vector3 neutralPosition, Quaternion upright, float curl)
        {
            pelvis.position = neutralPosition + new Vector3(0f, -.19f * curl, -.035f);
            pelvis.rotation = Quaternion.AngleAxis(15f * curl, Vector3.right) * upright;

            foreach (string side in new[] { "l", "r" })
            {
                Transform thigh = MotionLibraryBaker.Bone(rig, "thigh_" + side);
                Transform shin = MotionLibraryBaker.Bone(rig, "calf_" + side);
                Transform foot = MotionLibraryBaker.Bone(rig, "foot_" + side);
                float sign = Mathf.Sign(thigh.position.x - pelvis.position.x);
                Vector3 heel = pelvis.position + new Vector3(sign * .145f, -.29f, .25f);
                MotionTwoBone.Reach(thigh, shin, foot, Vector3.Lerp(foot.position, heel, curl), Vector3.forward);

                Transform upper = MotionLibraryBaker.Bone(rig, "upperarm_" + side);
                Transform lower = MotionLibraryBaker.Bone(rig, "lowerarm_" + side);
                Transform hand = MotionLibraryBaker.Bone(rig, "hand_" + side);
                Vector3 grip = pelvis.position + new Vector3(sign * .23f, .15f, .44f);
                MotionTwoBone.Reach(upper, lower, hand, Vector3.Lerp(hand.position, grip, curl), Vector3.right * sign);
            }
        }

        static AnimationClip WriteClip(Transform rig, Transform[] bones, List<PoseKey> keys)
        {
            var clip = new AnimationClip { name = "Aerial Roll", frameRate = 60f, wrapMode = WrapMode.ClampForever };
            var bindings = new List<EditorCurveBinding>(bones.Length * 7);
            var curves = new List<AnimationCurve>(bones.Length * 7);
            for (int i = 0; i < bones.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[i], rig);
                Quaternion last = Quaternion.identity;
                var channels = Enumerable.Range(0, 7).Select(_ => new Keyframe[keys.Count]).ToArray();
                for (int j = 0; j < keys.Count; j++)
                {
                    PoseKey key = keys[j];
                    Quaternion q = key.rotations[i];
                    if (j > 0 && Quaternion.Dot(last, q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    last = q;
                    for (int k = 0; k < 3; k++)
                        channels[k][j] = new Keyframe(key.time, key.positions[i][k]);
                    for (int k = 0; k < 4; k++)
                        channels[k + 3][j] = new Keyframe(key.time, q[k]);
                }

                for (int k = 0; k < channels.Length; k++)
                {
                    bool position = k < 3;
                    string property = (position ? "m_LocalPosition." : "m_LocalRotation.") + "xyzw"[position ? k : k - 3];
                    var curve = new AnimationCurve(channels[k]);
                    for (int j = 0; j < curve.length; j++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), property));
                    curves.Add(curve);
                }
            }

            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetPath);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                clip = existing;
            }
            else
                AssetDatabase.CreateAsset(clip, AssetPath);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }
    }
}
