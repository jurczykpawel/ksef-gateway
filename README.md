# KSeF Gateway

> REST API do polskiego Krajowego Systemu e-Faktur (KSeF). Wysyłasz JSON, dostajesz numer KSeF. Odbierasz faktury bez znajomości ich numeru. Jedno wywołanie HTTP w obie strony.

<details>
<summary>🇬🇧 In English</summary>

**KSeF Gateway** is a REST API gateway for Poland's National e-Invoice System (KSeF). Send a simple JSON invoice, get a KSeF number back. Receive invoices issued to you without knowing their number - search by date instead. One HTTP call instead of building XML, AES-256 encryption, session management, and token handling.

```bash
curl -X POST https://your-gateway/ksef/invoice \
  -d '{"seller":{"nip":"..."},"buyer":{"nip":"..."},"items":[{"name":"Service","unitPrice":100,"vatRate":23}]}'
# → {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}
```

**Quick start:** `docker compose up` and you're done. No local .NET required.

**Features:** sending and receiving invoices, official Ministry of Finance SDK (CIRFMF), PDF with QR code, 60+ endpoints, multi-NIP, one-click deploy (Render/Lambda/Azure). Ready for mandatory KSeF (production, not just test).

**Full README:** [README.en.md](README.en.md)

</details>

![License](https://img.shields.io/badge/License-AGPL%20v3-blue)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Open Source](https://img.shields.io/badge/Open%20Source-100%25-brightgreen)
[![CI](https://github.com/jurczykpawel/ksef-gateway/actions/workflows/ci.yml/badge.svg)](https://github.com/jurczykpawel/ksef-gateway/actions/workflows/ci.yml)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)

---

## Problem

Integracja z KSeF bezpośrednio oznacza:
- Budowanie XML FA(3) od zera (skomplikowany schemat, polskie nazwy pól jak P_13_1, P_14_1)
- Szyfrowanie faktur AES-256 kluczem publicznym RSA KSeF
- Zarządzanie sesjami (otwarcie, wysłanie, sprawdzanie statusu, zamknięcie)
- Autoryzacja tokenem z podpisami XAdES i auto-odświeżaniem
- Obsługa limitów żądań, ponawiania prób i kodów błędów z API KSeF

**KSeF Gateway zajmuje się tym wszystkim.** Wysyłasz prosty JSON, dostajesz numer KSeF.

### Bez KSeF Gateway

```
Twoja aplikacja → zbuduj XML → zaszyfruj AES-256 → wymień klucze RSA → otwórz sesję
→ wyślij zaszyfrowaną fakturę → sprawdzaj status → sparsuj odpowiedź → zamknij sesję
→ obsłuż błędy, ponów próby, odśwież token...
```

### Z KSeF Gateway

```bash
curl -X POST https://twoj-serwer/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{"seller":{"nip":"..."},"buyer":{"nip":"..."},"items":[{"name":"Usługa","unitPrice":100,"vatRate":23}]}'

# → {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}
```

---

## Dlaczego KSeF Gateway?

- **Jedno wywołanie HTTP** - wysyłasz JSON, dostajesz numer KSeF. Usługa obsługuje szyfrowanie, sesje, odpytywanie statusu
- **Proste dane wejściowe JSON** - `{seller, buyer, items}` z automatycznym liczeniem VAT. Nie musisz znać XML
- **Odbieranie bez znajomości numeru** - KSeF nie wysyła powiadomień e-mail/webhook; zamiast tego [przeglądaj lub odpytuj o faktury wystawione na Ciebie](#odbieranie-faktur) po dacie
- **PDF z kodem QR** - pobierz zweryfikowany PDF faktury po numerze KSeF, jednym wywołaniem
- **Oficjalne SDK w środku** - opakowuje [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp), utrzymywane przez Ministerstwo Finansów
- **60+ automatycznie odkrytych endpointów** z SDK przez refleksję .NET
- **Wdrażaj gdziekolwiek** - Docker, Render (jednym kliknięciem), AWS Lambda, Azure
- **Nie potrzebujesz .NET lokalnie** - wszystko buduje się i działa w Dockerze

---

## Bezpieczeństwo

Usługa uwierzytelnia *samą siebie* w KSeF (token lub certyfikat - patrz niżej), ale sama z siebie **nie ma żadnego sposobu, żeby uwierzytelnić tego, kto ją wywołuje**. Wdróż ją pod publicznym adresem bez zabezpieczenia, a każdy, kto znajdzie ten adres, może wysyłać lub czytać prawdziwe faktury dla NIP-u, na który jest skonfigurowana.

**`GATEWAY_API_KEY` zamyka tę lukę.** Każde żądanie oprócz `GET /health` (używanego przez health checki/monitoring) musi zawierać ten klucz jako nagłówek `X-Api-Key`, inaczej usługa je odrzuci - łącznie z `/scalar/v1` i JSON-em OpenAPI, więc sama powierzchnia API też nie jest wystawiona:

| Sytuacja | Odpowiedź |
|---|---|
| `GATEWAY_API_KEY` nie ustawiony na usłudze | `503` - zawodzi **domknięte**: odmawia wszystkiego zamiast po cichu zostać otwarta |
| Brakujący lub zły nagłówek `X-Api-Key` | `401` |
| Poprawny nagłówek `X-Api-Key` | Żądanie przechodzi normalnie |

Wygeneruj silny klucz raz, trzymaj w tajemnicy - tak samo jak `KSEF_TOKEN`:

```bash
openssl rand -hex 32
```

> To jest osobne od autoryzacji `KSEF_TOKEN`/certyfikatem, która chroni własne połączenie usługi *do* KSeF. Potrzebujesz obu: jednego żeby usługa mogła rozmawiać z KSeF, drugiego żeby tylko Ty mógł rozmawiać z usługą.

**Obrona w głąb dla produkcji - allowlista IP:** klucz API to jedyna rzecz stojąca między publicznym adresem a całym internetem, więc dla prawdziwego wdrożenia dołóż warstwę sieciową: postaw usługę za Cloudflare albo reverse proxy i **przepuszczaj tylko adres(y), z których faktycznie ją wołasz** - zwykle egress IP Twojego serwera automatyzacji (n8n) czy backendu. Dzięki temu nawet gdyby klucz wyciekł, użyje go wyłącznie zaufany adres. Działa to najlepiej, gdy caller to stały serwer ze stabilnym IP; adresy domowe/mobilne bywają zmienne, a wewnętrzne adresy VPN (np. Tailscale `100.x`) nie są widoczne dla publicznej usługi - wtedy kieruj ruch przez stały host i to jego IP dodaj do allowlisty. Nie polegaj na kluczu jako jedynej warstwie.

### Wymuś ruch tylko przez proxy (`TRUSTED_PROXY_SECRET`)

Jest haczyk: allowlista IP na Cloudflare/proxy chroni tylko ruch, który *przez nie przechodzi*. Jeśli platforma daje usłudze publiczny adres origin (np. `*.onrender.com`), ktoś może uderzyć w niego **bezpośrednio, z pominięciem proxy** - i wtedy cała allowlista IP jest do obejścia (zostaje sam klucz API).

Domknij to opcjonalnym sekretem proxy. Ustaw `TRUSTED_PROXY_SECRET`, a w proxy wstrzykuj ten sam sekret jako nagłówek (Cloudflare: Transform Rule → *Set static header*; domyślna nazwa `X-Trusted-Proxy-Secret`, zmienisz przez `TRUSTED_PROXY_HEADER`). Od tej chwili każde żądanie poza `GET /health` musi nieść ten nagłówek:

- ruch przez Twój Cloudflare/proxy → nagłówek jest → przechodzi (i podlega allowliście IP na proxy),
- strzał wprost w origin z pominięciem proxy → brak nagłówka → `403`.

Opcja jest **domyślnie wyłączona**: bez `TRUSTED_PROXY_SECRET` nic się nie zmienia (chroni tylko klucz API). `GET /health` zawsze pomija ten check, żeby health-check platformy (uderzający w origin bezpośrednio) działał.

| Zmienna | Rola |
|---|---|
| `TRUSTED_PROXY_SECRET` | Ustaw, by włączyć. Sekret wspólny między proxy a usługą (`openssl rand -hex 32`). |
| `TRUSTED_PROXY_HEADER` | Opcjonalna nazwa nagłówka (domyślnie `X-Trusted-Proxy-Secret`). |

> **Przy konfiguracji uważaj na dwie rzeczy:** (1) w proxy ustaw nagłówek trybem **overwrite** (Cloudflare: *Set static header*, nie *Add*) — inaczej ewentualna kopia od klienta doklei się do sekretu (`wartość-klienta, sekret`) i odrzuci **cały** legalny ruch; (2) origin nie może logować ani odbijać nagłówków żądań — wyciek sekretu = ktoś powtórzy go wprost w origin i obejdzie zabezpieczenie. Gdy włączysz opcję, a proxy nie wstrzykuje nagłówka, wszystko poza `/health` zwróci `403` (a `/health` dalej świeci zielono) — usługa loguje wtedy przy starcie, że enforcement jest ON.

### Gdzie hostować i jak traktowany jest certyfikat

Żeby usługa mogła sama podpisywać żądania do KSeF (i wstawać bez Ciebie po restarcie), klucz prywatny - a jeśli jest zaszyfrowany, to i jego hasło - musi być dla niej dostępny w czasie działania. Wynika z tego kilka rzeczy, które warto rozumieć, zanim wybierzesz hosting:

- **Sekrety nie trafiają do repozytorium ani do obrazu.** Certyfikat, klucz i `contexts.json` podajesz w runtime (zmienne środowiskowe, Secret Files albo zamontowany plik) - nigdy nie commitujesz ich i nie wbudowujesz w obraz Dockera.
- **Klucz może leżeć zaszyfrowany na dysku** (`PrivateKeyPassword` / `KSEF_KEY_PASSWORD`), a usługa odszyfrowuje go dopiero w pamięci przy starcie. To chroni przed *częściowym* wyciekiem - ktoś, kto dostanie sam plik klucza bez hasła, ma coś bezużytecznego. Nie chroni przed pełnym przejęciem hosta, bo hasło i tak jest na tym samym hoście. Sam klucz nigdy nie opuszcza serwera - do KSeF lecą wyłącznie podpisy, nie klucz.
- **Każdy host to „zaufana strona".** Managed PaaS (Render, Fly, Railway…) odszyfrowuje Twoje sekrety i wstrzykuje je do kontenera, więc platforma technicznie ma do nich dostęp. Ale to samo dotyczy własnego VPS-a: dostawca ma dostęp do pamięci i dysku Twojej maszyny (warstwa hypervisora). **Self-hosting nie eliminuje tego problemu - zmienia tylko, komu ufasz.**
- **Współdzielony serwer = większy promień rażenia.** Jeśli postawisz usługę na maszynie, na której działa też kilka innych rzeczy, luka w *którejkolwiek* z nich może sięgnąć po certyfikat KSeF. Odizolowana, minimalna instancja jest pod tym względem czystsza niż zatłoczony własny serwer.

**Co realnie chroni - niezależnie od hosta:**

1. Największe ryzyko to nie kradzież certyfikatu, tylko **ktoś wywołuje usługę i wystawia fałszywe faktury na Twój NIP**. Dlatego mocny `GATEWAY_API_KEY` + ograniczenie, kto może wołać usługę (allowlista IP / reverse proxy), znaczą więcej niż to, gdzie fizycznie leży certyfikat.
2. Certyfikat jest **odwoływalny** - przy podejrzeniu kompromitacji generujesz nowy na portalu KSeF, a stary przestaje działać.
3. Dla większości wdrożeń **reputowany managed host to rozsądny i bezpieczny wybór** - jego izolacja bywa lepsza niż zatłoczonego własnego serwera. Jeśli zależy Ci na maksymalnej izolacji, użyj dedykowanej, minimalnej maszyny tylko na tę usługę.

---

## Wypróbuj Live Demo

Bez konfiguracji. Publiczna instancja demo działa na darmowym tierze Render, uwierzytelniona certyfikatem do jednorazowej firmy **TEST** KSeF (nie prawdziwy biznes). Samofakturowanie jest włączone, więc możesz wysłać fakturę i od razu odnaleźć siebie jako nabywcę:

```bash
DEMO_URL="https://ksef-api-rfm0.onrender.com"
DEMO_KEY="0679d36400bfdedcaf1f7d1f5774d0d94ffae5a9f6bc7596cbf24de94d42a8ee"
DEMO_NIP="3202004132"

curl -X POST "$DEMO_URL/ksef/invoice" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $DEMO_KEY" \
  -d '{
    "invoiceNumber": "DEMO/'"$(date +%s)"'",
    "issueDate": "'"$(date +%F)"'",
    "saleDate": "'"$(date +%F)"'",
    "seller": { "nip": "'"$DEMO_NIP"'", "name": "Demo sp. z o.o.", "address": { "street": "ul. Demo 1", "city": "00-001 Warszawa" } },
    "buyer": { "nip": "'"$DEMO_NIP"'", "name": "Demo sp. z o.o.", "address": { "street": "ul. Demo 1", "city": "00-001 Warszawa" } },
    "items": [ { "name": "Demo usługa", "quantity": 1, "unitPrice": 100, "vatRate": 23 } ]
  }'
# → {"success":true,"data":{"ksefNumber":"..."}}

curl "$DEMO_URL/ksef/invoices/received?from=2026-01-01&to=2026-12-31" \
  -H "X-Api-Key: $DEMO_KEY"
# → {"success":true,"data":{"invoices":[{"ksefNumber":"...","invoiceNumber":"DEMO/...",...}],"hasMore":false}}
```

Albo zaimportuj [kolekcję Bruno](#testowanie-z-bruno) i przełącz na środowisko `demo` - każde żądanie działa od razu, żadnych danych do szukania.

> **To demo jest publiczne i współdzielone** - nie wysyłaj niczego wrażliwego. To jednorazowy NIP środowiska TEST, niepowiązany z żadnym prawdziwym biznesem, a klucz API może zostać rotowany bez ostrzeżenia w razie nadużycia.

---

## Szybki Start

### Wymagania wstępne
- Docker & Docker Compose
- GitHub PAT z uprawnieniem `read:packages` ([utwórz tutaj](https://github.com/settings/tokens/new?scopes=read:packages))

> **Dlaczego potrzebny jest GitHub PAT?** Oficjalne SDK KSeF ([CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp)) jest publikowane jako pakiety NuGet na GitHub Packages, nie na nuget.org. GitHub Packages wymaga uwierzytelnienia nawet dla pakietów z publicznych repozytoriów - to [znane ograniczenie GitHub](https://github.com/orgs/community/discussions/26634). PAT z uprawnieniem `read:packages` to jedyny sposób na ich pobranie podczas budowania. Wygenerowanie zajmuje 30 sekund.

### 1. Sklonuj i skonfiguruj

```bash
git clone --recurse-submodules https://github.com/jurczykpawel/ksef-gateway.git
cd ksef-gateway
cp .env.example .env
# Edytuj .env: ustaw GITHUB_PAT i GATEWAY_API_KEY (np. `openssl rand -hex 32`)
```

> **Dlaczego `.env` potrzebuje `GATEWAY_API_KEY`?** Usługa nie ma żadnej innej autoryzacji dla wywołujących - patrz [Bezpieczeństwo](#bezpieczeństwo) niżej. Każde żądanie oprócz `GET /health` potrzebuje go z powrotem jako nagłówek `X-Api-Key`, inaczej usługa je odrzuci.

### 2. Wygeneruj testowy token KSeF

```bash
docker compose --profile tools run --rm token-generator
```

Skopiuj wynik (`KSEF_TOKEN`, `KSEF_NIP`, `KSEF_ENV`) do pliku `.env`.

> **Co to robi?** Patrz [Jak Działa Generator Tokenów](#jak-działa-generator-tokenów) niżej. Wolisz przetestować [ścieżkę autoryzacji certyfikatem](#autoryzacja-certyfikatem-alternatywa-dla-tokenów)? `docker compose --profile tools run --rm cert-generator` robi to samo dla certyfikatów - patrz [Jak Działa Generator Certyfikatów](#jak-działa-generator-certyfikatów).

### 3. Uruchom usługę

```bash
docker compose up --build
```

API: `http://localhost:8080` | Dokumentacja: `http://localhost:8080/scalar/v1`

### 4. Wyślij pierwszą fakturę

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": {
      "nip": "TWOJ_NIP",
      "name": "Twoja Firma sp. z o.o.",
      "address": { "street": "ul. Testowa 1", "city": "00-001 Warszawa" }
    },
    "buyer": {
      "nip": "5265877635",
      "name": "Nabywca sp. z o.o.",
      "address": { "street": "ul. Kupiecka 5", "city": "30-001 Krakow" }
    },
    "items": [
      { "name": "Usługa konsultingowa", "quantity": 10, "unitPrice": 150, "vatRate": 23 }
    ],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'

# Odpowiedź: {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}

# Pobierz PDF z kodem QR
curl -H "X-Api-Key: $GATEWAY_API_KEY" -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf
```

> **Wspiera też surowy XML** (`POST /ksef/send`) i format JSON xml-js (`POST /ksef/send/json`) - patrz [Wysyłanie Faktur](#wysyłanie-faktur) niżej.

### 5. Znajdź fakturę wysłaną do Ciebie

Na TEST/DEMO możesz spróbować od razu bez drugiej firmy: ustaw `buyer.nip` na **ten sam** NIP, którego użyłeś jako `seller.nip` powyżej (KSeF wspiera samofakturowanie), potem odnajdź siebie jako nabywcę:

```bash
curl "http://localhost:8080/ksef/invoices/received?from=2026-03-01&to=2026-04-01" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -H "X-KSeF-NIP: TWOJ_NIP"

# Odpowiedź: {"success":true,"data":{"invoices":[{"ksefNumber":"...","invoiceNumber":"FV/2026/001",...}],"hasMore":false}}
```

Nie musisz znać numeru KSeF wcześniej - patrz [Odbieranie Faktur](#odbieranie-faktur) niżej po pełny obraz, łącznie z odpytywaniem o nowe faktury.

---

## Testowanie z Bruno

W katalogu `bruno/` znajduje się kolekcja [Bruno](https://www.usebruno.com/) - wszystkie endpointy z asercjami.

**Konfiguracja:**
1. Zainstaluj Bruno (aplikacja desktopowa albo CLI: `npm install -g @usebruno/cli`)
2. Otwórz kolekcję w Bruno desktop: **Open Collection** → wybierz `bruno/`
3. Wybierz środowisko `local`, `render` (Twój własny deploy na Render) albo `demo` ([live demo](#wypróbuj-live-demo) - żadnych danych do ustawiania, już wypełnione)
4. Dla `local`/`render`: ustaw `apiKey` na Twój `GATEWAY_API_KEY` i `sellerNip` na Twój NIP - nagłówek na poziomie kolekcji (`collection.bru`) automatycznie wysyła `apiKey` jako `X-Api-Key` w każdym żądaniu

**Uruchamianie z CLI:**
```bash
# Health check (jedyny endpoint, który nie potrzebuje klucza API)
cd bruno && bru run health.bru --env local

# Cała kolekcja (wymaga GATEWAY_API_KEY, KSEF_TOKEN + KSEF_NIP w .env)
bru run --env local
```

`send-xml.bru` i `send-invoice.bru` automatycznie zapisują zwrócony `ksefNumber` jako zmienną - po wysłaniu `get-invoice-xml` i `get-invoice-pdf` działają od razu.

**Co jest w kolekcji:**

| Żądanie | Endpoint |
|---|---|
| `health.bru` | `GET /health` |
| `status.bru` | `GET /ksef/status` |
| `contexts.bru` | `GET /ksef/contexts` |
| `send-invoice.bru` | `POST /ksef/invoice` (przyjazny JSON) |
| `send-xml.bru` | `POST /ksef/send` (surowy XML FA(3)) |
| `send-xml-explicit-nip.bru` | `POST /ksef/send` z nagłówkiem `X-KSeF-NIP` (multi-NIP) |
| `send-json.bru` | `POST /ksef/send/json` (format xml-js) |
| `get-invoice-xml.bru` | `GET /ksef/invoice/{ksefNumber}` |
| `get-invoice-pdf.bru` | `GET /ksef/invoice/{ksefNumber}/pdf` |
| `list-received-invoices.bru` | `GET /ksef/invoices/received` |
| `list-new-received-invoices.bru` | `GET /ksef/invoices/received/new` |
| `list-issued-invoices.bru` | `GET /ksef/invoices/issued` |

---

## Endpointy API

> **Każdy endpoint poniżej oprócz `GET /health` wymaga nagłówka `X-Api-Key`** (patrz [Bezpieczeństwo](#bezpieczeństwo)). Przykłady poniżej pomijają go dla zwięzłości - zakładaj, że tam jest.

### Endpointy Workflow (wysokopoziomowe)

| Metoda | Endpoint | Wejście | Wyjście |
|--------|----------|-------|--------|
| `POST` | `/ksef/invoice` | Przyjazny JSON `{seller, buyer, items}` | Numer KSeF |
| `POST` | `/ksef/send` | Ciało XML FA(3) | Numer KSeF |
| `POST` | `/ksef/send/json` | JSON (format xml-js, 1:1 z XML) | Numer KSeF |
| `GET` | `/ksef/invoice/{ksefNumber}` | - | XML faktury |
| `GET` | `/ksef/invoice/{ksefNumber}/pdf` | - | PDF z kodem QR |
| `GET` | `/ksef/invoices/received` | `?from=&to=&page=&pageSize=` | Lista faktur, które odebrałeś (rola nabywcy) |
| `GET` | `/ksef/invoices/received/new` | `?since=` | Nowe faktury od checkpointu, do odpytywania/synchronizacji |
| `GET` | `/ksef/invoices/issued` | `?from=&to=&page=&pageSize=` | Lista faktur, które wystawiłeś (rola sprzedawcy) |
| `GET` | `/ksef/status` | - | Status usługi + KSeF |
| `GET` | `/health` | - | Health check |

### Automatycznie odkryte endpointy SDK (niskopoziomowe)

Wszystkie 60+ metod z oficjalnego `IKSeFClient` są wystawione jako `POST /ksef/{grupa}/{metoda}`:

| Grupa | Endpointy | Opis |
|-------|-----------|-------------|
| `online-session` | 3 | Interaktywne wysyłanie faktur |
| `batch-session` | 4 | Wysyłanie wsadowe (pakiety ZIP) |
| `invoice-download` | 4 | Pobieranie, zapytania, eksport faktur |
| `session-status` | 9 | Status sesji/faktury, UPO |
| `authorization` | 5 | Wyzwanie autoryzacji, autoryzacja tokenem |
| `ksef-token` | 4 | Generowanie, sprawdzanie, unieważnianie tokenów |
| `certificate` | 7 | Zarządzanie certyfikatami KSeF |
| `permissions` | 17 | Nadawanie, cofanie, szukanie uprawnień |
| `lighthouse` | 2 | Status systemu KSeF |

Pełna interaktywna dokumentacja pod `/scalar/v1`.

---

## Wysyłanie Faktur

### Opcja 1: Przyjazny JSON (zalecane)

Prosty JSON z czytelnymi polami. Usługa buduje XML FA(3), liczy sumy VAT, obsługuje wszystkie pola KSeF automatycznie.

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": {
      "nip": "1234567890",
      "name": "Sprzedawca sp. z o.o.",
      "address": { "street": "ul. Testowa 1", "city": "00-001 Warszawa" }
    },
    "buyer": {
      "nip": "0987654321",
      "name": "Nabywca sp. z o.o.",
      "address": { "street": "ul. Kupiecka 2", "city": "00-002 Warszawa" }
    },
    "items": [
      { "name": "Usługa konsultingowa", "quantity": 10, "unitPrice": 150, "vatRate": 23 },
      { "name": "Hosting serwera", "quantity": 1, "unitPrice": 50, "vatRate": 23 }
    ],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'
```

### Opcja 2: Surowy XML FA(3)

Dla systemów, które już generują XML KSeF (oprogramowanie księgowe, ERP):

```bash
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -d @invoice.xml
```

### Opcja 3: JSON odzwierciedlający XML (format xml-js)

Reprezentacja JSON 1:1 XML-a FA(3) w [formacie compact xml-js](https://www.npmjs.com/package/xml-js#compact-notation). Zero utrzymania - gdy zmienia się XSD, struktura JSON zmienia się automatycznie. Pełny przykład w [`examples/invoice.json`](examples/invoice.json).

```bash
curl -X POST http://localhost:8080/ksef/send/json \
  -H "Content-Type: application/json" \
  -d @examples/invoice.json
```

### Odpowiedź (wszystkie opcje)

```json
{
  "success": true,
  "data": {
    "ksefNumber": "1234567890-20260326-5EC118800000-05",
    "status": "accepted",
    "statusDescription": "Sukces"
  }
}
```

### Odpowiedzi błędów

Każdy endpoint zwraca ten sam kształt przy błędzie: `{"success": false, "data": null, "error": "..."}`. Kod statusu HTTP mówi, czy ponowić próbę i jak:

| Status | Znaczenie | Co zrobić |
|--------|---------|------------|
| `400` | Złe dane wejściowe (brak NIP, nieprawidłowy XML, źle sformowane ciało) | Popraw żądanie, nie ponawiaj bez zmian |
| `401` | Brakujący lub zły nagłówek `X-Api-Key` - patrz [Bezpieczeństwo](#bezpieczeństwo) | Wyślij poprawny klucz API usługi |
| `429` | Zaraz przekroczysz (albo przekroczyłeś) limit żądań KSeF | Poczekaj tyle, ile mówi nagłówek `Retry-After` (sekundy), potem ponów |
| `502` | Własne API KSeF odrzuciło lub nie powiodło się żądanie | Sprawdź `error` po kod/wiadomość błędu KSeF; nie zawsze bezpiecznie ponowić bez zastanowienia |
| `503` | Jedno z: `GATEWAY_API_KEY` w ogóle nie skonfigurowany na usłudze, wyłącznik obwodu SDK KSeF jest otwarty (ma nagłówek `Retry-After` - poczekaj i ponów), albo usługa nie jest jeszcze uwierzytelniona dla tego NIP-u (`TokenPool` wciąż startuje - brak `Retry-After`, ponów wkrótce) | Sprawdź wiadomość `error`, żeby wiedzieć który przypadek |
| `500` | Nieoczekiwany błąd w samej usłudze | Sprawdź logi usługi; proszę zgłoś issue |

Odpowiedzi `429` i wyłącznika obwodu `503` zawierają nagłówek `Retry-After` (sekundy) - uszanuj go zamiast ponawiać natychmiast, szczególnie przy [endpointach odbierania z limitem żądań](#odbieranie-faktur).

---

## Generowanie PDF

```bash
# Pobierz PDF z kodem QR po numerze KSeF (jedno wywołanie - wewnętrznie pobiera XML z KSeF)
curl -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf

# Wygeneruj PDF bezpośrednio z XML
curl -X POST http://localhost:8080/pdf/invoice \
  -H "Content-Type: application/xml" \
  -d @invoice.xml -o faktura.pdf

# Wygeneruj PDF z kodem QR
curl -X POST "http://localhost:8080/pdf/invoice?nrKSeF={ksefNumber}" \
  -H "Content-Type: application/xml" \
  -d @invoice.xml -o faktura.pdf
```

PDF-y są generowane przy użyciu oficjalnej biblioteki [CIRFMF/ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator). Kody QR zawierają URL weryfikacyjny KSeF z hashem SHA-256 - skanowalny i weryfikowany przez KSeF.

### Konfiguracja usługi PDF (`PDF_SERVICE_URL` / `PDF_SERVICE_SECRET`)

Generowanie PDF to osobna usługa (`ksef-pdf`), którą brama woła po HTTP. Jak ją wskazać, zależy od tego, gdzie stoi:

- **Lokalnie (docker compose) albo płatny Render (private networking):** zostaw domyślne - brama woła ją po sieci prywatnej (`http://ksef-pdf:3000`, a na Render `fromService … hostport`). Bez sekretu. `PDF_SERVICE_URL` bez schematu (`host:port`) jest OK - brama sama dostawia `http://`.
- **Darmowy Render:** darmowe web services **nie mogą przyjmować ruchu po sieci prywatnej**, więc bramy nie dobijesz do usługi PDF wewnętrznie. Ustaw `PDF_SERVICE_URL` na **publiczny adres** usługi PDF (`https://<twoja-usluga-pdf>.onrender.com`) i ustaw **ten sam** `PDF_SERVICE_SECRET` na obu usługach. Brama wysyła go jako nagłówek `X-Pdf-Secret`, a usługa PDF odrzuca żądania bez niego (`403`) - dzięki temu publiczny endpoint PDF nie jest otwarty. Wygeneruj: `openssl rand -hex 32`.

Gdy `PDF_SERVICE_SECRET` jest pusty, usługa PDF jest otwarta (do użytku w sieci prywatnej). `GET /health` zawsze pomija ten check.

---

## Odbieranie Faktur

KSeF nie wysyła powiadomień e-mail/webhook - faktury wystawione na Ciebie po prostu leżą w systemie. Te endpointy pozwalają je znaleźć bez wcześniejszej znajomości ich numeru KSeF.

Oba endpointy szukają faktur, gdzie **Twój NIP jest nabywcą** (`Podmiot2`), zgodnie z tym, jak KSeF rozwiązuje kontekst wywołującego - patrz [Tryb Multi-NIP](#tryb-multi-nip), jeśli prowadzisz usługę dla więcej niż jednej firmy: przekaż `X-KSeF-NIP`, żeby wybrać, w skrzynce którego NIP-u szukać. Wymaga tokenu z uprawnieniem `InvoiceRead` - patrz uwaga w [Token Produkcyjny](#token-produkcyjny-krok-po-kroku).

### Przeglądaj to, co odebrałeś

```bash
curl "http://localhost:8080/ksef/invoices/received?from=2026-06-01&to=2026-07-01" \
  -H "X-KSeF-NIP: TWOJ_NIP"
```

```json
{
  "success": true,
  "data": {
    "invoices": [
      {
        "ksefNumber": "8094031464-20260701-3EA2E3400000-26",
        "invoiceNumber": "FV/DOSTAWCA/2026/042",
        "issueDate": "2026-07-01T00:00:00+00:00",
        "permanentStorageDate": "2026-07-01T06:54:30.083815+00:00",
        "sellerNip": "8094031464",
        "sellerName": "Dostawca Testowy sp. z o.o.",
        "netAmount": 555, "grossAmount": 682.65, "vatAmount": 127.65,
        "currency": "PLN",
        "hasAttachment": false
      }
    ],
    "hasMore": false
  }
}
```

Pobierz PDF tak samo, jak dla faktury, którą wysłałeś - `GET /ksef/invoice/{ksefNumber}/pdf` działa dla obu ról, żaden dodatkowy endpoint nie jest potrzebny.

| Parametr zapytania | Domyślnie | Uwagi |
|---|---|---|
| `from` | 30 dni temu | Data/data-czas ISO 8601 |
| `to` | teraz | Data/data-czas ISO 8601. **KSeF ogranicza zakres `from`-`to` do 3 miesięcy na wywołanie** - przechodź przez starszą historię kilkoma wywołaniami |
| `page` | `0` | Offset strony liczony od zera |
| `pageSize` | `50` | Max faktur na stronę |

### Odpytuj o nowe faktury (synchronizacja / powiadomienia)

```bash
# Pierwsze wywołanie - jeszcze bez checkpointu
curl "http://localhost:8080/ksef/invoices/received/new" -H "X-KSeF-NIP: TWOJ_NIP"
# → {"invoices": [...], "nextSince": "2026-07-01T07:00:00Z"}

# Zapisz nextSince samodzielnie, przekaż z powrotem następnym razem - wrócą tylko naprawdę nowe faktury
curl "http://localhost:8080/ksef/invoices/received/new?since=2026-07-01T07:00:00Z" -H "X-KSeF-NIP: TWOJ_NIP"
```

Podłącz to do workflow cron/n8n odpytującego co 15-30 minut, żeby dostawać powiadomienia (e-mail/Slack/webhook), gdy coś nowego się pojawi - sama usługa pozostaje bezstanowa, Twój workflow trzyma checkpoint.

Endpoint `query/metadata` KSeF (który to opakowuje) jest ograniczony do **20 żądań/godzinę** - własny limiter żądań usługi egzekwuje to proaktywnie (429 z `Retry-After`, zanim żądanie w ogóle dotrze do KSeF). Nie odpytuj częściej niż co 15 minut, zgodnie z zaleceniem samego KSeF. Przy bardzo dużym wolumenie faktur użyj zamiast tego surowego endpointu `invoice-download/export-invoices` (asynchroniczny eksport wsadowy) - jeszcze nie opakowanego tutaj w przyjazny endpoint.

**Gotowy do zaimportowania przykład n8n:** [`examples/n8n/receive-invoices.json`](examples/n8n/receive-invoices.json) ([wersja polska](examples/n8n/receive-invoices-PL.json)) - odpytuje `/ksef/invoices/received/new` co 20 minut, trzyma checkpoint we własnych danych statycznych workflow, pobiera PDF każdej nowej faktury na dysk i zostawia węzeł `Notify (TODO)` do podłączenia Slacka/e-maila/Discorda.

---

## Faktury, Które Wystawiłeś

`GET /ksef/invoices/issued` przegląda faktury, gdzie **Twój NIP jest sprzedawcą** (`Podmiot1`) - lustrzane odbicie [Odbierania Faktur](#odbieranie-faktur) powyżej, na sytuację gdy już wiesz, że coś wysłałeś, ale nie masz pod ręką numeru KSeF (np. zgubiłeś odpowiedź, albo budujesz ekran "moje faktury"). Ta sama autoryzacja, te same limity żądań, ten sam wybór multi-NIP przez `X-KSeF-NIP`.

```bash
curl "http://localhost:8080/ksef/invoices/issued?from=2026-06-01&to=2026-07-01" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -H "X-KSeF-NIP: TWOJ_NIP"
```

```json
{
  "success": true,
  "data": {
    "invoices": [
      {
        "ksefNumber": "8094031464-20260701-3EA2E3400000-26",
        "invoiceNumber": "FV/2026/001",
        "issueDate": "2026-07-01T00:00:00+00:00",
        "permanentStorageDate": "2026-07-01T06:54:30.083815+00:00",
        "buyerIdentifierType": "Nip",
        "buyerIdentifierValue": "5265877635",
        "buyerName": "Klient Testowy sp. z o.o.",
        "netAmount": 555, "grossAmount": 682.65, "vatAmount": 127.65,
        "currency": "PLN",
        "hasAttachment": false
      }
    ],
    "hasMore": false
  }
}
```

Tożsamość nabywcy to `buyerIdentifierType`/`buyerIdentifierValue` zamiast płaskiego pola NIP - KSeF dopuszcza nabywców identyfikowanych czymś innym niż NIP (np. `VatUe` dla numerów VAT UE), w przeciwieństwie do sprzedawców, którzy zawsze są identyfikowani przez NIP.

Te same parametry zapytania `from`/`to`/`page`/`pageSize` i limit 3-miesięcznego zakresu co w [Odbieraniu Faktur](#odbieranie-faktur). Pobierz PDF tym samym endpointem `GET /ksef/invoice/{ksefNumber}/pdf` używanym wszędzie indziej. Nie ma tu wariantu odpytywania/`new` - w przeciwieństwie do faktur odebranych od kogoś innego, już wiesz, kiedy coś wysłałeś, więc nie ma o czym być powiadamianym.

---

## Integracja z E-Commerce (Sellf, WooCommerce itd.)

KSeF Gateway to samodzielna usługa - wdróż ją osobno i wywołuj jej API ze swojej platformy e-commerce. Żadnych wtyczek, żadnego SDK, tylko HTTP.

### Przepływ

```
Klient płaci → Webhook platformy → (opcjonalnie: transformacja n8n) → POST /ksef/invoice → numer KSeF
```

### Opcja 1: Integracja bezpośrednia

Dodaj wywołanie `POST /ksef/invoice` w handlerze udanej płatności:

```bash
curl -X POST https://twoj-serwer-ksef.onrender.com/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": { "nip": "TWOJ_NIP", "name": "Twoja Firma", "address": { "street": "...", "city": "..." } },
    "buyer": { "nip": "NIP_NABYWCY", "name": "Klient", "address": { "street": "...", "city": "..." } },
    "items": [{ "name": "Nazwa produktu", "quantity": 1, "unitPrice": 100, "vatRate": 23 }],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'
```

### Opcja 2: n8n jako pośrednik (no-code, zalecane)

Większość platform wysyła webhooki we własnym formacie, nie w formacie KSeF. Użyj [n8n](https://n8n.io/), żeby to przetłumaczyć:

1. **Węzeł Webhook** - odbiera webhook płatności z Twojej platformy (Sellf, WooCommerce, Stripe itd.)
2. **Węzeł Transform** - mapuje JSON platformy na format faktury KSeF (sprzedawca, nabywca, pozycje)
3. **Węzeł HTTP Request** - wysyła `POST /ksef/invoice` do Twojej usługi

Zero zmian kodu w Twojej platformie e-commerce. Skonfiguruj URL webhooka w ustawieniach platformy, resztą zajmie się n8n.

Gotowe do zaimportowania workflow w [`examples/n8n/`](examples/n8n/):
- **Sellf → KSeF** (`sellf-ksef.json`) - produkty cyfrowe, sprawdzanie NIP, dane sprzedawcy ze zmiennych n8n
- **WooCommerce → KSeF** (`woocommerce-ksef.json`) - zamówienia WooCommerce, automatyczne wykrywanie stawki VAT, pomijanie konsumentów
- **Odbieranie i Pobieranie Faktur** (`receive-invoices.json`) - odpytuje o faktury wystawione *na Ciebie* co 20 minut, pobiera PDF-y, z checkpointem, żeby nic nie zostało pominięte ani zeskanowane ponownie - patrz [Odbieranie Faktur](#odbieranie-faktur)

### Wdróż usługę

Potrzebujesz działającej instancji KSeF Gateway. Wybierz jedną:

- **[Wdróż na Render](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)** (jednym kliknięciem, darmowy tier)
- **StackPilot**: `./local/deploy.sh ksef-gateway --ssh=vps` ([github.com/jurczykpawel/stackpilot](https://github.com/jurczykpawel/stackpilot))
- **Docker Compose**: `docker compose up` (self-hosted)

---

## Jak Działa Generator Tokenów

Generator tokenów automatyzuje to, co normalnie wymaga kwalifikowanego podpisu elektronicznego. Działa **tylko w środowisku TEST**, używając certyfikatu samopodpisanego.

```
Krok 1: Wygeneruj losowy NIP z poprawną sumą kontrolną
Krok 2: POST /auth/challenge → pobierz jednorazowe wyzwanie z KSeF
Krok 3: Utwórz certyfikat X.509 samopodpisany (akceptowany tylko na TEST)
Krok 4: Zbuduj XML AuthTokenRequest, podpisz XAdES
Krok 5: POST /auth/xades-signature → wyślij podpisane żądanie autoryzacji
Krok 6: Odpytuj GET /auth/{ref} aż status = 200 (autoryzacja zakończona)
Krok 7: POST /auth/token/redeem → dostań accessToken + refreshToken
Krok 8: POST /tokens → wygeneruj token KSeF z InvoiceRead + InvoiceWrite
Krok 9: Odpytuj aż status tokenu = Active
```

Wynik: `KSEF_TOKEN`, `KSEF_NIP`, `KSEF_ENV` - wklej do `.env`.

Token żyje aż do unieważnienia. Usługa używa go codziennie: szyfruje go kluczem publicznym KSeF, dostaje JWT, auto-odświeża przed wygaśnięciem.

### Jak Działa Generator Certyfikatów

`docker compose --profile tools run --rm cert-generator` robi certyfikatowy odpowiednik - też **tylko TEST**, używając jednorazowego certyfikatu samopodpisanego:

```
Krok 1: Wygeneruj losowy NIP z poprawną sumą kontrolną
Krok 2: Utwórz certyfikat X.509 samopodpisany (akceptowany tylko na TEST)
Krok 3: POST /auth/challenge → pobierz jednorazowe wyzwanie z KSeF
Krok 4: Zbuduj XML AuthTokenRequest, podpisz XAdES
Krok 5: POST /auth/xades-signature → wyślij podpisane żądanie autoryzacji
Krok 6: Odpytuj GET /auth/{ref} aż status = 200 (autoryzacja zweryfikowana)
Krok 7: Wyeksportuj certyfikat + klucz prywatny jako PEM do ./output
```

Wynik: `test-cert.crt`, `test-cert.key`, `test-cert.nip` w `./output` - te same pliki, które dostajesz pobierając prawdziwy certyfikat produkcyjny z portalu, tylko samopodpisane zamiast wydanego przez MF. W przeciwieństwie do generatora tokenów, zatrzymuje się po zweryfikowaniu, że autoryzacja się powiodła - dowodzi, że kod ładowania certyfikatu w usłudze działa, nie tworzy długo żyjących poświadczeń.

### KSeF wchodzi obowiązkowo — etapami

Obowiązek e-fakturowania przez KSeF jest w Polsce wprowadzany etapami i obejmuje zarówno **wystawianie**, jak i **odbieranie** faktur — kolejne grupy firm są nim stopniowo obejmowane. Dokładne terminy i zwolnienia bywają aktualizowane, więc to, co dotyczy Twojej firmy, sprawdź w oficjalnym źródle: [podatki.gov.pl/ksef](https://www.podatki.gov.pl/ksef/).

Wniosek jest prosty: prędzej czy później integracja z KSeF przestaje być opcjonalna. Lepiej mieć ją gotową i przetestowaną wcześniej, niż wdrażać na ostatnią chwilę — poniższa konfiguracja produkcyjna właśnie to umożliwia.

### Test vs Produkcja

| Krok | TEST | PRODUKCJA |
|------|------|------------|
| Dowód tożsamości | Certyfikat samopodpisany (automatyczny) | Profil Zaufany (darmowy), kwalifikowany podpis elektroniczny, albo kwalifikowana pieczęć elektroniczna |
| NIP | Losowy, dowolna wartość | Prawdziwy, zarejestrowany w urzędzie skarbowym |
| Generowanie tokenu | Ten sam przepływ | Ten sam przepływ |
| Kto to robi | Skrypt (jedna komenda) | Właściciel firmy albo upoważniony przedstawiciel (jednorazowo) |

### Token Produkcyjny (krok po kroku)

Uzyskanie tokenu produkcyjnego zajmuje około 10 minut i dla większości firm **nic nie kosztuje**. **Profil Zaufany jest darmowy** (prawdopodobnie masz go już przez logowanie do banku albo mObywatel) i wystarcza do wygenerowania tokenu - **nie** musisz kupować kwalifikowanego podpisu elektronicznego.

> **Jednoosobowa działalność gospodarcza (JDG):** Zaloguj się bezpośrednio, żadna wcześniejsza rejestracja nie jest potrzebna.
> **Spółki (sp. z o.o., SA, fundacje):** Przed pierwszym logowaniem złóż formularz **ZAW-FA** w urzędzie skarbowym - jednorazowa formalność - chyba że firma ma już kwalifikowaną pieczęć elektroniczną przypisaną do swojego NIP-u, która to zastępuje.

**Przez Aplikację Podatnika KSeF 2.0:**

1. Wejdź na [ap.ksef.mf.gov.pl](https://ap.ksef.mf.gov.pl/)
2. Kliknij **Zaloguj** i uwierzytelnij się jednym z:
   - **Profil Zaufany** (ePUAP / mObywatel / logowanie bankiem) - darmowe, bez wcześniejszej konfiguracji, najłatwiejsza ścieżka dla JDG
   - Podpis kwalifikowany (SimplySign, Certum, Szafir)
   - Pieczęć kwalifikowana (tylko firmy - też zastępuje wymóg ZAW-FA powyżej)
   - e-Dowód (elektroniczny dowód osobisty z NFC)
3. Wpisz **NIP** swojej firmy i kliknij **Uwierzytelnij**
4. Przejrzyj i podpisz żądanie uwierzytelnienia
5. Przejdź do zakładki **Tokeny**
6. Kliknij **Generuj token**
7. Wpisz opisową **nazwę** tokenu (np. "ksef-gateway API")
8. Wybierz uprawnienia:
   - **Wystawianie faktur** (InvoiceWrite) - wysyłanie faktur
   - **Odczyt faktur** / **Przeglądanie faktur** (InvoiceRead) - pobieranie faktur (**wymagane** też dla [Odbierania Faktur](#odbieranie-faktur) i [Faktur, Które Wystawiłeś](#faktury-które-wystawiłeś) - `/ksef/invoices/received`, `/ksef/invoices/issued` i `/ksef/invoice/{ksefNumber}` wszystkie tego potrzebują, nie tylko `/ksef/send`)
9. Potwierdź swoją metodą uwierzytelniania
10. **Skopiuj token natychmiast** - jest wyświetlany tylko raz
11. Ustaw w swoim `.env`:
    ```
    KSEF_TOKEN=<token, który właśnie skopiowałeś>
    KSEF_NIP=<NIP Twojej firmy>
    KSEF_ENV=PRODUCTION
    ```
12. Zrestartuj usługę. Żadnych zmian kodu, żadnego przebudowywania - tylko zmienne środowiskowe powyżej.

Musisz to zrobić tylko raz. Jeśli zgubisz token, unieważnij go w portalu i wygeneruj nowy.

> **Kto może wygenerować token?** Tylko osoba upoważniona do reprezentowania firmy (właściciel, członek zarządu wpisany w KRS, albo ktoś z uprawnieniem KSeF nadanym przez nich).

### Autoryzacja Certyfikatem (Alternatywa dla Tokenów)

Zamiast tokenu usługa może uwierzytelniać się **certyfikatem KSeF** - parą certyfikat + klucz prywatny wydaną przez portal KSeF. Każde (ponowne) logowanie jest podpisywane certyfikatem (XAdES) zamiast prezentowania statycznego sekretu. To oficjalnie wspierana ścieżka autoryzacji.

**Wypróbuj najpierw na TEST:**

```bash
docker compose --profile tools run --rm cert-generator
```

Jedna komenda generuje jednorazowy certyfikat samopodpisany, weryfikuje że faktycznie uwierzytelnia się w żywym API KSeF TEST i zapisuje `test-cert.crt` + `test-cert.key` do `./output`, plus losowy NIP, którym się zarejestrował. Wskaż `KSEF_CERT_PATH`/`KSEF_KEY_PATH`/`KSEF_NIP` na te pliki z `KSEF_ENV=TEST`, żeby wypróbować cały przepływ przed dotknięciem prawdziwego certyfikatu. Certyfikaty samopodpisane działają tylko na TEST - produkcja potrzebuje prawdziwego z portalu, poniżej.

**Uzyskanie certyfikatu produkcyjnego:**

1. Zaloguj się na [ap.ksef.mf.gov.pl](https://ap.ksef.mf.gov.pl/) tak samo jak dla tokenu (Profil Zaufany, podpis kwalifikowany itd.)
2. Przejdź do **Certyfikaty** → **Wnioskuj o certyfikat**
3. Nazwij certyfikat i ustaw hasło chroniące klucz prywatny (portal egzekwuje własne zasady dla obu - postępuj zgodnie z tym, o co aktualnie pyta formularz)
4. Pobierz dwa wygenerowane pliki: certyfikat (`.crt`) i klucz prywatny (`.key`), oba w formacie PEM

**Używanie go w usłudze:**

```
KSEF_CERT_PATH=/app/certs/company.crt
KSEF_KEY_PATH=/app/certs/company.key
KSEF_KEY_PASSWORD=<tylko jeśli klucz prywatny jest chroniony hasłem>
KSEF_NIP=<NIP Twojej firmy>
KSEF_ENV=PRODUCTION
```

Albo per-kontekst w `contexts.json`:

```json
{
  "nip": "1234567890",
  "certificatePath": "/app/certs/company.crt",
  "privateKeyPath": "/app/certs/company.key",
  "privateKeyPassword": "tylko-jesli-zaszyfrowany",
  "label": "Firma A (certyfikat)"
}
```

Zamontuj pliki cert/klucz tylko do odczytu, tak samo jak `contexts.json`:

```yaml
volumes:
  - ./certs:/app/certs:ro
```

Kontekst potrzebuje dokładnie jednego z: `token`, `certificatePath` + `privateKeyPath`, albo `certificateContent` + `privateKeyContent`. Wszystko inne (endpointy, limity żądań, multi-NIP) działa identycznie niezależnie od tego, którego używa kontekst.

**Brak dostępnych montowań plików (Lambda, Container Apps)?** Użyj zamiast tego `certificateContent`/`privateKeyContent` (albo `KSEF_CERT_CONTENT`/`KSEF_KEY_CONTENT`) - surowego tekstu PEM zamiast ścieżki. Preferuj formę opartą na ścieżce wszędzie, gdzie montowanie pliku jest praktyczne (bind mount Docker Compose, Render Secret Files) - plik pozostaje poza zwykłymi zrzutami zmiennych środowiskowych (`docker inspect`, listy procesów) w sposób, w jaki sekret oparty na treści nie może. Patrz [Wdrożenie w Chmurze](#wdrożenie-w-chmurze), jak to jest podłączone na każdej platformie.

---

## Konfiguracja

| Zmienna | Wymagana | Domyślnie | Opis |
|----------|----------|---------|-------------|
| `GATEWAY_API_KEY` | Tak | - | Współdzielony sekret, który wywołujący muszą wysyłać jako `X-Api-Key` - patrz [Bezpieczeństwo](#bezpieczeństwo). Usługa zawodzi domknięta (503), jeśli nieustawiona |
| `KSEF_TOKEN` | Tak* | - | Token autoryzacji KSeF |
| `KSEF_CERT_PATH` | Tak* | - | Ścieżka do certyfikatu KSeF (PEM) - alternatywa dla `KSEF_TOKEN`, patrz [Autoryzacja Certyfikatem](#autoryzacja-certyfikatem-alternatywa-dla-tokenów) |
| `KSEF_KEY_PATH` | Tak* | - | Ścieżka do klucza prywatnego certyfikatu (PEM) - wymagana razem z `KSEF_CERT_PATH` |
| `KSEF_CERT_CONTENT` | Tak* | - | Certyfikat KSeF jako surowa treść PEM - alternatywa dla `KSEF_CERT_PATH` na platformach bez montowania plików |
| `KSEF_KEY_CONTENT` | Tak* | - | Klucz prywatny jako surowa treść PEM - wymagany razem z `KSEF_CERT_CONTENT` |
| `KSEF_KEY_PASSWORD` | Nie | - | Hasło do klucza prywatnego, jeśli jest zaszyfrowany (działa z formą ścieżki albo treści) |
| `KSEF_NIP` | Tak | - | NIP dla kontekstu autoryzacji |
| `KSEF_ENV` | Nie | `TEST` | Środowisko: `TEST`, `DEMO`, `PRODUCTION` |
| `KSEF_API_PORT` | Nie | `8080` | Port API usługi |
| `KSEF_QR_URL` | Nie | `https://qr-test.ksef.mf.gov.pl` | Bazowy URL weryfikacji QR |
| `GITHUB_PAT` | Build | - | GitHub PAT z `read:packages` dla SDK CIRFMF |
| `KSEF_CONTEXTS_FILE` | Nie | `/app/contexts.json` | Ścieżka do pliku konfiguracji multi-NIP |
| `GATEWAY_LICENSE` | Nie | - | Klucz licencji multi-NIP - potrzebny tylko dla więcej niż 1 NIP, patrz [Licencjonowanie Multi-NIP](#licencjonowanie-multi-nip) |

\* Podaj dokładnie jedno: `KSEF_TOKEN`, `KSEF_CERT_PATH` + `KSEF_KEY_PATH`, albo `KSEF_CERT_CONTENT` + `KSEF_KEY_CONTENT`.

### Tryb Multi-NIP

Żeby obsługiwać faktury dla wielu firm, stwórz plik `contexts.json`:

```json
[
  {
    "nip": "1234567890",
    "token": "token-ksef-dla-firmy-a",
    "label": "Firma A"
  },
  {
    "nip": "0987654321",
    "certificatePath": "/app/certs/firma-b.crt",
    "privateKeyPath": "/app/certs/firma-b.key",
    "label": "Firma B (certyfikat)"
  }
]
```

Konteksty mogą dowolnie mieszać tokeny i certyfikaty - patrz [Autoryzacja Certyfikatem](#autoryzacja-certyfikatem-alternatywa-dla-tokenów) powyżej. Każdy kontekst autoryzuje się niezależnie, więc to pokrywa też konfigurację w stylu biura rachunkowego - **jeden certyfikat reprezentujący kilka NIP-ów klientów** (wypisz ten sam `certificatePath`/`certificateContent` pod wieloma kontekstami, po jednym na NIP). Sama usługa jest niezależna od NIP-u co do tego, który certyfikat stoi za którym kontekstem; to, czy dany certyfikat faktycznie może uwierzytelnić się w NIP-ie, do którego formalnie nie należy, zależy od autoryzacji samego KSeF po stronie tego NIP-u (nadanie praw reprezentacji/`ProxyEntity` posiadaczowi certyfikatu) - to prawdziwy krok w portalu KSeF, wykonywany per firma, poza kontrolą usługi, i coś, czego własne narzędzia TEST tego projektu nie sprawdzają (`CertGenerator` zawsze tworzy certyfikat ograniczony do jednego NIP-u, którym się rejestruje).

Zamontuj go w Docker Compose (już skonfigurowane w `docker-compose.yml`):

```yaml
volumes:
  - ./contexts.json:/app/contexts.json:ro
```

Usługa automatycznie wykrywa, którego NIP-u użyć, na podstawie:
1. Nagłówka `X-KSeF-NIP` (jawny)
2. NIP-u sprzedawcy z ciała faktury
3. Kontekstu domyślnego (pierwszy na liście albo ze zmiennej środowiskowej `KSEF_NIP`)

Sprawdź uwierzytelnione konteksty: `GET /ksef/contexts`

> **Uwaga:** Zmienne środowiskowe `KSEF_TOKEN` + `KSEF_NIP` nadal działają w trybie single-NIP. Jeśli obecne są jednocześnie zmienne środowiskowe i `contexts.json`, kontekst ze zmiennych środowiskowych jest dodawany do listy.

### Licencjonowanie Multi-NIP

Jeden NIP jest darmowy, zawsze - żadnej licencji, żadnego limitu czasowego. Prowadzenie **więcej niż jednego NIP-u** (biura rachunkowe, grupy holdingowe, agencje obsługujące kilku klientów) wymaga licencji multi-NIP - jednorazowa opłata, odblokowuje nielimitowane NIP-y na tej instancji na zawsze.

**[👉 Kup licencję multi-NIP](https://sellf.techskills.academy/p/ksef-gateway-multi-nip)**

Ustaw klucz licencji jako `GATEWAY_LICENSE`. Weryfikacja jest w pełni offline (sprawdzenie podpisu ECDSA wobec zbuforowanego klucza publicznego) - token licencji nigdy nie opuszcza Twojego serwera, a chwilowa awaria sieci nie blokuje Ci dostępu (patrz niżej).

Jeśli `contexts.json` konfiguruje więcej NIP-ów niż pozwala Twoja licencja, usługa nie odmawia startu - aktywuje pierwsze `N` (Twój domyślny NIP jest zawsze zachowany, nawet jeśli nie był pierwszy w pliku) i loguje wyraźne ostrzeżenie, wymieniając które zostały pominięte. Sprawdź `GET /ksef/status` po `license: {licensed, maxNips, activeNips, email, expiresAt}` (`email` jest tylko dla Twojej własnej informacji - do jakiego zakupu należy ta licencja - nie jest sprawdzane wobec niczego).

| Sytuacja | Zachowanie |
|---|---|
| `GATEWAY_LICENSE` nieustawiony | Darmowy tier - tylko pierwszy skonfigurowany NIP |
| Ważna licencja | Nielimitowane NIP-y |
| Licencja wygasła/unieważniona/źle sformowana | Powrót do darmowego tieru (1 NIP) - nigdy nie crashuje usługi |
| Serwer licencji Sellf chwilowo nieosiągalny | Serwuje ostatni zweryfikowany wynik przez do 7 dni (zbuforowany), potem powraca do darmowego tieru - awaria nie blokuje Cię wstecznie, ale też nie przedłuża się w nieskończoność |

---

## Architektura

```
docker compose up
       |
  ksef-api:8080            ksef-pdf:3000
  (ASP.NET 9)              (Node.js + tsx)
       |                        |
  CIRFMF C# SDK           CIRFMF ksef-pdf-generator
  (NuGet, GitHub Pkgs)     (submoduł git)
       |
  API KSeF (MF)
  TEST / DEMO / PROD
```

Dwa kontenery, bez bazy danych, bez Redis. Stan autoryzacji w pamięci (restart = ponowna autoryzacja w sekundy).

### Kluczowe Komponenty

| Komponent | Rola |
|-----------|------|
| **SdkReflector** | Odkrywa interfejsy SDK przez refleksję .NET przy starcie |
| **EndpointMapper** | Rejestruje każdą metodę jako `POST /ksef/{grupa}/{metoda}` |
| **TokenPool** | Usługa w tle: autoryzacja KSeF per-NIP (token albo certyfikat/XAdES) + auto-odświeżanie |
| **WorkflowEndpoints** | Wysokopoziomowe: `/ksef/send`, `/ksef/send/json`, `/ksef/invoice/{nr}/pdf` |
| **InvoiceDownloadEndpoints** | Wysokopoziomowe: `/ksef/invoices/received`, `/ksef/invoices/received/new`, `/ksef/invoices/issued` |
| **EndpointErrorHandling** | Współdzielony `Guard()` - pozwala błędom limitu żądań/wyłącznika obwodu/API KSeF wypłynąć jako poprawne 429/503/502 (z `Retry-After`) zamiast płaskiego 500 |
| **Usługa PDF** | XML do PDF przez bibliotekę CIRFMF + generowanie kodu QR |
| **JSON-do-XML** | `js2xml()` - konwersja JSON/XML zero-utrzymania |

### Zasady Projektowe
- **Cienki wrapper** - zero logiki biznesowej, tłumaczenie HTTP-na-SDK
- **Auto-adaptacyjny** - zmiany SDK propagują się przy przebudowie (refleksja)
- **Przejrzysta kryptografia** - wywołujący wysyłają plaintext, usługa szyfruje
- **JSON zero-utrzymania** - odzwierciedla XML 1:1, brak kodu mapującego
- **Odporny** - błędy autoryzacji nie crashują, ponawiają automatycznie

---

## Stack Technologiczny

| Technologia | Rola |
|------------|------|
| [ASP.NET 9 Minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) | Warstwa HTTP usługi |
| [CIRFMF KSeF.Client](https://github.com/CIRFMF/ksef-client-csharp) | Oficjalne SDK KSeF (Ministerstwo Finansów) |
| [CIRFMF ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator) | Oficjalne generowanie PDF z XML FA(3) |
| [xml-js](https://www.npmjs.com/package/xml-js) | Dwukierunkowa konwersja JSON/XML |
| [pdfmake](http://pdfmake.org/) | Renderowanie PDF z natywnym wsparciem QR |
| [Scalar](https://scalar.com/) | UI dokumentacji API |
| [Docker Compose](https://docs.docker.com/compose/) | Orkiestracja |

---

## Wdrożenie w Chmurze

### Render (jednym kliknięciem)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)

Kliknij przycisk, ustaw trzy zmienne środowiskowe (`GITHUB_PAT`, `KSEF_TOKEN`, `KSEF_NIP`), gotowe. Obie usługi (API + PDF) wdrażają się automatycznie z `render.yaml`. Wolisz certyfikat? Zostaw `KSEF_TOKEN` puste, wgraj cert/klucz jako [Secret Files](https://render.com/docs/configure-environment-variables) w dashboardzie i ustaw `KSEF_CERT_PATH`/`KSEF_KEY_PATH` na `/etc/secrets/<nazwapliku>` zamiast tego.

**Multi-NIP z kilkoma certyfikatami na Render:** Secret Files nie są ograniczone do jednej pary - wgraj tyle plików cert/klucz, ile masz firm (np. `firma-a.crt`/`firma-a.key`, `firma-b.crt`/`firma-b.key`, ...), *plus* sam `contexts.json` jako kolejny Secret File, gdzie `certificatePath`/`privateKeyPath` każdego wpisu wskazuje na odpowiedni `/etc/secrets/<nazwapliku>`. Potem ustaw `KSEF_CONTEXTS_FILE=/etc/secrets/contexts.json` (zmienna środowiskowa) zamiast `KSEF_TOKEN`/`KSEF_CERT_PATH`. Wymaga [licencji multi-NIP](#licencjonowanie-multi-nip) dla więcej niż jednego NIP-u.

### AWS Lambda

Wdróż jako funkcję serverless Lambda z Function URL (bez API Gateway - unika timeoutu 29s).

```bash
cd deploy/aws
sam build --build-arg GITHUB_PAT=<twój-pat>
sam deploy --guided
```

Szczegóły w [`deploy/aws/README.md`](deploy/aws/README.md).

### Azure Container Apps

Wdróż jako zarządzane kontenery - odzwierciedla Docker Compose, zero zmian kodu.

```bash
az deployment group create \
  --resource-group ksef-gateway \
  --template-file deploy/azure/main.bicep \
  --parameters ksefToken=<token> ksefNip=<nip>
```

Szczegóły w [`deploy/azure/README.md`](deploy/azure/README.md).

| | Docker Compose | Render | AWS Lambda | Azure Container Apps |
|---|---|---|---|---|
| Konfiguracja | `docker compose up` | Przycisk jednym kliknięciem | SAM CLI | Azure CLI + Bicep |
| Cold start | Brak | ~30s (darmowy tier) | ~3-5s | ~5-10s (albo 0 z minReplicas=1) |
| Koszt (mały ruch) | Koszt serwera | Dostępny darmowy tier | Prawie zero | ~40-60 zł/miesiąc |
| Usługa PDF | Wliczona | Wliczona | Osobne wdrożenie | Wliczona (kontener wewnętrzny) |
| Multi-NIP | Montowanie `contexts.json` | Zmienne środowiskowe | Zmienne środowiskowe (single-NIP) | Zmienne środowiskowe albo Azure Files |
| Autoryzacja certyfikatem | Montowanie pliku (`certificatePath`) | Secret Files (`certificatePath`) | Treść (`certificateContent`, bez montowania plików) | Treść (`certificateContent`, bez montowania plików) |

---

## Plan Rozwoju

- [x] Automatyczne odkrywanie 60+ endpointów SDK przez refleksję
- [x] Autoryzacja tokenem z odświeżaniem w tle
- [x] `POST /ksef/send` - wysyłanie faktury jednym wywołaniem (XML)
- [x] `POST /ksef/send/json` - wysyłanie faktury w JSON (format zero-utrzymania)
- [x] `GET /ksef/invoice/{nr}/pdf` - PDF ze zweryfikowanym kodem QR
- [x] Generowanie PDF oficjalną biblioteką CIRFMF
- [x] Generator tokenów dla środowiska TEST
- [x] Dokumentacja API Scalar
- [x] Konfiguracja jedną komendą Docker
- [x] Limitowanie żądań po stronie klienta (proaktywne, zgodnie z oficjalnymi limitami MF)
- [x] `POST /ksef/invoice` - przyjazny JSON z automatycznym liczeniem VAT
- [x] 81 testów jednostkowych/integracyjnych (SdkReflector, RateLimiter, InvoiceXmlBuilder, walidacja XSD, KSeF E2E)
- [x] GitHub Actions CI + skanowanie sekretów TruffleHog
- [x] Tryb multi-NIP / multi-tenant
- [x] Kolekcja Bruno do testowania ręcznego i automatycznego
- [x] Wsparcie wdrożenia na AWS Lambda
- [x] Wsparcie wdrożenia na Azure Container Apps

---

## Współpraca

Wkład mile widziany! Zobacz [CONTRIBUTING.md](CONTRIBUTING.md) po szczegóły.

---

## Licencja

AGPL-3.0 - zobacz [LICENSE](LICENSE)

Jeśli modyfikujesz KSeF Gateway i oferujesz go jako usługę, AGPL-3.0 wymaga, żebyś udostępnił zmodyfikowany kod źródłowy swoim użytkownikom.

---

## Podziękowania

- **[CIRFMF](https://github.com/CIRFMF)** (Centrum Informatyki Resortu Finansów) - oficjalne SDK KSeF, generator PDF i dokumentacja
- **[Dokumentacja KSeF](https://github.com/CIRFMF/ksef-docs)** - przewodnik integracji z KSeF 2.0
- **[Scalar](https://scalar.com/)** - UI dokumentacji API
