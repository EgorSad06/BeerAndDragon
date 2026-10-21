#!/usr/bin/env python3
"""Проверка XML с результатами Unity Test Framework (NUnit 3).

Unity завершается с кодом 0 и failed=0, даже если тестовый класс не загрузился (runstate="NotRunnable",
например параметризованная фикстура вне пространства имён) -- такие тесты просто не выполняются.
Скрипт падает, если общий результат прогона не Passed или есть незагруженные наборы.

  python ci/check_test_results.py build/editmode.xml [--min-total 50]
"""
import argparse
import sys
import xml.etree.ElementTree as ET


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("xml", nargs="+")
    ap.add_argument("--min-total", type=int, default=1, help="минимум выполненных тестов")
    a = ap.parse_args()

    problems = []
    total = 0
    for path in a.xml:
        root = ET.parse(path).getroot()
        run = root if root.tag == "test-run" else root.find("test-run")
        total += int(run.get("total", 0))
        if not run.get("result", "").startswith("Passed"):
            problems.append(f"{path}: результат прогона {run.get('result')}")
        for suite in run.iter("test-suite"):
            if suite.get("runstate") == "NotRunnable":
                reason = ""
                for prop in suite.iter("property"):
                    if prop.get("name") == "_SKIPREASON":
                        reason = prop.get("value", "").splitlines()[0:2]
                problems.append(f"{path}: набор {suite.get('fullname')} не загружен: {' '.join(reason)}")
        for case in run.iter("test-case"):
            if case.get("result") == "Failed":
                problems.append(f"{path}: упал {case.get('fullname')}")

    print(f"Выполнено тестов: {total}")
    if total < a.min_total:
        problems.append(f"выполнено {total} тестов, ожидалось не меньше {a.min_total}")
    for p in problems:
        print("ERROR " + p)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
