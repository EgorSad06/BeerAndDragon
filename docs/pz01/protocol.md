# Протокол независимого воспроизведения запуска (ПЗ-1, Р6)

Проект выполняется одним студентом. По указанию преподавателя проверку по инструкции [RUN.md](../RUN.md)
выполняет сам автор, но в **чистом окружении**: в отдельном свежем клоне вне рабочей папки, без `Library/`,
без сборок и без сгенерированных файлов, с очищенной папкой журналов. Все действия — только по инструкции.
Каждое действие, которого нет в инструкции, записывается как замечание. После исправления инструкции проверка
повторяется с начала.

| Поле | Значение |
|---|---|
| Проверяющий | <ФИО>, <группа> (автор проекта; одиночный проект — по указанию преподавателя) |
| Окружение | Windows 11 Pro 10.0.26200, PowerShell 5.1, Git 2.49.0, Git LFS 3.6.1, Python 3.14.3, .NET SDK 9.0.308 / 10.0.300, Unity 6000.3.0f1; каталог `C:\Users\Huawei\pz01-clean` (пустой) |

## Проход 1 — 2026-10-20 09:55, commit `c1536bb`
Команды из RUN.md: `git lfs install` → `git clone` → `git checkout` → `git lfs pull` → `build.ps1 -Mode Release`
(1105 с, из них импорт ассетов) → `build.ps1 -Mode Development` → шаги 4.1–4.4 → `check_component.py`.
Компонент собрался и запустился, проверка дала 3/3. Найдены замечания:

| № | Замечание | Исправление |
|---|---|---|
| 1 | Шаг 4.3: обещаны строки `[DEBUG]` при `logLevel=verbose`, но за 5 с прогона их нет | `GameConfig.Apply` пишет `[DEBUG][Config] применено: ...` (`a749d45`) |
| 2 | Сработавший в Development `Debug.Assert` есть в журнале, но `run.ps1` его не показывает (фильтр по префиксам) | сообщение `[ASSERT][Build] ...`, фильтр `run.ps1` учитывает `ASSERT` (`a749d45`) |
| 3 | Журнал сборки печатает в `options=` лишние флаги (`Il2CPP`, `StripDebugSymbols`) — у `BuildOptions` есть элементы с одинаковыми битами | флаги `Development` / `AllowDebugging` выводятся явно (`a749d45`) |
| 4 | Скрытая зависимость: средство просмотра `AsmInspect` (net8.0) требует среду выполнения .NET 8; в инструкции её нет, а на машине с одним SDK 9/10 запуск не удался бы | `RollForward=Major`, в RUN.md — .NET SDK ≥ 8 (`d1c2fb3`) |

## Проход 2 — 2026-10-20 10:42, commit `d1c2fb3` (повтор с исходного состояния)
Клон удалён, папка журналов очищена, клонирование заново. Всё по RUN.md:

| Шаг | Результат |
|---|---|
| Клон, `git lfs pull`, `HEAD` | `d1c2fb3`; `Library/` и `build/` отсутствуют |
| `build.ps1 -Mode Release` | код 0, 717 с; `[Build] build_info: commit=d1c2fb3 dirty=False`, `Development=False` |
| `build.ps1 -Mode Development` | код 0, 89 с; `Development=True AllowDebugging=True` |
| `git status --short` после сборок | пусто — сборка не меняет исходники |
| 4.1 `-version` | `BeerAndDragon 0.1.0+d1c2fb3 (Release)`, код 0 |
| 4.2 штатный запуск | `SMOKE OK`, 0 ошибок, `targetFps 60`, `commit d1c2fb3`, код 0 |
| 4.3 `test-config.json` | `[DEBUG][Config] применено: ... targetFrameRate=30`, код 0 |
| 4.4 `bad-config.json` | `[ERROR][Config] ... targetFps = sixty -- ожидается целое число 15..300`, код 2 |
| `check_component.py` | 3/3, код 0 |
| Раздел 5 (режимы) | Development: `[ASSERT][Build] Debug.Assert сработал`; Release: строки нет; неверная конфигурация — код 2 в обоих |
| Раздел 6 (состав) | Development: 110 `.pdb`, `DebuggableAttribute 0x107 [... DisableOptimizations]`, 176,6 МБ; Release: 0 `.pdb`, `0x2`, 119,9 МБ |
| Поставка вне исходников | копия `build\release` в `C:\Users\Huawei\pz01-deploy`: `-version` и `-ciSmoke 5` — код 0 |
| EditMode-тесты (`-runTests`) | 58 из 58 пройдено |
| Раздел 7 (исходное состояние) | `git checkout -- game-config.json`, удаление `report.json`; `git status` пуст |

**Итог:** по инструкции компонент собирается и запускается в чистом окружении без действий вне инструкции.
Замечаний во втором проходе нет.
