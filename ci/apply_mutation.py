#!/usr/bin/env python3
"""Вносит один засеянный дефект из ci/mutations.json в рабочую копию.

  python ci/apply_mutation.py --list          -- список мутантов (JSON-массив id, для матрицы CI)
  python ci/apply_mutation.py <id>            -- применить мутанта (код меняется на месте!)
  python ci/apply_mutation.py <id> --check    -- только проверить, что замена находится в коде

Падает с ошибкой, если строка для замены не найдена -- значит, код поменялся,
и mutations.json надо обновить (иначе "живой" мутант был бы ложным).
"""
import json
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def load():
    with open("ci/mutations.json", encoding="utf-8") as fh:
        return json.load(fh)["mutants"]


def main(argv):
    mutants = load()
    if not argv or argv[0] == "--list":
        print(json.dumps([m["id"] for m in mutants]))
        return 0

    check_all = argv[0] == "--check-all"
    targets = mutants if check_all else [m for m in mutants if m["id"] == argv[0]]
    if not targets:
        print(f"Нет мутанта '{argv[0]}'")
        return 2

    for m in targets:
        with open(m["file"], encoding="utf-8") as fh:
            src = fh.read()
        n = src.count(m["find"])
        if n != 1:
            print(f"::error::Мутант {m['id']}: строка найдена {n} раз в {m['file']} -- обнови ci/mutations.json")
            return 1
        if check_all or "--check" in argv:
            print(f"OK   {m['id']}: {m['what']}")
            continue
        with open(m["file"], "w", encoding="utf-8") as fh:
            fh.write(src.replace(m["find"], m["replace"]))
        print(f"Внесён дефект {m['id']} в {m['file']}: {m['what']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
