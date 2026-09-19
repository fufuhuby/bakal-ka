# Databáze naměřených dat

CSV zůstává primárním záznamem. Databáze je **odvozenina** — staví se
pokaždé od nuly ze souborů, takže se s nimi nemůže rozejít, a když se
smaže, nic se neztratí.

```
brýle ──adb──> CSV na Disku ──> prehled.csv   (rychlý přehled do Excelu)
                           └──> bakalarka.db  (dotazy, statistika, práce)
```

## Jak ji postavit

Dvojklik na **`Vytvořit databázi.bat`**, nebo:

```bash
python Tools/databaze.py
```

Staví se i sama po každém stažení dat z brýlí (`Stáhnout data z brýlí.bat`).
Výsledek je `H:\Můj disk\bakalarka\Data z brýlí\bakalarka.db`.

## Jak ji otevřít

**Klikací cesta.** Jednou nainstalovat prohlížeč:

```
winget install DBBrowserForSQLite.DBBrowserForSQLite
```

Pak stačí dvojklik na **`Otevřít databázi.bat`** (nebo rovnou na
`bakalarka.db`). V záložce *Browse Data* se listuje po tabulkách,
v *Execute SQL* se píšou dotazy. Žádný server se nespouští.

**Bez instalace.** Python v počítači už je, takže jde ptát se hned:

```bash
python Tools/dotaz.py                            # co v databázi je
python Tools/dotaz.py v_srovnani                 # zkratka za SELECT * FROM
python Tools/dotaz.py "SELECT ... "              # libovolný dotaz
python Tools/dotaz.py v_bloky --csv vysledky.csv # do Excelu
```

## Co v ní je

| tabulka | řádek = | k čemu |
|---|---|---|
| `session` | jedna odehraná session | kdo, kdy, která podmínka, dohráno / nedohráno |
| `blok` | jeden blok včetně tutoriálů a zahozených pokusů | čas, chyby, terče, odkrytí plánku |
| `udalost` | jeden řádek logu | co přesně se v bloku dělo |
| `hlas` | jeden hlasový povel | přepis, délka nahrávky, latence rozpoznání, důvod odmítnutí |

Tutoriály a nedohrané bloky se **nemažou** — bez nich nejde zjistit, kolik
pokusů se zahodilo a proč, a na to se u pilotu ptá každý. Do analýzy je
pouští až pohled `v_bloky`.

### Pohledy

| pohled | co vrací |
|---|---|
| `v_bloky` | jen měřené, dostavěné bloky dokončených session — množina do výsledků |
| `v_srovnani` | podmínka × zátěž: počet bloků, čas, hit rate, reakční čas, chyby |
| `v_latence_hlasu` | kolik z času hlasového bloku spolkl rozpoznávač |

## Na co se hodí ptát

```sql
-- Hlavní srovnání.
SELECT * FROM v_srovnani;

-- Kolik z naměřeného času hlasové podmínky je čekání na OpenAI.
SELECT * FROM v_latence_hlasu ORDER BY podil_procent DESC;

-- Proč se hlasové povely odmítaly.
SELECT duvod, COUNT(*) AS kolik
FROM hlas WHERE duvod IS NOT NULL
GROUP BY duvod ORDER BY kolik DESC;

-- Jak dlouho trvalo objekt umístit (od vzniku po přijatou pokládku).
-- Tohle je manipulace, ne vyžádání — rozdíl mezi podmínkami by tu být
-- neměl, a když je, něco se do něj plete.
SELECT b.podminka, COUNT(*) AS objektu, ROUND(AVG(u.rt_s), 2) AS rt_s
FROM udalost u JOIN blok b ON b.id = u.blok_id
WHERE u.udalost = 'StepCompleted' AND b.zmereny = 1
GROUP BY b.podminka;

-- Které objekty se pletly.
SELECT tvar, barva, velikost, COUNT(*) AS spatne
FROM udalost WHERE udalost = 'WrongObjectCreated'
GROUP BY tvar, barva, velikost ORDER BY spatne DESC;
```

## Turso (cloud)

Turso je tentýž SQLite, jen hostovaný — soubor se nahraje beze změny.

**Oficiální `turso` CLI pro Windows neexistuje** (poslední vydání má
binárky jen pro macOS a Linux, jinak WSL). Nahrání proto obstará
`turso.py`, který dělá totéž přes HTTP API: založí databázi se
`seed.type = "database_upload"`, vystaví k ní token a pošle soubor.

Jednou: na <https://app.turso.tech> → *Account Settings* → *API Tokens* →
*Create Token*, a token uložit do `Tools/.turso-token` (je v `.gitignore`).

Pak už jen:

```bash
python Tools/turso.py
```

Skript databázi v cloudu **pokaždé smaže a založí znovu**. Není to
hrubost: `/v1/upload` umí zapsat jen do prázdné databáze, do naplněné
vrátí *„database already exists"*. Aktualizovat obsah jinak nejde — a
ztratit se nemá co, protože zdrojem pravdy jsou CSV a cloud je kopie.

Čerstvě založená databáze se pár vteřin rozbíhá a do té doby odpovídá
404 nebo 401. Skript to čeká a token si v každém pokusu razí znovu, takže
to vypadá jen jako „databáze se ještě rozbíhá, čekám ...".

Vzniklá databáze běží v **Irsku** (`aws-eu-west-1`). Jakmile se měří
s lidmi, je umístění úložiště věc do informovaného souhlasu a „někde
v USA" se vysvětluje hůř. Změnit jde přes `--misto`.

Aby šel soubor nahrát, staví ho `databaze.py` rovnou podle požadavků
Tursa: `journal_mode = WAL`, `page_size = 4096`, `auto_vacuum = 0`,
kódování UTF-8. Nic z toho se nemá měnit ručně.

## MAMP / phpMyAdmin

Když je potřeba vidět data v MySQL:

```bash
python Tools/databaze.py --mysql
```

Vznikne `bakalarka_mysql.sql`. V MAMPu se spustí MySQL, v phpMyAdminu se
zvolí **Import** a nahraje se ten soubor; databázi `bakalarka` si vytvoří
sám.

MySQL tu ale není hlavní cesta: potřebuje běžící server, data se sbírají
offline v brýlích a archiv má přežít odevzdání práce. Jeden soubor, který
se otevře i za pět let bez instalace čehokoli, je pro tenhle případ lepší.
