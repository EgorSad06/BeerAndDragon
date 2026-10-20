#!/usr/bin/env python3
"""Модульная карта из docs/architecture/modules.json -> module-map.md (таблица + Mermaid) и module-map.png.
Соответствие карты коду проверяет ci/check_module_map.py.

  python tools/pz02/render_map.py
"""
import json
import math
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOC = os.path.join(ROOT, "docs", "architecture")
DATA = json.load(open(os.path.join(DOC, "modules.json"), encoding="utf-8"))
MODULES = DATA["modules"]
FOCUS = {"ObjectPool", "BulletHoles"}   # модули ПЗ-2


def cycles():
    return {tuple(sorted(c["between"])): c["why"] for c in DATA.get("cycles", [])}


def write_md():
    cyc = cycles()
    L = ["# Модульная карта BeerAndDragon", "",
         "Источник — [`modules.json`](modules.json); этот файл и `module-map.png` генерирует `python tools/pz02/render_map.py`.",
         "Соответствие карты коду проверяет `python ci/check_module_map.py` (и CI, `_checks.yml`).", "",
         "**Легенда.** `A --> B` — исходный код A зависит от контракта или типов B (ссылается на них в коде). "
         "Направление вызова может отличаться: `Weapons` вызывает реализацию пула, но зависит только от `IObjectPool` и фабрики. "
         "Двойная стрелка — цикл (объяснён ниже). Статус: «работает» — код есть и вызывается; «план» — модуля ещё нет. "
         "Жирная рамка — модули ПЗ-2.", "",
         "![Модульная карта](module-map.png)", "", "```mermaid", "graph TD"]
    for m in MODULES:
        label = f'{m["id"]}<br/>{m["status"]}'
        L.append(f'  {m["id"]}["{label}"]' if m["status"] != "план" else f'  {m["id"]}(["{label}"])')
    for m in MODULES:
        for d in m["dependsOn"]:
            L.append(f'  {m["id"]} --> {d}')
    L += ["  classDef focus stroke-width:4px,stroke:#d97706", "  classDef plan stroke-dasharray: 5 5",
          "  class " + ",".join(sorted(FOCUS)) + " focus",
          "  class " + ",".join(m["id"] for m in MODULES if m["status"] == "план") + " plan", "```", "",
          "## Модули", "",
          "| Модуль | Ответственность | Контракт | Зависит от | Где | Статус | Автор записи |",
          "|---|---|---|---|---|---|---|"]
    for m in MODULES:
        deps = ", ".join(m["dependsOn"]) or "—"
        L.append(f'| **{m["id"]}** | {m["responsibility"]} | {m["contract"]} | {deps} | `{m["location"]}` | {m["status"]} | {m["author"]} |')
    L += ["", "## Циклические зависимости", "",
          "Циклы — в игровом коде, где компоненты одного игрока обращаются друг к другу напрямую. "
          "Модули ПЗ-2 (`ObjectPool`, `BulletHoles`) в циклах не участвуют: `ObjectPool` не зависит ни от чего, "
          "`BulletHoles` — только от `ObjectPool` и `Diagnostics`.", "",
          "| Модули | Почему |", "|---|---|"]
    for (a, b), why in cyc.items():
        L.append(f"| {a} ↔ {b} | {why} |")
    open(os.path.join(DOC, "module-map.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")


def draw_png():
    from PIL import Image, ImageDraw, ImageFont
    W, H = 1760, 1000
    img = Image.new("RGB", (W, H), "white")
    d = ImageDraw.Draw(img)
    f_b = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 21)
    f = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 16)
    f_s = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 15)

    rows = {"tooling": ["EditorTools"], "presentation": ["UI"],
            "gameplay": ["Movement", "Player", "Items", "Skate", "Weapons"],
            "domain": ["BulletHoles"], "core": ["Diagnostics", "Config", "ObjectPool", "BatchRaycast"]}
    ys = {"tooling": 90, "presentation": 235, "gameplay": 430, "domain": 640, "core": 830}
    xs = {"tooling": [820], "presentation": [560], "gameplay": [300, 580, 860, 1140, 1420],
          "domain": [1420], "core": [380, 740, 1150, 1540]}
    bw, bh = 230, 74
    pos = {}
    for layer, ids in rows.items():
        for i, mid in enumerate(ids):
            pos[mid] = (xs[layer][i], ys[layer])
    by_id = {m["id"]: m for m in MODULES}
    cyc = cycles()

    def edge_point(a, b):
        (x1, y1), (x2, y2) = pos[a], pos[b]
        dx, dy = x2 - x1, y2 - y1
        t = min((bw / 2) / abs(dx) if dx else 1e9, (bh / 2) / abs(dy) if dy else 1e9)
        return x1 + dx * t, y1 + dy * t

    def arrow(p, q, color, width, both=False):
        d.line([p, q], fill=color, width=width)
        for (tip, base) in ([(q, p), (p, q)] if both else [(q, p)]):
            ang = math.atan2(tip[1] - base[1], tip[0] - base[0])
            for s in (-0.42, 0.42):
                d.line([tip, (tip[0] - 14 * math.cos(ang + s), tip[1] - 14 * math.sin(ang + s))], fill=color, width=width)

    def arc(a, b, color, width, both, k):
        # Дуга для несоседних модулей одного ряда: k > 0 -- под рядом, k < 0 -- над рядом
        (x1, y1), (x2, y2) = pos[a], pos[b]
        sy = y1 + (bh / 2 if k > 0 else -bh / 2)
        depth = k * (22 + abs(x2 - x1) * 0.05)
        pts = []
        for i in range(41):
            t = i / 40
            x = (1 - t) ** 2 * x1 + 2 * (1 - t) * t * (x1 + x2) / 2 + t * t * x2
            y = (1 - t) ** 2 * sy + 2 * (1 - t) * t * (sy + 2 * depth) + t * t * sy
            pts.append((x, y))
        d.line(pts, fill=color, width=width)
        for tip, base in ([(pts[-1], pts[-4]), (pts[0], pts[3])] if both else [(pts[-1], pts[-4])]):
            ang = math.atan2(tip[1] - base[1], tip[0] - base[0])
            for s2 in (-0.42, 0.42):
                d.line([tip, (tip[0] - 14 * math.cos(ang + s2), tip[1] - 14 * math.sin(ang + s2))], fill=color, width=width)

    same_row_long = 0
    drawn = set()
    for m in MODULES:
        for dep in m["dependsOn"]:
            key = tuple(sorted((m["id"], dep)))
            if key in drawn:
                continue
            drawn.add(key)
            both = key in cyc
            tooling = m["id"] == "EditorTools"
            color = (200, 60, 60) if both else (175, 175, 175) if tooling else (217, 119, 6) if m["id"] in FOCUS or dep in FOCUS else (70, 90, 120)
            w = 1 if tooling else 3 if m["id"] in FOCUS or dep in FOCUS else 2
            (xa, ya), (xb, yb) = pos[m["id"]], pos[dep]
            between = any(py == ya and min(xa, xb) < px < max(xa, xb) for px, py in pos.values())
            if ya == yb and between:
                same_row_long += 1
                arc(m["id"], dep, color, w, both, 1 if same_row_long % 2 else -1)
            else:
                arrow(edge_point(m["id"], dep), edge_point(dep, m["id"]), color, w, both)

    for mid, (x, y) in pos.items():
        m = by_id[mid]
        box = [x - bw / 2, y - bh / 2, x + bw / 2, y + bh / 2]
        focus, plan = mid in FOCUS, m["status"] == "план"
        fill = (255, 243, 224) if focus else (245, 245, 245) if plan else (232, 240, 254)
        if plan:
            for i in range(int(box[0]), int(box[2]), 14):
                d.line([(i, box[1]), (min(i + 7, box[2]), box[1])], fill=(120, 120, 120), width=2)
                d.line([(i, box[3]), (min(i + 7, box[2]), box[3])], fill=(120, 120, 120), width=2)
            for j in range(int(box[1]), int(box[3]), 14):
                d.line([(box[0], j), (box[0], min(j + 7, box[3]))], fill=(120, 120, 120), width=2)
                d.line([(box[2], j), (box[2], min(j + 7, box[3]))], fill=(120, 120, 120), width=2)
            d.rectangle([box[0] + 2, box[1] + 2, box[2] - 2, box[3] - 2], fill=fill)
        else:
            d.rounded_rectangle(box, 10, fill=fill, outline=(217, 119, 6) if focus else (70, 90, 120), width=5 if focus else 2)
        d.text((x, y - 13), mid, font=f_b, fill=(20, 20, 20), anchor="mm")
        status_color = (22, 130, 50) if m["status"] == "работает" else (120, 120, 120)
        d.text((x, y + 17), f'{m["status"]} · {m["author"]}', font=f_s, fill=status_color, anchor="mm")

    for layer, y in ys.items():
        d.text((14, y), {"tooling": "инструменты", "presentation": "интерфейс", "gameplay": "игровая логика",
                         "domain": "предметные модули", "core": "ядро"}[layer], font=f_s, fill=(140, 140, 140), anchor="lm")

    # Легенда
    lx, ly = 1180, 40
    d.rounded_rectangle([lx, ly, lx + 500, ly + 190], 8, fill=(250, 250, 250), outline=(200, 200, 200))
    d.text((lx + 14, ly + 12), "Легенда", font=f_b, fill=(20, 20, 20))
    items = [((70, 90, 120), 2, False, "A → B: код A зависит от контракта B"),
             ((217, 119, 6), 3, False, "зависимость модулей ПЗ-2"),
             ((200, 60, 60), 2, True, "цикл (объяснён в module-map.md)"),
             ((175, 175, 175), 1, False, "инструменты редактора (не в игре)")]
    for i, (c, w, both, label) in enumerate(items):
        yy = ly + 55 + i * 30
        arrow((lx + 20, yy), (lx + 90, yy), c, w, both)
        d.text((lx + 105, yy), label, font=f, fill=(40, 40, 40), anchor="lm")
    d.text((lx + 14, ly + 172), "Рамка: жирная — модули ПЗ-2, пунктир — статус «план»", font=f_s, fill=(40, 40, 40), anchor="lm")
    d.text((14, 22), "Модульная карта BeerAndDragon (docs/architecture/modules.json, проверка: ci/check_module_map.py)",
           font=f_b, fill=(20, 20, 20))
    img.save(os.path.join(DOC, "module-map.png"))


if __name__ == "__main__":
    write_md()
    draw_png()
    print("ok")
