# Build state
- Phase: audit / blockout / slice / expand / QA
- Unity version / pipeline / scene path / backup path:
- Existing repo status and changes to preserve:
- Actual player radius, height, slope, step, roll length, camera:
- Anchors (world/local) / protected areas / route config:
- Selected geology and approved asset IDs / prefab GUIDs:
- Rejected IDs + reason:
- Completed zones and modified files:
- Last screenshots (camera pose / FOV / exposure):
- Baseline / after metrics:
- QA passed / failed / not run:
- Blocker / next single action:

- Route baseline and per-zone length comparison: route-baseline.md
- Old cliff objects/colliders removed or disabled in work copy; shared Terrain floor retained:
- New ready-made rock mesh sources/GUIDs and clay evidence:
- QA16–QA18 evidence:


- Meshy MCP availability / balance check status (no keys):
- Approved Meshy credit cap / confirmed spent / reserved / unknown submissions:
- Missing shape slots / two checked candidates / task IDs / next poll:
- Generated mesh source hashes / prefab GUIDs / imported QA evidence:
- Independent Unity work while Meshy pending:
- QA19–QA21 evidence:

- v4.4 optimization: actual per-role budgets / asset-optimization-report.json / approved exceptions:
- QA22–QA25 / benchmark evidence / unresolved performance blockers:
- Existing Graybox route retained; work scene / replacement progress by zone:

## 26 ноября 2026 · v4.4 и участок 1–25 м

- Архив проверен и установлен в `.agents/skills/unity-winter-xuanwu`. Предыдущая версия и исходный Git-статус сохранены в [pre-v4.4](Backups/pre-v4.4). Рабочие файлы и настройки MCP сохранены. Инструкция и материалы в `Docs/Xuanwu`, [утверждённая схема](../Source/ROUTE_APPROVED.png) определяет шесть зон и пологий переход 5→6.
- 13 исходников в `Assets/Editor/XuanwuTools`. Unity 6000.3.8f1 скомпилировала пакет, 12 меню зарегистрированы. Scene audit и Performance inventory запустились и создали отчёты. Полный изолированный smoke suite имеет статус NOT RUN. Исправлены ссылки на INSTALL_FOR_AI в двух Python-тестах, 15/15 прошли.
- Unity MCP проверен операциями чтения, сохранения и Play Mode. Meshy MCP проверен чтением баланса, 1300 на момент проверки. Генераций и расходов 0. Проверка скачивания нового FBX через Meshy не запускалась.
- [Рабочая копия v4.4](../../../Assets/_Game/Scenes/Xuanwu_Work_Assets_v44.unity) отделена от `Xuanwu_Work_Assets` и исходного Graybox. Четыре готовых Rock Face 01 заменены локально упрощённым префабом. На отрезке 1–25 м из ближнего борта вырезаны крутые боковые грани в рендере и коллайдере. Пол по центру и старый маршрут сохранены. Дальние и переходные скальные границы ещё требуют полной замены.
- 291 статическая проба, 0 отсутствующих участков пола, 0 препятствий, максимальный уклон 20,54°. Капсула прошла от тоннеля до 25 м и обратно в Play Mode, все 290 проверок на земле. Игровой ввод, кувырок и динамическая камера NOT RUN. [Кадр](../../../XuanwuReports/V44/reference-segment.png).
- [Отчёт о производительности](../../../XuanwuReports/V44/performance.md). Полная приёмка QA01–QA25 остаётся открытой. До измерения кадра и полной замены бортов на эталоне решения не распространены на остальные зоны.

## 26 ноября 2026 · Первый участок заново из Winter_new

Создана отдельная сцена Assets/_Game/Scenes/Xuanwu_Work_Fresh44.unity и проверена побайтовая копия исходника. Эталон от бетонного выхода длиной 24 м. Рендер и коллизии исходного Terrain отключены в этой рабочей копии. Новые скалы собраны из шести Rock Face 01 и четырёх Boulder 01, LOD упрощены из библиотечных FBX. Проверка пола 285/285, штатная капсула в Play Mode 276/276 шагов с опорой. Ноль генераций Meshy. Сравнение CPU/GPU baseline/after и ручные проверки камеры и переката открыты. Участок заканчивается на отметке 24 м. Отчёт и контрольные кадры в XuanwuReports/Fresh44.

## 26 ноября 2026 · Ущелье и подход к лапе в Fresh44

Отметка 24 м состыкована с участком до камеры лапы, длина продолжения по поверхности 341.78 м. Проверка трёх линий 1758/1758 без пробелов или препятствий. Штатный CharacterController в Play Mode прошёл туда и обратно 1450/1450 шагов с опорой. Модель лапы сохранена, на подходе ось локально уточнена по свободному коридору. Новое окружение 184179 треугольников по инвентарю, Meshy 0 кредитов. Время кадра, ввод, перекат и камера остаются открытыми. Отчёт XuanwuReports/Fresh44/ravine-report.md.

## 26 ноября 2026 · Высотный профиль скал по Winter_new

В Xuanwu_Work_Fresh44.unity сверены поперечные сечения исходного Terrain слева и справа. Подогнана верхняя отметка 45 существующих скальных модулей. Сохранены 18 крутых переходов по выборке перепадов, превышающих 8 м на 5 м. Срезы высот измерены медианой трёх близких сечений. На дальних гребнях использованы 22 объёмных Boulder 01 (847 / 415 треугольников, материал общий, коллизий и теней нет), заглублённые со стороны склона. Высота верхней части ограничена на выбросах и вблизи соседних зон. Максимальное отклонение верха ближайших модулей от измеренных отметок до / после: 30.17 / 5.04 м. Профили и контрольные кадры в XuanwuReports/Fresh44.

Стык, пол и лапа на месте. 285 + 1758 проверенных точек без препятствий, 170 вертикальных проб над центральной линией без перекрытия новыми скалами. Повторный проход штатным CharacterController: 1450/1450 шагов с опорой, 0 боковых столкновений. Meshy 0 кредитов. Сравнительный GPU-профиль не измерен. Резервная копия рабочей сцены перед проходом: Backups/Xuanwu_Work_Fresh44-before-cliff-heights.unity.
