using System.Collections.Generic;
using System.IO;
using ShadowTan.Combat;
using ShadowTan.Core;
using ShadowTan.Player;
using UnityEditor;
using UnityEngine;

namespace ShadowTan.EditorTools
{
    // Пункты меню, которые собирают данные и настройки проекта без ручного YAML (§29.4).
    public static class PlayerSetupMenu
    {
        const string DataRoot = "Assets/_Project/Data";
        const string SurfaceRoot = DataRoot + "/Surfaces";

        struct SurfaceRow
        {
            public string Name;
            public bool AcceptsStab;

            public SurfaceRow(string name, bool acceptsStab)
            {
                Name = name;
                AcceptsStab = acceptsStab;
            }
        }

        static readonly SurfaceRow[] Surfaces =
        {
            new SurfaceRow("Surface_Default", false),
            new SurfaceRow("Surface_Stone", false),
            new SurfaceRow("Surface_Sandstone", false),
            new SurfaceRow("Surface_Boulder", false),
            new SurfaceRow("Surface_Ash", false),
            new SurfaceRow("Surface_Sand", false),
            new SurfaceRow("Surface_Bamboo", false),
            new SurfaceRow("Surface_Metal", false),
            new SurfaceRow("Surface_Snow", false),
            new SurfaceRow("Surface_Rock", false),
            new SurfaceRow("Surface_DragonScale", false),
            new SurfaceRow("Surface_Feathers", true),
            new SurfaceRow("Surface_SnakeRidge", false),
            new SurfaceRow("Surface_TurtleShell", false),
            new SurfaceRow("Surface_Flesh", true),
            new SurfaceRow("Surface_Pearl", false),
            new SurfaceRow("Surface_Greybox", false)
        };

        [MenuItem("ShadowTan/Setup/Player/Create Data Assets")]
        public static void CreatePlayerData()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(DataRoot + "/Player");
            EnsureFolder(SurfaceRoot);

            var configPath = DataRoot + "/Player/PlayerConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<PlayerConfig>(configPath) == null)
            {
                var config = ScriptableObject.CreateInstance<PlayerConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            foreach (var row in Surfaces)
            {
                var path = $"{SurfaceRoot}/{row.Name}.asset";
                var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(path);
                if (surface == null)
                {
                    surface = ScriptableObject.CreateInstance<SurfaceDefinition>();
                    AssetDatabase.CreateAsset(surface, path);
                }

                surface.displayName = row.Name;
                surface.acceptsStab = row.AcceptsStab;
                EditorUtility.SetDirty(surface);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ShadowTan: PlayerConfig и поверхности созданы в Assets/_Project/Data");
        }

        [MenuItem("ShadowTan/Setup/Configure Layers")]
        public static void ConfigureLayers()
        {
            var tagManagerPath = "ProjectSettings/TagManager.asset";
            var tagManager = AssetDatabase.LoadAllAssetsAtPath(tagManagerPath);
            if (tagManager == null || tagManager.Length == 0)
            {
                Debug.LogError("ShadowTan: не найден ProjectSettings/TagManager.asset");
                return;
            }

            var serialized = new SerializedObject(tagManager[0]);
            var layers = serialized.FindProperty("layers");

            var names = new Dictionary<int, string>
            {
                { Layers.GroundIndex, Layers.Ground },
                { Layers.WallIndex, Layers.Wall },
                { Layers.CoverIndex, Layers.Cover },
                { Layers.PlayerIndex, Layers.Player },
                { Layers.PlayerHitboxIndex, Layers.PlayerHitbox },
                { Layers.ColossusIndex, Layers.Colossus },
                { Layers.WeakPointIndex, Layers.WeakPoint },
                { Layers.GrabPointIndex, Layers.GrabPoint },
                { Layers.KillVolumeIndex, Layers.KillVolume },
                { Layers.TriggerIndex, Layers.Trigger },
                { Layers.LavaIndex, Layers.Lava },
                { Layers.CameraOnlyIndex, Layers.CameraOnly },
                { Layers.InteractableIndex, Layers.Interactable }
            };

            var changed = 0;
            foreach (var pair in names)
            {
                var element = layers.GetArrayElementAtIndex(pair.Key);
                if (element.stringValue == pair.Value) continue;

                if (!string.IsNullOrEmpty(element.stringValue))
                {
                    Debug.LogWarning($"ShadowTan: слой {pair.Key} был \"{element.stringValue}\", переименован в \"{pair.Value}\". " +
                                     "Проверь объекты, которые использовали старый слой.");
                }

                element.stringValue = pair.Value;
                changed++;
            }

            if (changed > 0)
            {
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"ShadowTan: настроено слоёв — {changed}. Матрицу столкновений §7.6 задай в Edit → Project Settings → Physics вручную.");
        }

        [MenuItem("ShadowTan/Validate/Player Setup")]
        public static void ValidatePlayerSetup()
        {
            var player = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<PlayerController>()
                : Object.FindAnyObjectByType<PlayerController>();

            if (player == null)
            {
                Debug.LogError("ShadowTan: в проекте нет PlayerController");
                return;
            }

            var missing = new List<string>();
            var playerObject = player.gameObject;
            Require<PlayerMotor>(playerObject, missing);
            Require<PlayerInputReader>(playerObject, missing);
            Require<PlayerAnimatorBridge>(playerObject, missing);
            Require<SwordController>(playerObject, missing);
            Require<GrabController>(playerObject, missing);
            Require<PlayerQuery>(playerObject, missing);
            Require<PlayerKillReceiver>(playerObject, missing);
            Require<FallTracker>(playerObject, missing);

            var character = player.GetComponent<CharacterController>();
            if (character == null) missing.Add("CharacterController");

            var input = player.GetComponent<PlayerInputReader>();
            if (input != null)
            {
                var serializedInput = new SerializedObject(input);
                var actions = serializedInput.FindProperty("actions");
                if (actions == null || actions.objectReferenceValue == null) missing.Add("InputActionAsset");
            }

            if (missing.Count == 0)
                Debug.Log($"ShadowTan: {player.name} — обязательные компоненты на месте. " +
                          "Осталось проверить ссылки в инспекторе и слой Player.");
            else
                Debug.LogError($"ShadowTan: {player.name} — не хватает: {string.Join(", ", missing)}");
        }

        static void Require<T>(GameObject player, List<string> missing) where T : Component
        {
            if (player.GetComponent<T>() == null) missing.Add(typeof(T).Name);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
