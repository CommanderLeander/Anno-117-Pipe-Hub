# JSON Protocol

Every WebSocket message is exactly one JSON object. Required fields for every message:

| Field | Type | Rule |
|---|---|---|
| `schemaVersion` | Integer | Currently `1`; handle unknown values defensively |
| `type` | String | Event type; ignore unknown types |
| `receivedAtUtc` | String | ISO-8601 UTC timestamp when the Hub received it |

The machine-readable contract is in `schema/anno117-event.schema.json`.

## hub.status

Sent first after a successful connection and again whenever status changes.

```json
{"schemaVersion":1,"type":"hub.status","receivedAtUtc":"2026-09-25T12:00:00Z","hub":{"status":{"hubStatus":"Ready","pipeStatus":"Connected","connectedClients":1,"lastMessageAtUtc":"2026-09-25T11:59:58Z","protocolVersion":2,"error":null},"listener":{"lanEnabled":false,"bindAddress":"127.0.0.1","port":8766,"state":"Listening","connectedClients":1,"error":null}}}
```

`hubStatus` is the language-neutral machine value `Ready`; dashboard translations are presentation-only. `pipeStatus` is one of `Waiting`, `Connected`, `Disconnected`, `Timeout`, `NotFound`, or `Busy`; `lastMessageAtUtc`, `protocolVersion`, and `error` may be `null`. The listener port is an integer from 1024 through 65535.

## state.snapshot

Sent directly after `hub.status`. `snapshots` is an array of the currently held `area.production.statistics` objects and may be empty.

```json
{"schemaVersion":1,"type":"state.snapshot","receivedAtUtc":"2026-09-25T12:00:00Z","snapshots":[{"schemaVersion":1,"type":"area.production.statistics","receivedAtUtc":"2026-09-25T11:59:58Z","statistics":{"sessionId":1,"islandId":2,"areaIndex":0,"sessionGuid":123456,"areaName":"North Sea","rawTimestamp":987654321,"entries":[]}}]}
```

## session.start and session.end

`session.start` contains a `session.sessionName` string. `session.end` contains only the common envelope fields.

```json
{"schemaVersion":1,"type":"session.start","receivedAtUtc":"2026-09-25T12:00:00Z","session":{"sessionName":"My campaign"}}
```

```json
{"schemaVersion":1,"type":"session.end","receivedAtUtc":"2026-09-25T13:00:00Z"}
```

## area.production.statistics

The statistics object contains session and area identifiers, a raw timestamp, and an `entries` array. Each entry contains production, consumption, productivity, income, profit, building, workforce, and maintenance values. The two GUID maps are sent by the real Hub as JSON objects, not arrays:

```json
{
	"workforceGuidToAmount": { "500": 12 },
	"buildingGuidToAmount": { "600": 3 }
}
```

JSON object keys are the numeric GUIDs converted to strings by JSON serialization; values are integer amounts. Duplicate GUIDs are already combined by the decoder. Clients should accept this object form.

```json
{"schemaVersion":1,"type":"area.production.statistics","receivedAtUtc":"2026-09-25T12:01:00Z","statistics":{"sessionId":1,"islandId":2,"areaIndex":0,"sessionGuid":123456,"areaName":"North Sea","rawTimestamp":987654321,"entries":[{"productGuid":42,"productGeneration":10.0,"productConsumption":2.0,"productDelta":8.0,"perfectProductGeneration":12.0,"perfectProductConsumption":1.0,"amountOfBuildings":3,"totalMaintenance":4,"totalIncome":100.5,"totalProfit":90,"summedProductivity":5.5,"averageProductivity":4.5,"workforceGuidToAmount":{"500":12},"buildingGuidToAmount":{"600":3}}]}}
```

`sessionId`, `islandId`, and `areaIndex` are non-negative counters. `sessionGuid`, `productGuid`, building/workforce GUIDs, amounts, and building values are integers. Production, income, and productivity values are JSON numbers using the single-precision values from the pipe protocol. `rawTimestamp` is intentionally an uninterpreted integer because its unit is not defined unambiguously by the reference. `areaName` is a string and `entries` is always an array, even when empty.

## Authentication

Local and LAN mode require no WebSocket authentication. After connecting, the client receives `hub.status`, `state.snapshot`, and subsequent Hub events.

---

# JSON-Protokoll

Jede WebSocket-Nachricht ist genau ein JSON-Objekt. Pflichtfelder aller Nachrichten:

| Feld | Typ | Regel |
|---|---|---|
| `schemaVersion` | Integer | Aktuell `1`; unbekannte Werte kontrolliert behandeln |
| `type` | String | Ereignistyp; unbekannte Typen ignorieren |
| `receivedAtUtc` | String | ISO-8601 UTC-Zeitpunkt des Hub-Empfangs |

Der JSON-Schema-Vertrag liegt unter `schema/anno117-event.schema.json`.

## hub.status

Wird nach erfolgreicher Verbindung zuerst gesendet und bei Statusänderungen erneut.

```json
{"schemaVersion":1,"type":"hub.status","receivedAtUtc":"2026-09-25T12:00:00Z","hub":{"status":{"hubStatus":"Ready","pipeStatus":"Connected","connectedClients":1,"lastMessageAtUtc":"2026-09-25T11:59:58Z","protocolVersion":2,"error":null},"listener":{"lanEnabled":false,"bindAddress":"127.0.0.1","port":8766,"state":"Listening","connectedClients":1,"error":null}}}
```

`hubStatus` ist der sprachneutrale Maschinenwert `Ready`; Übersetzungen gehören nur in die Dashboard-Anzeige. `pipeStatus` ist `Waiting`, `Connected`, `Disconnected`, `Timeout`, `NotFound` oder `Busy`. `lastMessageAtUtc`, `protocolVersion` und Fehler dürfen `null` sein. Listener-Port ist ein Integer von 1024 bis 65535.

## state.snapshot

Wird direkt nach `hub.status` gesendet. `snapshots` ist ein Array der aktuell gehaltenen `area.production.statistics`-Objekte und kann leer sein.

```json
{"schemaVersion":1,"type":"state.snapshot","receivedAtUtc":"2026-09-25T12:00:00Z","snapshots":[{"schemaVersion":1,"type":"area.production.statistics","receivedAtUtc":"2026-09-25T11:59:58Z","statistics":{"sessionId":1,"islandId":2,"areaIndex":0,"sessionGuid":123456,"areaName":"North Sea","rawTimestamp":987654321,"entries":[]}}]}
```

## session.start

```json
{"schemaVersion":1,"type":"session.start","receivedAtUtc":"2026-09-25T12:00:00Z","session":{"sessionName":"Meine Kampagne"}}
```

`sessionName` ist eine Zeichenkette und kann leer sein.

## session.end

```json
{"schemaVersion":1,"type":"session.end","receivedAtUtc":"2026-09-25T13:00:00Z"}
```

## area.production.statistics

```json
{"schemaVersion":1,"type":"area.production.statistics","receivedAtUtc":"2026-09-25T12:01:00Z","statistics":{"sessionId":1,"islandId":2,"areaIndex":0,"sessionGuid":123456,"areaName":"North Sea","rawTimestamp":987654321,"entries":[{"productGuid":42,"productGeneration":10.0,"productConsumption":2.0,"productDelta":8.0,"perfectProductGeneration":12.0,"perfectProductConsumption":1.0,"amountOfBuildings":3,"totalMaintenance":4,"totalIncome":100.5,"totalProfit":90,"summedProductivity":5.5,"averageProductivity":4.5,"workforceGuidToAmount":{"500":12},"buildingGuidToAmount":{"600":3}}]}}
```

`sessionId`, `islandId` und `areaIndex` sind nichtnegative Zähler; `sessionGuid`, `productGuid`, Gebäude-/Workforce-`guid` und alle `amount`- sowie Gebäudewerte sind Integer. Produktions-, Einkommens- und Produktivitätswerte sind JSON-Zahlen (IEEE-754 single-precision aus dem Pipe-Protokoll). `rawTimestamp` ist absichtlich ein uninterpretierter Integer: Seine Zeiteinheit ist in der Referenz nicht zweifelsfrei definiert. `areaName` ist eine Zeichenkette. `entries` ist immer ein Array, auch wenn es leer ist.

Die Maps des Binärprotokolls werden als JSON-Objekte ausgegeben. Die numerischen GUIDs sind dabei String-Schlüssel, zum Beispiel `{ "500": 12 }`; die Werte sind Integer-Mengen. Doppelte GUIDs sind im Decoder bereits addiert.

## Authentifizierung

Im lokalen und im LAN-Modus ist keine WebSocket-Authentifizierung erforderlich. Nach dem Verbinden empfängt der Client zuerst `hub.status`, `state.snapshot` und danach die laufenden Hub-Ereignisse.
