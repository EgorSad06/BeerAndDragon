#!/usr/bin/env python3
"""Анализ состава поставки (SBOM) для Release Gate.

Собирает в формате CycloneDX 1.5 (JSON):
  - движок Unity (версия из ProjectSettings/ProjectVersion.txt);
  - Unity-пакеты из Packages/packages-lock.json (+ встроенные модули);
  - сторонние ассет-паки из Assets/Game/Assets и Assets/Game/Animation
    (с поиском License/README рядом -- лицензия "неизвестна", если файла нет).
Дополнительно пишет человекочитаемый отчёт sbom.md.

Запуск:  python ci/sbom.py --version 1.0.0 --out build/sbom [--fail-on-unknown-license]
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import sys
import uuid

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ASSET_ROOTS = ["Assets/Game/Assets", "Assets/Game/Animation"]
LICENSE_NAMES = re.compile(r"^(licen[cs]e|copying|readme)[^/]*\.(txt|md)$", re.I)


def unity_version():
    with open("ProjectSettings/ProjectVersion.txt", encoding="utf-8") as fh:
        m = re.search(r"m_EditorVersion:\s*(\S+)", fh.read())
    return m.group(1) if m else "unknown"


def unity_packages():
    with open("Packages/packages-lock.json", encoding="utf-8") as fh:
        deps = json.load(fh).get("dependencies", {})
    comps = []
    for name, info in sorted(deps.items()):
        version = info.get("version", "")
        source = info.get("source", "")
        comps.append({
            "type": "library",
            "bom-ref": f"pkg:unity/{name}@{version}",
            "name": name,
            "version": version,
            "group": "unity-package",
            "purl": f"pkg:generic/unity/{name}@{version}",
            "properties": [
                {"name": "unity:source", "value": source},
                {"name": "unity:depth", "value": str(info.get("depth", ""))},
            ],
        })
    return comps


def folder_hash(path):
    """Стабильный хэш содержимого папки (имена + размеры файлов) -- чтобы ловить подмену ассетов."""
    h = hashlib.sha256()
    for root, _, files in sorted(os.walk(path)):
        for f in sorted(files):
            if f.endswith(".meta"):
                continue
            p = os.path.join(root, f)
            h.update(os.path.relpath(p, path).replace("\\", "/").encode("utf-8"))
            h.update(str(os.path.getsize(p)).encode())
    return h.hexdigest()


def asset_packs():
    comps, unknown = [], []
    for root in ASSET_ROOTS:
        if not os.path.isdir(root):
            continue
        for entry in sorted(os.listdir(root)):
            path = os.path.join(root, entry)
            if not os.path.isdir(path) or entry == "Generated":
                continue
            license_files = []
            for r, _, files in os.walk(path):
                license_files += [os.path.join(r, f).replace("\\", "/") for f in files if LICENSE_NAMES.match(f)]
            license_text = ""
            for lf in license_files:
                if "licen" in os.path.basename(lf).lower():
                    with open(lf, encoding="utf-8", errors="replace") as fh:
                        # первая содержательная строка (пропускаем рамки из ----- и ====)
                        license_text = next((l.strip() for l in fh.read(2000).splitlines()
                                             if re.search(r"[A-Za-zА-Яа-я]", l)), "")[:120]
                    break
            comp = {
                "type": "data",
                "bom-ref": f"asset:{root}/{entry}",
                "name": entry,
                "group": "third-party-asset",
                "hashes": [{"alg": "SHA-256", "content": folder_hash(path)}],
                "properties": [{"name": "path", "value": f"{root}/{entry}".replace("\\", "/")}],
            }
            if license_files:
                comp["licenses"] = [{"license": {"name": license_text or os.path.basename(license_files[0])}}]
                comp["properties"].append({"name": "license-files", "value": "; ".join(license_files)})
            else:
                comp["properties"].append({"name": "license", "value": "UNKNOWN"})
                unknown.append(f"{root}/{entry}")
            comps.append(comp)
    return comps, unknown


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.0.0-dev")
    ap.add_argument("--out", default="build/sbom")
    ap.add_argument("--fail-on-unknown-license", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    uv = unity_version()
    packages = unity_packages()
    assets, unknown = asset_packs()

    bom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.5",
        "serialNumber": f"urn:uuid:{uuid.uuid4()}",
        "version": 1,
        "metadata": {
            "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "component": {"type": "application", "name": "BeerAndDragon", "version": args.version},
            "tools": [{"name": "ci/sbom.py"}],
        },
        "components": [{"type": "framework", "name": "Unity", "version": uv, "bom-ref": f"unity@{uv}"}] + packages + assets,
    }
    with open(os.path.join(args.out, "sbom.cdx.json"), "w", encoding="utf-8") as fh:
        json.dump(bom, fh, ensure_ascii=False, indent=2)

    lines = [f"# Состав поставки BeerAndDragon {args.version}", "",
             f"- Unity: **{uv}**", f"- Unity-пакетов: **{len(packages)}**", f"- Сторонних ассет-паков: **{len(assets)}**", ""]
    lines += ["## Сторонние ассеты", "", "| Пакет | Лицензия |", "|---|---|"]
    for a in assets:
        lic = a.get("licenses", [{}])[0].get("license", {}).get("name", "**НЕИЗВЕСТНА**")
        lines.append(f"| {a['properties'][0]['value']} | {lic} |")
    lines += ["", "## Unity-пакеты (прямые зависимости)", "", "| Пакет | Версия | Источник |", "|---|---|---|"]
    for p in packages:
        if p["properties"][1]["value"] == "0":
            lines.append(f"| {p['name']} | {p['version']} | {p['properties'][0]['value']} |")
    if unknown:
        lines += ["", "## ⚠ Ассеты без лицензии", "",
                  "Перед публичным релизом нужно подтвердить права на использование:", ""]
        lines += [f"- {u}" for u in unknown]
    with open(os.path.join(args.out, "sbom.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    print(f"SBOM: Unity {uv}, пакетов {len(packages)}, ассет-паков {len(assets)}, без лицензии {len(unknown)}")
    for u in unknown:
        print(f"::warning::Нет файла лицензии у ассетов: {u}" if os.environ.get("GITHUB_ACTIONS") else f"WARN  нет лицензии: {u}")
    return 1 if unknown and args.fail_on_unknown_license else 0


if __name__ == "__main__":
    sys.exit(main())
