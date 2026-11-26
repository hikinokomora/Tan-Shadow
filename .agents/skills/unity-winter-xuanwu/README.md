## v4.3 — Meshy shape-only fallback
**v4.3 — обязательное уточнение:** готовые легально бесплатные модели остаются первым выбором; если нужной **формы** нет в проекте, кеше и каталоге после проверки максимум двух кандидатов, использовать Meshy MCP для генерации отдельного 3D-меша по `references/MESHY_WORKFLOW.md`. Это единственное дополнение к правилам источников видимой скальной геометрии v4.1: разрешены готовые ассеты и прошедшие приёмку сгенерированные Meshy ассеты. Цвет не является причиной генерации. Никаких старых примитивных стен, Terrain-вертикалей или процедурных скальных границ. Генерации только в заранее утверждённом лимите кредитов; не покупать кредиты. При конфликте это уточнение заменяет прежние указания «только готовые бесплатные» и «при нехватке формы только blocker».
Установка: `references/MESHY_MCP_SETUP.md`.

# Обновление v4.1
Сначала прочитать `references/GRAYBOX_REBUILD_CONTRACT.md`: новая обязательная директива, пользовательская схема и антиреференс. QA16–QA18 дополняют исходные проверки.

# Unity Winter Xuanwu Skill v4

Portable skill for the existing Unity project. Copy this folder into `.agents/skills/unity-winter-xuanwu/` (or agent's skill location); don't copy the entire skill under Assets. In Unity only copy C# files from `scripts/unity-editor/` into `Assets/Editor/XuanwuTools/` after backup. Open `SKILL.md` first, then `scripts/README.md`. Original guide preserved under references; v4 rules override legacy. `assets/ASSET_ATLAS.html` opens in browser. JSON canonical; regenerate derivative outputs after editing. No third-party 3D files are included. Preview availability depends on provider/Internet; reference screenshots from Battlefield are linked, not copied. The local GDD JPEGs and original schematic diagrams are included.

Prompt: «Используй skill unity-winter-xuanwu v4. Выполни аудит текущей зимней сцены, сохрани исходный грейбокс как резерв, измерь и сохрани его маршрут; полностью замени скальные границы готовыми или одобренными Meshy 3D-ассетами по GRAYBOX_REBUILD_CONTRACT.md и создай окружение по GDD и Fjell 652. Сначала протестируй Editor Tools на копии, используй единый каталог, соблюдай бесплатность и лицензии, проверяй проходимость от третьего лица. Не реализовывай бой, погоду и анимации. Фиксируй результаты в build-state и QA».

Read `VALIDATION_REPORT.md`: no Unity access here; compile and Play Mode required. Never claim generated level before actual work. Existing project version, render pipeline and real dimensions are unknown.

Актуальная схема от 26.11.2026: `references/ROUTE_APPROVED.md` и `visual-references/gdd/ROUTE_APPROVED.png`. Она заменяет прежнюю планировку, но не остальные правила GDD.

## v4.3 — инструкция промптов Meshy
Перед любой будущей генерацией прочитай `references/MESHY_PROMPT_GUIDE.md` в Skill, выбери один шаблон `references/MESHY_PROMPTS.md` или `templates/meshy-prompt-library.json`, создай заполненную рабочую копию `templates/meshy-shape-request.json`. Не генерируй в рамках установки. Сохрани текущий бюджет и журналы, не перезаписывай их шаблонами.

## v4.4 — Optimization First
Активная версия v4.4. `references/OPTIMIZATION_FIRST.md` и `templates/optimization-budgets.json` обязательны и заменяют прежние числовые ориентиры. Переустановить по корневому INSTALL_FOR_AI.md, сохранить рабочие данные и MCP; после тестов сразу выполнять окружение на существующем Graybox, не ждать отдельной команды. Исторические разделы про «только установку» относятся к прежним версиям.


**v4.4:** установка не тратит кредиты; после проверки инструментов пользователь разрешил построение окружения в рабочей копии Graybox. См. INSTALL_FOR_AI.md, раздел 8.
