# Z nasbíraných CSV udělá jednu přehledovou tabulku: jeden řádek = jedna
# session. Syrové soubory zůstávají, jen se uklidí do podsložky.
#
# PROČ: v jedné složce leží čtyři až pět souborů na každou session a jejich
# jména nesou jen čas. Zjistit z toho, co se kdy naměřilo a co za něco stojí,
# jde jen otevíráním jednoho po druhém.

function Sestav-Prehled {
    param([string]$Slozka)

    $syrova = Join-Path $Slozka "syrova data"
    if (-not (Test-Path $syrova)) { New-Item -ItemType Directory -Path $syrova | Out-Null }

    # Uklidit vše kromě samotného přehledu.
    Get-ChildItem $Slozka -File -Filter "*.csv" |
        Where-Object { $_.Name -ne "prehled.csv" } |
        ForEach-Object { Move-Item $_.FullName -Destination $syrova -Force }

    $radky = New-Object System.Collections.Generic.List[string]
    $radky.Add("datum;cas;participant;podminka;bloku;dokonceno;cisty_cas_s;postihy_s;celkem_s;chyby;aktivaci;zasahu;soubor")

    $souhrny = Get-ChildItem $syrova -File -Filter "*_souhrn_*.csv" | Sort-Object Name -Descending

    foreach ($f in $souhrny) {
        $participant = ""
        $podminka = ""
        $bloku = 0
        $cisty = 0.0
        $postihy = 0.0
        $chyby = 0
        $aktivaci = 0
        $zasahu = 0
        $dokonceno = "ano"

        foreach ($r in (Get-Content $f.FullName)) {
            if ($r.Length -eq 0) { continue }

            if ($r[0] -eq '#') {
                $c = $r.Split(';')
                if ($c.Length -ge 2 -and $c[0] -match "participant") { $participant = $c[1].Trim() }
                continue
            }

            $p = $r.Split(';')
            if ($p.Length -lt 17 -or $p[0] -eq "blok") { continue }
            if ($p[1] -eq "1") { continue }   # trénink se nepočítá

            if ($bloku -eq 0) { $podminka = if ($p[2] -eq "Voice") { "hlasová" } else { "klasická" } }

            $bloku++
            $cisty    += [double]($p[5] -replace ',', '.')
            $postihy  += [double]($p[7] -replace ',', '.')
            $chyby    += [int][double]($p[10] -replace ',', '.')
            $aktivaci += [int][double]($p[13] -replace ',', '.')
            $zasahu   += [int][double]($p[14] -replace ',', '.')

            # Nedohraná stavba = session nic neznamená, i když má krátký čas.
            if ([int][double]($p[8] -replace ',', '.') -lt [int][double]($p[9] -replace ',', '.')) { $dokonceno = "ne" }
        }

        if ($bloku -eq 0) { continue }

        # Jméno souboru nese datum a čas: P01_souhrn_20260918_204729.csv
        $casti = $f.BaseName.Split('_')
        $d = $casti[2]; $t = $casti[3]
        $datum = "{0}-{1}-{2}" -f $d.Substring(0,4), $d.Substring(4,2), $d.Substring(6,2)
        $cas = "{0}:{1}" -f $t.Substring(0,2), $t.Substring(2,2)

        $radky.Add(("{0};{1};{2};{3};{4};{5};{6:F1};{7:F1};{8:F1};{9};{10};{11};{12}" -f `
            $datum, $cas, $participant, $podminka, $bloku, $dokonceno,
            $cisty, $postihy, ($cisty + $postihy), $chyby, $aktivaci, $zasahu, $f.Name))
    }

    # UTF-8 S BOM: bez něj Excel i Sheets rozhází diakritiku ve sloupci podmínky.
    $cil = Join-Path $Slozka "prehled.csv"
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($cil, ($radky -join "`r`n"), $utf8Bom)

    return $radky.Count - 1
}
