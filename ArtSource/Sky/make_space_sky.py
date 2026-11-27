# Небо космоса для Space_ISS: почти чёрная пустота и слабая туманность, без единой звезды.
# Выход — равнопромежуточная HDR-карта (Radiance .hdr, линейные значения), Unity импортирует её как кубмапу для HDRI Sky.
# Запуск: python make_space_sky.py <выходной .hdr>   (нужны numpy и opencv-python)
import os, sys
import numpy as np
import cv2

W, H = 4096, 2048
rng = np.random.default_rng(7)

lon = (np.arange(W) + 0.5) / W * 2 * np.pi
lat = (0.5 - (np.arange(H) + 0.5) / H) * np.pi
lon, lat = np.meshgrid(lon, lat)
dirs = np.stack([np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)], -1).astype(np.float32)

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
img = img * (1 - 0.7 * lanes * band[..., None])

out = sys.argv[1]
os.makedirs(os.path.dirname(out), exist_ok=True)
ok = cv2.imwrite(out, img[..., ::-1].astype(np.float32))
print("ok" if ok else "fail", img.max(), img.mean())
