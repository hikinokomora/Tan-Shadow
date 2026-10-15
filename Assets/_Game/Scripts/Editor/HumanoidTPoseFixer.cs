using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Ставит гуманоидному аватару FBX правильную эталонную T-позу.
    // Зачем: у анимаций Mixamo без скина аватар строится по первому кадру, а не по T-позе —
    // голова опущена, плечи завалены, стопы вытянуты носком. Ретаргет переносит эти ошибки в каждую анимацию:
    // персонаж смотрит в пол, сутулится и ходит на цыпочках. После исправления эталон у источника и у нашей
    // модели одинаковый (корпус и шея вертикально, руки в стороны ладонями вниз, ноги прямо, стопа плоско),
    // и движение переносится как есть.
    public static class HumanoidTPoseFixer
    {
        public struct Options
        {
            public bool fixFeet;      // для нашей модели стопы уже стоят на полу в бинд-позе
            public float footPitch;   // угол «щиколотка → основание пальцев» вниз от горизонта для плоской стопы, °
        }

        public static readonly Options Mixamo = new Options { fixFeet = true, footPitch = 30f };
        public static readonly Options Character = new Options { fixFeet = false, footPitch = 0f };

        [MenuItem("Tools/Tan Shadow/Fix Humanoid T-Pose (Mixamo animations)")]
        static void FixSelectedMixamo()
        {
            int count = 0;
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (Fix(path, Mixamo)) count++;
            }
            Debug.Log($"HumanoidTPoseFixer: fixed {count} model(s).");
        }

        public static bool Fix(string path, Options options)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) return false;

            var description = importer.humanDescription;
            if (description.human == null || description.human.Length == 0) return false;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = Object.Instantiate(asset);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var byName = new Dictionary<string, Transform>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (t != instance.transform) byName[t.name] = t;

                // начинаем с текущей эталонной позы аватара
                foreach (var bone in description.skeleton)
                    if (byName.TryGetValue(bone.name, out var t)) { t.localPosition = bone.position; t.localRotation = bone.rotation; }

                var human = new Dictionary<string, Transform>();
                foreach (var h in description.human)
                    if (byName.TryGetValue(h.boneName, out var t)) human[h.humanName] = t;

                Transform H(string name) => human.TryGetValue(name, out var t) ? t : null;

                Vector3 up = Vector3.up;
                Vector3 forward = Vector3.Cross(H("RightUpperLeg").position - H("LeftUpperLeg").position, up).normalized;
                Vector3 right = Vector3.Cross(up, forward).normalized;

                // корпус, шея, голова — вертикально
                var spineChain = new List<Transform>();
                foreach (var n in new[] { "Spine", "Chest", "UpperChest", "Neck", "Head" })
                    if (H(n) != null) spineChain.Add(H(n));
                for (int i = 0; i + 1 < spineChain.Count; i++) Aim(spineChain[i], spineChain[i + 1].position, up);
                var head = H("Head");
                if (head != null && head.childCount > 0) Aim(head, head.GetChild(0).position, up);

                foreach (var side in new[] { "Left", "Right" })
                {
                    Vector3 outward = side == "Left" ? -right : right;
                    AimChain(H(side + "Shoulder"), H(side + "UpperArm"), outward);
                    AimChain(H(side + "UpperArm"), H(side + "LowerArm"), outward);
                    AimChain(H(side + "LowerArm"), H(side + "Hand"), outward);
                    AimChain(H(side + "Hand"), H(side + " Middle Proximal"), outward);

                    // ладонью вниз: большой палец смотрит вперёд
                    var index = H(side + " Index Proximal");
                    var little = H(side + " Little Proximal");
                    var forearm = H(side + "LowerArm");
                    if (index != null && little != null && forearm != null)
                    {
                        Vector3 across = Vector3.ProjectOnPlane(index.position - little.position, outward);
                        if (across.sqrMagnitude > 1e-8f)
                        {
                            float twist = Vector3.SignedAngle(across, forward, outward);
                            forearm.rotation = Quaternion.AngleAxis(twist, outward) * forearm.rotation;
                        }
                    }

                    foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
                    {
                        var p = H($"{side} {finger} Proximal");
                        var m = H($"{side} {finger} Intermediate");
                        var d = H($"{side} {finger} Distal");
                        AimChain(p, m, outward);
                        AimChain(m, d, outward);
                        if (d != null && d.childCount > 0) Aim(d, d.GetChild(0).position, outward);
                    }
                    var t1 = H(side + " Thumb Proximal");
                    var t2 = H(side + " Thumb Intermediate");
                    var t3 = H(side + " Thumb Distal");
                    if (t1 != null && t2 != null)
                    {
                        Vector3 thumbDir = (t2.position - t1.position).normalized;
                        AimChain(t2, t3, thumbDir);
                        if (t3 != null && t3.childCount > 0) Aim(t3, t3.GetChild(0).position, thumbDir);
                    }

                    AimChain(H(side + "UpperLeg"), H(side + "LowerLeg"), Vector3.down);
                    AimChain(H(side + "LowerLeg"), H(side + "Foot"), Vector3.down);
                    if (options.fixFeet)
                    {
                        var foot = H(side + "Foot");
                        var toes = H(side + "Toes");
                        if (foot != null && toes != null)
                        {
                            Vector3 heading = Vector3.ProjectOnPlane(toes.position - foot.position, up);
                            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : forward;
                            float pitch = options.footPitch * Mathf.Deg2Rad;
                            Aim(foot, toes.position, (heading * Mathf.Cos(pitch) - up * Mathf.Sin(pitch)).normalized);
                            if (toes.childCount > 0) Aim(toes, toes.GetChild(0).position, heading);
                        }
                    }
                }

                var skeleton = description.skeleton;
                for (int i = 0; i < skeleton.Length; i++)
                    if (byName.TryGetValue(skeleton[i].name, out var t)) skeleton[i].rotation = t.localRotation;
                description.skeleton = skeleton;
                importer.humanDescription = description;
                importer.SaveAndReimport();
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void AimChain(Transform bone, Transform child, Vector3 direction)
        {
            if (bone != null && child != null) Aim(bone, child.position, direction);
        }

        static void Aim(Transform bone, Vector3 childPosition, Vector3 direction)
        {
            Vector3 current = childPosition - bone.position;
            if (current.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(current, direction) * bone.rotation;
        }
    }
}
