# Stáhne naměřená data z brýlí rovnou do složky na Google Disku.
#
# PROČ PŘÍMO NA DISK: data pak nemusí nikdo přenášet ručně a záloha mimo
# počítač vzniká sama. CSV v brýlích zůstává primárním záznamem — tohle
# je kopie, takže když stahování selže, nic se neztratí.

# ZÁMĚRNĚ "Continue": adb píše průběh stahování na chybový výstup, i když
# všechno proběhlo v pořádku. Při "Stop" by se skript ukončil hláškou
# o chybě přesně ve chvíli, kdy se data úspěšně stáhla.
$ErrorActionPreference = "Continue"

$cil = "H:\Můj disk\bakalarka\Data z brýlí"
$zdroj = "/sdcard/Android/data/cz.cvut.bp.mrassembly/files/BP_Data"

$adb = @(
  "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
  "C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $adb) { Write-Host "adb se nenašlo." -ForegroundColor Red; pause; exit 1 }

if (-not (Test-Path $cil)) {
  Write-Host "Cílová složka neexistuje: $cil" -ForegroundColor Red
  Write-Host "Běží Disk Google a je připojený jako H:?" -ForegroundColor Yellow
  pause; exit 1
}

$zarizeni = (& $adb devices | Select-String "device$")
if (-not $zarizeni) {
  Write-Host "Brýle nejsou připojené (nebo nemají povolené ladění přes USB)." -ForegroundColor Red
  pause; exit 1
}

$pred = (Get-ChildItem $cil -File -ErrorAction SilentlyContinue).Count
Write-Host "Stahuji z brýlí do: $cil"

# Stahuje se do dočasné složky a teprve pak se kopíruje, aby se do cíle
# nikdy nedostal soubor rozepsaný v půlce přenosu — Disk by ho začal
# synchronizovat neúplný.
$docasna = Join-Path $env:TEMP ("bryle_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Path $docasna | Out-Null

& $adb pull $zdroj $docasna | Out-Null

$stazene = Get-ChildItem (Join-Path $docasna "BP_Data") -File -ErrorAction SilentlyContinue
foreach ($f in $stazene) { Copy-Item $f.FullName -Destination $cil -Force }
Remove-Item $docasna -Recurse -Force

$po = (Get-ChildItem $cil -File).Count
Write-Host ""
Write-Host ("staženo souborů: {0}" -f $stazene.Count) -ForegroundColor Green
Write-Host ("nových: {0}   celkem ve složce: {1}" -f ($po - $pred), $po) -ForegroundColor Green
. (Join-Path $PSScriptRoot "prehled.ps1")
$pocet = Sestav-Prehled -Slozka $cil
Write-Host ("přehled sestaven: {0} session -> prehled.csv" -f $pocet) -ForegroundColor Green
Write-Host ""
Write-Host "Disk je synchronizuje sám, nic dalšího dělat nemusíš."
pause
