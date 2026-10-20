# Сборка и запуск компонента из чистого окружения

Инструкция рассчитана на машину, где нет ничего из проекта. Всё, что нужно, перечислено в разделе
«Предварительные условия» или ставится по шагам ниже. Команды — для PowerShell (Windows).

Компонент — игра `BeerAndDragon.exe`. Наблюдаемые свойства:

| Свойство | Как увидеть |
|---|---|
| Идентификатор версии | `-version` → `BeerAndDragon 0.1.0+<короткий SHA коммита> (Release|Development)`; по SHA находится исходный код: `git show <SHA>` |
| Конфигурация | `Assets/StreamingAssets/game-config.json` (в сборке — `BeerAndDragon_Data/StreamingAssets/`), другой файл — `-config <путь>`. Параметры: `targetFps` (15..300), `logLevel` (`info` / `verbose`) |
| Диагностика | Журнал `%USERPROFILE%\AppData\LocalLow\DefaultCompany\BeerAndDragon\logs\game_<дата>.log`: версия, окружение, аргументы, конфигурация, результат. Секретов нет |
| Результат | Код завершения: `0` — успех, `1` — ошибки во время smoke-прогона, `2` — неверная конфигурация |

## 1. Предварительные условия
| Что | Версия | Как проверить |
|---|---|---|
| Windows 10/11 x64, PowerShell 5.1+ | — | `$PSVersionTable.PSVersion` |
| Git и Git LFS | Git ≥ 2.40, LFS ≥ 3.0 | `git --version`, `git lfs version` |
| Unity Hub + **Unity 6000.3.0f1** с модулем **Windows Build Support (Mono)** | ровно эта версия | файл `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe` существует |
| Активированная лицензия Unity Personal (вход в Unity Hub) | — | Unity Hub → Preferences → Licenses: есть лицензия |
| Python | ≥ 3.10, без сторонних пакетов | `python --version` |
| .NET SDK (только для просмотра состава результата, раздел 6) | 8.0 | `dotnet --list-sdks` |
| Свободное место | ~6 ГБ (проект, `Library/`, две сборки) | — |

Unity ставится так: Unity Hub → *Installs → Install Editor → Archive → 6000.3.0f1*, в списке модулей отметить
*Windows Build Support (Mono)*. Если Unity установлен в другую папку, укажите путь параметром `-Unity` у `build.ps1`.

## 2. Получение исходного кода
```powershell
git lfs install
git clone https://github.com/EgorSad06/BeerAndDragon.git
cd BeerAndDragon
git checkout 0104-pz01-<код>-v1        # сдаваемая версия (тег); для последней версии -- main
git lfs pull                           # модели/текстуры/звуки; без этого Unity получит текстовые указатели LFS
git rev-parse --short=7 HEAD           # запомнить: этот SHA должна показать версия собранной игры
```

## 3. Сборка
```powershell
.\tools\pz01\build.ps1 -Mode Release          # -> build\release\BeerAndDragon.exe
.\tools\pz01\build.ps1 -Mode Development      # -> build\dev\BeerAndDragon.exe
```
Первая сборка импортирует ассеты и создаёт `Library/` — это 10–20 минут; повторные сборки — 1–3 минуты.
Скрипт выводит строки `[Build]` (режим, настройки, коммит, результат) и код завершения Unity (`0` — успех).
Полный журнал — `build\build-<режим>.log`. Если PowerShell запрещает скрипты:
`powershell -ExecutionPolicy Bypass -File .\tools\pz01\build.ps1 -Mode Release`.

Без скрипта (то же самое):
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -batchmode -quit -projectPath . `
  -executeMethod BuildTool.BuildRelease -buildOutput build\release -logFile build\build-Release.log
```

## 4. Запуск и ожидаемый результат
Все запуски — без окна (`-batchmode -nographics`), вывод — строки журнала и код завершения.
```powershell
# 4.1 Версия: строка BeerAndDragon 0.1.0+<SHA из шага 2> (Release), код 0
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -version

# 4.2 Штатный запуск: 5 секунд игры после загрузки сцены, отчёт в report.json, код 0
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -ciSmoke 5 -diagReport "$PWD\report.json"
Get-Content report.json                 # "errors": 0, "targetFps": 60, "commit": "<SHA>"

# 4.3 Другая конфигурация: targetFps 30 и подробный журнал -- строка [DEBUG][Config] применено: ... targetFrameRate=30
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz01\test-config.json -ciSmoke 5 -diagReport "$PWD\report.json"

# 4.4 Неверная конфигурация: [ERROR][Config] ... targetFps = sixty ..., код 2
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz01\bad-config.json
```
Обычная игра с окном: `.\build\release\BeerAndDragon.exe` (журнал — в папке из таблицы выше).

Автоматическая проверка тех же трёх условий (её же выполняет CI):
```powershell
python ci\check_component.py --exe build\release\BeerAndDragon.exe --expect-commit (git rev-parse --short=7 HEAD)
```

## 5. Режимы Development и Release на одном сценарии
```powershell
# Версия показывает режим: (Development) / (Release)
.\tools\pz01\run.ps1 -Exe build\dev\BeerAndDragon.exe -version
# Проверка assert: в Development -- строка [ASSERT][Build] Debug.Assert сработал; в Release её нет (вызов вырезан)
.\tools\pz01\run.ps1 -Exe build\dev\BeerAndDragon.exe -assertProbe -ciSmoke 3
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -assertProbe -ciSmoke 3
# Неверная конфигурация отклоняется в обоих режимах (проверка явная, не через assert): код 2
.\tools\pz01\run.ps1 -Exe build\dev\BeerAndDragon.exe -config ci\pz01\bad-config.json
```

## 6. Просмотр состава результата
```powershell
dotnet run --project tools\pz01\AsmInspect -- build\release\BeerAndDragon.exe build\release\UnityPlayer.dll build\release\BeerAndDragon_Data\Managed\Assembly-CSharp.dll
dotnet run --project tools\pz01\AsmInspect -- build\dev\BeerAndDragon_Data\Managed\Assembly-CSharp.dll
Get-ChildItem build\dev -Recurse -Filter *.pdb | Measure-Object     # отладочные символы есть только в Development
Get-Content build\dev\BeerAndDragon_Data\boot.config                # player-connection-*, wait-for-managed-debugger -- только Development
```
`AsmInspect` показывает импортируемые DLL нативных модулей и для управляемой сборки — версию, ссылки на сборки и
`DebuggableAttribute`. Флаг `DisableOptimizations` означает, что JIT не оптимизирует код (Development).

## 7. Остановка, повторный запуск, исходное состояние
- **Остановка:** запуски из шага 4 завершаются сами (`-version` — сразу, `-ciSmoke N` — через N секунд после загрузки сцены).
  Окно игры закрывается крестиком или `Alt+F4`. Зависший процесс: `Stop-Process -Name BeerAndDragon`.
- **Повторный запуск:** те же команды шага 4. Состояние между запусками не сохраняется; сохраняются только файлы
  журналов в папке `logs`.
- **Исходное состояние:**
  ```powershell
  git checkout -- Assets/StreamingAssets/game-config.json      # вернуть конфигурацию по умолчанию
  Remove-Item -Recurse -Force build, report.json -ErrorAction SilentlyContinue
  Remove-Item -Recurse -Force "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\BeerAndDragon\logs"
  ```
  Полный сброс до чистого клона: `git clean -xfd` — удаляет также `Library/`, и следующая сборка снова будет первой.

## 8. Частые проблемы
| Симптом | Причина | Что сделать |
|---|---|---|
| `build.ps1`: «Не найден Unity 6000.3.0f1» | другая версия или папка Unity | установить 6000.3.0f1 через Hub или передать `-Unity <путь к Unity.exe>` |
| В журнале сборки `No valid Unity Editor license found` | не выполнен вход в Unity Hub | войти в Hub и активировать Personal-лицензию |
| Розовые материалы, ошибки импорта FBX/PNG | не скачаны LFS-объекты | `git lfs install`, `git lfs pull` |
| Версия показывает `-dirty` | собрано с незакоммиченными изменениями | закоммитить или `git stash`, собрать заново |
| Версия показывает `unknown` | при сборке не было git в `PATH` и не задан `GITHUB_SHA` | установить Git, собрать заново |
