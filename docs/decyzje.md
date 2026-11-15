# Decyzje

Zmiany kontraktów (Shared, mapy Modbus, JSON) i odstępstwa od dokumentu
„Koncepcja systemu OT – wersja finalna” (listopad 2026).

## Zakres tej wersji

| Decyzja | Dlaczego |
|---|---|
| Tylko Windows, uruchamianie przez `start.cmd` zamiast `start.sh` | Wersja pokazowa działa na komputerze służbowym z Windows |
| Bez Suricaty, bez programu atakującego, bez reguł IDS | Poza zakresem tej wersji |
| Schemat silnika w zwykłym SVG, bez JointJS+ | Plan B z dokumentu; zero zależności po stronie przeglądarki |
| .NET 10 | Aktualna wersja LTS |

## Rozszerzenia kontraktu

| Zmiana | Dlaczego |
|---|---|
| Nowe: dziennik poleceń z HMI (plik `commands.jsonl`, ostatnie 100 na ekranie); wiadomości `commandLog` i `command` | Widać, kto, kiedy i co kazał maszynie zrobić |
| Nowe polecenie `setSpeed` z polem `value` | Zadawanie prędkości z HMI (NICE w dokumencie) |
| `status` ma dodatkowe pola `speedSetpoint`, `running`, `estopLatched`, `fault` | HMI pokazuje nastawę i lampki z bitów ST_*, które brama i tak odczytuje (FC02) |
| Nowa wiadomość `link` | HMI odróżnia „brama nie działa” od „brama działa, ale nie widzi PLC” |
| Brama czyta dodatkowo holding register SPEED_SETPOINT (FC03) | Ekran pokazuje nastawę, którą faktycznie ma PLC, a nie tę, którą ostatnio wpisał operator |

## Doprecyzowania tam, gdzie dokument zostawiał wybór

| Temat | Przyjęte |
|---|---|
| Definicja STATE | Opisana w `docs/mapy-modbus.md`; „pracuje” od chwili, gdy do nastawy brakuje najwyżej 20 obr/min |
| RESET bez aktywnych blokad podczas pracy | Nie zatrzymuje silnika (RESET nie zmienia RUN) |
| BRAKE | Równy zatrzaskowi e-stopu: 1 od e-stopu do udanego RESET |
| Kilka awarii naraz | ALARM_CODE pokazuje jedną w kolejności reguł: napęd, przegrzanie, HMI |
| SPEED_SETPOINT powyżej 3000 | PLC obcina do 3000 i poprawia wartość w swoim rejestrze |
| Adres spoza mapy A | PLC odpowiada wyjątkiem Modbus 02 zamiast zwracać zera |
| RPM przy utracie łączności z napędem | PLC zostawia ostatnią odczytaną wartość |
| Heartbeat | Brama wysyła go zawsze, gdy działa, także gdy żadna przeglądarka nie jest podłączona |
| Sprawdzanie nagłówka Origin w `/ws` | Brama odrzuca WebSocket otwierany przez obcą stronę WWW; to jedyne zabezpieczenie dodane ponad dokument |
