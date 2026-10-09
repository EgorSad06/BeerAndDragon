#!/usr/bin/env python3
"""Лёгкий статический анализ C#-скриптов Unity (без Unity и без лицензии).

ОШИБКИ (ломают pipeline):
  E001  using UnityEditor / UnityEditor.* в рантайм-скрипте без #if UNITY_EDITOR
        -> редактор компилирует, а сборка игры падает;
  E002  файл не в UTF-8;
  E003  несбалансированные #if / #endif.
ПРЕДУПРЕЖДЕНИЯ:
  W101  имя MonoBehaviour/ScriptableObject не совпадает с именем файла
        (Unity не даст повесить такой скрипт на объект из инспектора);
  W102  устаревшее API Unity 6 (FindObjectOfType / FindObjectsOfType);
  W103  дорогие вызовы в Update/FixedUpdate/LateUpdate (GameObject.Find, Find*ObjectByType, GetComponent);
  W104  пустой Update/FixedUpdate/LateUpdate (зря тратит время каждый кадр);
  W105  Debug.Log в Update/FixedUpdate/LateUpdate (спам в лог каждый кадр).

Запуск:  python ci/lint_cs.py [--strict] [пути...]   (--strict: предупреждения тоже ломают)
По умолчанию проверяет все отслеживаемые git'ом *.cs в Assets/.
"""
import os
import re
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")  # кириллица в консоли Windows

HOT_METHODS = ("Update", "FixedUpdate", "LateUpdate")
EXPENSIVE = [
    (re.compile(r"\bGameObject\.Find\w*\s*\("), "GameObject.Find"),
    (re.compile(r"\bFind(First|Any)?Object(s)?(Of|By)Type\b"), "Find*ObjectByType"),
    (re.compile(r"\bGetComponent(InChildren|InParent|s)?\s*<"), "GetComponent"),
]

findings = []  # (level, code, path, line, message)


def add(level, code, path, line, msg):
    findings.append((level, code, path, line, msg))


def tracked_cs():
    out = subprocess.run(["git", "ls-files", "-z", "--", "Assets/*.cs", "Assets/**/*.cs"], capture_output=True, check=True).stdout
    return sorted(set(p.decode("utf-8") for p in out.split(b"\0") if p))


def is_editor_path(path):
    return "/Editor/" in path.replace("\\", "/") or path.replace("\\", "/").startswith("Editor/")


def strip_comments_and_strings(src):
    # Заменяем содержимое комментариев и строк пробелами, сохраняя переносы строк (для номеров строк)
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith("//", i):
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", src[i:j])); i = j
        elif c == '"' or (c in "@$" and i + 1 < n and src[i + 1] == '"'):
            start = i
            verbatim = c == "@"
            i += 2 if c in "@$" else 1
            while i < n:
                if src[i] == "\\" and not verbatim:
                    i += 2; continue
                if src[i] == '"':
                    if verbatim and i + 1 < n and src[i + 1] == '"':
                        i += 2; continue
                    i += 1; break
                i += 1
            out.append('"' + re.sub(r"[^\n]", " ", src[start + 1:i - 1]) + '"')
        elif c == "'" and i + 2 < n:
            j = src.find("'", i + 1 if src[i + 1] != "\\" else i + 3)
            j = i + 1 if j < 0 else j
            out.append(" " * (j + 1 - i)); i = j + 1
        else:
            out.append(c); i += 1
    return "".join(out)


def line_of(text, pos):
    return text.count("\n", 0, pos) + 1


def editor_guarded_ranges(lines):
    """Строки внутри #if UNITY_EDITOR ... #endif (грубо, без учёта #else)."""
    guarded, depth_stack = set(), []
    for idx, raw in enumerate(lines, 1):
        s = raw.strip()
        if s.startswith("#if"):
            depth_stack.append("UNITY_EDITOR" in s)
        elif s.startswith("#endif"):
            if depth_stack:
                depth_stack.pop()
        elif s.startswith("#else") and depth_stack:
            depth_stack[-1] = False
        if any(depth_stack):
            guarded.add(idx)
    return guarded


def method_bodies(code, name):
    """Позиции тел методов void <name>() { ... } (простое сопоставление скобок)."""
    for m in re.finditer(r"\bvoid\s+" + name + r"\s*\(\s*\)\s*\{", code):
        start = m.end()
        depth, i = 1, start
        while i < len(code) and depth:
            if code[i] == "{":
                depth += 1
            elif code[i] == "}":
                depth -= 1
            i += 1
        yield m.start(), start, i - 1


def lint_file(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    try:
        src = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        add("ERROR", "E002", path, 1, "файл не в UTF-8")
        return

    lines = src.splitlines()
    code = strip_comments_and_strings(src)
    editor = is_editor_path(path)

    # E003: баланс препроцессора
    opens = sum(1 for l in lines if l.strip().startswith("#if"))
    closes = sum(1 for l in lines if l.strip().startswith("#endif"))
    if opens != closes:
        add("ERROR", "E003", path, 1, f"#if: {opens}, #endif: {closes}")

    # E001: UnityEditor в рантайм-коде
    if not editor:
        guarded = editor_guarded_ranges(lines)
        for m in re.finditer(r"\busing\s+UnityEditor\b|\bUnityEditor\.\w+", code):
            ln = line_of(code, m.start())
            if ln not in guarded:
                add("ERROR", "E001", path, ln, "UnityEditor в рантайм-скрипте без #if UNITY_EDITOR -- сборка игры упадёт")

    # W101: имя класса = имя файла
    file_class = os.path.splitext(os.path.basename(path))[0]
    for m in re.finditer(r"\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([\w.]+)", code):
        cls, base = m.group(1), m.group(2)
        if base.split(".")[-1] in ("MonoBehaviour", "ScriptableObject", "NetworkBehaviour") and cls != file_class:
            add("WARN", "W101", path, line_of(code, m.start()),
                f"класс {cls} ({base}) лежит в файле {file_class}.cs -- Unity требует одинаковые имена")

    # W102: устаревшее API
    for m in re.finditer(r"\bFindObjectsOfType\b|\bFindObjectOfType\b", code):
        add("WARN", "W102", path, line_of(code, m.start()), "устаревшее API -- используйте FindFirstObjectByType / FindObjectsByType")

    # W103-W105: горячие методы
    for name in HOT_METHODS:
        for decl, start, end in method_bodies(code, name):
            body = code[start:end]
            if not body.strip():
                add("WARN", "W104", path, line_of(code, decl), f"пустой {name}()")
                continue
            for rx, label in EXPENSIVE:
                for m in rx.finditer(body):
                    add("WARN", "W103", path, line_of(code, start + m.start()), f"{label} в {name}() -- вызывается каждый кадр, лучше закэшировать")
            for m in re.finditer(r"\bDebug\.Log\s*\(", body):
                add("WARN", "W105", path, line_of(code, start + m.start()), f"Debug.Log в {name}() -- спам в лог каждый кадр")


def main(argv):
    strict = "--strict" in argv
    paths = [a for a in argv if not a.startswith("--")] or tracked_cs()
    for p in paths:
        if os.path.isfile(p):
            lint_file(p)

    gh = os.environ.get("GITHUB_ACTIONS")
    for level, code, path, line, msg in sorted(findings, key=lambda f: (f[0] != "ERROR", f[2], f[3])):
        if gh:
            kind = "error" if level == "ERROR" or strict else "warning"
            print(f"::{kind} file={path},line={line},title={code}::{msg}")
        else:
            print(f"{level:5} {code} {path}:{line}  {msg}")

    n_err = sum(1 for f in findings if f[0] == "ERROR")
    n_warn = len(findings) - n_err
    print(f"\nПроверено файлов: {len(paths)} | ошибок: {n_err} | предупреждений: {n_warn}")
    return 1 if n_err or (strict and n_warn) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
