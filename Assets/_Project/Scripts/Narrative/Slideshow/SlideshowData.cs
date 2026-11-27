using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowTan.Narrative
{
    // Содержимое одного слайд-шоу: слайды и сцена, куда идти после него (ARCHITECTURE.md §19.2).
    [CreateAssetMenu(menuName = "ShadowTan/Narrative/Slideshow", fileName = "Slideshow_")]
    public sealed class SlideshowData : ScriptableObject
    {
        public string slideshowId;
        public List<Slide> slides = new List<Slide>();

        [Tooltip("Сцена после последнего слайда и после пропуска. Пока заглушка: следующая сцена по порядку.")]
        public string nextScene;
    }

    // Слайд — одна картинка и одна или несколько реплик. Реплики листаются, картинка остаётся.
    [Serializable]
    public sealed class Slide : ISerializationCallbackReceiver
    {
        public Sprite image;
        public List<SlideLine> lines = new List<SlideLine>();

        // Старый формат: одна реплика прямо в слайде. Переносится в lines при загрузке.
        [SerializeField, HideInInspector] private AudioClip voiceOver;
        [SerializeField, HideInInspector] private string captionRu;
        [SerializeField, HideInInspector] private string captionZh;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (lines == null) lines = new List<SlideLine>();
            // Сравнение ссылок, а не UnityEngine.Object.operator!=: при асинхронной загрузке сцены десериализация идёт
            // не в главном потоке, и оператор Unity там бросает исключение.
            bool hasLegacy = !ReferenceEquals(voiceOver, null) || !string.IsNullOrEmpty(captionRu) || !string.IsNullOrEmpty(captionZh);
            if (hasLegacy && lines.Count == 0)
                lines.Add(new SlideLine { voiceOver = voiceOver, captionRu = captionRu, captionZh = captionZh });
            voiceOver = null;
            captionRu = captionZh = null;
        }
    }

    [Serializable]
    public sealed class SlideLine
    {
        [Tooltip("Дорожка реплики. Пока она играет, листать нельзя.")]
        public AudioClip voiceOver;

        [TextArea] public string captionRu;
        [TextArea] public string captionZh;
    }
}
