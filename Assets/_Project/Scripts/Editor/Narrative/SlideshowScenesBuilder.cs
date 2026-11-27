using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShadowTan.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShadowTan.EditorTools.Narrative
{
    // Собирает префаб экрана слайд-шоу, ассеты-заглушки и пять сцен слайд-шоу.
    // Повторный запуск пересобирает префаб и сцены, но не трогает уже заполненные ассеты данных.
    public static class SlideshowScenesBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Narrative/Slideshows";
        private const string SlidesArtDir = "Assets/_Project/Art/Slides/_Placeholder";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/SlideshowScreen.prefab";
        private const string ScenesDir = "Assets/_Project/Scenes/Slideshows";
        private const string IconsDir = "Assets/_Project/UI/Sprites/Input";
        private const string InputPath = "Assets/_Project/Settings/Input/ShadowTanInput.inputactions";

        private struct Def
        {
            public string Id, SceneName, NextScene, Ru, Zh;
            public Color Tint;
        }

        private static readonly Def[] Defs =
        {
            new Def { Id = "qinglong", SceneName = "Slideshow_Qinglong", NextScene = "Spring_Qinglong",      Ru = "Весна", Zh = "春天", Tint = new Color(0.25f, 0.55f, 0.35f) },
            new Def { Id = "zhuque",   SceneName = "Slideshow_Zhuque",   NextScene = "Summer_Zhuque_01_Dry", Ru = "Лето",  Zh = "夏天", Tint = new Color(0.70f, 0.25f, 0.15f) },
            new Def { Id = "baihu",    SceneName = "Slideshow_Baihu",    NextScene = "Autumn_Baihu",         Ru = "Осень", Zh = "秋天", Tint = new Color(0.70f, 0.50f, 0.20f) },
            new Def { Id = "xuanwu",   SceneName = "Slideshow_Xuanwu",   NextScene = "Winter_Xuanwu",        Ru = "Зима",  Zh = "冬天", Tint = new Color(0.25f, 0.35f, 0.60f) },
            new Def { Id = "finale",   SceneName = "Slideshow_Finale",   NextScene = "",                     Ru = "Финал", Zh = "终章", Tint = new Color(0.45f, 0.45f, 0.45f) },
        };

        private const int PlaceholderSlides = 3;
        private static readonly string[] ZhNumbers = { "一", "二", "三", "四", "五" };

        [MenuItem("ShadowTan/Narrative/Build Slideshow Scenes")]
        public static void Build()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty)
            {
                Debug.LogError("[Slideshow] В открытой сцене есть несохранённые изменения. Сохраните её и запустите сборку снова.");
                return;
            }
            string returnTo = active.path;

            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SlidesArtDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            Directory.CreateDirectory(ScenesDir);

            var config = LoadOrCreate<SlideshowConfig>($"{DataDir}/SlideshowConfig.asset");
            var loadingConfig = LoadOrCreate<LoadingCoverConfig>($"{DataDir}/LoadingCoverConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            var prefab = BuildPrefab(config, loadingConfig, input);

            var scenePaths = new List<string>();
            foreach (var def in Defs)
            {
                var data = BuildData(def);
                scenePaths.Add(BuildScene(def, prefab, data));
            }

            AddToBuildSettings(scenePaths);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(returnTo)) EditorSceneManager.OpenScene(returnTo);
            Debug.Log("[Slideshow] Сцены слайд-шоу собраны.");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static SlideshowData BuildData(Def def)
        {
            string path = $"{DataDir}/Slideshow_{def.Id}.asset";
            var data = LoadOrCreate<SlideshowData>(path);
            if (data.slides.Count > 0)
            {
                // Дозаполнить пустые картинки заглушками, остальное не трогать.
                for (int i = 0; i < data.slides.Count; i++)
                    if (data.slides[i].image == null) data.slides[i].image = PlaceholderSprite(def, i);
                // Старые заглушки (одна реплика на слайд) дополнить примером второй реплики.
                if (data.slides.Count > 1 && data.slides[1].lines.Count == 1
                    && (data.slides[1].lines[0].captionRu ?? "").Contains("текст-заглушка"))
                    data.slides[1].lines.Add(PlaceholderSecondLine(def));
                EditorUtility.SetDirty(data);
                return data;
            }

            data.slideshowId = def.Id;
            data.nextScene = def.NextScene;
            for (int i = 0; i < PlaceholderSlides; i++)
            {
                var slide = new Slide { image = PlaceholderSprite(def, i) };
                slide.lines.Add(new SlideLine
                {
                    captionRu = $"{def.Ru}. Слайд {i + 1} — текст-заглушка.",
                    captionZh = $"{def.Zh}。第{ZhNumbers[i]}张幻灯片——占位文字。",
                });
                // На втором слайде две реплики — пример смены текста без смены картинки.
                if (i == 1) slide.lines.Add(PlaceholderSecondLine(def));
                data.slides.Add(slide);
            }
            EditorUtility.SetDirty(data);
            return data;
        }

        private static SlideLine PlaceholderSecondLine(Def def) => new SlideLine
        {
            captionRu = $"{def.Ru}. Слайд 2, вторая реплика — картинка та же.",
            captionZh = $"{def.Zh}。第二张幻灯片，第二句——图片不变。",
        };

        private static Sprite PlaceholderSprite(Def def, int slideIndex)
        {
            string path = $"{SlidesArtDir}/S_{def.Id}_{slideIndex:00}.png";
            if (!File.Exists(path))
            {
                const int w = 960, h = 540;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                var px = new Color[w * h];
                var top = def.Tint * (0.6f + 0.2f * slideIndex);
                var bottom = def.Tint * 0.25f;
                for (int y = 0; y < h; y++)
                {
                    var c = Color.Lerp(bottom, top, y / (float)h);
                    c.a = 1f;
                    for (int x = 0; x < w; x++) px[y * w + x] = c;
                }
                tex.SetPixels(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[Slideshow] Не удалось загрузить спрайт-заглушку {path}");
            return sprite;
        }

        private static GameObject BuildPrefab(SlideshowConfig config, LoadingCoverConfig loadingConfig, InputActionAsset input)
        {
            var root = new GameObject("SlideshowScreen", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.AddComponent<GraphicRaycaster>(); // клики ЛКМ по кнопкам A/D
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;

            var bg = NewImage("Background", root.transform);
            Stretch(bg.rectTransform, Vector2.zero, Vector2.one);
            bg.color = Color.black;

            var slide = new GameObject("Slide", typeof(RectTransform));
            slide.transform.SetParent(root.transform, false);
            Stretch((RectTransform)slide.transform, Vector2.zero, Vector2.one);

            // Область под картинку: ниже подсказки пропуска и выше подписей.
            var area = new GameObject("ImageArea", typeof(RectTransform));
            area.transform.SetParent(slide.transform, false);
            Stretch((RectTransform)area.transform, new Vector2(0.2f, 0.27f), new Vector2(0.8f, 0.86f));

            var image = NewImage("Image", area.transform);
            var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            var edgeFade = NewImage("EdgeFade", image.transform);
            Stretch(edgeFade.rectTransform, Vector2.zero, Vector2.one);

            // Русская строка над китайской, обе по центру ниже картинки.
            var ru = NewText("CaptionRu", slide.transform, 46, TextAnchor.MiddleCenter);
            Stretch(ru.rectTransform, new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.21f));
            var zh = NewText("CaptionZh", slide.transform, 34, TextAnchor.MiddleCenter);
            Stretch(zh.rectTransform, new Vector2(0.05f, 0.07f), new Vector2(0.95f, 0.13f));

            // Чёрный слой поверх слайда: им гасится слайд, а подсказки остаются выше.
            var fadeCover = NewImage("FadeCover", root.transform);
            Stretch(fadeCover.rectTransform, Vector2.zero, Vector2.one);
            fadeCover.color = Color.black;

            var skip = new GameObject("SkipHold", typeof(RectTransform));
            skip.transform.SetParent(root.transform, false);
            var skipRt = (RectTransform)skip.transform;
            skipRt.anchorMin = skipRt.anchorMax = skipRt.pivot = new Vector2(1f, 1f);
            skipRt.anchoredPosition = new Vector2(-48f, -40f);
            skipRt.sizeDelta = new Vector2(720f, 90f);

            var hintRu = NewText("HintRu", skip.transform, 28, TextAnchor.LowerRight);
            Rect(hintRu.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-86f, 0f));
            var hintZh = NewText("HintZh", skip.transform, 22, TextAnchor.UpperRight);
            Rect(hintZh.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(-86f, -4f));

            var ringBg = NewImage("RingBackground", skip.transform);
            var ringGlow = NewImage("RingGlow", skip.transform);
            var ringFill = NewImage("RingFill", skip.transform);
            foreach (var ring in new[] { ringBg, ringGlow, ringFill })
            {
                var rt = ring.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-32f, 0f);
                rt.sizeDelta = new Vector2(64f, 64f);
            }
            // Размер подобран так, чтобы середина ореола совпала с серединой кольца.
            ringGlow.rectTransform.sizeDelta = new Vector2(92f, 92f);
            foreach (var ring in new[] { ringGlow, ringFill })
            {
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillClockwise = true;
            }

            var indicator = skip.AddComponent<SkipHoldIndicator>();
            Assign(indicator, ("hintRu", hintRu), ("hintZh", hintZh), ("ringBackground", ringBg), ("ringGlow", ringGlow), ("ringFill", ringFill));

            // Подсказки листания внизу слева: стрелка над клавишей.
            var nav = new GameObject("NavHints", typeof(RectTransform));
            nav.transform.SetParent(root.transform, false);
            var navRt = (RectTransform)nav.transform;
            navRt.anchorMin = navRt.anchorMax = navRt.pivot = Vector2.zero;
            navRt.anchoredPosition = new Vector2(48f, 40f);
            navRt.sizeDelta = new Vector2(200f, 150f);
            var prevHint = BuildNavHint(nav.transform, "Prev", "A", 0f, mirrorArrow: false);
            var nextHint = BuildNavHint(nav.transform, "Next", "D", 100f, mirrorArrow: true);

            // Подсказка последнего слайда: внизу справа, напротив A/D.
            var cont = new GameObject("ContinueHint", typeof(RectTransform), typeof(CanvasGroup));
            cont.transform.SetParent(root.transform, false);
            var contRt = (RectTransform)cont.transform;
            contRt.anchorMin = contRt.anchorMax = contRt.pivot = new Vector2(1f, 0f);
            contRt.anchoredPosition = new Vector2(-48f, 56f);
            contRt.sizeDelta = new Vector2(760f, 80f);
            var contRu = NewText("HintRu", cont.transform, 28, TextAnchor.LowerRight);
            Rect(contRu.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var contZh = NewText("HintZh", cont.transform, 22, TextAnchor.UpperRight);
            Rect(contZh.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, -4f));
            var continueHint = cont.AddComponent<ContinueHint>();
            Assign(continueHint, ("hintRu", contRu), ("hintZh", contZh));

            var player = root.AddComponent<SlideshowPlayer>();
            Assign(player,
                ("config", config), ("loadingConfig", loadingConfig), ("input", input),
                ("fadeCover", fadeCover), ("slideImage", image), ("slideFitter", fitter), ("slideEdgeFade", edgeFade),
                ("captionRu", ru), ("captionZh", zh),
                ("skipIndicator", indicator), ("voiceSource", audio),
                ("prevHint", prevHint), ("nextHint", nextHint), ("continueHint", continueHint));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static NavHint BuildNavHint(Transform parent, string name, string key, float x, bool mirrorArrow)
        {
            var go = new GameObject(name + "Hint", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(96f, 150f);

            // Иконки квадратные с большим прозрачным полем, поэтому рамки крупнее видимой клавиши.
            Image Icon(string id, string state, float y, float size, bool mirror)
            {
                var img = NewImage($"{id}_{state}", go.transform);
                img.sprite = IconSprite($"{IconsDir}/UI_{id}_{state}.png");
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0f);
                irt.anchoredPosition = new Vector2(0f, y);
                irt.sizeDelta = new Vector2(size, size);
                if (mirror) irt.localScale = new Vector3(-1f, 1f, 1f);
                return img;
            }

            const float keyY = 48f, keySize = 130f, arrowY = 110f, arrowSize = 110f;
            // Невидимая зона клика по клавише со стрелкой: у самих иконок большие прозрачные поля, и они перекрываются.
            var hit = NewImage("ClickArea", go.transform);
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var hitRt = hit.rectTransform;
            hitRt.anchorMin = hitRt.anchorMax = new Vector2(0.5f, 0f);
            hitRt.pivot = new Vector2(0.5f, 0f);
            hitRt.anchoredPosition = new Vector2(0f, 4f);
            hitRt.sizeDelta = new Vector2(88f, 136f);

            Icon($"Key_{key}", "Inactive", keyY, keySize, false);
            var keyLit = Icon($"Key_{key}", "Active", keyY, keySize, false);
            Icon("Arrow_Left", "Inactive", arrowY, arrowSize, mirrorArrow);
            var arrowLit = Icon("Arrow_Left", "Active", arrowY, arrowSize, mirrorArrow);

            var hint = go.AddComponent<NavHint>();
            var so = new SerializedObject(hint);
            var arr = so.FindProperty("litImages");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = keyLit;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = arrowLit;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hint;
        }

        private static Sprite IconSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError($"[Slideshow] Нет иконки {path}");
                return null;
            }
            if (importer.textureType != TextureImporterType.Sprite || importer.maxTextureSize != 256)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static string BuildScene(Def def, GameObject prefab, SlideshowData data)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var screen = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Assign(screen.GetComponent<SlideshowPlayer>(), ("data", data));

            string path = $"{ScenesDir}/{def.SceneName}.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static void AddToBuildSettings(IEnumerable<string> paths)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var toAdd = new List<string>(paths);

            // Сцены колоссов — цели перехода-заглушки.
            foreach (var def in Defs.Where(d => !string.IsNullOrEmpty(d.NextScene)))
            {
                string p = AssetDatabase.FindAssets($"t:Scene {def.NextScene}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == def.NextScene);
                if (p != null) toAdd.Add(p);
            }

            foreach (var p in toAdd)
                if (scenes.All(s => s.path != p))
                    scenes.Add(new EditorBuildSettingsScene(p, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(string name, Transform parent, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.fontSize = size;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max) => Rect(rt, min, max, Vector2.zero, Vector2.zero);

        private static void Rect(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private static void Assign(Object target, params (string field, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
                so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
