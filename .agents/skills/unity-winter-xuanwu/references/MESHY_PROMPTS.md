# Meshy: библиотека промптов Сюаньу v4.4

Канонические правила составления запросов: [MESHY_PROMPT_GUIDE.md](MESHY_PROMPT_GUIDE.md). Машиночитаемые исходники — `../templates/meshy-prompt-library.json`. Все примеры авторские, адаптированы под проект; не испытаны реальными генерациями. Отправлять только после проверки формы и бюджета. Английский — соглашение проекта, не требование Meshy. Числа зон не отправлять в промпте. Пропорции уточнять по измеренному слоту; масштаб устанавливать в Unity. Не объединять несколько строк в один запрос.

## wall — Вертикальная стена ущелья
Зоны: 2, 3, 5.

```text
Single alpine bedrock cliff wall module, tall broad angular fractured planes, thick solid mass, irregular layered ledges and asymmetric outline, natural realistic geology, game-ready mesh, no ground plane or detached rocks. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Высота больше глубины; широкие грани; не колонна и не тонкая пластина.

## wall_long — Протяжённая стена
Зоны: 2, 4.

```text
Single elongated alpine cliff wall module, much wider than deep, continuous fractured face with broad staggered rock planes, irregular top ridge, thick rock volume, realistic game-ready mesh, no ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Протяжённая грань без одинаковых повторов; плотный объём.

## corner_in — Внутренний угол
Зоны: 2, 3, 5.

```text
Single concave alpine bedrock corner, two thick fractured rock faces meeting at an inward angle, irregular stepped natural strata, open inside corner, realistic game-ready mesh, no ground or detached pieces. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Вогнутый угол оставляет свободный внутренний проход.

## corner_out — Внешний угол
Зоны: 2, 3, 4.

```text
Single convex alpine cliff buttress, two broad angular rock faces meeting at a protruding irregular ridge, thick asymmetrical mass, layered fractures, realistic game-ready mesh, no ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Выпуклое ребро; не симметричная призма.

## overhang — Навес над лапой/головой
Зоны: 3, 5.

```text
Single natural cantilevered alpine rock overhang, thick attached root, broad projecting slab with fractured underside, open space beneath the projection, realistic angular bedrock, game-ready mesh, no support columns or ground. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Толстый корень, широкая нижняя поверхность, открытый просвет; не мост.

## overhang_side — Боковой навес
Зоны: 3, 5.

```text
Single asymmetric projecting alpine cliff block, thick root on one side and short broad overhanging lip, irregular fractured underside, layered angular bedrock, game-ready mesh, no detached pieces or ground. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Короткий боковой выступ, не перекрывает весь проход.

## edge — Кромка обрыва
Зоны: 4.

```text
Single alpine cliff lip module, irregular near-horizontal upper ledge and steep fractured outer drop face, thick bedrock below the lip, continuous asymmetric edge, realistic game-ready mesh, no stairs or ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Резкая внешняя грань; верх можно состыковать со снегом, без ступеней.

## edge_turn — Поворот кромки обрыва
Зоны: 4.

```text
Single curved alpine cliff corner lip, continuous irregular convex upper edge, thick steep fractured outer rock face, broad angular strata, realistic game-ready mesh, no ground plane or detached rocks. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Поворот кромки без ложного ответвления и провалов в полу.

## seam — Соединительный клин
Зоны: 2, 3, 4, 5.

```text
Single compact wedge-shaped alpine bedrock outcrop, broad irregular angular faces, thick tapered silhouette, layered fractures matching a cliff wall, realistic game-ready mesh, no gravel or detached pieces. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Клин закрывает стык двух новых ассетов; не камень на тропе.

## buttress — Массивный контрфорс
Зоны: 2, 3, 5.

```text
Single broad alpine rock buttress, thick lower mass tapering into a fractured upper ridge, oblique angular rock planes, asymmetric natural strata, realistic game-ready mesh, no ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Наклонные природные грани; не искусственная опора.

## ledge — Скальный уступ под снегом
Зоны: 2, 4.

```text
Single thick alpine bedrock shelf, broad gently sloping upper face and angular fractured side faces, irregular natural layered silhouette, realistic game-ready mesh, no stairs or ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Уступ формирует боковую границу или основание; не обязательная ступень на маршруте.

## ridge — Дальний горный гребень
Зоны: 4.

```text
Single alpine mountain ridge massif, several connected uneven pointed peaks above broad steep fractured slopes, asymmetric coherent bedrock silhouette, realistic game-ready mesh, no terrain tile or surrounding scenery. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Читаемый дальний силуэт, единый массив; не целый ландшафт.

## peak — Отдельный дальний пик
Зоны: 4.

```text
Single angular alpine mountain peak, broad tapered solid base and irregular fractured summit, steep layered rock planes, realistic game-ready mesh, no terrain tile or surrounding scene. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Один пик, без тонких игл и островов.

## snow_drift — Снежная насыпь (резерв)
Зоны: 2, 3, 4, 5.

```text
Single low elongated wind-sculpted snowbank mesh, smooth convex crest and gently tapering sides, broad continuous base, subtle asymmetry, realistic game-ready geometry, no rocks or ground plane. Low-poly game environment mesh, broad readable planes, simplified fractures, no microgeometry, no detached fragments.
```

Приёмка: Плавный объём для стыка; не заменяет основной Terrain.

