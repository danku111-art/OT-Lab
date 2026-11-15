# OT-Lab

Mały system OT do nauki: operator w przeglądarce uruchamia, zatrzymuje i awaryjnie
zatrzymuje symulowany silnik DC, którym przez Modbus TCP steruje uproszczony sterownik (soft-PLC).

```
przeglądarka (HMI)  ──WebSocket/JSON──▶  Brama :8080  ──Modbus TCP, mapa A──▶  PLC :5020  ──Modbus TCP, mapa B──▶  Silnik :5021
```

Każdy element rozmawia tylko z sąsiadem. Wszystkie decyzje sterowania podejmuje PLC:
HMI wyświetla, brama tłumaczy JSON na Modbus, silnik liczy swoją fizykę.

> E-stop w tym projekcie to symulacja programowa. Nie jest funkcją bezpieczeństwa maszyny.

## Uruchomienie (Windows)

Potrzebny jest [.NET SDK 10](https://dotnet.microsoft.com/download). Node.js nie jest wymagany.

1. Dwuklik na `start.cmd`. Skrypt buduje rozwiązanie, otwiera trzy okna (Silnik, PLC, Brama) i przeglądarkę.
2. HMI: <http://localhost:8080>
3. Koniec: `stop.cmd` albo zamknięcie trzech okien.

Ręcznie, w trzech osobnych terminalach, w tej kolejności:

```
dotnet run --project Motor
dotnet run --project Plc
dotnet run --project Gateway
```

Jeśli brama wystartuje później niż 5 s po PLC, PLC zgłosi „brak komunikacji z HMI”.
To poprawne zachowanie watchdoga; wystarczy kliknąć Reset.

Testy:

```
dotnet test
```

## Co pokazać na demo

| Scenariusz | Jak | Co widać |
|---|---|---|
| Start i stop | Start, potem Stop | „rozpędza się” → „pracuje” → „hamuje” → „stoi” |
| E-stop | E-STOP podczas pracy, potem Start | szybkie hamowanie, stan „e-stop”, Start nic nie robi; w dzienniku Start ma wynik „wysłano” i stan przed „e-stop” |
| Reset | Reset po e-stopie | stan „stoi”, silnik nie rusza sam |
| Nastawa | wpisz 2400, Ustaw | silnik przyspiesza do nowej prędkości |
| Przegrzanie | klawisz `P` w oknie Silnik | po kilkunastu sekundach alarm i zatrzymanie; Reset działa dopiero poniżej 60 °C (wyłącz `P` i poczekaj) |
| Utrata silnika | zamknij okno Silnik | alarm „brak komunikacji z napędem”; po ponownym uruchomieniu silnika potrzebny Reset |
| Utrata bramy | zamknij okno Brama podczas pracy | w oknie PLC po 2 s alarm i RUN=0; HMI pokazuje „brak połączenia z bramą” |
| Utrata PLC | zamknij okno PLC | HMI pokazuje „brama nie ma połączenia z PLC” i kreski zamiast starych wartości |
| Dziennik | dowolne polecenia, potem restart bramy | historia zostaje na ekranie; cały plik: `Gateway/logs/commands.jsonl` |
| Dwie przeglądarki | otwórz HMI w dwóch oknach | polecenie z jednego okna pojawia się w dzienniku w obu |

## Projekty

| Katalog | Co to jest |
|---|---|
| `Shared` | Wspólny słownik: mapy adresów Modbus, stany, kody alarmów, kształt wiadomości. Bez logiki, bez pakietów NuGet. |
| `Motor` | Symulator silnika: slave Modbus (mapa B). Fizyka w `MotorModel`, warstwa Modbus w `MotorService`. |
| `Plc` | Soft-PLC: slave dla bramy (mapa A), master dla silnika (mapa B), cykl 100 ms. Cała logika w `PlcLogic`. |
| `Gateway` | Brama: serwuje HMI, WebSocket ↔ Modbus, heartbeat, dziennik poleceń. HMI jest w `wwwroot`. |
| `Hmi` | Kod TypeScript ekranu HMI. Skompilowany `app.js` jest w repozytorium, w `Gateway/wwwroot`. |
| `TestClient` | Konsola do rozmowy z PLC bezpośrednio przez Modbus, bez HMI i bramy. |
| `Tests` | Testy xUnit: reguły PLC, model silnika, kontrakt JSON, dziennik poleceń. |
| `docs` | [Mapy Modbus](docs/mapy-modbus.md), [kontrakt WebSocket](docs/websocket.md), [decyzje](docs/decyzje.md). |

Porty i czasy są w `appsettings.json` każdego programu. Adresy rejestrów zmienia się tylko w `Shared`.

### Zmiana HMI

Po zmianie `Hmi/src/app.ts` trzeba go skompilować (tu potrzebny jest Node.js):

```
cd Hmi
npm install
npm run build
```

Pliki `index.html` i `style.css` edytuje się bezpośrednio w `Gateway/wwwroot`; wystarczy odświeżyć stronę.

## Gdzie szukać w kodzie

- Reguły sterowania i ich kolejność: `Plc/PlcLogic.cs`, testy w `Tests/PlcLogicTests.cs`
- Polecenia jako impulsy, odczyt i kasowanie w jednym locku: `Plc/PlcMemory.cs`, metoda `TakeCommands`
- Cykl sterownika: `Plc/PlcService.cs`
- Tłumaczenie JSON → Modbus i wpis do dziennika: `Gateway/HmiHub.cs`, metoda `Execute`
- Dziennik poleceń: `Gateway/CommandLog.cs`
