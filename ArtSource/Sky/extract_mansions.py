# Оцифровка карты 28 лунных стоянок (二十八宿) с картинки-референса: звёзды и линии созвездий.
# Картинка — вид неба снизу, север сверху, восток слева (青龙 слева, 白虎 справа, 朱雀 снизу, 玄武 сверху),
# в центре — полюс (北极) и четыре помощника (四辅).
# Звезда на картинке — цветное кольцо, линии соединяют кольца одного созвездия.
# Выход — JSON со звёздами (пиксельные координаты, группа) и рёбрами; его читает make_space_sky.py.
# Запуск: python extract_mansions.py <референс.png|webp> <выход.json> [отладочная картинка.png]
import json, sys
import numpy as np
import cv2

src, out = sys.argv[1], sys.argv[2]
debug = sys.argv[3] if len(sys.argv) > 3 else None

im = cv2.imread(src)
if im is None: sys.exit("не читается " + src)
H, W = im.shape[:2]
hsv = cv2.cvtColor(im, cv2.COLOR_BGR2HSV)
hue, sat, val = hsv[..., 0].astype(int), hsv[..., 1].astype(int), hsv[..., 2].astype(int)

# Всё светлое на тёмно-синем фоне: кольца, линии и подписи.
bright = (val > 120).astype(np.uint8)
# Линии у серых и бледных созвездий тусклее колец (фон ≈93), для них порог ниже.
line_mask = (val > 106).astype(np.uint8)

# Звезда = кольцо. Ищем двумя способами и объединяем:
#   1) дырка внутри светлого кольца (внутренний контур светлой маски);
#   2) тёмная заливка внутри кольца (у многих колец она темнее фона: ≈30–50 против ≈93).
# Ни один способ сам по себе не находит все кольца. Ложные срабатывания на иероглифах подписей
# отсеиваются ниже: у них нет линий или «созвездие» размером с иероглиф.
def round_blobs(contours, accept):
    found = []
    for i, c in enumerate(contours):
        if not accept(i): continue
        area = cv2.contourArea(c)
        if not 6 <= area <= 260: continue
        per = cv2.arcLength(c, True)
        circ = 4 * np.pi * area / (per * per) if per > 0 else 0
        x, y, w, h = cv2.boundingRect(c)
        if circ < 0.55 or not 0.6 <= w / h <= 1.6: continue
        m = cv2.moments(c)
        found.append([m["m10"] / m["m00"], m["m01"] / m["m00"], np.sqrt(area / np.pi)])
    return found

holes, hier = cv2.findContours(bright, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
dark_blobs, _ = cv2.findContours((val < 72).astype(np.uint8), cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
stars = []
for s in round_blobs(holes, lambda i: hier[0][i][3] >= 0) + round_blobs(dark_blobs, lambda i: True):
    if all(np.hypot(s[0] - t[0], s[1] - t[1]) > 5 for t in stars): stars.append(s)

# Ручные поправки под референс 1024×917 (сверено по отладочной картинке):
# тусклые кольца, которые находятся плохо или не классифицируются по цвету, — группа задаётся явно,
# центр уточняется по тёмным пикселям рядом, найденная поблизости точка заменяется.
MISSED = [(530, 148, "north"), (530, 207, "north"),   # 虚
          (320, 125, "north"),                        # 斗, верхняя звезда
          (410, 163, "north"),                        # 牛
          (173, 461, "east"),                         # 房, левая звезда
          (398, 668, "south"), (404, 715, "south"),   # 翼
          (710, 742, "south")]                        # 鬼, центр
# подписи, которые всё равно похожи на созвездие (x0, y0, x1, y1)
TEXT_BOXES = [(598, 410, 655, 455)]  # 四辅
sx, sy = W / 1024, H / 917
for x, y, group in MISSED:
    x, y = x * sx, y * sy
    stars = [t for t in stars if np.hypot(x - t[0], y - t[1]) >= 8]
    win = val[int(y) - 9:int(y) + 10, int(x) - 9:int(x) + 10]
    ys, xs = np.nonzero(win < 90)
    if len(xs): x, y = int(x) - 9 + xs.mean(), int(y) - 9 + ys.mean()
    stars.append([x, y, 4.0, group])
stars = [s for s in stars if not any(x0 * sx <= s[0] <= x1 * sx and y0 * sy <= s[1] <= y1 * sy
                                     for x0, y0, x1, y1 in TEXT_BOXES)]

# Цвет кольца: берём самые яркие пиксели в кольцевой зоне вокруг дырки.
def ring_color(cx, cy, r_in):
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.hypot(xx - cx, yy - cy)
    zone = (d > r_in + 0.5) & (d < r_in + 4.5) & (bright > 0)
    if zone.sum() == 0: return None
    v = val[zone]; k = v >= np.percentile(v, 60)
    return float(np.median(hue[zone][k])), float(np.median(sat[zone][k])), float(np.median(v[k]))

cx0, cy0 = W * 0.5, H * 0.48
groups = []
keep = []
forced = []
for s in stars:
    if len(s) == 4:                                    # ручная поправка: группа задана явно
        keep.append(s[:3]); groups.append(s[3]); forced.append(True); continue
    col = ring_color(*s)
    if col is None: continue
    h, sa, v = col
    # зелёные кольца бледные (насыщенность ~45), поэтому порог по насыщенности низкий
    if sa >= 25 and 35 <= h <= 90: g = "east"            # 青龙, зелёный
    elif sa >= 25 and (h <= 12 or h >= 160): g = "south"  # 朱雀, красный
    elif sa >= 25 and 13 <= h <= 34: g = "pole"           # 北极 и 四辅, жёлтый
    elif sa < 25:
        # белые (白虎, справа) и серые (玄武, сверху) различаем по положению: 玄武 — верхняя дуга
        ang = np.degrees(np.arctan2(-(s[1] - cy0), s[0] - cx0))   # 0 — вправо, 90 — вверх
        g = "north" if s[1] < 240 or (s[1] < 330 and s[0] < 745) else "west"
    else:
        continue
    keep.append(s); groups.append(g); forced.append(False)
stars = np.array(keep)

# Рёбра: отрезок между кольцами почти целиком светлый и не проходит через третью звезду.
edges = []
n = len(stars)
for a in range(n):
    for b in range(a + 1, n):
        if groups[a] != groups[b]: continue
        pa, pb = stars[a, :2], stars[b, :2]
        d = np.linalg.norm(pb - pa)
        if d > 140: continue
        gap = stars[a, 2] + 3.5, d - stars[b, 2] - 3.5
        if gap[1] - gap[0] < 2:                       # кольца почти касаются
            ok = True
        else:
            u = (pb - pa) / d
            ts = np.linspace(gap[0], gap[1], max(4, int(gap[1] - gap[0])))
            pts = pa + ts[:, None] * u
            # линия толщиной ~2 px: смотрим светлый пиксель в окрестности 1 px
            lit = []
            for x, y in pts:
                xi, yi = int(round(x)), int(round(y))
                lit.append(line_mask[max(0, yi - 1):yi + 2, max(0, xi - 1):xi + 2].any())
            ok = np.mean(lit) > 0.92
        if not ok: continue
        # через третью звезду — это две короткие связи, а не одна длинная
        through = False
        for c in range(n):
            if c in (a, b): continue
            pc = stars[c, :2]
            t = np.dot(pc - pa, pb - pa) / (d * d)
            if 0 < t < 1 and np.linalg.norm(pa + t * (pb - pa) - pc) < stars[c, 2] + 3: through = True; break
        if not through: edges.append([a, b])

# Созвездия = связные компоненты по рёбрам.
def components(n, edges):
    parent = list(range(n))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for a, b in edges: parent[find(a)] = find(b)
    return [find(i) for i in range(n)]

# Ложные звёзды из подписей: одиночные кольца без линий и «созвездия» размером с иероглиф
# (замкнутые штрихи иероглифа дают несколько дырок вплотную друг к другу).
roots = components(n, edges)
good = []
for i in range(n):
    members = [j for j in range(n) if roots[j] == roots[i]]
    spread = np.ptp(stars[members, :2], axis=0).max() if len(members) > 1 else 0
    lengths = [np.linalg.norm(stars[a, :2] - stars[b, :2]) for a, b in edges if roots[a] == roots[i]]
    good.append(forced[i] or (len(members) > 1 and spread > 20 and np.mean(lengths) >= 12))
index = {old: new for new, old in enumerate(i for i in range(n) if good[i])}
stars = stars[[i for i in range(n) if good[i]]]
groups = [g for g, ok in zip(groups, good) if ok]
edges = [[index[a], index[b]] for a, b in edges if a in index and b in index]
n = len(stars)
roots = components(n, edges)
comp = {}
asterism = [comp.setdefault(r, len(comp)) for r in roots]

data = {
    "image": [W, H],
    "center": [cx0, cy0],
    "stars": [{"x": round(float(s[0]), 2), "y": round(float(s[1]), 2), "group": g, "asterism": a}
              for s, g, a in zip(stars, groups, asterism)],
    "edges": edges,
}
with open(out, "w") as f: json.dump(data, f, ensure_ascii=False, indent=1)

counts = {g: groups.count(g) for g in sorted(set(groups))}
print("stars", n, counts, "edges", len(edges), "asterisms", len(comp))

if debug:
    dbg = (im * 0.35).astype(np.uint8)
    colors = {"east": (80, 255, 80), "south": (60, 60, 255), "west": (255, 255, 255), "north": (255, 160, 60), "pole": (0, 220, 255)}
    for a, b in edges:
        cv2.line(dbg, tuple(int(v) for v in stars[a, :2]), tuple(int(v) for v in stars[b, :2]), colors[groups[a]], 1, cv2.LINE_AA)
    for (x, y, r), g, k in zip(stars, groups, asterism):
        cv2.circle(dbg, (int(x), int(y)), 4, colors[g], -1, cv2.LINE_AA)
        cv2.putText(dbg, str(k), (int(x) + 5, int(y) - 4), cv2.FONT_HERSHEY_PLAIN, 0.8, colors[g], 1)
    cv2.imwrite(debug, dbg)
