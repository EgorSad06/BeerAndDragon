#!/usr/bin/env python3
"""Проверка минимального компонента (ПЗ-1): запускает собранную игру без графики и проверяет
наблюдаемый результат. Используется в CI (.github/workflows/pz01.yml) и локально.

  python ci/check_component.py --exe build/release/BeerAndDragon.exe --expect-commit a13fe29

Проверки (любая не выполнена -> код возврата 1):
  1. -version            -> код 0, строка "BeerAndDragon X.Y.Z+<commit>", commit совпадает с ожидаемым
  2. допустимый конфиг   -> smoke-прогон, код 0, в отчёте 0 ошибок, targetFps из конфига, тот же commit
  3. неверный конфиг     -> код 2 и сообщение [ERROR][Config] в журнале
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
VERSION_RE = re.compile(r"BeerAndDragon (\d+\.\d+\.\d+)\+([0-9a-f]{7}|unknown)(-dirty)? \((Development|Release)\)")


def run(exe, args, timeout):
    """Запуск игры без окна; журнал Unity -- во временный файл. Возвращает (код, текст журнала)."""
    log = tempfile.NamedTemporaryFile(prefix="pz01-", suffix=".log", delete=False).name
    cmd = [exe, "-batchmode", "-nographics", "-logFile", log] + args
    print("$ " + " ".join(cmd))
    try:
        code = subprocess.run(cmd, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        code = "timeout"
    with open(log, encoding="utf-8", errors="replace") as f:
        text = f.read()
    os.unlink(log)
    return code, text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", required=True, help="путь к собранной игре")
    ap.add_argument("--expect-commit", required=True, help="короткий SHA, который должна показать версия")
    ap.add_argument("--config", default=os.path.join(HERE, "pz01", "test-config.json"))
    ap.add_argument("--bad-config", default=os.path.join(HERE, "pz01", "bad-config.json"))
    ap.add_argument("--smoke-seconds", default="5")
    ap.add_argument("--timeout", type=int, default=180)
    a = ap.parse_args()

    expect = a.expect_commit[:7]
    with open(a.config, encoding="utf-8") as f:
        expect_fps = json.load(f)["targetFps"]
    results = []

    def check(name, ok, detail):
        results.append((name, ok, detail))
        print(("PASS " if ok else "FAIL ") + name + " -- " + detail)

    # 1. Версия
    code, log = run(a.exe, ["-version"], a.timeout)
    m = VERSION_RE.search(log)
    check("version", code == 0 and m is not None and m.group(2) == expect,
          f"код {code}, строка: {m.group(0) if m else 'не найдена'}, ожидался commit {expect}")

    # 2. Штатный запуск с допустимой конфигурацией
    report = os.path.join(tempfile.gettempdir(), "pz01-smoke-report.json")
    if os.path.exists(report):
        os.unlink(report)
    code, log = run(a.exe, ["-config", os.path.abspath(a.config), "-ciSmoke", a.smoke_seconds, "-diagReport", report], a.timeout)
    if os.path.exists(report):
        with open(report, encoding="utf-8") as f:
            r = json.load(f)
        ok = code == 0 and r.get("errors") == 0 and r.get("targetFps") == expect_fps and r.get("commit") == expect
        check("smoke", ok, f"код {code}, ошибок {r.get('errors')}, targetFps {r.get('targetFps')} (ожидалось {expect_fps}), "
                           f"commit {r.get('commit')}, ~{r.get('avgFps')} FPS")
    else:
        check("smoke", False, f"код {code}, отчёт не создан; хвост журнала:\n" + "\n".join(log.splitlines()[-15:]))

    # 3. Неверная конфигурация
    code, log = run(a.exe, ["-config", os.path.abspath(a.bad_config)], a.timeout)
    msg = next((l for l in log.splitlines() if "[ERROR][Config]" in l), None)
    check("bad-config", code == 2 and msg is not None, f"код {code} (ожидался 2), сообщение: {msg or 'нет [ERROR][Config]'}")

    failed = [n for n, ok, _ in results if not ok]
    print(f"\nИтог: {len(results) - len(failed)}/{len(results)} проверок пройдено" + (f", провалены: {', '.join(failed)}" if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
