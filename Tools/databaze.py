# -*- coding: utf-8 -*-
"""
Z nasbíraných CSV postaví SQLite databázi.

PROČ VEDLE CSV: CSV je primární záznam a tak to zůstává — píše ho aplikace
v brýlích a nic ho nepřepisuje. Jenže na otázku „liší se hit rate mezi
podmínkami, když se počítají jen dokončené bloky" se v tabulkovém
procesoru odpovídá kopírováním sloupců mezi listy, a ta odpověď se nedá
zopakovat ani zkontrolovat. V databázi je to jeden dotaz, který se dá
otisknout do práce.

DATABÁZE JE ODVOZENINA, NE ORIGINÁL. Staví se pokaždé od nuly ze souborů,
takže se nemůže rozejít s tím, co se naměřilo — a když se smaže, nic se
neztratí. Proto se taky nikam nedopisuje: přepsat celý soubor je levnější
než hlídat, co už v něm je.

SQLite, a ne server: měří se offline v brýlích, data nosí jeden člověk
a archiv má přežít odevzdání práce. Jeden soubor, který se otevře i za pět
let bez instalace čehokoli. Turso je tentýž formát v cloudu, takže se
výsledek dá nahrát beze změny (`turso db create --from-file`).

Spuštění:
    python databaze.py                     — výchozí složka na Disku
    python databaze.py "C:\\jina\\slozka"    — jiná složka s CSV
"""

import glob
import io
import os
import re
import sqlite3
import sys

VYCHOZI_SLOZKA = r"H:\Můj disk\bakalarka\Data z brýlí"
NAZEV_DB = "bakalarka.db"


# ---------------------------------------------------------------- čtení CSV

def cti(cesta):
    """Vrátí (hlavicka, sloupce, radky). Hlavička jsou řádky s '#'."""
    hlavicka = {}
    sloupce = None
    radky = []

    # utf-8-sig: aplikace píše BOM, aby Excel nerozhodil diakritiku.
    for radek in io.open(cesta, encoding="utf-8-sig").read().splitlines():
        if not radek:
            continue
        if radek.startswith("#"):
            kus = radek.lstrip("# ").split(";", 1)
            if len(kus) == 2:
                hlavicka[kus[0].strip()] = kus[1].strip()
            continue
        if sloupce is None:
            sloupce = [s.strip() for s in radek.split(";")]
            continue
        hodnoty = radek.split(";")
        if len(hodnoty) == len(sloupce):
            radky.append(dict(zip(sloupce, hodnoty)))

    return hlavicka, sloupce, radky


def cislo(text):
    """Desetinné číslo z CSV. Bere tečku i čárku — hlavní sloupce píše
    aplikace invariantně s tečkou, ale texty v poli detail vznikají
    skládáním řetězců pod českým jazykem, takže mají čárku."""
    if text is None:
        return None
    text = text.strip().replace(",", ".")
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def cele(text):
    h = cislo(text)
    return int(h) if h is not None else None


def cas_ze_jmena(jmeno):
    """'P01_B0_Menu_SingleTask_T1_20260919_124317' -> '2026-09-19 12:43:17'."""
    m = re.search(r"_(\d{8})_(\d{6})$", jmeno)
    if not m:
        return None
    d, t = m.group(1), m.group(2)
    return "%s-%s-%s %s:%s:%s" % (d[:4], d[4:6], d[6:8], t[:2], t[2:4], t[4:6])


# ---------------------------------------------------------------- schéma

SCHEMA = """
PRAGMA foreign_keys = ON;

-- Jedna odehraná session = jeden soubor *_souhrn_*.csv.
CREATE TABLE session (
    id           INTEGER PRIMARY KEY,
    participant  TEXT    NOT NULL,
    skupina      INTEGER,
    podminka     TEXT,              -- Menu / Voice, podle bloků v session
    konec        TEXT,              -- kdy se session uzavřela
    dokoncena    INTEGER NOT NULL,  -- 0 = některý blok zůstal nedostavěný
    soubor       TEXT    NOT NULL UNIQUE
);

-- Jeden blok = jeden soubor P01_B*_*.csv. Tutoriály a nedohrané bloky
-- v tabulce ZŮSTÁVAJÍ: bez nich by nešlo zjistit, kolik pokusů se zahodilo
-- a proč, a to je věc, na kterou se u pilotu ptá každý.
CREATE TABLE blok (
    id                    INTEGER PRIMARY KEY,
    session_id            INTEGER REFERENCES session(id),
    participant           TEXT    NOT NULL,
    poradi                INTEGER,          -- -1 = tutoriál
    trenink               INTEGER NOT NULL DEFAULT 0,
    podminka              TEXT,             -- Menu / Voice
    zatez                 TEXT,             -- SingleTask / DualTask
    sablona               TEXT,
    zacatek               TEXT,
    zmereny               INTEGER NOT NULL, -- 1 = má řádek v souhrnu

    cisty_cas_s           REAL,
    postihu               INTEGER,
    postih_s              REAL,
    kroku_hotovo          INTEGER,
    kroku_celkem          INTEGER,
    spatnych_objektu      INTEGER,
    odmitnutych_pokladek  INTEGER,
    sekundarni_bezela     INTEGER,
    aktivaci              INTEGER,
    zasahu                INTEGER,
    hit_rate              REAL,
    prum_rt_s             REAL,
    predloha_na_vyzadani  INTEGER,
    odkryti               INTEGER,
    odkryti_s             REAL,
    ukonceni              TEXT,
    soubor                TEXT    NOT NULL UNIQUE
);

-- Každý řádek logu. Tady se dá dohledat, co přesně se v bloku dělo.
CREATE TABLE udalost (
    id           INTEGER PRIMARY KEY,
    blok_id      INTEGER NOT NULL REFERENCES blok(id),
    t            REAL,
    udalost      TEXT,
    krok         INTEGER,
    tvar         TEXT,
    barva        TEXT,
    velikost     TEXT,
    pos_err_m    REAL,
    rot_err_deg  REAL,
    rt_s         REAL,
    detail       TEXT
);

-- Hlasové povely rozebrané z pole detail. Vlastní tabulka proto, že
-- latence rozpoznávání je samostatná veličina: vstupuje do naměřeného
-- času hlasové podmínky, ale není to cena mluvení — je to cena modelu.
CREATE TABLE hlas (
    id            INTEGER PRIMARY KEY,
    blok_id       INTEGER NOT NULL REFERENCES blok(id),
    udalost_id    INTEGER NOT NULL REFERENCES udalost(id),
    t             REAL,
    vysledek      TEXT,     -- ObjectRequested / VoiceMisrecognized / ...
    prepis        TEXT,
    nahravka_s    REAL,     -- jak dlouhá promluva se poslala
    rozpoznani_s  REAL,     -- jak dlouho trvala odpověď modelu
    hlasitost     REAL,
    sum           REAL,
    prah          REAL,
    duvod         TEXT      -- proč se povel odmítl, když se odmítl
);

CREATE INDEX ix_blok_session ON blok(session_id);
CREATE INDEX ix_udalost_blok ON udalost(blok_id, udalost);
CREATE INDEX ix_hlas_blok    ON hlas(blok_id);

-- ---- Pohledy: to, na co se člověk ptá, bez skládání JOINů ----

-- Měřené bloky dokončených session. Přesně ta množina, ze které se
-- počítají výsledky do práce.
CREATE VIEW v_bloky AS
SELECT b.id, b.participant, s.skupina, b.podminka, b.zatez, b.sablona,
       b.poradi, b.cisty_cas_s, b.postih_s,
       b.cisty_cas_s + b.postih_s               AS celkem_s,
       b.spatnych_objektu, b.odmitnutych_pokladek,
       b.aktivaci, b.zasahu, b.hit_rate, b.prum_rt_s,
       b.odkryti, b.odkryti_s, b.ukonceni, s.konec AS session_konec
FROM blok b
JOIN session s ON s.id = b.session_id
WHERE b.zmereny = 1 AND b.trenink = 0 AND b.kroku_hotovo = b.kroku_celkem;

-- Hlavní srovnání: podmínka × zátěž.
CREATE VIEW v_srovnani AS
SELECT participant, podminka, zatez,
       COUNT(*)                  AS bloku,
       ROUND(AVG(cisty_cas_s), 1) AS cas_s,
       ROUND(AVG(hit_rate),   3) AS hit_rate,
       ROUND(AVG(prum_rt_s),  3) AS rt_s,
       ROUND(AVG(spatnych_objektu), 2) AS spatnych_objektu
FROM v_bloky
GROUP BY participant, podminka, zatez;

-- Kolik z naměřeného času hlasové podmínky spolkl rozpoznávač.
CREATE VIEW v_latence_hlasu AS
SELECT b.participant, b.poradi, b.sablona,
       COUNT(*)                         AS povelu,
       ROUND(AVG(h.rozpoznani_s), 2)    AS rozpoznani_s,
       ROUND(SUM(h.rozpoznani_s), 1)    AS rozpoznani_celkem_s,
       ROUND(b.cisty_cas_s, 1)          AS cisty_cas_s,
       ROUND(100.0 * SUM(h.rozpoznani_s) / b.cisty_cas_s, 1) AS podil_procent
FROM hlas h
JOIN blok b ON b.id = h.blok_id
WHERE b.zmereny = 1
GROUP BY b.id;
"""


# ---------------------------------------------------------------- import

SOUHRN_SLOUPCE = [
    ("cisty_cas_s", cislo), ("postihu", cele), ("postih_s", cislo),
    ("kroku_hotovo", cele), ("kroku_celkem", cele),
    ("spatnych_objektu", cele), ("odmitnutych_pokladek", cele),
    ("sekundarni_bezela", cele), ("aktivaci", cele), ("zasahu", cele),
    ("hit_rate", cislo), ("prum_rt_s", cislo),
    ("predloha_na_vyzadani", cele), ("odkryti", cele), ("odkryti_s", cislo),
]

# prepis="..." rec=1,71s rozpoznani=1,25s hlasitost=0,2213 sum=0,0013 prah=0,0090 -> Red Cube M
VZOR_HLAS = re.compile(
    r'prepis="(?P<prepis>[^"]*)"'
    r'(?:\s+rec=(?P<rec>[\d,\.]+)s)?'
    r'(?:\s+rozpoznani=(?P<rozp>[\d,\.]+)s)?'
    r'(?:\s+hlasitost=(?P<hlas>[\d,\.]+))?'
    r'(?:\s+sum=(?P<sum>[\d,\.]+))?'
    r'(?:\s+prah=(?P<prah>[\d,\.]+))?'
    r'(?:\s+duvod=(?P<duvod>.+))?')


def nacti_session(db, syrova):
    """Souhrny -> tabulka session. Vrací seznam (id, konec, participant)."""
    okna = []

    for cesta in sorted(glob.glob(os.path.join(syrova, "*_souhrn_*.csv"))):
        hlavicka, _, radky = cti(cesta)
        if not radky:
            continue

        mereno = [r for r in radky if r.get("trenink") == "0"]
        if not mereno:
            continue

        dokoncena = all(cele(r["kroku_hotovo"]) == cele(r["kroku_celkem"])
                        for r in mereno)

        kurzor = db.execute(
            "INSERT INTO session (participant, skupina, podminka, konec, dokoncena, soubor)"
            " VALUES (?,?,?,?,?,?)",
            (hlavicka.get("participant", "?"),
             cele(hlavicka.get("skupina")),
             mereno[0].get("podminka"),
             hlavicka.get("ukonceno") or cas_ze_jmena(os.path.basename(cesta)[:-4]),
             1 if dokoncena else 0,
             os.path.basename(cesta)))

        okna.append({
            "id": kurzor.lastrowid,
            "participant": hlavicka.get("participant", "?"),
            "konec": hlavicka.get("ukonceno") or "",
            "radky": radky,
        })

    return okna


def prirad_session(okna, participant, zacatek):
    """Session, do které blok spadá: nejbližší uzavření po jeho startu.

    Bloky a souhrn spolu nemají žádný odkaz — jediné, co je spojuje, je
    čas. Blok patří té session, která se uzavřela jako první PO jeho
    začátku; nedohrané pokusy tím zůstanou bez session, což je správně.
    """
    nejlepsi = None
    for o in okna:
        if o["participant"] != participant or not o["konec"] or not zacatek:
            continue
        if o["konec"] < zacatek:
            continue
        if nejlepsi is None or o["konec"] < nejlepsi["konec"]:
            nejlepsi = o
    return nejlepsi


def nacti_bloky(db, syrova, okna):
    pocet_udalosti = 0
    pocet_povelu = 0

    for cesta in sorted(glob.glob(os.path.join(syrova, "*.csv"))):
        jmeno = os.path.basename(cesta)
        if "_souhrn_" in jmeno:
            continue

        hlavicka, _, radky = cti(cesta)
        participant = hlavicka.get("participant", "?")
        poradi = cele(hlavicka.get("block"))
        zacatek = hlavicka.get("started") or cas_ze_jmena(jmeno[:-4])

        session = prirad_session(okna, participant, zacatek)

        # Řádek souhrnu pro tenhle blok. Souhrn čísluje od jedné, log od nuly.
        #
        # Jednou použitý řádek se označí. Zopakovaný blok má v souhrnu dva
        # řádky se stejným číslem i šablonou, a bez značky by se oba
        # pokusy navěsily na ten první — do analýzy by se tatáž čísla
        # dostala dvakrát a průměr by se posunul k opakovanému bloku.
        souhrn = None
        if session is not None and poradi is not None:
            for r in session["radky"]:
                if r.get("_pouzito"):
                    continue
                if cele(r.get("blok")) == poradi + 1 and r.get("sablona") == hlavicka.get("template"):
                    souhrn = r
                    r["_pouzito"] = "1"
                    break

        hodnoty = {
            "session_id": session["id"] if session and souhrn else None,
            "participant": participant,
            "poradi": poradi,
            "trenink": cele(souhrn["trenink"]) if souhrn else (1 if poradi == -1 else 0),
            "podminka": hlavicka.get("condition"),
            "zatez": hlavicka.get("load"),
            "sablona": hlavicka.get("template"),
            "zacatek": zacatek,
            "zmereny": 1 if souhrn else 0,
            "ukonceni": souhrn.get("ukonceni") if souhrn else None,
            "soubor": jmeno,
        }
        # .get, ne [] — starší souhrny mají méně sloupců než dnešní. Chybějící
        # veličina má zůstat prázdná, ne shodit celý import.
        for nazev, prevod in SOUHRN_SLOUPCE:
            hodnoty[nazev] = prevod(souhrn.get(nazev)) if souhrn else None

        sloupce = ", ".join(hodnoty)
        otazniky = ", ".join("?" * len(hodnoty))
        kurzor = db.execute("INSERT INTO blok (%s) VALUES (%s)" % (sloupce, otazniky),
                            tuple(hodnoty.values()))
        blok_id = kurzor.lastrowid

        for r in radky:
            u = db.execute(
                "INSERT INTO udalost (blok_id, t, udalost, krok, tvar, barva, velikost,"
                " pos_err_m, rot_err_deg, rt_s, detail) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                (blok_id, cislo(r.get("t")), r.get("event") or None, cele(r.get("step")),
                 r.get("shape") or None, r.get("color") or None, r.get("size") or None,
                 cislo(r.get("pos_err_m")), cislo(r.get("rot_err_deg")), cislo(r.get("rt_s")),
                 r.get("detail") or None))
            pocet_udalosti += 1

            detail = r.get("detail") or ""
            if "prepis=" not in detail:
                continue

            m = VZOR_HLAS.search(detail)
            if not m:
                continue

            duvod = m.group("duvod")
            if duvod and duvod.startswith("->"):
                duvod = None       # '-> Red Cube M' není důvod odmítnutí

            db.execute(
                "INSERT INTO hlas (blok_id, udalost_id, t, vysledek, prepis, nahravka_s,"
                " rozpoznani_s, hlasitost, sum, prah, duvod) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                (blok_id, u.lastrowid, cislo(r.get("t")), r.get("event") or None,
                 m.group("prepis"), cislo(m.group("rec")), cislo(m.group("rozp")),
                 cislo(m.group("hlas")), cislo(m.group("sum")), cislo(m.group("prah")),
                 duvod))
            pocet_povelu += 1

    return pocet_udalosti, pocet_povelu


# ---------------------------------------------------------------- MySQL

# Typy jsou skoro stejné, jen se jinak jmenují. VARCHAR s délkou je tu
# proto, že MySQL neumí indexovat TEXT bez uvedení délky — a sloupce
# se jménem souboru mají UNIQUE.
TYPY = {"INTEGER": "INT", "REAL": "DOUBLE", "TEXT": "TEXT"}


def uvozovky(hodnota):
    if hodnota is None:
        return "NULL"
    if isinstance(hodnota, (int, float)):
        return repr(hodnota)
    return "'" + str(hodnota).replace("\\", "\\\\").replace("'", "\\'") + "'"


def export_mysql(db, cil):
    """Vysype databázi jako SQL dump pro MySQL (MAMP, phpMyAdmin).

    PROČ TO TU JE A PROČ TO NENÍ HLAVNÍ CESTA: MySQL potřebuje běžící
    server. Data se sbírají offline v brýlích a archiv má přežít odevzdání
    práce, takže primární je soubor. Tohle je pro případ, kdy chce někdo
    vidět data v phpMyAdminu — obsah je stejný, jen se naimportuje.
    """
    radky = ["-- Vygenerováno z bakalarka.db, needituj ručně.",
             "SET NAMES utf8mb4;",
             "CREATE DATABASE IF NOT EXISTS bakalarka"
             " CHARACTER SET utf8mb4 COLLATE utf8mb4_czech_ci;",
             "USE bakalarka;"]

    tabulky = [r[0] for r in db.execute(
        "SELECT name FROM sqlite_master WHERE type='table'"
        " AND name NOT LIKE 'sqlite_%' ORDER BY rowid")]

    for tabulka in reversed(tabulky):
        radky.append("DROP TABLE IF EXISTS `%s`;" % tabulka)

    for tabulka in tabulky:
        sloupce = db.execute("PRAGMA table_info(%s)" % tabulka).fetchall()
        kusy = []
        for _, jmeno, typ, nenull, vychozi, pk in sloupce:
            mysql_typ = "VARCHAR(255)" if jmeno == "soubor" else TYPY.get(typ, "TEXT")
            kus = "  `%s` %s" % (jmeno, mysql_typ)
            if pk:
                kus += " AUTO_INCREMENT PRIMARY KEY"
            elif nenull:
                kus += " NOT NULL"
            kusy.append(kus)
        radky.append("CREATE TABLE `%s` (\n%s\n) ENGINE=InnoDB;" % (tabulka, ",\n".join(kusy)))

    for tabulka in tabulky:
        sloupce = [s[1] for s in db.execute("PRAGMA table_info(%s)" % tabulka)]
        nazvy = ", ".join("`%s`" % s for s in sloupce)
        for radek in db.execute("SELECT %s FROM %s" % (nazvy, tabulka)):
            radky.append("INSERT INTO `%s` (%s) VALUES (%s);"
                         % (tabulka, nazvy, ", ".join(uvozovky(h) for h in radek)))

    for jmeno, sql in db.execute(
            "SELECT name, sql FROM sqlite_master WHERE type='view' ORDER BY rowid"):
        radky.append("DROP VIEW IF EXISTS `%s`;" % jmeno)
        radky.append(sql.strip() + ";")

    io.open(cil, "w", encoding="utf-8").write("\n".join(radky) + "\n")
    return len(radky)


# ---------------------------------------------------------------- běh

def main():
    argumenty = [a for a in sys.argv[1:] if not a.startswith("--")]
    chce_mysql = "--mysql" in sys.argv
    slozka = argumenty[0] if argumenty else VYCHOZI_SLOZKA
    syrova = os.path.join(slozka, "syrova data")
    if not os.path.isdir(syrova):
        syrova = slozka
    if not os.path.isdir(syrova):
        print("Složka s daty neexistuje: " + slozka)
        return 1

    cil = os.path.join(slozka, NAZEV_DB)

    # Staví se od nuly. Zdrojem pravdy jsou CSV, takže dopisovat do staré
    # databáze by znamenalo jen šanci, že se rozejdou.
    if os.path.exists(cil):
        os.remove(cil)

    db = sqlite3.connect(cil)

    # NASTAVENÍ, KTERÉ VYŽADUJE TURSO PŘI NAHRÁNÍ SOUBORU. Tři z nich jsou
    # stejně výchozí, ale spoléhat na výchozí hodnoty knihovny znamená, že
    # se to jednou tiše změní a nahrání skončí na 400 bez vysvětlení.
    # Stránka i kódování se musí nastavit na PRÁZDNÉ databázi, po první
    # tabulce už se změnit nedají.
    db.execute("PRAGMA page_size = 4096")
    db.execute("PRAGMA auto_vacuum = 0")
    db.execute("PRAGMA encoding = 'UTF-8'")
    db.execute("PRAGMA journal_mode = WAL")

    db.executescript(SCHEMA)

    okna = nacti_session(db, syrova)
    udalosti, povely = nacti_bloky(db, syrova, okna)
    db.commit()

    bloku = db.execute("SELECT COUNT(*) FROM blok").fetchone()[0]
    mereno = db.execute("SELECT COUNT(*) FROM v_bloky").fetchone()[0]

    print("databáze: " + cil)
    print("  session      %d" % len(okna))
    print("  bloků        %d  (z toho do analýzy %d)" % (bloku, mereno))
    print("  událostí     %d" % udalosti)
    print("  hlas. povelů %d" % povely)
    print("")
    print("srovnání (pohled v_srovnani):")
    print("  %-12s %-11s %5s %8s %9s %7s" %
          ("podmínka", "zátěž", "bloků", "čas_s", "hit_rate", "rt_s"))
    for r in db.execute("SELECT podminka, zatez, bloku, cas_s, hit_rate, rt_s"
                        " FROM v_srovnani ORDER BY zatez, podminka"):
        print("  %-12s %-11s %5d %8.1f %9.3f %7.3f" % r)

    if chce_mysql:
        dump = os.path.join(slozka, "bakalarka_mysql.sql")
        print("")
        print("MySQL dump: %s (%d řádků)" % (dump, export_mysql(db, dump)))

    # Záznam ve WAL se musí složit zpátky do souboru, jinak by v .db chyběla
    # data, která leží vedle v .db-wal — a přesně ten soubor se nahrává
    # do Tursa a kopíruje na Disk.
    db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    db.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
