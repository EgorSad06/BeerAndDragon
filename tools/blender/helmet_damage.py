"""Стадии повреждения шлема рыцаря для портрета (HUD): рисуем по 3D-позиции каждого текселя головы, без швов.

  blender -b --factory-startup -P tools/blender/knight_head.py -- build/portrait      (треугольники головы -> JSON)
  python tools/blender/helmet_damage.py <albedo.png> build/portrait/head_tris.json Assets/Game/Resources/UI/Portrait

KnightHelmet_0..4.png: 0 -- чистый, 1 -- грязь (90% HP), 2 -- + царапины (80%), 3 -- + вмятина с трещинами (70%),
4 -- + кровь на шлеме (30%). Каждая стадия включает предыдущие."""
import json, sys
import numpy as np
from PIL import Image

src, tris_json, out = sys.argv[1], sys.argv[2], sys.argv[3]
N = 1024
rng = np.random.default_rng(5)
base = np.asarray(Image.open(src).convert("RGB").resize((N, N), Image.LANCZOS)).astype(float) / 255
tris = json.load(open(tris_json))
# UV шлема зеркальные (обе половины на одних пикселях). Правую половину (x < 0 в Blender, правый бок рыцаря)
# переносим в пустой угол текстуры -- тогда повреждения несимметричные. Тот же сдвиг делает PlayerPortrait в Unity.
SHIFT = np.array([-0.38, -0.55])
src_px = np.zeros((N, N, 2), int)                # откуда брать исходный цвет для текселя
src_px[..., 0], src_px[..., 1] = np.meshgrid(np.arange(N), np.arange(N))[1], np.meshgrid(np.arange(N), np.arange(N))[0]

# растеризация треугольников головы в UV: для каждого текселя -- точка на шлеме
pos = np.full((N, N, 3), np.nan)
for t in tris:
    p = np.array(t["p"])
    shifted = p[:, 0].mean() < 0
    uv = (np.array(t["uv"]) + (SHIFT if shifted else 0)) * N
    uv[:, 1] = N - uv[:, 1]                     # строки картинки сверху вниз
    x0, y0 = np.floor(uv.min(0)).astype(int) - 1
    x1, y1 = np.ceil(uv.max(0)).astype(int) + 1
    xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1, N)), np.arange(max(y0, 0), min(y1, N)))
    q = np.stack([xs + 0.5, ys + 0.5], -1)
    a, b, c = uv
    m = np.array([[b[0] - a[0], c[0] - a[0]], [b[1] - a[1], c[1] - a[1]]])
    if abs(np.linalg.det(m)) < 1e-9:
        continue
    l = (q - a) @ np.linalg.inv(m).T
    w1, w2 = l[..., 0], l[..., 1]
    w0 = 1 - w1 - w2
    e = -0.08                                    # чуть шире треугольника -- без щелей на краях островов
    inside = (w0 >= e) & (w1 >= e) & (w2 >= e)
    pp = w0[..., None] * p[0] + w1[..., None] * p[1] + w2[..., None] * p[2]
    pos[ys[inside], xs[inside]] = pp[inside]
    if shifted:   # цвет -- из исходного места атласа
        src_px[ys[inside], xs[inside], 0] = ys[inside] + int(round(SHIFT[1] * N))
        src_px[ys[inside], xs[inside], 1] = xs[inside] - int(round(SHIFT[0] * N))
mask = ~np.isnan(pos[..., 0])
base = base[np.clip(src_px[..., 0], 0, N - 1), np.clip(src_px[..., 1], 0, N - 1)]
P = np.nan_to_num(pos)
print("head texels", mask.sum())
lo, hi = P[mask].min(0), P[mask].max(0)
center = (lo + hi) / 2


def noise3(p, freq, seed):
    """Гладкий 3D-шум: сумма синусов со случайными направлениями."""
    r = np.random.default_rng(seed)
    acc = np.zeros(p.shape[:2])
    for k in range(6):
        d = r.normal(size=3); d /= np.linalg.norm(d)
        acc += np.sin(p @ d * freq * (1 + k * 0.7) + r.uniform(0, 6.3)) / (1 + k * 0.5)
    return (acc - acc[mask].min()) / (np.ptp(acc[mask]) + 1e-9)


def apply(img, amount, color):
    amount = np.clip(amount, 0, 1) * mask
    return img * (1 - amount[..., None]) + np.array(color) * amount[..., None]


# 1) грязь: бурые пятна, гуще снизу
up = (P[..., 2] - lo[2]) / (hi[2] - lo[2])
dirt = np.clip((noise3(P, 55, 1) - 0.45) * 3.0, 0, 1) * (0.55 + 0.45 * (1 - up))
grain = rng.random((N, N))
s1 = apply(base, dirt * 0.8 * (0.75 + 0.5 * grain), (0.45, 0.35, 0.2))       # светлая бурая грязь -- видна и на тёмном забрале

# 2) царапины: светлые штрихи с тёмной кромкой
s2 = s1.copy()
for k in range(16):
    a = center + (rng.random(3) - 0.5) * (hi - lo) * 0.9
    d = rng.normal(size=3); d /= np.linalg.norm(d)
    L = rng.uniform(0.05, 0.1)
    t = np.clip((P - a) @ d, 0, L)
    dist = np.linalg.norm(P - (a + t[..., None] * d), axis=-1)
    s2 = apply(s2, (dist < 0.008) * 0.8, (0.12, 0.12, 0.13))
    s2 = apply(s2, (dist < 0.0045) * 0.95, (0.93, 0.93, 0.95))

# 3) вмятина: сверху-спереди слева (у Blender: перёд -Y, левый бок рыцаря +X)
half = (hi - lo) / 2
# вершина шлема, ближайшая к точке слева-спереди-сверху; PlayerPortrait вдавливает ту же вершину меша
want = center + half * np.array([0.8, -0.75, 0.45])
verts = np.array([p for t in tris for p in t["p"]])
vtx = verts[np.argmin(np.linalg.norm(verts - want, axis=-1))]
dent = vtx - (vtx - center) * 0.18
print("dent", dent.round(3).tolist())
r = np.linalg.norm(P - dent, axis=-1)
s3 = apply(s2, np.clip(1 - r / 0.07, 0, 1) ** 1.1 * 0.9, (0.05, 0.05, 0.07))     # тень в ямке
s3 = apply(s3, ((r > 0.065) & (r < 0.078) & (P[..., 2] > dent[2] + 0.01)) * 0.45, (0.85, 0.86, 0.9))                 # блик на краю
nrm = (dent - center) / np.linalg.norm(dent - center)
for k in range(5):                                                                  # трещины от центра
    d = rng.normal(size=3); d -= (d @ nrm) * nrm; d /= np.linalg.norm(d)
    L = rng.uniform(0.05, 0.09)
    t = np.clip((P - dent) @ d, 0, L)
    dist = np.linalg.norm(P - (dent + t[..., None] * d), axis=-1)
    s3 = apply(s3, (dist < 0.006 * (1 - t / L) + 0.002) * 0.95, (0.02, 0.02, 0.03))

# 4) кровь на шлеме: брызги у вмятины и потёки вниз
s4 = s3.copy()
blood = (r < 0.06) * (noise3(P, 160, 3) > 0.25) + (r < 0.035)
for k in range(12):
    src_pt = dent + np.array([rng.uniform(-0.06, 0.03), rng.uniform(-0.05, 0.03), rng.uniform(-0.02, 0.02)])
    horiz = np.linalg.norm((P - src_pt)[..., :2], axis=-1)
    length = rng.uniform(0.08, 0.22)
    below = (P[..., 2] < src_pt[2]) & (P[..., 2] > src_pt[2] - length)
    width = 0.014 * (1 - np.clip((src_pt[2] - P[..., 2]) / length, 0, 1)) + 0.0025
    blood = np.maximum(blood, (horiz < width) * below)
blood = np.maximum(blood, (noise3(P, 220, 4) > 0.8) * 1.0)                           # мелкие капли
dark = 0.55 + 0.45 * noise3(P, 90, 6)
s4 = apply(s4, blood * 0.92, np.zeros(3))
s4 = s4 + (blood * 0.92 * mask)[..., None] * (np.array([0.42, 0.02, 0.02]) * dark[..., None])

for i, img in enumerate((base, s1, s2, s3, s4)):
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(f"{out}/KnightHelmet_{i}.png")
if len(sys.argv) < 5:
    sys.exit(0)
# превью головы (5-й аргумент): обрез вокруг островов головы
ys, xs = np.nonzero(mask)
box = (xs.min() - 8, ys.min() - 8, xs.max() + 8, ys.max() + 8)
row = [Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).crop(box) for img in (base, s1, s2, s3, s4)]
w, h = row[0].size
sheet = Image.new("RGB", (w * 5, h))
for i, im in enumerate(row):
    sheet.paste(im, (i * w, 0))
sheet.resize((w * 5 * 2, h * 2), Image.NEAREST).save(sys.argv[4])
print("box", box)
