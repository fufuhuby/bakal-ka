# -*- coding: utf-8 -*-
"""
Nahraje bakalarka.db do Tursa (SQLite v cloudu).

PROČ NE PŘES turso CLI: oficiální nástroj pro Windows neexistuje — poslední
vydání má binárky jen pro macOS a Linux, na Windows se jede přes WSL.
Instalovat kvůli jednomu příkazu celý linuxový subsystém je zbytečné,
protože Turso umí totéž přes HTTP API. Tenhle skript dělá přesně to, co by
udělalo `turso db create --from-file`:

    1. založí databázi se seed.type = "database_upload"
    2. vystaví k ní přístupový token
    3. pošle na ni soubor .db

CO POTŘEBUJEŠ MÍT: platform API token z <https://app.turso.tech> →
Account Settings → API Tokens → Create Token. Ulož ho do souboru
Tools/.turso-token (je v .gitignore) nebo do proměnné TURSO_API_TOKEN.
Token se nikde nevypisuje ani nezapisuje do logu.

DATABÁZE V CLOUDU SE POKAŽDÉ ZAKLÁDÁ ZNOVU. Endpoint /v1/upload umí
zapsat jen do prázdné databáze — do naplněné vrátí „database already
exists". Aktualizovat obsah tedy nejde jinak než smazat a založit.
Nevadí to: zdrojem pravdy jsou CSV, cloud je kopie.

Spuštění:
    python turso.py                — přestaví databázi v cloudu z aktuálního .db
    python turso.py --nazev pokus  — jiné jméno databáze
    python turso.py --misto aws-us-east-1  — jiná oblast (výchozí je Irsko)
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://api.turso.tech"
VYCHOZI_DB = r"H:\Můj disk\bakalarka\Data z brýlí\bakalarka.db"
VYCHOZI_NAZEV = "bakalarka"
SLOZKA = os.path.dirname(os.path.abspath(__file__))
SOUBOR_TOKENU = os.path.join(SLOZKA, ".turso-token")

# Hledá se na víc místech. Soubor bez přípony jde ve Windows založit jen
# oklikou, takže je normální, že skončí jako token.txt nebo ve složce
# token\ — a hledat ho po adresářích je otravnější než sem připsat řádek.
MISTA = [
    SOUBOR_TOKENU,
    os.path.join(SLOZKA, "turso-token.txt"),
    os.path.join(SLOZKA, "token.txt"),
    os.path.join(SLOZKA, "token", "token.txt"),
    os.path.join(SLOZKA, "token", "turso.txt"),
]


def token():
    z_prostredi = os.environ.get("TURSO_API_TOKEN", "").strip()
    if z_prostredi:
        return z_prostredi

    for cesta in MISTA:
        if not os.path.exists(cesta):
            continue
        with open(cesta, encoding="utf-8-sig") as f:
            obsah = f.read().strip()
        if obsah:
            print("token: " + os.path.relpath(cesta, SLOZKA))
            return obsah
        print("POZOR: %s existuje, ale je prázdný." % os.path.relpath(cesta, SLOZKA))

    return None


def zavolej(metoda, url, klic, telo=None, binarne=None):
    """Jedno volání API. Vrací (stav, rozparsovaná odpověď)."""
    hlavicky = {"Authorization": "Bearer " + klic}

    if binarne is not None:
        data = binarne
        hlavicky["Content-Type"] = "application/octet-stream"
        hlavicky["Content-Length"] = str(len(binarne))
    elif telo is not None:
        data = json.dumps(telo).encode("utf-8")
        hlavicky["Content-Type"] = "application/json"
    else:
        data = None

    pozadavek = urllib.request.Request(url, data=data, headers=hlavicky, method=metoda)

    try:
        with urllib.request.urlopen(pozadavek, timeout=180) as odpoved:
            syrove = odpoved.read().decode("utf-8", "replace")
            try:
                return odpoved.status, json.loads(syrove) if syrove else {}
            except ValueError:
                return odpoved.status, syrove
    except urllib.error.HTTPError as chyba:
        syrove = chyba.read().decode("utf-8", "replace")
        try:
            return chyba.code, json.loads(syrove) if syrove else {}
        except ValueError:
            return chyba.code, syrove
    except urllib.error.URLError as chyba:
        return 0, str(chyba.reason)


def main():
    argumenty = sys.argv[1:]
    nazev = VYCHOZI_NAZEV
    if "--nazev" in argumenty:
        nazev = argumenty[argumenty.index("--nazev") + 1]
    cesta = VYCHOZI_DB
    if "--db" in argumenty:
        cesta = argumenty[argumenty.index("--db") + 1]

    klic = token()
    if not klic:
        print("Chybí platform API token.")
        print("")
        print("Vezmi ho na https://app.turso.tech -> Account Settings -> API Tokens")
        print("a ulož do souboru:")
        print("    " + SOUBOR_TOKENU)
        print("(je v .gitignore, do repozitáře se nedostane)")
        return 1

    if not os.path.exists(cesta):
        print("Databáze neexistuje: " + cesta)
        print("Postav ji nejdřív: Vytvořit databázi.bat")
        return 1

    # ---- organizace ----
    stav, data = zavolej("GET", API + "/v1/organizations", klic)
    if stav != 200 or not data:
        print("Nepodařilo se načíst organizace (stav %s): %s" % (stav, data))
        return 1
    organizace = data[0]["slug"]
    print("organizace: " + organizace)

    # ---- skupina ----
    #
    # Skupina je místo, kde databáze fyzicky běží. Čerstvý účet žádnou
    # nemá, takže se musí založit — jinak vrátí založení databáze jen
    # „group not found" a není poznat, co chybí.
    #
    # IRSKO, NE AMERIKA. Data z měření mají zůstat v EU: jakmile se
    # měří s lidmi, je umístění úložiště věc, která patří do souhlasu,
    # a „někde v USA" se vysvětluje podstatně hůř.
    misto = "aws-eu-west-1"
    if "--misto" in argumenty:
        misto = argumenty[argumenty.index("--misto") + 1]

    skupiny_url = "%s/v1/organizations/%s/groups" % (API, organizace)
    stav, data = zavolej("GET", skupiny_url, klic)
    skupiny = data.get("groups", []) if isinstance(data, dict) else []

    if skupiny:
        skupina = skupiny[0]["name"]
        print("skupina:    %s (%s)" % (skupina, skupiny[0].get("primary", "?")))
    else:
        skupina = "default"
        stav, data = zavolej("POST", skupiny_url, klic,
                             {"name": skupina, "location": misto})
        if stav not in (200, 409):
            print("Nepodařilo se založit skupinu (stav %s): %s" % (stav, data))
            return 1
        print("skupina:    %s (nově založena v %s)" % (skupina, misto))

    zaklad = "%s/v1/organizations/%s/databases" % (API, organizace)

    # ---- smazání té staré ----
    #
    # MUSÍ SE MAZAT. Do databáze, která už data má, /v1/upload nezapíše —
    # vrátí „database already exists". Nahrát nový obsah tedy znamená
    # založit ji znovu. Ztratit se nemá co: cloud je kopie CSV.
    stav, _ = zavolej("DELETE", "%s/%s" % (zaklad, nazev), klic)
    if stav == 200:
        print("stará databáze v cloudu smazána")

    # ---- založení ----
    #
    # seed.type "database_upload" je jediný režim, do kterého se dá poslat
    # hotový soubor. Bez něj by endpoint /v1/upload odmítl i správný .db.
    stav, data = zavolej("POST", zaklad, klic, {
        "name": nazev,
        "group": skupina,
        "seed": {"type": "database_upload"},
    })

    if stav == 200:
        hostitel = data["database"]["Hostname"]
        print("databáze založena: " + nazev)
    elif stav == 409:
        # Mazání proběhlo, a přesto pořád existuje — na pozadí se ještě
        # neuklidila. Nahrát do ní nepůjde, takže má cenu jen počkat.
        print("Databáze %s ještě nezmizela. Spusť skript za chvíli znovu." % nazev)
        return 1
    else:
        print("Založení selhalo (stav %s): %s" % (stav, data))
        return 1

    # ---- nahrání ----
    with open(cesta, "rb") as f:
        obsah = f.read()

    print("nahrávám %.0f kB na %s ..." % (len(obsah) / 1024.0, hostitel))

    # ČERSTVĚ ZALOŽENÁ DATABÁZE CHVÍLI NEEXISTUJE. Platform API odpoví na
    # založení okamžitě, ale vlastní úložiště se rozbíhá ještě pár vteřin.
    # Do té doby vrací 404 („Namespace doesn't exist") nebo 401 („token
    # does not have the permissions") — druhé je zavádějící, protože token
    # je v pořádku, jen ještě není proti čemu ho ověřit.
    #
    # Token se proto razí ZNOVU V KAŽDÉM POKUSU. Ten vystavený dřív, než
    # databáze existovala, už platný nebude a opakovat s ním nahrání
    # donekonečna by skončilo stejnou hláškou.
    token_db = None
    stav = None

    for pokus in range(10):
        stav, data = zavolej(
            "POST", "%s/%s/auth/tokens?authorization=full-access" % (zaklad, nazev), klic)
        if stav == 200 and "jwt" in data:
            token_db = data["jwt"]
            stav, data = zavolej("POST", "https://%s/v1/upload" % hostitel,
                                 token_db, binarne=obsah)
            if stav == 200:
                break
            if stav not in (401, 404):
                print("Nahrání selhalo (stav %s): %s" % (stav, data))
                return 1

        if pokus == 0:
            print("databáze se ještě rozbíhá, čekám ...")
        time.sleep(6)

    if stav != 200 or token_db is None:
        print("Databáze se nerozběhla ani po deseti pokusech (stav %s)." % stav)
        print("Zkus skript spustit znovu — databáze v cloudu už existuje.")
        return 1

    # ---- kontrola dotazem ----
    #
    # Odpověď 200 říká jen to, že soubor dorazil. Jestli se dá číst,
    # se pozná až dotazem — a to je jediné, co nás zajímá.
    # ZKOUŠÍ SE OPAKOVANĚ. Čerstvě založená databáze chvíli neodpovídá —
    # vrací 404 „Namespace doesn't exist", protože nahrání skončilo dřív,
    # než se stihla rozběhnout. Napoprvé to vypadá jako selhání nahrání,
    # přestože soubor už dávno dorazil.
    dotaz = {"requests": [
        {"type": "execute", "stmt": {"sql":
            "SELECT (SELECT COUNT(*) FROM session), (SELECT COUNT(*) FROM blok),"
            " (SELECT COUNT(*) FROM udalost), (SELECT COUNT(*) FROM hlas)"}},
        {"type": "close"},
    ]}

    for pokus in range(6):
        stav, data = zavolej("POST", "https://%s/v2/pipeline" % hostitel, token_db, dotaz)
        if stav == 200:
            break
        if pokus == 0:
            print("databáze se ještě rozbíhá, čekám ...")
        time.sleep(5)

    if stav != 200:
        print("Nahráno, ale kontrolní dotaz selhal (stav %s): %s" % (stav, data))
        return 1

    radek = data["results"][0]["response"]["result"]["rows"][0]
    hodnoty = [bunka["value"] for bunka in radek]

    print("")
    print("hotovo: https://%s" % hostitel)
    print("  session %s | bloků %s | událostí %s | hlasových povelů %s" % tuple(hodnoty))
    print("")
    print("Data jsou vidět i v prohlížeči na https://app.turso.tech")
    return 0


if __name__ == "__main__":
    sys.exit(main())
