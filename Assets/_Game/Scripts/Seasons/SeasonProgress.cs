using System;
using UnityEngine;

namespace TanShadow.Seasons
{
    public enum Season { Spring, Summer, Autumn, Winter }

    // Победы над колоссами. Defeated — сигнал «колосс убит» (по нему VictoryToSpace уводит в космос).
    // Последняя победа хранится в PlayerPrefs, пока нет полноценного сохранения (ARCHITECTURE §23.6).
    public static class SeasonProgress
    {
        const string Key = "TanShadow.LastDefeatedSeason";

        // null — ещё никого не победили.
        public static Season? LastDefeated
        {
            get
            {
                int v = PlayerPrefs.GetInt(Key, -1);
                return v < 0 || v > 3 ? (Season?)null : (Season)v;
            }
        }

        public static event Action<Season> Defeated;

        public static void MarkDefeated(Season season)
        {
            PlayerPrefs.SetInt(Key, (int)season);
            PlayerPrefs.Save();
            Defeated?.Invoke(season);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        // Сторона света колосса (ARCHITECTURE §1.3): весна — восток, лето — юг, осень — запад, зима — север.
        // В сценах +Z — север, +X — восток.
        public static Vector3 Direction(Season season)
        {
            switch (season)
            {
                case Season.Spring: return Vector3.right;
                case Season.Summer: return Vector3.back;
                case Season.Autumn: return Vector3.left;
                default: return Vector3.forward;
            }
        }
    }
}
