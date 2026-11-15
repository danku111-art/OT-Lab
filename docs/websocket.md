# Kontrakt WebSocket: HMI ↔ brama

Adres: `ws://localhost:8080/ws`. Wiadomości to JSON, nazwy pól i wartości w camelCase.
Po stronie C# kształt wiadomości jest w `Shared/Messages.cs`, po stronie HMI w `Hmi/src/app.ts`.
Test `Tests/ContractTests.cs` pilnuje, żeby brama wysyłała dokładnie taki JSON jak tutaj.

## HMI → brama: polecenie

```json
{ "cmd": "start" }
{ "cmd": "setSpeed", "value": 1200 }
```

`cmd`: `start`, `stop`, `estop`, `reset` albo `setSpeed` (wtedy `value` = obr/min).

## Brama → HMI: stan (co ok. 200 ms)

```json
{
  "type": "status",
  "state": "running",
  "rpm": 1500,
  "temperatureCelsius": 42.5,
  "alarm": "none",
  "speedSetpoint": 1500,
  "running": true,
  "estopLatched": false,
  "fault": false
}
```

- `state`: `stopped`, `starting`, `running`, `braking`, `estop`, `fault`
- `alarm`: `none`, `overheat`, `hmiCommLost`, `driveCommLost`
- `running`, `estopLatched`, `fault`: bity ST_RUNNING, ST_ESTOP, ST_FAULT z PLC

## Brama → HMI: brak łączności z PLC

Wysyłane zamiast `status`, dopóki brama nie może odczytać PLC:

```json
{ "type": "link", "plcConnected": false }
```

## Brama → HMI: dziennik poleceń

Zaraz po połączeniu brama wysyła ostatnie 100 wpisów (najstarszy pierwszy):

```json
{ "type": "commandLog", "entries": [ { "time": "...", "cmd": "start", "...": "..." } ] }
```

Potem każde nowe polecenie, od dowolnej przeglądarki, trafia do wszystkich podłączonych HMI:

```json
{
  "type": "command",
  "entry": {
    "time": "2026-11-16T09:30:00.123+01:00",
    "cmd": "setSpeed",
    "value": 1200,
    "source": "127.0.0.1:50000",
    "result": "sent",
    "stateBefore": "stopped"
  }
}
```

- `result`: `sent` (brama zapisała polecenie do PLC), `rejected` (nieznane albo błędne
  polecenie, nic nie wysłano), `error` (brak łączności z PLC)
- `stateBefore`: ostatni stan PLC znany bramie w chwili polecenia; brak pola = brama nie miała łączności
- `value` jest tylko przy `setSpeed`, `detail` (opis) tylko przy `rejected` i `error`

`sent` nie znaczy „wykonane”. Start wysłany przy zatrzaśniętym e-stopie ma `result: "sent"`
i `stateBefore: "estop"`, a silnik stoi dalej, bo tak zdecydował PLC.

## Plik dziennika

Te same wpisy brama dopisuje do `Gateway/logs/commands.jsonl`, jedna linia JSON na polecenie.
Plik rośnie bez ograniczeń; ekran pokazuje ostatnie 100 wpisów, także po restarcie bramy.
Cały plik można pobrać z `http://localhost:8080/commands.jsonl`.
