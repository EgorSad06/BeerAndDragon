# Сборка компонента в режиме Development или Release (Windows, PowerShell 5.1+).
#   .\tools\pz01\build.ps1 -Mode Release
#   .\tools\pz01\build.ps1 -Mode Development -Out C:\tmp\dev
# Unity берётся из стандартного пути Unity Hub; другой путь -- параметр -Unity.
param(
    [ValidateSet('Development', 'Release')][string]$Mode = 'Release',
    [string]$Out,
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe'
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path $Unity)) {
    Write-Error "Не найден Unity 6000.3.0f1: $Unity. Установи эту версию через Unity Hub (модуль Windows Build Support (Mono)) или укажи -Unity <путь>."
    exit 3
}
if (-not $Out) {
    if ($Mode -eq 'Development') { $Out = Join-Path $root 'build\dev' } else { $Out = Join-Path $root 'build\release' }
}
New-Item -ItemType Directory -Force (Join-Path $root 'build') | Out-Null
$log = Join-Path $root "build\build-$Mode.log"

Write-Host "Unity: $Unity"
Write-Host "Сборка $Mode -> $Out (журнал: $log)"
$unityArgs = @('-batchmode', '-quit', '-projectPath', "`"$root`"", '-executeMethod', "BuildTool.Build$Mode",
               '-buildOutput', "`"$Out`"", '-logFile', "`"$log`"")
$p = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow

# Существенные строки журнала: настройки режима, идентификатор, результат
Select-String -Path $log -Pattern '\[Build\]', 'error CS' | ForEach-Object { $_.Line }
Write-Host "Код завершения Unity: $($p.ExitCode)"
exit $p.ExitCode
