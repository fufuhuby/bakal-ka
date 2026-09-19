using System;
using System.Collections;
using System.Collections.Generic;
using BP.Core;
using BP.Logging;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace BP.Input
{
    /// <summary>
    /// Podmínka B — hlasové ovládání.
    ///
    /// Emituje naprosto stejné události jako menu, takže zbytek aplikace
    /// rozdíl mezi podmínkami nezná. To je jádro validity: podmínky se liší
    /// POUZE způsobem, jakým se objekt vyžádá.
    ///
    /// POSLOUCHÁ TRVALE, nemá tlačítko pro mluvení. Kdyby se muselo držet
    /// tlačítko, zaplatil by participant za hlas rukou — a hypotéza tvrdí
    /// právě to, že hlas ruce uvolní. Řeč se hledá lokálně podle hlasitosti
    /// a na server jde teprve úsek, ve kterém někdo mluvil.
    ///
    /// PROČ WHISPER A NE REALTIME API: Realtime umí vykonat příkaz dřív, než
    /// větu doříkáš. Pro aplikaci je to lepší, pro měření horší — completion
    /// time je hlavní závislá proměnná a u průběžného rozpoznávání není ostrý
    /// okamžik, kdy příkaz padl. Tady je: konec řeči, odeslání, odpověď.
    /// Doba rozpoznání se navíc měří zvlášť a jde do logu, takže se dá
    /// v analýze vykázat i odečíst.
    /// </summary>
    public class VoiceRequestSource : MonoBehaviour, IObjectRequestSource
    {
        public event Action<ObjectRequest> ObjectRequested;
        public event Action UndoRequested;

        /// <summary>Participant si řekl o plánek. Ve třetím bloku jediná cesta.</summary>
        public event Action RevealRequested;

        /// <summary>Stav pro nápovědu na panelu — co se právě děje.</summary>
        public event Action<string> StatusChanged;

        /// <summary>
        /// Naměřená čísla poslední promluvy — hlasitost, šum, práh, délka
        /// nahrávky a doba rozpoznání. Pro ladicí řádek v okně; do měření
        /// nepatří, ale při zkoušení ušetří stahování logů z brýlí.
        /// </summary>
        public event Action<string> DebugChanged;

        public InteractionCondition Condition => InteractionCondition.Voice;

        [Header("Klíč a model")]
        [Tooltip("Textový soubor s OpenAI klíčem. Patří do Resources a NESMÍ " +
                 "se commitovat.\n\nPOZOR: klíč zabalený do APK se z balíčku dá " +
                 "vytáhnout. Pro měření ve vlastních brýlích to stačí, pro " +
                 "cokoli distribuovaného musí klíč zůstat na serveru.")]
        [SerializeField] private TextAsset apiKeyFile;

        [Tooltip("Model přepisu na endpointu /audio/transcriptions. " +
                 "gpt-4o-transcribe místo staršího whisper-1: na češtinu " +
                 "komolí znatelně míň, a endpoint i pole formuláře jsou " +
                 "stejné, takže je to výměna jednoho řetězce.")]
        [SerializeField] private string model = "gpt-4o-transcribe";
        [SerializeField] private string language = "cs";

        [Header("Mikrofon")]
        [Tooltip("Část názvu mikrofonu, který se má použít. Prázdné = první " +
                 "v seznamu.\n\n" +
                 "K ČEMU TO JE: při zkoušení z editoru bere Unity první " +
                 "zařízení, což je mikrofon notebooku — jenže prahy hlasitosti " +
                 "jsou nastavené podle mikrofonu v brýlích a ty dva se chovají " +
                 "jinak. Zadáním „Oculus“ se přes Link použije mikrofon " +
                 "headsetu a zkoušení na počítači odpovídá měření.\n\n" +
                 "V brýlích je mikrofon stejně jen jeden, takže se tím nic " +
                 "nezkazí: když se název nenajde, vezme se první v seznamu.")]
        [SerializeField] private string preferredMic = "Oculus";

        [SerializeField] private int sampleRate = 16000;

        [Tooltip("Délka kruhového bufferu v sekundách. Musí bezpečně pojmout " +
                 "nejdelší promluvu i s náběhem.")]
        [SerializeField] private float ringSeconds = 10f;

        [Header("Detekce řeči")]
        [Tooltip("Nejnižší hlasitost (RMS), pod kterou se nic nepovažuje za řeč. " +
                 "Absolutní pojistka; skutečný práh se navíc odvozuje od šumu " +
                 "v místnosti — viz speechToNoise.")]
        [SerializeField] private float speechRms = 0.003f;

        [Tooltip("Kolikrát hlasitější než šum v místnosti musí zvuk být, aby " +
                 "se považoval za řeč. PROČ NESTAČÍ PEVNÝ PRÁH: tichá laboratoř " +
                 "a místnost s ventilací mají jiný šum. S pevným prahem se " +
                 "v hlučnějším prostředí spustí nahrávání samo, pošle se do " +
                 "rozpoznávání ticho — a Whisper si na tichu VYMYSLÍ text, " +
                 "protože v jeho trénovacích datech tichu odpovídaly titulky " +
                 "a adresy webů. Takový výmysl by se v datech objevil jako " +
                 "nerozpoznaný povel, který nikdo neřekl.")]
        [SerializeField] private float speechToNoise = 1.35f;

        [Tooltip("O kolik musí špička promluvy přerůst práh, aby se odeslala. " +
                 "Je to TŘETÍ práh v řadě za hlasitostí a délkou, takže se " +
                 "jeho zvýšení pozná hned: participant musí mluvit hlasitěji, " +
                 "aniž by bylo poznat proč.")]
        [Range(1f, 2f)]
        [SerializeField] private float peakMargin = 1f;

        [Tooltip("Nejnižší špička, při které se nahrávka vůbec pošle k přepisu. " +
                 "PEVNÁ HODNOTA, nezávislá na šumu.\n\n" +
                 "Odvozeno z měření 18. 9.: skutečné povely měly špičku 0,05 " +
                 "a výš, výmysly z ticha 0,003 až 0,016. Hranice uprostřed " +
                 "je rozdělí s rezervou na obě strany.\n\n" +
                 "Zvýšit = participant musí mluvit hlasitěji. Snížit = model " +
                 "dostává ticho a vymýšlí si na něm povely, které nikdo neřekl.")]
        [SerializeField] private float minSpeechPeak = 0.02f;

        [Tooltip("Jak dlouhé ticho ukončí promluvu. POZOR: je to zároveň " +
                 "nejdelší pauza, jakou si participant může dovolit MEZI SLOVY. " +
                 "Při 0,45 s se „velká červená kostka\" rozpadla na tři kusy " +
                 "a musel mluvit jako kulomet.\n\n" +
                 "0,75 s nestačilo ani při klidném tempu: v měření 18. 9. se " +
                 "„oranžový osmistěn\" rozpadl na „Oranžový\" (chybí tvar) " +
                 "a „osmistěn\" (chybí barva). 1,1 s dává nad tou pauzou " +
                 "rezervu.\n\n" +
                 "CENA: o tuhle dobu se každý povel odešle později, a protože " +
                 "se sem započítává, PRODLUŽUJE naměřený čas hlasové podmínky. " +
                 "Rozpadlá věta ale stojí celé zopakování povelu, takže je to " +
                 "pořád výhodná výměna — jen se o ní musí vědět.")]
        [SerializeField] private float silenceToEnd = 1.1f;

        [Tooltip("Kolik zvuku před začátkem řeči se přibalí. Bez náběhu chybí " +
                 "první hláska a z „kostka“ zbude „ostka“.")]
        [SerializeField] private float preroll = 0.25f;

        [Tooltip("Kratší promluva se zahodí bez odeslání — je to nádech, " +
                 "zakašlání nebo rána do stolu, ne příkaz.")]
        [SerializeField] private float minSpeechSeconds = 0.25f;

        [Tooltip("Delší promluva se zahodí bez odeslání. POZOR: čas běží " +
                 "i během pauz mezi slovy, takže tři slova se dvěma nádechy " +
                 "se do tří sekund nevešla a celý povel zmizel — vypadalo to, " +
                 "že mikrofon neslyšel.")]
        [SerializeField] private float maxSpeechSeconds = 5f;

        [Header("Hlídač mikrofonu")]
        [Tooltip("Po kolika sekundách úplného ticha se zkusí jiné zařízení. " +
                 "Vybraný mikrofon totiž nemusí nic posílat — a hlasová " +
                 "podmínka pak tiše umře, aniž by v logu zůstal řádek.")]
        [SerializeField] private float tichoNezPrepnout = 4f;

        [Tooltip("Pod touhle hlasitostí se zvuk považuje za digitální ticho, " +
                 "ne za tichou místnost. Měřeno 19. 9.: mikrofon notebooku " +
                 "dával v klidu 0,0043, mrtvé zařízení přes Link 0,000014.")]
        [SerializeField] private float umrleTicho = 0.0002f;

        [Header("Ladění")]
        [SerializeField] private bool logToConsole = true;

        // ---- Stav ----

        private string _device;
        private AudioClip _ring;
        private int _readPos;
        private bool _inputEnabled;

        /// <summary>
        /// Řeší tenhle blok velikosti?
        ///
        /// MUSÍ TO TU BÝT, protože menu to dělá taky (MenuRequestSource
        /// .SetRequiresSize). V bloku bez velikostí menu vždycky pošle
        /// výchozí velikost, i kdyby si ji participant nějak vybral. Kdyby
        /// hlas místo toho poslal to, co zaznělo, stačilo by v bloku bez
        /// velikostí říct „velká modrá kostka" a vznikl by objekt velikosti L
        /// proti kroku M — tedy chyba, která v menu nemůže nastat. Rozdíl
        /// mezi podmínkami by pak zčásti měřil tohle, ne cenu ovládání.
        /// </summary>
        private bool _requiresSize;

        private bool _speaking;
        private int _speechStart;
        private float _silenceFor;
        private float _speechFor;
        private float _noiseFloor = 0.005f;
        private float _speechPeak;

        private bool _requestInFlight;
        private string _apiKey;
        private TrialLogger _logger;
        private float[] _chunk;

        // ---- Hlídač hluchého mikrofonu ----
        //
        // PROČ TO MUSÍ EXISTOVAT: mikrofon se vybírá podle názvu, jenže název
        // neříká nic o tom, jestli zařízení opravdu něco posílá. Při zkoušení
        // z editoru přes Link se hlásí „Headset Microphone (Oculus Virtual
        // Audio Device)", které vrací digitální ticho — měřeno 19. 9.: špička
        // 0,00003 proti 0,074 z mikrofonu notebooku. Hlasová podmínka tím
        // tiše umře: nic se nerozpozná a v logu nezůstane ani řádek, takže to
        // vypadá, že „hlas prostě nefunguje".
        //
        // Hlídač proto nevěří jménu, ale signálu: když z vybraného zařízení
        // několik vteřin nepřijde vůbec nic, zkusí další v pořadí.
        //
        // TICHO SE SČÍTÁ PO SNÍMCÍCH, neporovnává se razítko času. Když je
        // aplikace na pozadí — sundané brýle, nezaostřený editor — snímky se
        // nevykonávají, ale hodiny běží dál. Razítko by po návratu ukázalo
        // desítky vteřin ticha a hlídač by prohlásil za mrtvý mikrofon, který
        // je v pořádku. Nevykonaný snímek do ticha nepřispěje ničím.
        private float _ticho;
        private readonly HashSet<string> _vyzkousene = new HashSet<string>();
        private bool _hledaniVzdano;
        private int _koloHledani;

        /// <summary>Je vstup připravený? Když ne, hlasová podmínka nepojede.</summary>
        public bool IsReady => _ring != null && !string.IsNullOrEmpty(_apiKey);

        /// <summary>Logger běžícího bloku. Nastavuje TrialManager.</summary>
        public void SetLogger(TrialLogger logger) => _logger = logger;

        private void Awake()
        {
            _apiKey = apiKeyFile != null ? apiKeyFile.text.Trim() : "";

            if (string.IsNullOrEmpty(_apiKey))
                Debug.LogError("[Voice] Chybí OpenAI klíč — přiřaď apiKeyFile. "
                               + "Hlasová podmínka bez něj nepojede.", this);
        }

        private void OnDisable()
        {
            StopMic();
        }

        // ---- Zapínání ----

        /// <summary>
        /// Zapne nebo vypne PŘIJÍMÁNÍ povelů. Mikrofon přitom běží dál.
        ///
        /// PROČ SE MIKROFON NEVYPÍNÁ: Microphone.Start zabere na Questu
        /// znatelnou chvíli a při vypínání a zapínání mezi bloky to pokaždé
        /// zadrhlo o pár snímků — systém na to reaguje přesýpacími hodinami.
        /// Vypnutý příjem stačí: Update se hned vrátí a nahrávka se nikam
        /// neposílá, takže se mimo blok nic nesleduje ani neodesílá.
        ///
        /// Mikrofon se doopravdy zastaví až v OnDisable, tedy při ukončení
        /// aplikace nebo vypnutí komponenty.
        /// </summary>
        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;

            if (enabled)
            {
                // KAŽDÝ BLOK ZAČÍNÁ S ČISTOU TABULÍ. Zařízení, které mlčelo
                // posledně, mohlo mezitím naskočit; a kdyby si hlídač nesl
                // „vzdáno" z minulého bloku, druhá půlka session by jela
                // bez hlasu, aniž by to kdokoli poznal.
                _vyzkousene.Clear();
                _koloHledani = 0;
                _hledaniVzdano = false;

                StartMic();
                Dosynchronizovat();

                // KTERÝ MIKROFON BLOK POUŽIL, PATŘÍ DO KAŽDÉHO LOGU. Vybírá se
                // při startu aplikace, kdy ještě žádný logger neexistuje —
                // bez tohohle řádku by v datech nebylo, na čem session jela,
                // a hluchý mikrofon by se poznal až podle toho, že celý blok
                // neobsahuje jediný povel.
                Zaloguj(LogEvent.Note, "mikrofon=\"" + _device + "\"");
            }
            else
            {
                // Rozdělaná promluva se zahodí, ať se nedopošle do dalšího bloku.
                _speaking = false;
                _cekaNaOdeslani = null;
                Hlaska("");
            }
        }

        /// <summary>
        /// Rozběhne mikrofon, ale povely zatím nepřijímá.
        ///
        /// VOLÁ SE PŘI STARTU APLIKACE. Microphone.Start zabere na Questu
        /// znatelnou chvíli a systém na vynechané snímky reaguje přesýpacími
        /// hodinami. Když se to odbude hned po načtení, nikoho to neruší;
        /// při prvním hlasovém bloku nebo tutoriálu už to vypadá, jako by
        /// se aplikace zasekla zrovna ve chvíli, kdy se má začít.
        /// </summary>
        public void WarmUpMic()
        {
            StartMic();
            _inputEnabled = false;
        }

        /// <summary>
        /// Srovná čtecí místo se skutečnou pozicí nahrávání a rozjede hlídač.
        ///
        /// MIKROFON BĚŽÍ OD STARTU APLIKACE, ale čte se z něj jen během bloku.
        /// Mezitím ho smyčka přepsala i několikrát dokola, takže čtecí místo
        /// ukazuje na několik minut starý zvuk. Bez tohohle by se na začátku
        /// bloku zpracoval jedním rázem celý kruh staré nahrávky — a začátek
        /// prvního povelu by v něm utonul.
        /// </summary>
        private void Dosynchronizovat()
        {
            if (_ring == null || string.IsNullOrEmpty(_device)) return;

            var pos = Microphone.GetPosition(_device);
            _readPos = pos < 0 ? 0 : pos;

            _speaking = false;
            _silenceFor = 0f;
            _speechFor = 0f;
            _ticho = 0f;
        }

        /// <summary>Protějšek MenuRequestSource.SetRequiresSize. Volá TrialManager.</summary>
        public void SetRequiresSize(bool value) => _requiresSize = value;

        /// <summary>
        /// Co je právě povoleno. Protějšek MenuRequestSource.SetAllowedParts,
        /// který používá tutoriál.
        ///
        /// MUSÍ TO TU BÝT. V klasickém tutoriálu se zamyká menu, takže krok
        /// „vyber barvu" opravdu nepustí nic než barvu. U hlasu se nezamykalo
        /// nic, takže se dala celá stavba nadiktovat naráz a průvodce se
        /// přeskočil — jedna podmínka měla nácvik vedený, druhá ne, a přesně
        /// tomu má hlasový tutoriál zabránit.
        /// </summary>
        public void SetAllowedParts(MenuPart parts) => _allowed = parts;

        private MenuPart _allowed = MenuPart.All;

        private void StartMic()
        {
            if (_ring != null) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                // Dialog je asynchronní. Mikrofon se rozjede až příště —
                // proto se o povolení žádá i při startu bloku, ne jednou.
                Permission.RequestUserPermission(Permission.Microphone);
                Debug.LogWarning("[Voice] Žádám o povolení mikrofonu.");
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[Voice] Není žádný mikrofon.", this);
                return;
            }

            _device = VybratMikrofon();

            // KTERÝ MIKROFON SE POUŽIL, PATŘÍ DO LOGU. Unity si drží vlastní
            // pořadí zařízení a to se změní připojením sluchátek — session
            // by pak běžela na jiném mikrofonu, než sis myslel, a naměřené
            // hlasitosti by se nedaly srovnávat s ostatními.
            Debug.Log("[Voice] Mikrofon: " + _device);
            Zaloguj(LogEvent.Note, "mikrofon=\"" + _device + "\"");

            // Smyčkový záznam: mikrofon běží pořád a přepisuje kruhový buffer.
            // Bez smyčky by se po vyčerpání délky sám zastavil a poslouchání
            // by tiše skončilo uprostřed bloku.
            _ring = Microphone.Start(_device, true, Mathf.CeilToInt(ringSeconds), sampleRate);

            _readPos = 0;
            _speaking = false;
            _silenceFor = 0f;
            _speechFor = 0f;
            _ticho = 0f;

            if (logToConsole) Debug.Log("[Voice] Poslouchám (" + _device + ").");
            Hlaska("poslouchám");
        }

        private void StopMic()
        {
            if (_ring == null) return;

            Microphone.End(_device);
            _ring = null;
            _speaking = false;

            Hlaska("");
        }

        // ---- Detekce řeči ----

        private void Update()
        {
            if (!_inputEnabled) return;

            // HLÍDAČ BĚŽÍ JEŠTĚ PŘED ČTENÍM. Doopravdy mrtvé zařízení neposílá
            // ani jeden vzorek, takže se čtení níž vrátí hned na „žádné nové
            // vzorky" — a kdyby hlídač seděl až za tím, u toho nejhoršího
            // případu by se nikdy nespustil.
            HlidatMikrofon();

            if (_ring == null) return;

            // POZOR: NEKONČIT TU, KDYŽ BĚŽÍ ROZPOZNÁVÁNÍ. Dřív se při čekání
            // na odpověď mikrofon vůbec nečetl, takže se všechno vyslovené
            // během té sekundy ztratilo — participant měl dojem, že slovo
            // „nebylo slyšet", a napodruhé už prošlo. Navíc se nehýbalo
            // čtecí místo v kruhovém bufferu, takže se po návratu četl
            // kus staré nahrávky.
            //
            // Čte se tedy pořád; jen se odeslání odloží (viz _cekaNaOdeslani).

            var pos = Microphone.GetPosition(_device);
            if (pos < 0) return;

            var delka = _ring.samples;
            var novych = pos - _readPos;
            if (novych < 0) novych += delka;      // buffer se přetočil
            if (novych <= 0) return;

            // Zpracovává se po dávkách; dávka menší než pár milisekund nemá
            // dost vzorků na stabilní RMS.
            var minDavka = sampleRate / 50;        // 20 ms
            if (novych < minDavka) return;

            if (_chunk == null || _chunk.Length < novych) _chunk = new float[novych];
            CtiKruh(_readPos, novych, _chunk);
            _readPos = pos;

            var rms = Rms(_chunk, novych);
            var dt = novych / (float)sampleRate;

            // Živý mikrofon dává i v tiché místnosti hlasitost v řádu tisícin;
            // mrtvý vrací nuly. Mezi tím je propast dvou řádů, takže se
            // časovač nedá omylem osvěžit samotným šumem elektroniky.
            if (rms > umrleTicho) _ticho = 0f;

            // Šum se odhaduje jen z ticha, a pomalu. Kdyby se počítal i během
            // řeči, vytáhl by si práh nahoru a usekl konec věty.
            if (!_speaking) _noiseFloor = Mathf.Lerp(_noiseFloor, rms, 0.05f);

            var prah = Mathf.Max(speechRms, _noiseFloor * speechToNoise);

            if (rms >= prah)
            {
                if (!_speaking)
                {
                    _speaking = true;
                    _speechFor = 0f;
                    _speechPeak = 0f;

                    // Náběh: začátek se posune dozadu, aby nechyběla první hláska.
                    var zpet = Mathf.RoundToInt(preroll * sampleRate);
                    _speechStart = _readPos - novych - zpet;
                    if (_speechStart < 0) _speechStart += delka;
                }

                _silenceFor = 0f;
                _speechFor += dt;
                _speechPeak = Mathf.Max(_speechPeak, rms);

                if (_speechFor >= maxSpeechSeconds) ZahoditPromluvu("prilis dlouhe");
                return;
            }

            if (!_speaking) return;

            _silenceFor += dt;
            _speechFor += dt;

            if (_speechFor >= maxSpeechSeconds) { ZahoditPromluvu("prilis dlouhe"); return; }
            if (_silenceFor >= silenceToEnd) UkoncitPromluvu();
        }

        /// <summary>
        /// Hlídá, jestli z mikrofonu vůbec něco chodí, a když ne, zkusí jiný.
        ///
        /// Mlčení participanta se za poruchu nepovažuje: časovač osvěžuje
        /// jakýkoli zvuk nad hranicí digitálního ticha, a tu překročí i tichá
        /// místnost. Sem se tedy dojde jen tehdy, když zařízení neposílá
        /// doslova nic.
        /// </summary>
        private void HlidatMikrofon()
        {
            _ticho += Time.unscaledDeltaTime;

            if (_hledaniVzdano) return;
            if (_ticho < tichoNezPrepnout) return;

            ZkusitJinyMikrofon();
        }

        /// <summary>
        /// Přepne na další nevyzkoušené zařízení. Když dojdou, přestane se
        /// zkoušet — přepínat pořád dokola by bylo horší než zůstat u jednoho.
        /// </summary>
        private void ZkusitJinyMikrofon()
        {
            if (!string.IsNullOrEmpty(_device)) _vyzkousene.Add(_device);

            foreach (var dalsi in Microphone.devices)
            {
                if (dalsi == null || _vyzkousene.Contains(dalsi)) continue;

                Debug.LogWarning("[Voice] Mikrofon \"" + _device + "\" nedává signál — "
                                 + "přepínám na \"" + dalsi + "\".", this);
                Zaloguj(LogEvent.Note, "mikrofon \"" + _device + "\" bez signalu, prepinam na \""
                                       + dalsi + "\"");

                StopMic();
                _device = dalsi;
                _ring = Microphone.Start(_device, true, Mathf.CeilToInt(ringSeconds), sampleRate);

                // Časovač se posouvá tak jako tak. Kdyby se posunul jen při
                // úspěchu, další zařízení by se zkusilo hned v tomtéž snímku
                // a celý seznam by se protočil dřív, než kterékoli z nich
                // stihne poslat první vzorek.
                _ticho = 0f;

                if (_ring == null)
                {
                    _vyzkousene.Add(dalsi);
                    continue;
                }

                Dosynchronizovat();

                Debug.Log("[Voice] Mikrofon: " + _device);
                Zaloguj(LogEvent.Note, "mikrofon=\"" + _device + "\"");
                Hlaska("poslouchám");
                return;
            }

            // SEZNAM DOŠEL. Na Questu je ale zařízení jediné, takže „došel"
            // znamená jen tolik, že se to samé zkusí znovu — restart zaseknutého
            // mikrofonu je přesně to, co je potřeba, a zahodit hlasovou podmínku
            // na celý zbytek session kvůli jednomu líně naskakujícímu zařízení
            // by byla podstatně horší chyba.
            //
            // Do nekonečna se to zkoušet nedá: Microphone.Start na Questu zadrhne
            // o pár snímků a opakovat to každé čtyři vteřiny by z aplikace udělalo
            // trhavou přehlídku přesýpacích hodin. Tři kola stačí.
            _koloHledani++;

            if (_koloHledani < 3)
            {
                _vyzkousene.Clear();
                _ticho = 0f;

                Debug.LogWarning("[Voice] Mikrofon mlčí — zkouším ho nastartovat znovu ("
                                 + _koloHledani + ". kolo).", this);

                StopMic();
                StartMic();
                Dosynchronizovat();
                return;
            }

            _hledaniVzdano = true;

            Debug.LogError("[Voice] Žádný mikrofon nic neposílá. Zkoušeno: "
                           + string.Join(", ", Microphone.devices) + ".", this);
            Zaloguj(LogEvent.Note, "POZOR: zadny mikrofon nedava signal");
            Hlaska("mikrofon nic neslyší");
        }

        /// <summary>
        /// Zahodí rozepsanou promluvu bez odeslání. Šum ani hovor v místnosti
        /// nemá cenu posílat do rozpoznávání — stálo by to peníze a vrátilo by
        /// se něco, co se stejně odmítne.
        /// </summary>
        private void ZahoditPromluvu(string duvod)
        {
            _speaking = false;
            _silenceFor = 0f;

            if (logToConsole) Debug.Log("[Voice] Zahozeno (" + duvod + ").");
        }

        private void UkoncitPromluvu()
        {
            _speaking = false;

            var delka = _ring.samples;
            var pocet = _readPos - _speechStart;
            if (pocet < 0) pocet += delka;

            var sekundy = pocet / (float)sampleRate;

            if (sekundy < minSpeechSeconds)
            {
                if (logToConsole) Debug.Log("[Voice] Promluva "
                    + sekundy.ToString("F2") + " s — moc krátká, nezasílám.");
                return;
            }

            // Druhá pojistka: úsek musel někde výrazně přerůst šum, ne jen
            // přeškrábnout práh. Bez ní projde pomalé zesílení ventilace.
            //
            // BERE SE PŘÍSNĚJŠÍ ZE DVOU MĚŘÍTEK — pevné a relativní k šumu.
            // Relativní samo nestačí: v tiché místnosti je šum skoro nula,
            // takže jeho násobek vyjde pod pevným prahem a pojistka se tím
            // fakticky vypne. Přesně to se stalo 18. 9., když jsem srazil
            // peakMargin na 1,0 — prošel dech a model z něj složil „zelená
            // koule". Pevná hodnota drží spodní hranici bez ohledu na ticho.
            var minSpicka = Mathf.Max(minSpeechPeak,
                Mathf.Max(speechRms, _noiseFloor * speechToNoise) * peakMargin);

            if (_speechPeak < minSpicka)
            {
                if (logToConsole) Debug.Log("[Voice] Spicka " + _speechPeak.ToString("F4")
                    + " pod " + minSpicka.ToString("F4") + " — vypada to na hluk, nezasilam.");
                return;
            }

            var vzorky = new float[pocet];
            CtiKruh(_speechStart, pocet, vzorky);

            var wav = Wav(vzorky, sampleRate);

            // JEDNOMÍSTNÁ FRONTA. Souběžné dotazy by se daly poslat taky,
            // jenže odpovědi můžou dorazit v jiném pořadí, než v jakém se
            // mluvilo — „zpět" by se pak vyhodnotilo dřív než objekt, který
            // má vrátit. Čeká se tedy na doběhnutí a promluva se odešle hned
            // potom. Delší fronta smysl nedává: kdo mluví rychleji, než se
            // stíhá rozpoznávat, stejně nesleduje, co vzniká.
            if (_requestInFlight)
            {
                _cekaNaOdeslani = wav;
                _cekaDelka = sekundy;
                _cekaSpicka = _speechPeak;
                _cekaSum = _noiseFloor;
                if (logToConsole) Debug.Log("[Voice] Rozpoznavani bezi, promluva ceka ve fronte.");
                return;
            }

            Odeslat(wav, sekundy, _speechPeak, _noiseFloor);
        }

        private void Odeslat(byte[] wav, float sekundy, float spicka, float sum)
        {
            _requestInFlight = true;
            Hlaska("rozpoznávám…");

            StartCoroutine(Poslat(wav, sekundy, spicka, sum));
        }

        // HLASITOST SE VEZE S NAHRÁVKOU, nečte se při zpracování odpovědi.
        // Odložená promluva se odesílá až po doběhnutí té předchozí a mezitím
        // už _speechPeak patří někomu jinému — v datech se pak šestkrát po
        // sobě objevila identická hlasitost 0,0047 u různých povelů.
        private byte[] _cekaNaOdeslani;
        private float _cekaDelka;
        private float _cekaSpicka;
        private float _cekaSum;

        // ---- Whisper ----

        private IEnumerator Poslat(byte[] wav, float delkaPromluvy, float spicka, float sum)
        {
            var zacatek = Time.realtimeSinceStartup;

            var form = new WWWForm();
            form.AddBinaryData("file", wav, "prikaz.wav", "audio/wav");
            form.AddField("model", model);
            form.AddField("language", language);
            form.AddField("temperature", "0");
            form.AddField("response_format", "json");

            // Slovník v promptu není příkaz, ale nápověda k pravopisu —
            // Whisper pak míň komolí slova, která v běžné češtině nejsou častá.
            form.AddField("prompt",
                "Krátký český povel pro aplikaci ve smíšené realitě. "
                + "Slovník: " + CzechCommandParser.Slovnik + ".");

            using (var req = UnityWebRequest.Post(
                       "https://api.openai.com/v1/audio/transcriptions", form))
            {
                req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.timeout = 20;

                yield return req.SendWebRequest();

                var latence = Time.realtimeSinceStartup - zacatek;
                _requestInFlight = false;

                // Co se nastřádalo během rozpoznávání, jde na řadu hned teď.
                if (_cekaNaOdeslani != null)
                {
                    var dalsi = _cekaNaOdeslani;
                    var delka = _cekaDelka;
                    var spickaFronty = _cekaSpicka;
                    var sumFronty = _cekaSum;
                    _cekaNaOdeslani = null;
                    Odeslat(dalsi, delka, spickaFronty, sumFronty);
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Síť je jediná část hlasové podmínky, kterou nemáme pod
                    // kontrolou. Do logu jde i důvod, aby šlo v datech poznat
                    // výpadek od skutečného nerozpoznání.
                    Zaloguj(LogEvent.VoiceRejected,
                        "sit selhala: " + req.error + " po " + latence.ToString("F2") + "s");

                    Debug.LogError("[Voice] " + req.error, this);
                    Hlaska("chyba spojení");
                    yield break;
                }

                var odpoved = JsonUtility.FromJson<WhisperOdpoved>(req.downloadHandler.text);
                var prepis = odpoved != null ? odpoved.text : null;

                if (logToConsole)
                    Debug.Log("[Voice] \"" + prepis + "\"  (" + delkaPromluvy.ToString("F2")
                              + " s reci, rozpoznani " + latence.ToString("F2") + " s)");

                Zpracuj(prepis, delkaPromluvy, latence, spicka, sum);
            }
        }

        [Serializable]
        private class WhisperOdpoved
        {
            public string text;
        }

        // ---- Vyhodnocení ----

        private void Zpracuj(string prepis, float delkaPromluvy, float latence,
            float spicka, float sum)
        {
            // HLASITOST A PRÁH JDOU DO DAT. Bez nich se otázka „mluvím
            // dost nahlas?" nedá zodpovědět jinak než hádáním; takhle se
            // práh dá po zkušebním běhu nastavit podle naměřených čísel.
            var spolecne = "prepis=\"" + (prepis ?? "") + "\""
                           + " rec=" + delkaPromluvy.ToString("F2") + "s"
                           + " rozpoznani=" + latence.ToString("F2") + "s"
                           + " hlasitost=" + spicka.ToString("F4")
                           + " sum=" + sum.ToString("F4")
                           + " prah=" + Mathf.Max(speechRms, _noiseFloor * speechToNoise).ToString("F4");

            if (DebugChanged != null)
                DebugChanged("hlasitost " + spicka.ToString("F4")
                             + "   šum " + sum.ToString("F4")
                             + "   práh " + Mathf.Max(minSpeechPeak,
                                 Mathf.Max(speechRms, _noiseFloor * speechToNoise) * peakMargin).ToString("F4")
                             + "\n" + "nahrávka " + delkaPromluvy.ToString("F2") + " s"
                             + "   rozpoznání " + latence.ToString("F2") + " s");

            // Výmysly z ticha se do dat zapisují jinak než skutečné
            // nerozpoznání. Bez rozlišení by vypadaly jako selhání hlasu,
            // ačkoli participant v tu chvíli nic neřekl.
            if (JeVymysl(prepis))
            {
                Zaloguj(LogEvent.VoiceRejected, spolecne + " duvod=vymysl z ticha");
                return;
            }

            var prikaz = CzechCommandParser.Parse(prepis);

            switch (prikaz.Intent)
            {
                case VoiceIntent.Create:
                    if ((_allowed & MenuPart.Create) == 0)
                    {
                        Zaloguj(LogEvent.Note, spolecne + " -> zamceno pruvodcem");
                        Hlaska("teď ne");
                        return;
                    }

                    // V BLOKU S VELIKOSTMI JE VELIKOST POVINNÁ. Bez tohohle
                    // se „modrá krychle" tiše vyrobila jako střední, protože
                    // střední je výchozí hodnota — a participant si myslel,
                    // že velikost řekl, nebo že na ní nezáleží. V menu přitom
                    // bez vybrané velikosti vytvořit nejde, takže hlas
                    // odpouštěl něco, co klasická podmínka neodpouští.
                    if (_requiresSize && !prikaz.HasSize)
                    {
                        Zaloguj(LogEvent.VoiceMisrecognized, spolecne + " duvod=chybi velikost");
                        Hlaska("chybí velikost");
                        return;
                    }

                    var velikost = _requiresSize ? prikaz.Size : ShapeSizes.Default;

                    // VRÁCENÍ JDE PŘED VYTVOŘENÍM. „Zpět, malý modrý osmistěn"
                    // znamená nejdřív zahodit, co je v ruce, a teprve pak si
                    // říct o nový objekt — v opačném pořadí by vrácení smazalo
                    // právě vytvořený kus.
                    if (prikaz.AlsoUndo)
                    {
                        if ((_allowed & MenuPart.StepBack) == 0)
                        {
                            Zaloguj(LogEvent.Note, spolecne + " -> zpet zamceno pruvodcem");
                        }
                        else
                        {
                            Zaloguj(LogEvent.UndoUsed, spolecne + " -> hlasem (ve vete s objektem)");
                            if (UndoRequested != null) UndoRequested();
                        }
                    }

                    Zaloguj(LogEvent.ObjectRequested, spolecne + " -> " + prikaz.Color
                        + " " + prikaz.Shape + " " + ShapeSizes.Label(velikost)
                        + (_requiresSize || !prikaz.HasSize ? "" : " (velikost ignorovana)")
                        + (prikaz.AlsoReveal ? " + planek" : "")
                        + (prikaz.AlsoUndo ? " + zpet" : ""));

                    Hlaska(prepis);

                    if (ObjectRequested != null)
                        ObjectRequested(new ObjectRequest(prikaz.Shape, prikaz.Color,
                            velikost, Time.realtimeSinceStartup));

                    // Věta žádala objekt I plánek. Odkrytí se loguje zvlášť,
                    // aby se v datech dalo spočítat stejně jako samostatné
                    // „ukaž plán" — jinak by odkrytí schovaná v takové větě
                    // v počtu chyběla.
                    if (prikaz.AlsoReveal)
                    {
                        Zaloguj(LogEvent.ReferenceRevealed, spolecne + " -> hlasem (ve vete s objektem)");
                        if (RevealRequested != null) RevealRequested();
                    }
                    return;

                case VoiceIntent.Undo:
                    if ((_allowed & MenuPart.StepBack) == 0)
                    {
                        Zaloguj(LogEvent.Note, spolecne + " -> zamceno pruvodcem");
                        Hlaska("teď ne");
                        return;
                    }

                    Zaloguj(LogEvent.UndoUsed, spolecne);
                    Hlaska("zpět");
                    if (UndoRequested != null) UndoRequested();
                    return;

                case VoiceIntent.Reveal:
                    // Když je zamčené všechno (čeká se na start bloku), nesmí
                    // jít odkrýt plánek ani povelem — jinak by se dal obejít
                    // stejně jako tlačítkem.
                    if (_allowed == MenuPart.None)
                    {
                        Zaloguj(LogEvent.Note, spolecne + " -> zamceno pruvodcem");
                        Hlaska("teď ne");
                        return;
                    }

                    Zaloguj(LogEvent.ReferenceRevealed, spolecne + " -> hlasem");
                    Hlaska("plánek");
                    if (RevealRequested != null) RevealRequested();
                    return;

                default:
                    // Nerozpoznaný povel NENÍ chyba participanta a nesmí nic
                    // vytvořit. Do dat jde jako VoiceMisrecognized i s tím, co
                    // přesně chybělo — z toho se pak dá spočítat, jak často
                    // hlas selhal a proč.
                    Zaloguj(LogEvent.VoiceMisrecognized, spolecne + " duvod=" + prikaz.Problem);
                    Hlaska("nerozumím");
                    return;
            }
        }

        /// <summary>
        /// Pozná typický výplod Whisperu nad tichem. Model má v trénovacích
        /// datech spoustu titulkových stop, takže na ticho odpovídá adresami
        /// webů a poděkováním překladateli.
        /// </summary>
        private static bool JeVymysl(string prepis)
        {
            if (string.IsNullOrWhiteSpace(prepis)) return true;

            var t = CzechCommandParser.Normalizovat(prepis);

            if (t.Contains("www.") || t.Contains("http") || t.Contains(".cz")
                || t.Contains(".com")) return true;

            return t.Contains("titulky") || t.Contains("preklad")
                   || t.Contains("subtitle") || t.Contains("amara.org");
        }

        private void Zaloguj(LogEvent udalost, string detail)
        {
            if (_logger != null) _logger.Log(udalost, detail: detail);
        }

        /// <summary>
        /// Vybere mikrofon podle části názvu, jinak první v seznamu.
        /// </summary>
        private string VybratMikrofon()
        {
            var zarizeni = Microphone.devices;

            if (!string.IsNullOrEmpty(preferredMic))
            {
                foreach (var d in zarizeni)
                    if (d != null && d.IndexOf(preferredMic,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;

                Debug.LogWarning("[Voice] Mikrofon obsahující \"" + preferredMic
                                 + "\" nenalezen, beru první v pořadí.", this);
            }

            return zarizeni[0];
        }

        private void Hlaska(string text)
        {
            if (StatusChanged != null) StatusChanged(text);
        }

        // ---- Práce se zvukem ----

        /// <summary>
        /// Přečte úsek z kruhového bufferu i přes jeho konec. Unity umí číst
        /// jen souvisle od offsetu, takže se přetočení musí složit ze dvou čtení.
        /// </summary>
        private void CtiKruh(int od, int pocet, float[] cil)
        {
            var delka = _ring.samples;
            od %= delka;
            if (od < 0) od += delka;

            var doKonce = Mathf.Min(pocet, delka - od);
            var prvni = new float[doKonce];
            _ring.GetData(prvni, od);
            Array.Copy(prvni, 0, cil, 0, doKonce);

            var zbytek = pocet - doKonce;
            if (zbytek <= 0) return;

            var druhy = new float[zbytek];
            _ring.GetData(druhy, 0);
            Array.Copy(druhy, 0, cil, doKonce, zbytek);
        }

        private static float Rms(float[] data, int pocet)
        {
            double soucet = 0;
            for (var i = 0; i < pocet; i++) soucet += data[i] * (double)data[i];
            return Mathf.Sqrt((float)(soucet / Mathf.Max(1, pocet)));
        }

        /// <summary>16bitový mono WAV. Whisper bere i jiné formáty, ale tenhle
        /// se skládá bez knihovny a nemá kompresní ztráty.</summary>
        private static byte[] Wav(float[] vzorky, int rate)
        {
            var data = new short[vzorky.Length];
            for (var i = 0; i < vzorky.Length; i++)
                data[i] = (short)Mathf.RoundToInt(Mathf.Clamp(vzorky[i], -1f, 1f) * short.MaxValue);

            var wav = new byte[44 + data.Length * 2];
            var ascii = System.Text.Encoding.ASCII;

            ascii.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + data.Length * 2).CopyTo(wav, 4);
            ascii.GetBytes("WAVE").CopyTo(wav, 8);
            ascii.GetBytes("fmt ").CopyTo(wav, 12);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);
            BitConverter.GetBytes(rate).CopyTo(wav, 24);
            BitConverter.GetBytes(rate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            ascii.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(data.Length * 2).CopyTo(wav, 40);
            Buffer.BlockCopy(data, 0, wav, 44, data.Length * 2);

            return wav;
        }
    }
}
