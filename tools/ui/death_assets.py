"""Картинки экрана смерти (DeathScreen) из исходников в art/ui/death.

  python tools/ui/death_assets.py art/ui/death Assets/Game/Resources/UI/Death

DeathCard.png -- карта таро «Смерть»: оттенки серого, скруглённые углы (прозрачность).
DeathFog.png  -- дымка из кадра гифки: только облачная часть, контраст растянут,
                 края сшиты со сдвигом на полкадра, чтобы текстура тайлилась (шейдер её прокручивает).
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw


def card(src, dst):
    im = Image.open(src).convert("L")
    w, h = im.size
    radius = int(w * 0.04)
    mask = Image.new("L", (w * 4, h * 4), 0)                    # рисуем крупно и уменьшаем -- сглаженные углы
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w * 4 - 1, h * 4 - 1), radius * 4, fill=255)
    mask = mask.resize((w, h), Image.LANCZOS)
    out = Image.merge("RGBA", (im, im, im, mask))
    out.save(dst)
    print(os.path.basename(dst), out.size)


def fog(src, dst, size=256):
    im = Image.open(src).convert("L")
    w, h = im.size
    im = im.crop((0, int(h * 0.2), w, h)).resize((size, size), Image.LANCZOS)   # сверху почти чёрное -- отрезаем
    a = np.asarray(im, dtype=np.float32) / 255.0
    lo, hi = np.percentile(a, 2), np.percentile(a, 98)
    a = np.clip((a - lo) / (hi - lo), 0, 1)

    # Шов: по каждой оси смешиваем с копией, сдвинутой на полкадра, -- у краёв берём копию (там её середина),
    # в центре -- оригинал (там шов копии)
    t = np.abs(np.linspace(-1, 1, size))
    edge = np.clip((t - 0.3) / 0.65, 0, 1)
    edge = edge * edge * (3 - 2 * edge)
    a = a * (1 - edge[None, :]) + np.roll(a, size // 2, 1) * edge[None, :]
    a = a * (1 - edge[:, None]) + np.roll(a, size // 2, 0) * edge[:, None]

    rgb = (np.clip(a, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(np.stack([rgb] * 3, -1), "RGB").save(dst)
    print(os.path.basename(dst), (size, size))


if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    card(os.path.join(src, "death_card.jpg"), os.path.join(out, "DeathCard.png"))
    fog(os.path.join(src, "grave_fog.webp"), os.path.join(out, "DeathFog.png"))
