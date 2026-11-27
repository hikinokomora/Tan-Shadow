using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShadowTan.Gameplay.Autumn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace ShadowTan.EditorTools
{
    public static class AutumnAudioSetup
    {
        const string AudioPath = "Assets/_Project/Audio/Environment/Autumn";
        const string MixerPath = AudioPath + "/Wind/AutumnWind.mixer";
        const string BambooPath = AudioPath + "/Wind/BambooNoise.mp3";

        static readonly (string folder, string name)[] Files =
        {
            ("Footsteps", "StepOnSand"), ("Footsteps", "StepOnRock"),
            ("Footsteps", "SomersaultOnGround"), ("Footsteps", "SomersaultOnRock"),
            ("Tiger", "TigersRustle"), ("Tiger", "TigersJumpIntoBushes"),
            ("Tiger", "TigersGrowingEscalating"), ("Tiger", "TIgersHeadCrushes"),
            ("Tiger", "TigersDashIntoBushes"), ("Tiger", "TigersGrowing"),
            ("Tiger", "TigersDash"), ("Tiger", "TigersStepAfterCrushes"),
            ("Monument", "MonumentCrushes"), ("Monument", "MonumentPartickes"),
            ("Forest", "CuckooBirdSound")
        };

        [MenuItem("ShadowTan/Setup/Art/Setup Autumn Audio")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                (scene.path != AutumnVfxSetup.ArtScene && scene.path != AutumnVfxSetup.BattleScene))
                throw new InvalidOperationException("Open an autumn scene in Edit Mode.");

            var clips = ImportClips();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            var ambience = mixer.FindMatchingGroups("Ambience").Single(g => g.name == "Ambience");
            var effects = mixer.FindMatchingGroups("Master").Single(g => g.name == "Master");
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            if (scene.path == AutumnVfxSetup.BattleScene) BindBattle(all, clips, ambience, effects);
            else BindArt(all, clips, ambience, effects);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Autumn audio configured in {scene.name}: {clips.Count(p => p.Value)} source clips.");
        }

        static Dictionary<string, AudioClip> ImportClips()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Telegram Desktop");
            foreach (var (folder, name) in Files)
            {
                string directory = $"{AudioPath}/{folder}";
                if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder(AudioPath, folder);
                var destination = $"{directory}/{name}.mp3";
                var source = Path.Combine(downloads, name + ".mp3");
                if (!File.Exists(destination) && File.Exists(source)) File.Copy(source, destination);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var clips = new Dictionary<string, AudioClip>
            {
                ["BambooNoise"] = AssetDatabase.LoadAssetAtPath<AudioClip>(BambooPath)
            };
            foreach (var (folder, name) in Files)
            {
                string path = $"{AudioPath}/{folder}/{name}.mp3";
                if (AssetImporter.GetAtPath(path) is AudioImporter importer
                    && name != "TigersGrowingEscalating" && !importer.forceToMono)
                {
                    importer.forceToMono = true;
                    importer.SaveAndReimport();
                }
                clips[name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clips[name]) Debug.LogWarning($"Autumn SFX file missing: {name}.mp3");
            }
            return clips;
        }

        static void BindArt(Transform[] all, Dictionary<string, AudioClip> clips, AudioMixerGroup ambience, AudioMixerGroup effects)
        {
            var surface = all.Select(t => t.GetComponent<AutumnSurfaceFx>()).Single(c => c);
            var wind = all.Select(t => t.GetComponent<AutumnWindAudio>()).Single(c => c);
            var director = AutumnVfxSetup.Component<AutumnAudioDirector>(wind.transform);
            var bank = AssetDatabase.LoadAssetAtPath<AutumnAudioBank>(AudioPath + "/AutumnAudioBank.asset");
            if (!bank)
            {
                bank = ScriptableObject.CreateInstance<AutumnAudioBank>();
                AssetDatabase.CreateAsset(bank, AudioPath + "/AutumnAudioBank.asset");
            }
            AutumnVfxSetup.Set(bank, so =>
            {
                AutumnVfxSetup.Reference(so, "bambooAmbience", clips["BambooNoise"]);
                AutumnVfxSetup.Reference(so, "forestOneShot", clips["CuckooBirdSound"]);
                var mappings = so.FindProperty("surfaceClips");
                mappings.arraySize = 2;
                MapSurface(mappings.GetArrayElementAtIndex(0), "Surface_Sand", clips["StepOnSand"], clips["SomersaultOnGround"]);
                MapSurface(mappings.GetArrayElementAtIndex(1), "Surface_Stone", clips["StepOnRock"], clips["SomersaultOnRock"]);
            });
            var windSerialized = new SerializedObject(wind);
            var loop = windSerialized.FindProperty("ambient").objectReferenceValue as AudioSource;
            if (loop)
            {
                Undo.RecordObject(loop, "Autumn bamboo bed");
                loop.clip = bank.BambooAmbience;
                loop.outputAudioMixerGroup = ambience;
            }
            AutumnVfxSetup.Set(wind, so =>
            {
                AutumnVfxSetup.Reference(so, "output", ambience);
                var shots = so.FindProperty("bambooClips");
                if (bank.BambooAmbience != null)
                {
                    shots.arraySize = 1;
                    shots.GetArrayElementAtIndex(0).objectReferenceValue = bank.BambooAmbience;
                }
                so.FindProperty("ambientVolume").floatValue = .38f;
                so.FindProperty("oneShotVolume").floatValue = .54f;
                so.FindProperty("gustInterval").floatValue = 9f;
                so.FindProperty("lullInterval").floatValue = 16f;
            });
            AutumnVfxSetup.Set(director, so =>
            {
                AutumnVfxSetup.Reference(so, "bank", bank);
                AutumnVfxSetup.Reference(so, "windAudio", wind);
                AutumnVfxSetup.Reference(so, "ground", surface);
                AutumnVfxSetup.Reference(so, "surfaceMix", effects);
            });

            foreach (var name in new[] { "WindAtThreshold", "LooseStonesOnSlope" })
            {
                var source = all.Single(t => t.name == name).GetComponent<AudioSource>();
                if (!source || !source.clip || !AssetDatabase.GetAssetPath(source.clip).Contains("/Placeholder/")) continue;
                Undo.RecordObject(source, "Replace autumn entrance placeholder");
                source.clip = name == "WindAtThreshold" ? bank.BambooAmbience : null;
                source.outputAudioMixerGroup = ambience;
            }
        }

        static void MapSurface(SerializedProperty property, string name, AudioClip step, AudioClip roll)
        {
            property.FindPropertyRelative("surface").objectReferenceValue = AutumnVfxSetup.FindSurface(name);
            property.FindPropertyRelative("footstep").objectReferenceValue = step;
            property.FindPropertyRelative("roll").objectReferenceValue = roll;
        }

        static void BindBattle(Transform[] all, Dictionary<string, AudioClip> clips, AudioMixerGroup ambience, AudioMixerGroup effects)
        {
            var bank = AssetDatabase.LoadMainAssetAtPath("Assets/_Game/Data/Autumn/BaihuSoundBank.asset");
            var sounds = all.SelectMany(t => t.GetComponents<MonoBehaviour>())
                .Single(c => c && c.GetType().FullName == "TanShadow.Autumn.BaihuSounds");
            AutumnVfxSetup.Set(bank, so =>
            {
                Slot(so, "forestAmbience", clips["BambooNoise"], .38f, 0f);
                Slot(so, "forestOneShot", clips["CuckooBirdSound"], .34f);
                Slot(so, "rustle", clips["TigersRustle"], .62f);
                Slot(so, "fleetingRustle", clips["TigersJumpIntoBushes"], .52f);
                Slot(so, "aggroNoiseLoop", clips["TigersGrowingEscalating"], .42f, 0f);
                Slot(so, "pounce", clips["TigersDashIntoBushes"], .78f);
                Slot(so, "tigerLeapAway", clips["TigersDash"], .50f);
                Slot(so, "tigerRoar", clips["TigersGrowing"], .65f);
                Slot(so, "tigerGetUp", clips["TigersStepAfterCrushes"], .68f);
                Slot(so, "monumentImpact", clips["TIgersHeadCrushes"], .70f);
                Slot(so, "monumentCrack", clips["MonumentCrushes"], .48f);
                Slot(so, "monumentDebris", clips["MonumentPartickes"], .37f);
            });
            AutumnVfxSetup.Set(sounds, so =>
            {
                AutumnVfxSetup.Reference(so, "bank", bank);
                AutumnVfxSetup.Reference(so, "ambienceMix", ambience);
                AutumnVfxSetup.Reference(so, "effectsMix", effects);
            });
        }

        static void Slot(SerializedObject bank, string name, AudioClip clip, float volume, float spatial = 1f)
        {
            if (!clip) return;
            var slot = bank.FindProperty(name);
            var array = slot.FindPropertyRelative("clips");
            array.arraySize = 1;
            array.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            slot.FindPropertyRelative("volume").floatValue = volume;
            slot.FindPropertyRelative("spatial").floatValue = spatial;
            slot.FindPropertyRelative("pitchJitter").floatValue = spatial == 0f ? 0f : .035f;
        }
    }
}
