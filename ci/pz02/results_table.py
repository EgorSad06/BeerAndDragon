#!/usr/bin/env python3
"""Таблица результатов проверок модуля ObjectPool: случай контракта × реализация (ПЗ-2, рисунок 5).

  python ci/pz02/results_table.py build/editmode.xml
"""
import re
import sys
import xml.etree.ElementTree as ET

# Вид случая по имени теста
KIND = [
    (r"Throw", "ошибочный"),
    (r"Overflow|Capacity1|Prewarm|IsActive|ZeroLifetime", "граничный"),
    (r".", "штатный"),
]


def kind(name):
    return next(k for pat, k in KIND if re.search(pat, name))


def main():
    run = ET.parse(sys.argv[1]).getroot()
    rows = {}
    for case in run.iter("test-case"):
        m = re.match(r"(?:BeerAndDragon\.Tests\.)?(ObjectPoolContractTests|BulletHolesTests)\((\w+)\)\.(\w+)", case.get("fullname", ""))
        if not m:
            continue
        suite, impl, test = m.groups()
        rows.setdefault((suite, test), {})[impl] = case.get("result")

    total = {"Bounded": [0, 0], "Linked": [0, 0]}
    print(f"{'Набор':<24} {'Случай':<48} {'Вид':<10} {'Bounded':<8} {'Linked':<8}")
    print("-" * 100)
    for (suite, test), res in sorted(rows.items(), key=lambda r: (r[0][0], ["штатный", "граничный", "ошибочный"].index(kind(r[0][1])), r[0][1])):
        cells = []
        for impl in ("Bounded", "Linked"):
            r = res.get(impl, "—")
            total[impl][0] += r == "Passed"
            total[impl][1] += 1
            cells.append("PASS" if r == "Passed" else "FAIL" if r == "Failed" else r)
        print(f"{suite.replace('Tests', ''):<24} {test:<48} {kind(test):<10} {cells[0]:<8} {cells[1]:<8}")
    print("-" * 100)
    print("Итог: " + ", ".join(f"{impl} {p}/{n}" for impl, (p, n) in total.items()))
    return 0 if all(p == n and n > 0 for p, n in total.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
