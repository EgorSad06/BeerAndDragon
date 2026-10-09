# Исходное состояние для проверки

Как из пустой машины получить проект, который можно собрать, запустить и проверить по [требованиям](requirements.md).

## 1. Необходимые ресурсы
| Ресурс | Версия / объём | Зачем | Где взять |
|---|---|---|---|
| Unity Editor | **6000.3.0f1** (ровно эта, см. `ProjectSettings/ProjectVersion.txt`) + модуль *Windows Build Support (Mono)* | открыть, собрать, запустить тесты | Unity Hub → Installs → Install Editor → Archive |
| Лицензия Unity | Personal (бесплатная) | редактор и CI | Unity Hub → Preferences → Licenses |
| Git + **Git LFS** | Git ≥ 2.40, LFS ≥ 3 | модели, текстуры, звуки лежат в LFS (161 файл) | <https://git-scm.com>, `git lfs install` |
| Python | ≥ 3.10, без сторонних пакетов | быстрые проверки `ci/*.py` | <https://python.org> |
| Node.js | ≥ 18, без `npm install` | (необязательно) CodeMap — карта кода | <https://nodejs.org> |
| Диск | ~3 ГБ: проект ~1 ГБ + `Library/` после импорта ~2 ГБ | | |
| Железо для игры | Windows 10/11 x64, GPU уровня GTX 1060, 8 ГБ ОЗУ | NFR-08 | |
| GitHub Actions | runner `ubuntu-latest`, Docker-образы GameCI | CI | настроено в `.github/workflows/` |

## 2. Получение исходного состояния
```bash
git lfs install
git clone https://github.com/EgorSad06/BeerAndDragon.git
cd BeerAndDragon
git checkout main
git lfs pull
```
Проверка, что клон целый (без Unity, ~10 с):
```bash
python ci/check_repo.py
python ci/lint_cs.py
python ci/apply_mutation.py --check-all
```
Ожидаемо: `ошибок: 0` во всех трёх. Затем Unity Hub → *Add project from disk* → папка `BeerAndDragon`.
Первый импорт 5–15 минут (создаётся `Library/`, она в git не хранится).

## 3. Данные
| Данные | Где лежат | Откуда |
|---|---|---|
| Уровень | `Assets/Game/Scenes/SampleScene.unity` — единственная сцена в Build Settings | в репозитории |
| Параметры предметов (лечение 35 HP, ускорение ×1.5 на 8 с, размеры стопок) | `Assets/Game/Assets/guns/Generated/Items/*.asset` (ScriptableObject `ItemDefinition`) | в репозитории; пересоздаются меню *Tools → BeerAndDragon* |
| Параметры оружия, скейта, игрока | компоненты на объектах `SampleScene` (инспектор) | в репозитории |
| Сгенерированные модели/иконки/префабы | `Assets/Game/Assets/guns/Generated/` | создаются editor-скриптами `Assets/Game/Scripts/Editor/` |
| Сторонние ассеты | `Assets/Game/Assets/*`, `Assets/Game/Animation/*` | бесплатные паки (см. п. 6), в LFS |
| Тестовые данные | создаются в коде тестов (`TestUtil.MakeItem`, новые `GameObject`) | внешних файлов не нужно |
| Засеянные дефекты | `ci/mutations.json` | в репозитории |

Игра не обращается к сети и не хранит пользовательских данных; пишет только журнал
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\BeerAndDragon\logs\game_<дата>.log`.

## 4. Конфигурация
| Что | Где | Значение по умолчанию |
|---|---|---|
| Версия Unity | `ProjectSettings/ProjectVersion.txt` | 6000.3.0f1 |
| Версии пакетов | `Packages/manifest.json` + `packages-lock.json` (зафиксированы) | URP 17.3.0, Input System 1.16.0, Test Framework 1.6.0 |
| Версия игры | `ProjectSettings/ProjectSettings.asset` → `bundleVersion` | 0.1.0 |
| Обработка ввода | *Project Settings → Player → Active Input Handling* | **Both** (старый `Input` + Input System) |
| Аргументы запуска игры | командная строка | `-diag` — подробный журнал; `-ciSmoke N -diagReport путь.json` — автопрогон для CI |
| Пороги Release Gate | `.github/workflows/release.yml` → `env` | `MAX_STARTUP_SECONDS=30`, `MAX_ERRORS=0` |
| Секреты CI | GitHub → *Settings → Secrets and variables → Actions* | `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` |

## 5. Как проверить
| Что | Как | Ожидаемый результат |
|---|---|---|
| Автотесты | Unity → *Window → General → Test Runner → EditMode → Run All* | 37 тестов зелёные |
| Ручные FR | Play в `SampleScene`, критерии из [requirements.md](requirements.md) | соответствие критериям |
| Сборка | *File → Build Profiles → Windows → Build* | `BeerAndDragon.exe` запускается |
| CI | push в рабочую ветку / PR в `main` | зелёные `checks` и `unity-tests` |
| Мутации | Actions → *PZ checks* → Run workflow → `mutation` | все мутанты убиты |
| Release Gate | `git tag v0.1.0 && git push origin v0.1.0` | GitHub Release с zip, `SHA256SUMS.txt`, SBOM, отчётом |

## 6. Ограничения безопасности
1. **Секреты только в GitHub Secrets.** Лицензия и пароль Unity не коммитятся; `gitleaks` проверяет каждый PR
   и всю историю на релизе (NFR-05). Секреты не выводятся в журнал workflow (маскируются Actions).
   PR из форков секретов не получают — Unity-тесты в них пропускаются с предупреждением.
2. **Минимальные права workflow.** `permissions: contents: read` везде (+ `checks: write` для отчёта тестов), `contents: write` только у job публикации релиза.
3. **Целостность поставки.** Релизный архив публикуется вместе с `SHA256SUMS.txt`; Release Gate проверяет суммы
   после «установки» в чистую папку (NFR-04).
4. **Зависимости зафиксированы.** `packages-lock.json` + фиксированная версия Unity; состав поставки — SBOM
   (`sbom.cdx.json`, CycloneDX 1.5) с хэшами сторонних ассетов (NFR-06).
5. **Лицензии ассетов.** Звуки `Assets/Game/Assets/Sounds/JetSet/` взяты из Jet Set Radio (© SEGA) — только для учебного
   прототипа, **не для публичного распространения**; перед публичным релизом заменить. Остальные паки — бесплатные,
   ассеты без файла лицензии подсвечиваются в `sbom.md`.
6. **Игра локальная.** Нет сети, аккаунтов и персональных данных; журнал содержит только технические сведения
   (версия, ОС, железо, события игры).
7. **Инструменты разработки локальные.** CodeMap (`tools/codemap`) слушает только `127.0.0.1` и отдаёт файлы только из папки проекта.
