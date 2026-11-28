# Небо космоса: почти чёрная пустота, слабая туманность и созвездия 28 лунных стоянок (二十八宿).
# Выход — равнопромежуточная HDR-карта (Radiance .hdr, линейные значения), Unity импортирует её как кубмапу для HDRI Sky.
#
# Запуск (нужны numpy и opencv-python):
#   python make_space_sky.py <выход.hdr>                                   — только туманность (пустота)
#   python make_space_sky.py <выход.hdr> --sectors south,west,north         — космос: сектора по сторонам света
#   python make_space_sky.py <выход.hdr> --zenith east                      — уровень босса: один сектор в зените
#   --mansions <json>  — звёзды и линии (по умолчанию mansions.json рядом со скриптом, делает extract_mansions.py)
#   --preview <png>    — уменьшенная копия с тонмаппингом, чтобы посмотреть глазами
#
# Сектора: east — 青龙, весна (Цинлун); south — 朱雀, лето (Чжуцюэ); west — 白虎, осень (Байху); north — 玄武, зима (Сюаньу).
# Космос: сектора стоят относительно МКС — север вверху, юг внизу, восток справа от носа станции, запад слева.
# Уровень босса: его сектор раскидан по верхней полусфере.
# Развёртка — как у Unity (Skybox/Panoramic): u = atan2(x, z) / 2π + 0.5, v = 0 — зенит.
import argparse, json, os
import numpy as np
import cv2

ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--sectors", default="", help="через запятую: east,south,west,north")
ap.add_argument("--zenith", default="", help="один сектор в зените (уровень босса)")
ap.add_argument("--mansions", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "mansions.json"))
ap.add_argument("--preview", default="")
ap.add_argument("--width", type=int, default=4096)
# Яркости — в единицах карты; HDRI Sky в сцене умножает их на 2^exposure (космос: exposure 16.5 при камере EV 15).
ap.add_argument("--star", type=float, default=1.4, help="яркость центра звезды")
ap.add_argument("--halo", type=float, default=0.07, help="яркость ореола звезды относительно центра")
ap.add_argument("--line", type=float, default=0.05, help="яркость линии созвездия")
ap.add_argument("--star-size", type=float, default=0.16, help="радиус ядра звезды, градусов")
ap.add_argument("--line-width", type=float, default=0.05, help="полуширина линии, градусов")
ap.add_argument("--deg-per-px", type=float, default=0.1125, help="космос: размер созвездий, градусов неба на пиксель референса")
ap.add_argument("--boss-deg-per-px", type=float, default=0.12, help="уровень босса: размер созвездий, градусов на пиксель")
ap.add_argument("--min-altitude", type=float, default=25.0,
                help="уровень босса: созвездия не ниже этой высоты над горизонтом, градусов (стены ущелья закрывают горизонт)")
ap.add_argument("--iss-yaw", type=float, default=45.0,
                help="космос: поворот МКС вокруг вертикали, градусов (SpaceSceneBuilder.StationYaw); нос — её локальная +Z")
ap.add_argument("--gap", type=float, default=6.0, help="минимальный зазор между созвездиями, градусов")
ap.add_argument("--pull", type=float, default=0.004, help="сила, с которой созвездия группы стягиваются к своей точке")
args = ap.parse_args()

W, H = args.width, args.width // 2
rng = np.random.default_rng(7)
# numpy 2.x на macOS (Accelerate) пишет ложные предупреждения в matmul float32 — результат верный.
np.seterr(divide="ignore", over="ignore", invalid="ignore")

# Направление каждого пикселя: +X восток, +Y вверх, +Z север.
u = (np.arange(W) + 0.5) / W
v = (np.arange(H) + 0.5) / H
lon = (u - 0.5) * 2 * np.pi
polar = v * np.pi
lon, polar = np.meshgrid(lon, polar)
dirs = np.stack([np.sin(polar) * np.sin(lon), np.cos(polar), np.sin(polar) * np.cos(lon)], -1).astype(np.float32)

# ---------- Туманность ----------
def value_noise(p, cells):
    # 3D value noise на решётке cells^3, сглаженная (smoothstep) трилинейная интерполяция
    lattice = rng.random((cells + 1,) * 3).astype(np.float32)
    q = np.clip((p * 0.5 + 0.5) * (cells - 1), 0, cells - 1e-3)
    i = np.floor(q).astype(np.int32); f = q - i
    f = f * f * (3 - 2 * f)
    x, y, z = i[..., 0], i[..., 1], i[..., 2]
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]
    def L(dx, dy, dz): return lattice[x + dx, y + dy, z + dz]
    c00 = L(0,0,0) * (1-fx) + L(1,0,0) * fx; c10 = L(0,1,0) * (1-fx) + L(1,1,0) * fx
    c01 = L(0,0,1) * (1-fx) + L(1,0,1) * fx; c11 = L(0,1,1) * (1-fx) + L(1,1,1) * fx
    return (c00 * (1-fy) + c10 * fy) * (1-fz) + (c01 * (1-fy) + c11 * fy) * fz

def fbm(p, base, octaves):
    total, amp, norm = 0, 1.0, 0
    for o in range(octaves):
        total = total + value_noise(p, base * 2 ** o) * amp
        norm += amp; amp *= 0.55
    return total / norm

# Искажение координат — туманность волокнистая, а не «облачка».
warp = np.stack([fbm(dirs, 4, 3), fbm(dirs[..., [1, 2, 0]], 4, 3), fbm(dirs[..., [2, 0, 1]], 4, 3)], -1)
p = np.clip(dirs + (warp - 0.5) * 0.6, -1, 1)
cloud = fbm(p, 5, 6)
detail = fbm(p[..., [2, 1, 0]], 9, 5)

# Полоса туманности наклонена к экватору, чтобы она проходила через небо, а не была везде.
band_axis = np.array([0.35, 0.86, 0.37], np.float32); band_axis /= np.linalg.norm(band_axis)
band = np.exp(-((dirs @ band_axis) / 0.42) ** 2)

density = np.clip((cloud - 0.42) * 2.6, 0, 1) ** 2 * band
dust = np.clip((detail - 0.5) * 3, 0, 1)
density = density * (0.55 + 0.45 * dust)

deep = np.array([0.0012, 0.0014, 0.0030], np.float32)       # пустота: почти чёрная, чуть синяя
violet = np.array([0.050, 0.020, 0.080], np.float32)
teal = np.array([0.010, 0.045, 0.060], np.float32)
rose = np.array([0.080, 0.022, 0.030], np.float32)
hue = fbm(p * 0.7, 3, 3)[..., None]
tint = np.where(hue < 0.5, violet + (teal - violet) * (hue / 0.5), teal + (rose - teal) * ((hue - 0.5) / 0.5))
img = deep + tint * density[..., None]

# Тёмные прожилки пыли поверх свечения.
lanes = np.clip((fbm(p * 1.3, 7, 4) - 0.55) * 4, 0, 1)[..., None]
img = (img * (1 - 0.7 * lanes * band[..., None])).astype(np.float32)

# ---------- Созвездия ----------
# Все звёзды и линии белые, у всех символов одинаковые. Полюс (北极, 四辅) в небо не попадает.
WHITE = np.array([1.0, 1.0, 1.0], np.float32)
COLORS = {"east": WHITE, "south": WHITE, "west": WHITE, "north": WHITE}

sectors = [s for s in args.sectors.split(",") if s]
if args.zenith: sectors = [args.zenith]
for s in sectors:
    if s not in COLORS: raise SystemExit("неизвестный сектор " + s)

if sectors:
    data = json.load(open(args.mansions))
    pts = np.array([[s["x"], s["y"]] for s in data["stars"]], np.float32)
    grp = [s["group"] for s in data["stars"]]
    n_stars = len(grp)
    k = np.radians(args.boss_deg_per_px if args.zenith else args.deg_per_px)
    UP = np.array([0.0, 1.0, 0.0])

    # ---- Созвездия как отдельные единицы: связные по линиям группы звёзд; куски одной стоянки,
    # лежащие вплотную (斗 из двух частей, одиночная звезда 牛 рядом с остальными), склеиваются.
    parent = list(range(n_stars))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for a, b in data["edges"]: parent[find(a)] = find(b)
    for a in range(n_stars):
        for b in range(a + 1, n_stars):
            if grp[a] == grp[b] and grp[a] != "pole" and np.linalg.norm(pts[a] - pts[b]) < 26:
                parent[find(a)] = find(b)
    units = {}
    for i in range(n_stars):
        if grp[i] != "pole": units.setdefault(find(i), []).append(i)
    units = [(grp[m[0]], m) for m in units.values()]

    map_center = (pts.min(0) + pts.max(0)) * 0.5
    def image_angle(members):
        # порядок созвездий вдоль кольца на картинке (против часовой, если смотреть на картинку)
        c = pts[members].mean(0) - map_center
        return np.arctan2(-c[1], c[0])

    def rotation_from_zenith(c):
        # кратчайший поворот зенита в направление c (формула Родрига)
        axis = np.cross(UP, c); s = np.linalg.norm(axis); cosang = float(np.clip(c @ UP, -1, 1))
        if s < 1e-8: return np.eye(3) if cosang > 0 else np.diag([1.0, -1.0, -1.0])
        axis /= s; K = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
        return np.eye(3) + s * K + (1 - cosang) * (K @ K)

    def local_dirs(members):
        # Форма созвездия в зените: картинка — вид неба снизу (север сверху, восток слева),
        # азимутальная равнопромежуточная проекция вокруг центра созвездия.
        c = pts[members].mean(0)
        out = []
        for i in members:
            dx, dy = pts[i] - c
            r = np.hypot(dx, dy); theta = r * k
            east, north = (-dx / r, -dy / r) if r > 1e-6 else (0.0, 0.0)
            out.append([np.sin(theta) * east, np.cos(theta), np.sin(theta) * north])
        return np.array(out)

    unit_shape = [local_dirs(m) for _, m in units]
    # угловой радиус созвездия + половина зазора
    unit_radius = [float(np.arccos(np.clip(sh[:, 1], -1, 1)).max()) + np.radians(args.gap) * 0.5 for sh in unit_shape]

    def az_el(c):
        return np.degrees(np.arctan2(c[0], c[2])) % 360, np.degrees(np.arcsin(np.clip(c[1], -1, 1)))
    def from_az_el(az, el):
        az, el = np.radians(az), np.radians(el)
        return np.array([np.cos(el) * np.sin(az), np.sin(el), np.cos(el) * np.cos(az)])

    # ---- Раскладка.
    # Космос: каждый символ — компактная группа ровно в своей точке неба («по полюсам») относительно МКС:
    # север — вверху, юг — внизу, восток — справа от носа станции, запад — слева. Раскладываются все четыре
    # группы сразу (одинаково во всех сценах), рисуются только выбранные.
    # Уровень босса: созвездия его символа раскиданы по всему видимому небу — одно у зенита, остальные по кругу
    # на разной высоте, не ниже min-altitude.
    yaw = np.radians(args.iss_yaw)
    nose = np.array([np.sin(yaw), 0.0, np.cos(yaw)])          # локальная +Z станции после поворота
    right = np.cross(UP, nose)                                 # правая рука, если смотреть вдоль носа (Unity: Cross(up, forward))
    ANCHOR = {"north": UP.copy(), "south": -UP, "east": right, "west": -right}
    groups_used = [args.zenith] if args.zenith else ["east", "south", "west", "north"]
    anchor_of = {g: (UP.copy() if args.zenith else ANCHOR[g]) for g in groups_used}
    layout = [i for i, (g, _) in enumerate(units) if g in groups_used]
    centers = {}
    jitter = np.random.default_rng(11)   # своё зерно: раскладка одинакова во всех сценах
    for g in groups_used:
        a = anchor_of[g]
        ref = np.array([0.0, 0.0, 1.0]) if abs(a @ UP) > 0.9 else UP
        e1 = np.cross(ref, a); e1 /= np.linalg.norm(e1); e2 = np.cross(a, e1)
        mine = sorted([i for i in layout if units[i][0] == g], key=lambda i: image_angle(units[i][1]))
        n = len(mine)
        if args.zenith:
            # самое крупное созвездие — у зенита, остальные по кругу через одно выше и ниже
            big = max(mine, key=lambda i: unit_radius[i])
            ring = [i for i in mine if i != big]
            centers[big] = from_az_el(jitter.uniform(0, 360), 78.0 + jitter.uniform(-6, 6))
            for j, i in enumerate(ring):
                az = 360.0 * j / len(ring) + jitter.uniform(-15, 15)
                el = (55.0 if j % 2 else 35.0) + jitter.uniform(-6, 6)
                centers[i] = from_az_el(az, el)
            continue
        r0 = 2.2 * np.mean([unit_radius[i] for i in mine])
        for j, i in enumerate(mine):
            # кольцо вокруг точки группы в порядке картинки; раздвижка и стяжка дальше соберут его в плотную кучку
            phi = 2 * np.pi * j / n + jitter.uniform(-0.3, 0.3)
            r = r0 * jitter.uniform(0.8, 1.2)
            t = np.cos(phi) * e1 + np.sin(phi) * e2
            centers[i] = a * np.cos(r) + t * np.sin(r)

    # Раздвижка: пересекающиеся созвездия отталкиваются по дуге; все слегка стягиваются к точке своей группы.
    for it in range(3000):
        moved = False
        for a_i in range(len(layout)):
            for b_i in range(a_i + 1, len(layout)):
                a, b = layout[a_i], layout[b_i]
                ca, cb = centers[a], centers[b]
                ang = np.arccos(np.clip(ca @ cb, -1, 1))
                need = unit_radius[a] + unit_radius[b]
                if ang >= need: continue
                moved = True
                push = (need - ang) * 0.5 + 1e-4
                ta = ca - cb * (ca @ cb); ta = ta / (np.linalg.norm(ta) + 1e-9)
                tb = cb - ca * (ca @ cb); tb = tb / (np.linalg.norm(tb) + 1e-9)
                centers[a] = ca * np.cos(push) + ta * np.sin(push)
                centers[b] = cb * np.cos(push) + tb * np.sin(push)
        for i in layout:
            if args.zenith:
                # по всему небу: без стяжки, только не ниже min-altitude (с учётом размера созвездия)
                az, el = az_el(centers[i])
                centers[i] = from_az_el(az, min(max(el, args.min_altitude + np.degrees(unit_radius[i])), 89.9))
                continue
            c = centers[i] + anchor_of[units[i][0]] * args.pull
            centers[i] = c / np.linalg.norm(c)
        if not moved and it > 300: break
    overlaps = sum(1 for x in range(len(layout)) for y in range(x + 1, len(layout))
                   if np.arccos(np.clip(centers[layout[x]] @ centers[layout[y]], -1, 1))
                   < unit_radius[layout[x]] + unit_radius[layout[y]] - np.radians(args.gap) * 0.5)
    if overlaps: print("внимание: пересекающихся пар созвездий:", overlaps)
    for g in groups_used:
        spread = max(np.degrees(np.arccos(np.clip(centers[i] @ anchor_of[g], -1, 1))) + np.degrees(unit_radius[i])
                     for i in layout if units[i][0] == g)
        print(f"группа {g}: радиус {spread:.1f}°")

    # Направления звёзд: форма созвездия из зенита переносится кратчайшим поворотом в его центр.
    star_dirs = np.zeros((n_stars, 3))
    for i in layout:
        R = rotation_from_zenith(centers[i])
        for j, s_i in enumerate(units[i][1]):
            star_dirs[s_i] = R @ unit_shape[i][j]
    placed = set(s for i in layout for s in units[i][1])

    def to_pixel(d):
        uu = np.arctan2(d[0], d[2]) / (2 * np.pi) + 0.5
        vv = np.arccos(np.clip(d[1], -1, 1)) / np.pi
        return uu * W, vv * H

    def splat(layer, d, sigma_deg, reach):
        # Гауссово пятно вокруг направления d, угловой радиус sigma; в слой — максимум, а не сумма,
        # чтобы линия, нарисованная частыми точками, была ровной.
        px, py = to_pixel(d)
        r_px = reach * np.radians(sigma_deg) / np.pi * H
        y0, y1 = max(0, int(py - r_px) - 1), min(H, int(py + r_px) + 2)
        sinp = max(np.sin(np.pi * (py / H)), 0.05)
        rx = min(W // 2, int(r_px / sinp) + 2)
        cols = np.arange(int(px) - rx, int(px) + rx + 1) % W
        patch = dirs[y0:y1][:, cols]
        ang = np.arccos(np.clip(patch @ d, -1, 1))
        val = np.exp(-(ang / np.radians(sigma_deg)) ** 2)
        layer[y0:y1, cols] = np.maximum(layer[y0:y1, cols], val)

    for sector in sectors:
        idx = [i for i, g in enumerate(grp) if g == sector and i in placed]
        idxset = set(idx)
        core = np.zeros((H, W), np.float32)
        halo = np.zeros((H, W), np.float32)
        lines = np.zeros((H, W), np.float32)
        for i in idx:
            splat(core, star_dirs[i], args.star_size, 3.0)
            splat(halo, star_dirs[i], args.star_size * 4.0, 3.0)
        gap = np.radians(args.star_size * 2.5)            # линия не доходит до звезды, как на референсе
        step = np.radians(args.line_width * 0.6)
        for a, b in data["edges"]:
            if a not in idxset or b not in idxset: continue
            da, db = star_dirs[a], star_dirs[b]
            omega = np.arccos(np.clip(da @ db, -1, 1))
            if omega < 2 * gap + step: continue
            for t in np.arange(gap, omega - gap, step):
                # точка на дуге большого круга между звёздами
                d = (np.sin(omega - t) * da + np.sin(t) * db) / np.sin(omega)
                splat(lines, d, args.line_width, 2.5)
        color = COLORS[sector]
        light = core * args.star + halo * args.star * args.halo + lines * args.line
        img += light[..., None] * color

out = args.out
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
ok = cv2.imwrite(out, img[..., ::-1].astype(np.float32))
print("ok" if ok else "fail", out, "sectors:", ",".join(sectors) or "нет", "max", float(img.max()))

if args.preview:
    # Просмотр: экспозиция примерно как в игре (HDRI +16.5 EV при камере EV 15 → ×2.8), простой тонмаппинг.
    pv = img * 2.8
    pv = pv / (1 + pv)
    pv = np.power(np.clip(pv, 0, 1), 1 / 2.2)
    pv = cv2.resize(pv, (W // 2, H // 2), interpolation=cv2.INTER_AREA)
    cv2.imwrite(args.preview, (pv[..., ::-1] * 255).astype(np.uint8))
