#!/usr/bin/env python3
"""Прогон EditMode-тестов Unity из командной строки (МДК.03.02 ПЗ-2): обычный, с покрытием, с засеянными дефектами.

  python tools/mdk0302/run_tests.py                         -- все тесты, итог по AmmoMagazineTests
  python tools/mdk0302/run_tests.py --coverage              -- + отчёт покрытия (HTML) в build/mdk0302/coverage
  python tools/mdk0302/run_tests.py --seed id1,id2,...      -- засев: дефекты из ci/mutations.json по одному

Засев меняет код рабочей копии и после каждого прогона возвращает файл командой git checkout,
поэтому запускать его можно только на чистой рабочей копии (лучше -- в отдельном клоне).
Unity с этим проектом не должен быть открыт. Путь к Unity: --unity или переменная UNITY_EXE.
Код завершения: 0 -- все тесты PASS (или, при засеве, протокол записан); 1 -- есть FAIL; 2 -- BLOCKED (Unity не отработал).
"""
import argparse
import os
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

DEFAULT_UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe"
SUITE = "BeerAndDragon.Tests.AmmoMagazineTests"
COVERED = "+**/GunScript/AmmoMagazine.cs,+**/GunScript/Weapon.cs"


def run_unity(unity, project, out_dir, tag, coverage):
    os.makedirs(out_dir, exist_ok=True)
    results = os.path.join(out_dir, f"results-{tag}.xml")
    log = os.path.join(out_dir, f"unity-{tag}.log")
    if os.path.exists(results):
        os.remove(results)
    cmd = [unity, "-batchmode", "-nographics", "-projectPath", project, "-runTests", "-testPlatform", "EditMode",
           "-testResults", results, "-logFile", log]
    if coverage:
        cmd += ["-enableCodeCoverage", "-coverageResultsPath", os.path.join(out_dir, "coverage"),
                "-coverageOptions", f"generateHtmlReport;generateAdditionalMetrics;assemblyFilters:+Assembly-CSharp;pathFilters:{COVERED}"]
    started = time.time()
    code = subprocess.call(cmd)
    print(f"Unity завершился с кодом {code} за {time.time() - started:.0f} с; журнал {log}")
    return results if os.path.exists(results) else None


def read_results(path):
    cases = []
    for tc in ET.parse(path).getroot().iter("test-case"):
        cases.append((tc.get("fullname"), tc.get("result"), (tc.findtext("failure/message") or "").strip()))
    return cases


def status(result):
    return {"Passed": "PASS", "Failed": "FAIL", "Skipped": "NOT RUN", "Inconclusive": "NOT RUN"}.get(result, result)


def summary(cases, verbose_suite=True):
    mine = [c for c in cases if c[0].startswith(SUITE)]
    if verbose_suite:
        for name, result, _ in mine:
            print(f"  {status(result):7} {name[len('BeerAndDragon.Tests.'):]}")
    failed = [c for c in cases if c[1] != "Passed"]
    print(f"Всего тестов: {len(cases)}, из них AmmoMagazineTests: {len(mine)}; PASS: {len(cases) - len(failed)}, не PASS: {len(failed)}")
    return failed


def seed(args, ids):
    import json
    with open(os.path.join(args.project, "ci", "mutations.json"), encoding="utf-8") as fh:
        mutants = {m["id"]: m for m in json.load(fh)["mutants"]}
    if subprocess.run(["git", "status", "--porcelain", "--untracked-files=no"], cwd=args.project,
                      capture_output=True, text=True).stdout.strip():
        print("Рабочая копия изменена -- засев запрещён (он откатывает файлы через git checkout)")
        return 2
    rows = []
    for mid in ids:
        m = mutants[mid]
        print(f"\n=== Дефект {mid}: {m['what']}")
        subprocess.check_call([sys.executable, "ci/apply_mutation.py", mid], cwd=args.project)
        try:
            res = run_unity(args.unity, args.project, args.out, f"seed-{mid}", False)
        finally:
            subprocess.check_call(["git", "checkout", "--", m["file"]], cwd=args.project)
        if res is None:
            rows.append((mid, "BLOCKED", "Unity не записал результаты (ошибка компиляции?)"))
            continue
        failed = [c[0].split(".")[-1] for c in read_results(res) if c[1] == "Failed"]
        rows.append((mid, "обнаружен" if failed else "пропущен", ", ".join(failed) or "--"))
        print(f"  -> {rows[-1][1]}: {rows[-1][2]}")
    print("\nПротокол засева:")
    for r in rows:
        print(f"  {r[0]:28} {r[1]:10} {r[2]}")
    found = sum(1 for r in rows if r[1] == "обнаружен")
    print(f"Засеяно {len(rows)}, обнаружено {found}, пропущено {sum(1 for r in rows if r[1] == 'пропущен')}")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--unity", default=os.environ.get("UNITY_EXE", DEFAULT_UNITY))
    ap.add_argument("--project", default=os.getcwd())
    ap.add_argument("--out", default=os.path.join("build", "mdk0302"))
    ap.add_argument("--coverage", action="store_true")
    ap.add_argument("--seed", help="id дефектов из ci/mutations.json через запятую")
    args = ap.parse_args()
    args.project = os.path.abspath(args.project)
    args.out = os.path.abspath(os.path.join(args.project, args.out))
    print(f"Проект: {args.project}")
    print("Версия: " + subprocess.run(["git", "log", "-1", "--format=%h %s"], cwd=args.project,
                                      capture_output=True, text=True).stdout.strip())
    if args.seed:
        return seed(args, [s.strip() for s in args.seed.split(",") if s.strip()])
    res = run_unity(args.unity, args.project, args.out, "coverage" if args.coverage else "local", args.coverage)
    if res is None:
        print("BLOCKED: результатов нет -- см. журнал Unity")
        return 2
    return 1 if summary(read_results(res)) else 0


if __name__ == "__main__":
    sys.exit(main())
