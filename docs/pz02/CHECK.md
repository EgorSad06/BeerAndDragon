# Проверка модуля ObjectPool по инструкции (ПЗ-2)

Для проверяющего: что запустить и что должно получиться. Предварительные условия и сборка — как в [RUN.md](../RUN.md)
(Windows, Git + LFS, Unity 6000.3.0f1, Python ≥ 3.10, .NET SDK ≥ 8). Команды — PowerShell, из корня клона.

## 1. Версия
```powershell
git clone https://github.com/EgorSad06/BeerAndDragon.git; cd BeerAndDragon
git checkout 0104-pz02-<код>-v1; git lfs pull
git rev-parse --short=7 HEAD
```

## 2. Без Unity: карта, документация, засеянные дефекты
```powershell
python ci\check_module_map.py              # «Карта соответствует коду», циклы — «объяснён в карте», код 0
dotnet build tools\pz02\PoolDoc -nologo    # модуль собирается без Unity; 0 ошибок (CS1591 = ошибка)
python tools\pz02\docgen.py; git diff --exit-code docs\modules\object-pool-api.md   # справочник актуален: пустой diff, код 0
python ci\apply_mutation.py --check-all    # все мутанты, включая pool-* и linked-*, применимы
```

## 3. Тесты контракта с обеими реализациями
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -batchmode -projectPath . -runTests `
  -testPlatform EditMode -testResults build\editmode.xml -logFile build\tests.log | Out-Null
python ci\check_test_results.py build\editmode.xml --min-total 80
python ci\pz02\results_table.py build\editmode.xml   # таблица: случай x реализация
```
Ожидается:
- все тесты пройдены, нет незагруженных наборов;
- `ObjectPoolContractTests(Bounded)` и `ObjectPoolContractTests(Linked)` по 13 тестов;
- `BulletHolesTests(Bounded)` и `BulletHolesTests(Linked)` по 4.

## 4. Подмена реализации в игре
```powershell
.\tools\pz01\build.ps1 -Mode Release
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz02\bounded-config.json -decalProbe 1000 -ciSmoke 1
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz02\linked-config.json  -decalProbe 1000 -ciSmoke 1
.\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz02\bad-pool-config.json
```
Ожидается:
- в обоих запусках строка `[INFO][Config] … decalPool=bounded|linked`;
- строка `[INFO][Probe] decalProbe: pool=BoundedPool|LinkedPool N=1000 capacity=60 created=60 active=60 recycled=1000 …`
  с одинаковыми `created`, `active`, `recycled`;
- `heap+` и `time` различаются: это свойства реализаций, а не контракта;
- код 0;
- неверная конфигурация: `[ERROR][Config] … decalPool = magic -- ожидается "bounded" или "linked"`, код 2.

Код потребителя при подмене не меняется: оба запуска — один и тот же `BeerAndDragon.exe`, различается только файл
конфигурации. В `BulletHoles.cs` нет имён `BoundedPool`, `LinkedPool`, `ObjectPoolFactory`:
`Select-String -Path Assets\Game\Scripts\GunScript\BulletHoles.cs -Pattern 'BoundedPool|LinkedPool|ObjectPoolFactory'` — пусто.
