# Mapy adresów Modbus

Dwa połączenia Modbus TCP, dwie mapy. Adresy liczone od 0, unit ID = 1.
Źródłem prawdy jest kod: `Shared/ModbusMapA.cs` i `Shared/ModbusMapB.cs`.
Jeśli ten plik się z nimi nie zgadza, popraw ten plik.

## Mapa A: brama → PLC (PLC jest slave'em, 127.0.0.1:5020)

| Typ | Adres | Nazwa | Zapisuje | Czyta | Wartości |
|---|---|---|---|---|---|
| coil | 0 | CMD_START | brama | PLC | 1 = żądanie; PLC kasuje na 0 po obsłużeniu |
| coil | 1 | CMD_STOP | brama | PLC | jak wyżej |
| coil | 2 | CMD_ESTOP | brama | PLC | jak wyżej |
| coil | 3 | CMD_RESET | brama | PLC | jak wyżej |
| discrete input | 0 | ST_RUNNING | PLC | brama | 1 = PLC każe silnikowi pracować (RUN = 1) |
| discrete input | 1 | ST_ESTOP | PLC | brama | 1 = e-stop zatrzaśnięty |
| discrete input | 2 | ST_FAULT | PLC | brama | 1 = aktywna awaria (ALARM_CODE ≠ 0) |
| input register | 0 | STATE | PLC | brama | 0 stoi, 1 rozpędza się, 2 pracuje, 3 hamuje, 4 e-stop, 5 awaria |
| input register | 1 | RPM | PLC | brama | obr/min (kopia z silnika) |
| input register | 2 | TEMP | PLC | brama | 0,1 °C (kopia z silnika) |
| input register | 3 | ALARM_CODE | PLC | brama | 0 brak, 1 przegrzanie, 2 brak komunikacji z HMI, 3 brak komunikacji z napędem |
| holding register | 0 | SPEED_SETPOINT | brama | PLC | obr/min, 0–3000, domyślnie 1500; większą wartość PLC obcina do 3000 |
| holding register | 1 | HEARTBEAT | brama | PLC | licznik zwiększany co 500 ms |

Tabele PLC mają dokładnie tyle komórek, ile jest w mapie. Zapytanie o adres spoza mapy
dostaje wyjątek Modbus 02 (Illegal Data Address).

### Jak PLC wyznacza STATE

Sprawdzane po kolei, wygrywa pierwszy spełniony warunek:

1. e-stop zatrzaśnięty → **e-stop** (4)
2. aktywna awaria → **awaria** (5)
3. RUN = 1 i obroty mniejsze od nastawy o więcej niż 20 obr/min → **rozpędza się** (1)
4. RUN = 1 → **pracuje** (2)
5. RUN = 0 i obroty > 0 → **hamuje** (3)
6. w pozostałych przypadkach → **stoi** (0)

Gdy aktywnych jest kilka awarii naraz, ALARM_CODE pokazuje jedną, w kolejności:
napęd (3), przegrzanie (1), HMI (2).

## Mapa B: PLC → silnik (silnik jest slave'em, 127.0.0.1:5021)

| Typ | Adres | Nazwa | Zapisuje | Czyta | Wartości |
|---|---|---|---|---|---|
| coil | 0 | RUN | PLC | silnik | 1 = pracuj, 0 = wybieg do zatrzymania |
| coil | 1 | BRAKE | PLC | silnik | 1 = szybkie hamowanie (e-stop) |
| holding register | 0 | SPEED_REF | PLC | silnik | obr/min, 0–3000 |
| input register | 0 | RPM | silnik | PLC | obr/min |
| input register | 1 | TEMP | silnik | PLC | 0,1 °C (np. 253 = 25,3 °C) |
