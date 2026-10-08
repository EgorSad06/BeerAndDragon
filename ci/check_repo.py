#!/usr/bin/env python3
"""Быстрые проверки репозитория Unity-проекта (без Unity и без лицензии).

Проверяет только файлы, которые лежат в git (git ls-files), поэтому локальный мусор не мешает.

ОШИБКИ (ломают pipeline):
  - у файла/папки в Assets нет .meta  -> Unity сгенерирует новый GUID, ссылки в сценах слетят;
  - .meta без файла (сирота);
  - одинаковые GUID в разных .meta (типичный результат кривого мержа);
  - в git попали Library/Temp/Obj/Logs/UserSettings/Build или *.csproj/*.sln;
  - файл больше 50 МБ не в LFS (GitHub не примет > 100 МБ);
  - битый JSON в Packages/manifest.json / packages-lock.json;
  - сцена из EditorBuildSettings отсутствует в репозитории.
ПРЕДУПРЕЖДЕНИЯ:
  - сцены автосохранения Assets/_Recovery;
  - бинарные ассеты, которые по .gitattributes должны быть в LFS, но лежат обычным файлом.

Запуск:  python ci/check_repo.py
"""
import json
import os
import re
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")  # кириллица в консоли Windows
from collections import defaultdict

MAX_NON_LFS_MB = 50
FORBIDDEN_DIRS = ("Library/", "Temp/", "Obj/", "obj/", "Logs/", "UserSettings/", "Build/", "Builds/", "MemoryCaptures/")
FORBIDDEN_EXT = (".csproj", ".sln", ".slnx", ".pidb", ".userprefs", ".unityproj", ".booproj", ".svd")
LFS_EXT = {".fbx", ".blend", ".obj", ".png", ".jpg", ".jpeg", ".psd", ".tga", ".tif", ".tiff", ".exr", ".hdr",
           ".mp3", ".wav", ".ogg", ".aif", ".aiff", ".mp4", ".mov", ".zip", ".7z", ".dll", ".ttf", ".otf", ".pdf"}

errors, warnings = [], []


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8", check=True).stdout


def tracked_files():
    out = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout
    return [p.decode("utf-8") for p in out.split(b"\0") if p]


def lfs_files():
    try:
        out = git("lfs", "ls-files", "-n")
        return set(l.strip() for l in out.splitlines() if l.strip())
    except (subprocess.CalledProcessError, FileNotFoundError):
        warnings.append("git-lfs не установлен -- проверка LFS пропущена")
        return None


def check_meta(files):
    file_set = set(files)
    assets = [f for f in files if f.startswith("Assets/")]

    # Папки, в которых лежат отслеживаемые файлы, тоже должны иметь .meta
    dirs = set()
    for f in assets:
        parts = f.split("/")
        for i in range(2, len(parts)):
            dirs.add("/".join(parts[:i]))

    for f in assets:
        if f.endswith(".meta"):
            target = f[:-5]
            if target not in file_set and target not in dirs:
                errors.append(f"META-сирота (нет файла/папки): {f}")
        elif not os.path.basename(f).startswith(".") and f + ".meta" not in file_set:
            errors.append(f"Нет .meta у файла: {f}")
    for d in sorted(dirs):
        if d + ".meta" not in file_set and not os.path.basename(d).startswith("."):
            errors.append(f"Нет .meta у папки: {d}")


def check_guids(files):
    by_guid = defaultdict(list)
    for f in files:
        if not f.endswith(".meta") or not os.path.exists(f):
            continue
        with open(f, encoding="utf-8", errors="replace") as fh:
            m = re.search(r"^guid:\s*([0-9a-f]{32})", fh.read(), re.M)
        if m:
            by_guid[m.group(1)].append(f)
        else:
            errors.append(f"В .meta нет guid: {f}")
    for guid, metas in by_guid.items():
        if len(metas) > 1:
            errors.append(f"Дублирующийся GUID {guid}: " + ", ".join(metas))


def check_forbidden(files):
    for f in files:
        if f.startswith(FORBIDDEN_DIRS):
            errors.append(f"В git попала служебная папка Unity: {f}")
        elif f.endswith(FORBIDDEN_EXT):
            errors.append(f"В git попал сгенерированный файл IDE: {f}")
        elif f.startswith("Assets/_Recovery"):
            warnings.append(f"Сцена автосохранения в репозитории (лучше удалить): {f}")


def check_sizes(files, lfs):
    for f in files:
        if not os.path.isfile(f):
            continue
        size_mb = os.path.getsize(f) / (1024 * 1024)
        in_lfs = lfs is not None and f in lfs
        if lfs is not None and not in_lfs:
            if size_mb > MAX_NON_LFS_MB:
                errors.append(f"Файл {size_mb:.1f} МБ не в LFS: {f}")
            elif os.path.splitext(f)[1].lower() in LFS_EXT and size_mb > 1:
                warnings.append(f"Бинарный файл {size_mb:.1f} МБ не в LFS (по .gitattributes должен быть): {f}")


def check_packages():
    for path in ("Packages/manifest.json", "Packages/packages-lock.json"):
        if not os.path.exists(path):
            errors.append(f"Нет {path}")
            continue
        try:
            with open(path, encoding="utf-8") as fh:
                json.load(fh)
        except json.JSONDecodeError as e:
            errors.append(f"Битый JSON в {path}: {e}")


def check_build_scenes(files):
    path = "ProjectSettings/EditorBuildSettings.asset"
    if not os.path.exists(path):
        errors.append("Нет ProjectSettings/EditorBuildSettings.asset")
        return
    with open(path, encoding="utf-8") as fh:
        text = fh.read()
    scenes = re.findall(r"enabled: 1\s*\n\s*path: (.+)", text)
    if not scenes:
        errors.append("В Build Settings нет ни одной включённой сцены -- собирать нечего")
    for s in scenes:
        if s.strip() not in files:
            errors.append(f"Сцена из Build Settings не найдена в репозитории: {s.strip()}")


def main():
    files = tracked_files()
    lfs = lfs_files()
    check_forbidden(files)
    check_meta(files)
    check_guids(files)
    check_sizes(files, lfs)
    check_packages()
    check_build_scenes(files)

    for w in warnings:
        print(f"::warning::{w}" if os.environ.get("GITHUB_ACTIONS") else f"WARN  {w}")
    for e in errors:
        print(f"::error::{e}" if os.environ.get("GITHUB_ACTIONS") else f"ERROR {e}")
    print(f"\nПроверено файлов: {len(files)} | ошибок: {len(errors)} | предупреждений: {len(warnings)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
