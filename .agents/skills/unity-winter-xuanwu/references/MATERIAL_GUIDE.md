# Материалы и сопоставление

| Эталон | Назначение | Стартовые параметры (не измеренная физика) |
|---|---|---|
| Rock_Dark | основной угловатый камень | matte Lit, metallic 0, roughness ~0.75–0.9 |
| Rock_Cold | холодные выступы | та же фактура/масштаб, мягкое охлаждение |
| Rock_Neutral | вторичная скала | одинаковый свет и зернистость, без ярких пятен |
| Snow_Clean | основной Terrain и shell | metallic 0, roughness ~0.7–0.9, мягкий normal |
| Snow_Rough | края и стыки | чуть сильнее normal, без следов/грязи |
| Ice_Subtle | редкие вставки | матовый сине-серый; никакой стеклянной стены |

K1 нейтральный: умеренно снизить saturation и тепло. K2 коричневый/бежевый: обязательно убрать жёлто-рыжий, затемнить midtones и охладить, сохранить контраст и cracks. K3: снять зелень маской, растения удалить из mesh; если мох читается как фактура, отказаться. K4: согласовать белизну снега, убрать грязь; следы не закрашивать простым tint — сменить текстуру. K5: приглушить бирюзу. `recolor.py` даёт начальный batch BaseColor PNG; K3 не удаляет геометрию, отдельная маска/ручная коррекция обязательна при сильном налёте. Не менять Normal/AO/Height/Roughness этим скриптом. Не использовать tint вместо K2. Не менять чужие оригиналы.

MaterialLab: выдели подготовленные .mat в Project, Tools/Xuanwu/12, сохрани отдельно и изолируй сцену. Добавь реальные 3D-скалы к сферам и снеговой клин. Одинаковые свет, exposure, post, camera. Проверить texture scale, оттенок, roughness, normal (OpenGL vs DirectX), физическую ширину трещин. Для URP/Built-in smoothness=1−roughness; при metallic packed channel использовать корректный packing; HDRP mask map R metallic/G AO/B detail/A smoothness. BaseColor sRGB, data maps linear, normal Texture Type Normal. Mips включены. Shared materials, не плодить копии на каждую скалу. Не заменять уникальные UV-фототекстуры без проверки.

Опциональная Shader Graph функция в `scripts/unity-editor-optional/SnowBlend.hlsl` (ручная интеграция в выбранный Lit graph): world normal Y, порог, noise, painted mask. Держать 2 материала/слоя на старте. Сравнить стоимость на GPU; не включать по умолчанию. Для подвижного панциря использовать local-space маску, без world-space swimming.
