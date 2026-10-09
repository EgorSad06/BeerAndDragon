# BeerAndDragon

Шутер от первого лица в духе ULTRAKILL со средневековым сеттингом и юмором: рыцарь с дробовиком и мечом,
лечится сигаретами, ускоряется энергетиком и катается на скейте с трюками. Unity 6 (6000.3.0f1), URP.

## Где что лежит — навигатор по учебным требованиям

### Требования, MVP, правила, окружение
| Требование курса | Документ | Что внутри |
|---|---|---|
| Спецификация требований 0.1 (назначение, пользователи, сценарий, ошибки, FR/NFR с ID и критериями «условия → действие → результат») | [docs/requirements.md](docs/requirements.md) | разделы 1–6; ошибки `ERR-*`, функциональные `FR-*`, нефункциональные `NFR-*`; раздел 7 — трассировка «требование → тест» |
| Зафиксированный MVP (обязательное / резерв) | [docs/mvp.md](docs/mvp.md) | таблицы «Обязательно» и «Резерв», критерий готовности MVP |
| Правила репозитория: защищённая ветка, PR, автопроверки, независимое review | [docs/repo-rules.md](docs/repo-rules.md) | разделы 1, 2, 5 |
| Definition of Done | [docs/repo-rules.md#3](docs/repo-rules.md#3-definition-of-done-dod) + чек-лист в [шаблоне PR](.github/pull_request_template.md) | |
| Пример применения правил | [docs/repo-rules.md#4](docs/repo-rules.md#4-пример-применения-правил) | реальный PR `debag → main`: проверки нашли 107 битых `.meta`, исправлено до слияния |
| Исходное состояние: данные, конфигурация, ресурсы, безопасность | [docs/environment.md](docs/environment.md) | разделы 1–6 |
| Системное программирование: два направления, первый модуль, контракт | [docs/system-programming.md](docs/system-programming.md) | направления A (память) и B (параллелизм), модуль SP-1, контракт C1–C13 |
| Реализация первого модуля | [Assets/Game/Scripts/Core/BoundedPool.cs](Assets/Game/Scripts/Core/BoundedPool.cs) | пул объектов без аллокаций |
| Тесты первого модуля | [Assets/Game/Tests/Editor/BoundedPoolTests.cs](Assets/Game/Tests/Editor/BoundedPoolTests.cs) | 10 тестов, по одному на пункт контракта |

### CI (Minimal CI)
| Уровень | Файл | Когда запускается |
|---|---|---|
| Repository / PR checks | [.github/workflows/pr.yml](.github/workflows/pr.yml) → [_checks.yml](.github/workflows/_checks.yml) + [_unity-tests.yml](.github/workflows/_unity-tests.yml) | каждый PR и push в рабочие ветки |
| Main pipeline (сборка + тесты + артефакт) | [.github/workflows/main.yml](.github/workflows/main.yml) → [_unity-build.yml](.github/workflows/_unity-build.yml) | push в `main` |
| Проверки ПЗ (мутации, диагностика) | [.github/workflows/pz-checks.yml](.github/workflows/pz-checks.yml) | вручную: Actions → *PZ checks* |
| Release Gate | [.github/workflows/release.yml](.github/workflows/release.yml) | тег `vX.Y.Z` |
| Скрипты проверок (Python, без Unity) | [ci/](ci/) — `check_repo.py`, `lint_cs.py`, `apply_mutation.py`, `mutations.json`, `sbom.py` | локально и в CI |
| Описание CI и соответствие требованиям | [docs/CI.md](docs/CI.md) | |

### Код игры
```
Assets/Game/
├── Scenes/SampleScene.unity      единственный уровень (MVP)
├── Scripts/
│   ├── MainKnight/               передвижение, камера, бег по стенам           FR-MOV
│   ├── GunScript/                дробовик, меч, переключение, мишени           FR-WPN
│   ├── Player/                   здоровье, эффекты, зоны урона, анимация рыцаря FR-HP
│   ├── Items/                    инвентарь, предметы, подбор, использование    FR-INV
│   ├── Skate/                    скейт, трюки, грайнд, звуки                   FR-SK (резерв)
│   ├── UI/                       HUD: здоровье, патроны, сумка-инвентарь
│   ├── Diagnostics/              журнал, headless-прогон для CI                FR-DIAG
│   ├── Core/                     системные модули: пул объектов                SP-1
│   └── Editor/                   инструменты редактора (меню Tools → BeerAndDragon)
├── Tests/Editor/                 EditMode-тесты (37)
└── Assets/                       сторонние паки и сгенерированные ассеты (Git LFS)
tools/codemap/                    CodeMap — живая карта кода в браузере (codemap.cmd)
```

## Быстрый старт
Подробно — [docs/environment.md](docs/environment.md).
```bash
git lfs install
git clone https://github.com/EgorSad06/BeerAndDragon.git
python ci/check_repo.py
```
Unity Hub → *Add project from disk* → открыть `Assets/Game/Scenes/SampleScene.unity` → Play.

## Управление
| Клавиша | Действие |
|---|---|
| `WASD`, `Space`, `LeftShift`, `LeftControl` | ходьба, прыжок, скольжение, присед |
| `ЛКМ`, `R` | выстрел / удар, перезарядка |
| `1`–`9`, `Tab` | хотбар, сумка (перетаскивание предметов) |
| `E` | сесть на скейт / грайнд; на скейте: `Space` олли, `ЛКМ` флип, `ПКМ` грэб, `LeftControl` мануал |
| `V` | 1-е / 3-е лицо |
| `F11` | подробный журнал (FPS, память) |
