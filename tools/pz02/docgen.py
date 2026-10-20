#!/usr/bin/env python3
"""Справочник API модуля ObjectPool из XML-документации компилятора (ПЗ-2).

  dotnet build tools/pz02/PoolDoc
  python tools/pz02/docgen.py            # -> docs/modules/object-pool-api.md
"""
import os
import re
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "tools", "pz02", "PoolDoc", "bin", "Debug", "netstandard2.1", "PoolDoc.xml")
OUT = os.path.join(ROOT, "docs", "modules", "object-pool-api.md")
TYPES = ["IObjectPool`1", "PoolOverflow", "BoundedPool`1", "LinkedPool`1", "PoolKind", "ObjectPoolFactory"]


def pretty(name):
    return name.replace("`1", "<T>").replace("``1", "<T>").replace("{T}", "<T>")


def text(el):
    """Текст элемента документации: cref -> `Имя`, c/code -> `...`, para -> абзац."""
    if el is None:
        return ""
    out = [el.text or ""]
    for ch in el:
        if ch.tag in ("see", "seealso"):
            ref = ch.get("cref") or ch.get("langword") or ""
            out.append("`" + pretty(re.sub(r"^\w:", "", ref).split("(")[0].split(".")[-1]) + "`")
        elif ch.tag in ("paramref", "typeparamref"):
            out.append("`" + ch.get("name", "") + "`")
        elif ch.tag == "c":
            out.append("`" + "".join(ch.itertext()) + "`")
        elif ch.tag == "para":
            out.append("\n\n" + text(ch).strip() + "\n\n")
        elif ch.tag == "b":
            out.append("**" + "".join(ch.itertext()) + "**")
        elif ch.tag == "code":
            out.append("\n```csharp\n" + "".join(ch.itertext()).strip("\n") + "\n```\n")
        else:
            out.append(text(ch))
        out.append(ch.tail or "")
    return re.sub(r"[ \t]*\n[ \t]*", "\n", "".join(out)).strip()


def main():
    members = ET.parse(SRC).getroot().find("members")
    by_type = {t: [] for t in TYPES}
    for m in members:
        kind, full = m.get("name").split(":", 1)
        owner = full.split("(")[0]
        typ = owner if kind == "T" else owner.rsplit(".", 1)[0]
        if typ in by_type:
            by_type[typ].append((kind, full, m))

    lines = ["# Справочник API модуля ObjectPool", "",
             "Сгенерировано из XML-документации компилятора (`dotnet build tools/pz02/PoolDoc`, "
             "`python tools/pz02/docgen.py`). Контракт словами — [object-pool.md](object-pool.md).", ""]
    for t in TYPES:
        items = by_type[t]
        head = next((m for k, f, m in items if k == "T"), None)
        lines += [f"## `{pretty(t)}`", "", text(head.find("summary")) if head is not None else "", ""]
        if head is not None and head.find("remarks") is not None:
            lines += [text(head.find("remarks")), ""]
        if head is not None and head.find("example") is not None:
            lines += ["**Пример**", text(head.find("example")), ""]
        for kind, full, m in items:
            if kind == "T":
                continue
            name = pretty(full[len(t) + 1:]).replace("#ctor", "конструктор")
            name = re.sub(r"\(.*\)", "(…)", name)
            summ = text(m.find("summary"))
            if not summ and m.find("inheritdoc") is not None:
                summ = "см. `IObjectPool<T>`"
            lines.append(f"- **`{name}`** — {summ.replace(chr(10), ' ')}")
            for p in m.findall("param"):
                lines.append(f"  - `{p.get('name')}`: {text(p).replace(chr(10), ' ')}")
            if m.find("returns") is not None:
                lines.append(f"  - результат: {text(m.find('returns'))}")
            for e in m.findall("exception"):
                lines.append(f"  - ошибка `{pretty(e.get('cref', '')[2:].split('.')[-1])}`: {text(e)}")
        lines.append("")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    body = re.sub(r"\n{3,}", "\n\n", "\n".join(lines)).rstrip() + "\n"
    open(OUT, "w", encoding="utf-8").write(body)
    print("written", os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
