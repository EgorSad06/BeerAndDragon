# CI BeerAndDragon

GitHub Actions + [GameCI](https://game.ci) (Unity в Docker). Конвейер устроен по уровням из требований курса:
быстрые проверки на каждый PR, воспроизводимая сборка с артефактом на `main`, проверки ПЗ по запросу
и Release Gate на релиз-кандидат.

```
                    ┌───────────────────────── _checks.yml (без Unity, ~1 мин) ─────────────────────────┐
PR / рабочие ветки ─┤ структура репо · статический анализ C# · актуальность мутантов · поиск секретов    │
                    └───────────────────────────────────────────────────────────────────────────────────┘
                        └─► _unity-tests.yml: компиляция Unity + EditMode-тесты (~5–10 мин с кэшем)

main ───────────────► checks ─► tests ─► _unity-build.yml (Windows) ─► артефакт на 14 дней

PZ checks (вручную) ─► mutation: baseline + по прогону на каждый засеянный дефект ─► mutation score
                    └► diagnostics: Linux-сборка, запуск с -diag, журнал и отчёт

тег vX.Y.Z ─────────► checks ─► tests ─► сборка Win + Linux ─► Release gate ─► GitHub Release
                                                               пакет · установка · smoke-замер · SBOM
```

## Соответствие требованиям

| Требование | Где реализовано |
|---|---|
| **Repository/MR checks**: быстрые проверки, сборка, быстрые тесты, lint/static analysis | `pr.yml` → `_checks.yml` + `_unity-tests.yml` |
| **Main pipeline**: воспроизводимая сборка, автотесты, build artifact | `main.yml`: фиксированная версия Unity из `ProjectVersion.txt`, `packages-lock.json`, Docker-образ GameCI → артефакт `BeerAndDragon-StandaloneWindows64-<sha>` |
| **Проверки ПЗ** по запросу (mutation / seeded defect, диагностика) | `pz-checks.yml` (Actions → *PZ checks* → *Run workflow*) |
| **Release Gate**: пакет, установка, тесты, эксплуатационное измерение, защитная мера, состав поставки | `release.yml` |
| Покрытие — не самостоятельный gate | порогов покрытия нет; качество тестов оценивается засеянными дефектами |
| **Минимальная диагностируемость** | `Assets/Game/Scripts/Diagnostics/` — журнал запуска, операций и ошибок, режим `-diag` |

## Что проверяется

### Быстрые проверки (`ci/`, работают без Unity)
| Скрипт | Что ловит |
|---|---|
| `check_repo.py` | файлы без `.meta` и `.meta`-сироты (слетают ссылки в сценах), дубли GUID после мержа, `Library/`, `Temp/`, `*.csproj` в git, файлы > 50 МБ вне LFS, битый `manifest.json`, отсутствующие сцены Build Settings |
| `lint_cs.py` | **E001** `UnityEditor` в рантайм-коде без `#if UNITY_EDITOR` (редактор работает, сборка игры падает), **E002** не-UTF-8, **E003** несбалансированный `#if`; предупреждения: имя класса ≠ имя файла, устаревшее API, `GetComponent`/`Find` в `Update`, пустые `Update`, `Debug.Log` каждый кадр |
| `apply_mutation.py --check-all` | засеянные дефекты из `mutations.json` всё ещё применимы к коду |
| gitleaks | секреты (токены, пароли, ключи) в коде и истории |

Запуск локально (нужен только Python 3):
```bash
python ci/check_repo.py
python ci/lint_cs.py
python ci/apply_mutation.py --check-all
```

### Тесты
EditMode-тесты: `Assets/Game/Tests/Editor/` (инвентарь, здоровье, оружие: разброс, падение урона, патроны; перила для грайнда; пул объектов SP-1, включая отсутствие аллокаций).
Локально: Unity → *Window → General → Test Runner → EditMode → Run All*.

### Засеянные дефекты (mutation testing)
`ci/mutations.json` — 8 мутантов (перевёрнутое падение урона, переполнение стопки, половина урона, грайнд за концом перил,
патроны без лимита, слишком широкий разброс, пул отдаёт самый новый объект, двойной возврат в пул не ловится). Для каждого: дефект вносится в код → запускаются тесты → они **должны упасть**.
Итог — *mutation score* в Summary запуска. Выживший мутант = тесты не ловят эту ошибку → нужно добавить тест.
Новый мутант: добавить запись `{id, file, find, replace, what}` в `mutations.json`.

Вручную без CI: `python ci/apply_mutation.py damage-halved`, прогнать тесты в Unity, откатить `git checkout -- <файл>`.

### Release Gate
1. Быстрые проверки + поиск секретов по всей истории.
2. EditMode-тесты.
3. Сборка Windows (поставка) и Linux (для запуска на CI) → `zip` + `SHA256SUMS.txt`.
4. **Установка**: проверка контрольных сумм, распаковка в чистую папку, проверка состава (`.exe`, `UnityPlayer.dll`, `_Data`), размер.
5. **Эксплуатационное измерение**: игра запускается headless (`-batchmode -nographics -ciSmoke 10`), пишет `smoke-report.json`:
   время загрузки сцены, кадры, FPS, число ошибок. Пороги: `MAX_STARTUP_SECONDS=30`, `MAX_ERRORS=0` (в `release.yml`).
6. **Защитная мера** (выбрана: целостность поставки и отсутствие секретов): gitleaks по всей истории + проверка SHA-256 после «установки»;
   `SHA256SUMS.txt` публикуется вместе с релизом.
7. **Состав поставки**: `ci/sbom.py` → `sbom.cdx.json` (CycloneDX 1.5: Unity, все пакеты из `packages-lock.json`, сторонние ассеты с хэшами)
   и `sbom.md` с лицензиями. Ассеты без файла лицензии подсвечиваются предупреждением.

Релиз: `git tag v0.1.0 && git push origin v0.1.0` → после прохождения gate создаётся GitHub Release с пакетами, SBOM и отчётом.
Ручной запуск (Actions → *Release Gate*) проходит все проверки, но релиз не публикует.

## Диагностика в игре
- При старте в лог пишутся версия, Unity, платформа, железо, аргументы запуска.
- Все сообщения и ошибки (со стеком) дублируются в файл `<persistentDataPath>/logs/game_<дата>.log`
  (Windows: `%USERPROFILE%\AppData\LocalLow\<Company>\BeerAndDragon\logs`).
- Существенные операции: загрузка сцен, инвентарь (добавление/использование), смерть, скейт (посадка, бэйл, комбо).
- Формат: `[INFO|WARN|ERROR|DEBUG][Область] сообщение`.
- Диагностический режим: запуск с `-diag` или **F11** в игре — подробные сообщения, FPS и память раз в ~10 с.
- `-ciSmoke N -diagReport путь.json` — автозапуск для CI: N секунд после загрузки сцены, отчёт, выход с кодом 0/1.

## Настройка (один раз)
GameCI нужна лицензия Unity. Для Personal:
1. На компьютере, где Unity уже активирован, найти файл лицензии `C:\ProgramData\Unity\Unity_lic.ulf`.
2. GitHub → репозиторий → *Settings → Secrets and variables → Actions → New repository secret*:
   - `UNITY_LICENSE` — **всё содержимое** файла `Unity_lic.ulf`;
   - `UNITY_EMAIL` — почта аккаунта Unity;
   - `UNITY_PASSWORD` — пароль аккаунта Unity.
3. *Settings → Branches* → правило для `main`: *Require status checks to pass* → отметить `Repo checks + lint + secrets` и `EditMode tests`.

Без секретов быстрые проверки работают, а Unity-тесты в PR пропускаются с предупреждением; `main` и релиз без лицензии падают.

### Ограничения бесплатного GitHub
- **Git LFS**: трафик ограничен, поэтому LFS-файлы кэшируются (`actions/cache` по списку LFS-объектов) и качаются только при изменениях.
- Минуты Actions для приватного репозитория ограничены: Unity-сборка ~15–25 мин, тесты ~5–10 мин (с кэшем `Library`).
