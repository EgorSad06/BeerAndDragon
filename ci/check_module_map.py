#!/usr/bin/env python3
"""Сверка модульной карты (docs/architecture/modules.json) с исходным кодом.

Фактическая зависимость A -> B: в файлах модуля A (без комментариев и строк) встречается имя типа,
объявленного в модуле B. Скрипт сравнивает фактические зависимости с объявленными в карте и печатает
расхождения и циклы. Код возврата 1 -- карта не соответствует коду.

  python ci/check_module_map.py            # проверка
  python ci/check_module_map.py --actual   # только вывести фактические зависимости
"""
import argparse
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAP = os.path.join(ROOT, "docs", "architecture", "modules.json")
# Только типы верхнего уровня (без отступа): вложенные (Value, Data, State) упоминаются через имя внешнего
# типа, а их короткие имена совпадали бы с обычными словами вроде pair.Value
TYPE_DECL = re.compile(r"^(?:(?:public|internal|static|sealed|abstract|partial)\s+)*(?:class|struct|interface|enum)\s+([A-Z]\w*)", re.M)


def strip_code(text):
    """Убрать комментарии (включая /// XML-документацию) и строковые литералы."""
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"//[^\n]*", " ", text)
    text = re.sub(r'@"(?:[^"]|"")*"', '""', text)
    text = re.sub(r'\$?"(?:[^"\\\n]|\\.)*"', '""', text)
    return text


def module_files(m):
    files = []
    for pattern in m["files"]:
        files += glob.glob(os.path.join(ROOT, pattern), recursive=True)
    return sorted(set(os.path.normpath(f) for f in files if f.endswith(".cs")))


def actual_deps(modules):
    files = {m["id"]: module_files(m) for m in modules}
    owner, code = {}, {}
    for mid, fs in files.items():
        for f in fs:
            if f in owner:
                sys.exit(f"файл {f} входит в два модуля: {owner[f]} и {mid}")
            owner[f] = mid
            code[f] = strip_code(open(f, encoding="utf-8-sig").read())
    types = {}
    for f, text in code.items():
        for t in TYPE_DECL.findall(text):
            types.setdefault(t, owner[f])
    deps = {mid: {} for mid in files}
    for f, text in code.items():
        a = owner[f]
        for t, b in types.items():
            if b != a and re.search(r"\b" + t + r"\b", text):
                deps[a].setdefault(b, set()).add(t)
    unowned = sorted(os.path.relpath(f, ROOT) for f in map(os.path.normpath, glob.glob(os.path.join(ROOT, "Assets/Game/Scripts/**/*.cs"), recursive=True))
                     if f not in owner)
    return deps, unowned


def cycles(deps):
    found = []
    for a in deps:
        for b in deps[a]:
            if a < b and a in deps.get(b, {}):
                found.append((a, b))
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--actual", action="store_true")
    a = ap.parse_args()

    modules = json.load(open(MAP, encoding="utf-8"))["modules"]
    deps, unowned = actual_deps(modules)

    if a.actual:
        for mid in sorted(deps):
            for b in sorted(deps[mid]):
                print(f"{mid} -> {b}   ({', '.join(sorted(deps[mid][b]))})")
        return 0

    problems = []
    for m in modules:
        if m.get("status") == "план":
            if m["files"]:
                problems.append(f"{m['id']}: модуль со статусом «план» не должен иметь файлов")
            continue
        declared = set(m.get("dependsOn", []))
        actual = set(deps[m["id"]])
        for b in sorted(actual - declared):
            problems.append(f"{m['id']} -> {b}: есть в коде ({', '.join(sorted(deps[m['id']][b]))}), нет на карте")
        for b in sorted(declared - actual):
            problems.append(f"{m['id']} -> {b}: есть на карте, нет в коде")
    for f in unowned:
        problems.append(f"файл не отнесён ни к одному модулю: {f}")

    edges = sum(len(v) for v in deps.values())
    print(f"Модулей: {len(modules)}, зависимостей в коде: {edges}")
    declared_cycles = {tuple(sorted(c["between"])) for c in json.load(open(MAP, encoding="utf-8")).get("cycles", [])}
    for x, y in cycles(deps):
        mark = "объяснён в карте" if (x, y) in declared_cycles or (y, x) in declared_cycles else "НЕ объяснён"
        print(f"цикл: {x} <-> {y} ({mark})")
        if mark == "НЕ объяснён":
            problems.append(f"цикл {x} <-> {y} не объяснён в modules.json -> cycles")
    for p in problems:
        print("ERROR " + p)
    print("Карта соответствует коду" if not problems else f"Расхождений: {len(problems)}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
