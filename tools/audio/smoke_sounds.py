"""Звуки курения для ItemUser -- синтез из шума (без чужих записей), как звуки дробовика в WeaponSounds.

  python tools/audio/smoke_sounds.py Assets/Game/Resources/Sounds/Player/Smoke

lighter.wav  -- щелчок колёсика, чирк кремня, вспышка пламени
inhale.wav   -- затяжка: втягиваемый воздух нарастает, табак потрескивает
exhale.wav   -- короткий выдох после затяжки (дым изо рта и носа)
exhale_long.wav -- большой выдох облаком в конце
"""
import os
import struct
import sys

import numpy as np

SR = 44100
rng = np.random.default_rng(42)


def noise(sec):
    return rng.standard_normal(int(SR * sec))


def band(x, lo, hi):
    """Полосовой фильтр через FFT: оставляем частоты lo..hi Гц с мягкими краями."""
    f = np.fft.rfftfreq(len(x), 1 / SR)
    spec = np.fft.rfft(x)
    gain = np.clip((f - lo * 0.7) / (lo * 0.3 + 1e-9), 0, 1) * np.clip((hi * 1.3 - f) / (hi * 0.3), 0, 1)
    return np.fft.irfft(spec * gain, len(x))


def env(n, attack, release, shape=1.0):
    t = np.linspace(0, 1, n)
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    r = np.clip((1 - t) / max(release, 1e-4), 0, 1)
    return (a * r) ** shape


def norm(x, peak):
    return x / (np.abs(x).max() + 1e-9) * peak


def mix(length, *parts):
    out = np.zeros(int(SR * length))
    for start, sig in parts:
        i = int(SR * start)
        out[i:i + len(sig)] += sig[:len(out) - i]
    return out


def lighter():
    click = band(noise(0.012), 2500, 9000) * env(int(SR * 0.012), 0.02, 0.9, 2)
    flint = band(noise(0.07), 3000, 12000) * env(int(SR * 0.07), 0.1, 0.8)
    flint *= 1 + 0.8 * np.sin(np.linspace(0, 60, len(flint)))           # скрежет колёсика
    flame = band(noise(0.45), 150, 1800) * env(int(SR * 0.45), 0.08, 0.7, 1.5)
    return norm(mix(0.55, (0, norm(click, 1)), (0.01, norm(flint, 0.7)), (0.06, norm(flame, 0.55))), 0.8)


def inhale():
    n = int(SR * 0.9)
    air = band(noise(0.9), 900, 4500) * env(n, 0.7, 0.15, 1.3)            # нарастающий вдох
    crackle = np.zeros(n)
    for _ in range(40):                                                     # тлеющий табак трещит
        i = rng.integers(int(SR * 0.1), n - 200)
        crackle[i:i + 60] += rng.standard_normal(60) * np.exp(-np.arange(60) / 10) * rng.uniform(0.3, 1)
    crackle = band(crackle, 2000, 9000)
    return norm(norm(air, 0.6) + norm(crackle, 0.35), 0.55)


def exhale(sec, peak):
    n = int(SR * sec)
    breath = band(noise(sec), 250, 2600) * env(n, 0.08, 0.85, 1.4)
    return norm(breath, peak)


def save(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2").tobytes()
    hdr = (b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVEfmt " +
           struct.pack("<IHHIIHH", 16, 1, 1, SR, SR * 2, 2, 16) + b"data" + struct.pack("<I", len(pcm)))
    open(path, "wb").write(hdr + pcm)
    print(os.path.basename(path), round(len(x) / SR, 2), "s")


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    save(os.path.join(out, "lighter.wav"), lighter())
    save(os.path.join(out, "inhale.wav"), inhale())
    save(os.path.join(out, "exhale.wav"), exhale(0.8, 0.45))
    save(os.path.join(out, "exhale_long.wav"), exhale(1.6, 0.55))
