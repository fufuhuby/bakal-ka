# -*- coding: utf-8 -*-
"""
Položí databázi dotaz a vypíše výsledek jako tabulku.

PROČ TO TU JE: databáze je k ničemu, když se do ní nedá kouknout bez
instalace programu. Tohle běží na Pythonu, který v počítači už je —
takže se dá pracovat hned a DB Browser si nainstalovat, až bude čas.

Spuštění:
    python dotaz.py                            — co v databázi je
    python dotaz.py "SELECT * FROM v_srovnani"
    python dotaz.py v_srovnani                 — zkratka za SELECT * FROM
    python dotaz.py v_bloky --csv vysledky.csv — uloží místo výpisu
"""

import io
import os
import sqlite3
import sys

VYCHOZI_DB = r"H:\Můj disk\bakalarka\Data z brýlí\bakalarka.db"


def vypis(kurzor):
    nazvy = [d[0] for d in kurzor.description]
    radky = [["" if h is None else str(h) for h in r] for r in kurzor.fetchall()]

    # Šířka sloupce podle nejdelší hodnoty. Bez toho se čísla nedají
    # porovnat očima, což je jediné, k čemu je výpis v konzoli dobrý.
    sirky = [len(n) for n in nazvy]
    for r in radky:
        for i, h in enumerate(r):
            sirky[i] = max(sirky[i], len(h))

    print("  ".join(n.ljust(sirky[i]) for i, n in enumerate(nazvy)))
    print("  ".join("-" * s for s in sirky))
    for r in radky:
        print("  ".join(h.ljust(sirky[i]) for i, h in enumerate(r)))
    print("")
    print("%d řádků" % len(radky))


def do_csv(kurzor, cesta):
    nazvy = [d[0] for d in kurzor.description]
    radky = [";".join("" if h is None else str(h) for h in r) for r in kurzor.fetchall()]

    # Středník a BOM: stejně jako ostatní CSV v projektu, ať to Excel
    # otevře rovnou do sloupců a nerozhází diakritiku.
    io.open(cesta, "w", encoding="utf-8-sig").write(
        ";".join(nazvy) + "\n" + "\n".join(radky) + "\n")
    print("uloženo: %s  (%d řádků)" % (cesta, len(radky)))


def obsah(db):
    print("TABULKY A POHLEDY")
    for typ, jmeno in db.execute(
            "SELECT type, name FROM sqlite_master WHERE type IN ('table','view')"
            " AND name NOT LIKE 'sqlite_%' ORDER BY type DESC, rowid"):
        pocet = db.execute("SELECT COUNT(*) FROM %s" % jmeno).fetchone()[0]
        print("  %-9s %-18s %6d řádků" % (typ, jmeno, pocet))
    print("")
    print("Zkus třeba:  python dotaz.py v_srovnani")


def main():
    argumenty = sys.argv[1:]

    csv = None
    if "--csv" in argumenty:
        i = argumenty.index("--csv")
        csv = argumenty[i + 1] if i + 1 < len(argumenty) else "vysledky.csv"
        del argumenty[i:i + 2]

    cesta = VYCHOZI_DB
    if "--db" in argumenty:
        i = argumenty.index("--db")
        cesta = argumenty[i + 1]
        del argumenty[i:i + 2]

    if not os.path.exists(cesta):
        print("Databáze neexistuje: " + cesta)
        print("Postav ji nejdřív: Vytvořit databázi.bat")
        return 1

    db = sqlite3.connect(cesta)

    if not argumenty:
        obsah(db)
        return 0

    sql = " ".join(argumenty)
    # Holé jméno tabulky je zkratka. Psát SELECT * FROM pokaždé znovu
    # je přesně ta drobnost, kvůli které se do dat přestane koukat.
    if " " not in sql.strip():
        sql = "SELECT * FROM " + sql.strip()

    try:
        kurzor = db.execute(sql)
    except sqlite3.Error as chyba:
        print("Chyba dotazu: %s" % chyba)
        return 1

    if csv:
        do_csv(kurzor, csv)
    else:
        vypis(kurzor)
    return 0


if __name__ == "__main__":
    sys.exit(main())
