# Запуск собранного компонента без окна с выводом журнала и кода завершения.
#   .\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -version
#   .\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -ciSmoke 5 -diagReport report.json
#   .\tools\pz01\run.ps1 -Exe build\release\BeerAndDragon.exe -config ci\pz01\bad-config.json
# Все аргументы после -Exe передаются игре.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArgs
)

if (-not (Test-Path $Exe)) {
    Write-Error "Нет файла $Exe -- сначала собери: .\tools\pz01\build.ps1"
    exit 3
}
$log = Join-Path ([IO.Path]::GetTempPath()) ("pz01-run-" + [Guid]::NewGuid().ToString('N') + '.log')
$argsList = @('-batchmode', '-nographics', '-logFile', "`"$log`"") + ($GameArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } })

Write-Host ("> " + (Split-Path $Exe -Leaf) + " " + ($GameArgs -join ' '))
$stdout = "$log.stdout"   # служебный вывод плеера ([UnityMemory] ...) -- не показываем
$p = Start-Process -FilePath $Exe -ArgumentList $argsList -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout
Remove-Item $stdout -ErrorAction SilentlyContinue

# Строки диагностики игры ([INFO]/[WARN]/[ERROR]/[DEBUG]) и строка версии
Get-Content $log -Encoding UTF8 | Where-Object { $_ -match '^\[(INFO|WARN|ERROR|DEBUG)\]' -or $_ -match '^BeerAndDragon \d' }
Remove-Item $log -ErrorAction SilentlyContinue
Write-Host "Код завершения: $($p.ExitCode)"
exit $p.ExitCode
