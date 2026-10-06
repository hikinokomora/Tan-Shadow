using UnityEngine;

namespace TanShadow.UI
{
    // Шрифт с иероглифами из системы, чтобы не класть в репозиторий шрифт с чужой лицензией.
    // Финальные иероглифы (危, 化劲) заменим на каллиграфические спрайты.
    public static class CjkFont
    {
        static Font font;

        static readonly string[] Candidates =
        {
            "Microsoft YaHei", "Microsoft JhengHei", "SimHei", "SimSun", "KaiTi",
            "Noto Sans CJK SC", "Noto Serif CJK SC", "PingFang SC", "Hiragino Sans GB"
        };

        public static Font Get()
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont(Candidates, 64);
            return font;
        }
    }
}
