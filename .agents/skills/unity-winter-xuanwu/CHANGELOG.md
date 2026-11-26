# v4.3 — Meshy prompt engineering
- Добавлены проверенное руководство, 14 англоязычных шаблонов, JSON-библиотека, shape brief и офлайн-тесты.
- Уточнены image/multi-image, preview-only, проверка формы и повторные попытки.
- Сохранены предыдущие материалы и бюджет.

## v4.2 — 26.11.2026
- Official Meshy MCP setup, shape-only fallback, async task journal, budget gate, FBX staging helper, QA19–QA21. Existing catalogue, GDD, approved map and C# unchanged. No live calls, no Unity tests.

## v4.1 — полная замена скальных границ
- Утверждённая схема заменена точным изображением пользователя; добавлен антиреференс отказа.
- Грейбокс готов как маршрут, а не художественная форма: новые границы исключительно из готовых ассетов.
- Обновлены активные инструкции, добавлены QA16–QA18 и шаблон замеров. База ассетов, GDD и исходники инструментов не изменены.

# Changes
## v4 · route update · 2026-11-26
- Latest user-approved six-zone plan added; continuous walkable transition 5→6. Older plans retained as historical references. C# and asset database unchanged; no Unity runtime validation performed.
## v4 · 2026-11-24
- Unified catalog: 150 external listing records, includes original 95, newer candidates and exclusions; JSON → CSV, MD, HTML. Price and AI flags separate; latest explicit NoAI overrides earlier labels. No records automatically approved.
- Short SKILL, GDD contract, art bible, level recipes, material guide, performance and QA, decision tree.
- 12 menu commands + common helper; Python catalog query and rebuild, local BaseColor correction, optional Shader Graph HLSL function.
- Template configs and QA, material lab creation command, screenshot capture command, far mesh generator. Local schematic visual references and user GDD references.
- Limitations: no Unity compile or scene test; links/metadata not all independently rechecked, not all previews downloaded, no copied copyrighted Fjell images. No dynamic weather/tracks/combat. No claims of measured optimization.

## v4.4 — Optimization First
- Бюджеты треугольников по 14 ролям, приоритет оптимизации, минимальный Meshy preview, оптимизированные промпты, отдельное согласование дорогой модели/превышения порога.
- Обязательная приёмка actual tris, LOD, material slots, collider, повторов, profiler; QA22–QA25.
- Безопасная переустановка существующего skill и немедленный переход к построению окружения после проверок на готовом Graybox; старые правила ожидания отменены для этой задачи. Никакого изменения оригинала, контроллера или бюджета Meshy без разрешения.
