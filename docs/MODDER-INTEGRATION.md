# Anno 117 Pipe Hub: Modder Integration

## Summary

**Anno 117 Pipe Hub** is an unofficial community tool and is not a Ubisoft product. It reads the game's local Windows named pipe as the only pipe client and exposes decoded, read-only events through WebSocket.

A mod or tool client connects to the Hub. It does not open the game pipe itself and cannot send game commands through the Hub. Multiple clients can therefore consume the same data without competing for the named pipe.

## Quick start

1. Start `Anno 117 Pipe Hub`.
2. Start Anno 117 with the `/pipe` launch argument.
3. Connect your client to `ws://127.0.0.1:8766/ws`.
4. Process `hub.status` first, then `state.snapshot`.
5. Process live `session.start`, `session.end`, and `area.production.statistics` events.
6. Reconnect with bounded backoff after a disconnect. Each reconnect receives status and a current snapshot again.

Port `8766` is the default data-listener port and can be changed in the dashboard. The administration dashboard is local at `http://127.0.0.1:8765/` and is not the data WebSocket.

## Requirements and connection

- Windows with Anno 117 running with `/pipe`.
- A running Anno 117 Pipe Hub.
- A mod or tool with WebSocket/network access.

The Hub provides a read-only WebSocket endpoint. A normal HTTP request to `/ws` is not a data connection. Clients must not open `\\.\pipe\anno117` themselves.

In LAN mode, receive messages directly after opening the WebSocket. The connection uses unencrypted `ws://`; use a trusted private network only. The Hub accepts connections without an `Origin` header and connections with the Origin `http://127.0.0.1:8765`. Native clients that do not send an `Origin` can therefore connect in LAN mode too. Browser clients from other origins may be rejected with HTTP 403, including a page opened locally via `file://`, which may send a different or `null` Origin. This Origin check is not application authentication.

## JSON contract

Every message contains `schemaVersion`, `type`, and `receivedAtUtc`. Supported types are `hub.status`, `state.snapshot`, `session.start`, `session.end`, and `area.production.statistics`. The GUID maps in production entries are sent as JSON objects, for example:

```json
{
  "workforceGuidToAmount": { "500": 12 },
  "buildingGuidToAmount": { "600": 3 }
}
```

The numeric GUIDs become JSON string keys and the values are integer amounts. Clients should tolerate unknown fields and event types. See the [protocol reference](PROTOCOL.md) for the complete contract.

## Connection sequence

1. Open a WebSocket on the configured host and port.
2. In loopback mode, receive text messages directly.
3. In LAN mode, receive messages directly; no authentication object is required.

4. After a successful connection, the client receives `hub.status` first.
5. The Hub then sends `state.snapshot` with its current in-memory state.
6. New live events follow.

Each client queue is limited to 64 messages. Under load, older messages may be dropped so a slow client cannot block the pipe reader. Clients should read continuously and must not treat the WebSocket as a lossless event store.

## Local and LAN mode

### Local mode

The data listener binds to loopback by default:

```text
ws://127.0.0.1:8766/ws
```

No authentication is required. The Hub is the only reader of the game pipe and reconnects after a disconnection; any number of local WebSocket clients can subscribe to the same stream.

### LAN mode

The dashboard can select a private IPv4 address and port, for example:

```text
ws://192.168.0.50:8767/ws
```

LAN clients do not send an authentication message. Status and snapshot are sent immediately after the WebSocket connection is established. The Hub accepts connections without an `Origin` header and connections with the Origin `http://127.0.0.1:8765`. Native clients that do not send an `Origin` can therefore connect in LAN mode too. Browser clients from other origins may be rejected with HTTP 403, including a page opened locally via `file://`, which may send a different or `null` Origin. This Origin check is not application authentication.

LAN intentionally uses unencrypted `ws://`, not `wss://`. Use LAN only on a trusted private network. The Hub does not create Windows Firewall rules or router port forwarding.

## JSON message format

Every Hub message is a JSON object with these required fields:

```json
{
  "schemaVersion": 1,
  "type": "...",
  "receivedAtUtc": "2026-09-25T19:30:00.0000000+00:00"
}
```

- `schemaVersion` is currently `1`.
- `type` identifies the event.
- `receivedAtUtc` is the ISO-8601 timestamp when the Hub received the message.

Clients should tolerate unknown fields and event types. The complete machine-readable contract is in [schema/anno117-event.schema.json](../schema/anno117-event.schema.json).

### `hub.status`

This status snapshot is sent on connection and whenever the Hub status changes. It is not a game event. `hubStatus` is the stable machine value `Ready`; dashboard translations are presentation-only. `pipeStatus` is one of `Waiting`, `Connected`, `Disconnected`, `Timeout`, `NotFound`, or `Busy`; `protocolVersion`, `lastMessageAtUtc`, and `error` may be `null`.

### `state.snapshot`

This is sent after `hub.status` on every new client connection. `snapshots` is an array of `area.production.statistics` objects and may be empty. The Hub keeps this state only in memory; session start/end and Hub restart clear it.

### `session.start` and `session.end`

`session.start` contains `session.sessionName`. `session.end` contains the common envelope only.

### `area.production.statistics`

The statistics object contains session and area identifiers, a raw timestamp, and an `entries` array. Every entry contains production, consumption, productivity, building, maintenance, income, and profit values. The GUID maps use the real JSON object form produced by the Hub:

```json
{
  "workforceGuidToAmount": { "500": 12 },
  "buildingGuidToAmount": { "600": 2 }
}
```

The numeric GUIDs are JSON string keys and the values are integer amounts. Duplicate GUIDs are combined by the decoder. Do not infer names or units from numeric GUID values; separate product/building catalogs are required.

## Client examples and reconnect

The detailed browser JavaScript example follows in the German section below. The examples connect only to the Hub, handle reconnects, and never open the game pipe. They validate `schemaVersion`, handle unknown events defensively, and use bounded reconnect backoff.

## Troubleshooting

| Problem | Check or solution |
|---|---|
| Hub not started | Open `http://127.0.0.1:8765/` or start `Anno117PipeHub.exe`. |
| Game started without `/pipe` | Start Anno 117 with `/pipe`; without it the Hub remains in `Waiting` and retries. |
| Wrong port | Check the dashboard. The data port is not the dashboard port; the default is `8766`. |
| Connection refused | Check the Hub, listener state, host, port, and local firewall. Test loopback first. |
| LAN connection unavailable | Check the selected private IPv4 address and local firewall. |
| No statistics | Check pipe connection, `/pipe`, active session, and `state.snapshot`. The Hub creates no synthetic data. |
| Unknown event or schema | Ignore it defensively or log it; currently only `schemaVersion: 1` is implemented. |
| Slow client | Keep the receive loop running. Older messages may be dropped from the 64-message queue; read a fresh snapshot after reconnect. |

## Mod runtime limitations

Some mod runtimes do not permit TCP, WebSocket, or HTTP access. The Hub cannot bypass that runtime policy. Use a companion application or adapter when necessary:

```text
Anno 117 Pipe -> Anno 117 Pipe Hub -> Companion App -> Mod-compatible format/API
```

The companion app should validate and filter Hub JSON events and should not open the game pipe a second time.

## Integration checklist

- [ ] Hub started and dashboard reachable.
- [ ] Anno 117 started with `/pipe`.
- [ ] Correct WebSocket URL and data port used.
- [ ] LAN mode: private address and lack of TLS understood.
- [ ] `schemaVersion === 1` and `type` checked defensively.
- [ ] `hub.status` and `state.snapshot` handled on connection.
- [ ] All known live event types handled.
- [ ] Unknown events, fields, and schema versions tolerated.
- [ ] Fragmented and invalid JSON handled.
- [ ] Bounded reconnect backoff and a fresh snapshot implemented.
- [ ] No direct named-pipe connection from the mod or client.
- [ ] No assumptions about GUID names, timestamp units, or numeric units without verification.

The German section below contains the same detailed JavaScript examples.

---

# Anno 117 Pipe Hub: Integration für Modder

## Kurzfassung

**Anno 117 Pipe Hub** ist ein inoffizielles Community-Tool und kein Ubisoft-Produkt. Es liest die lokale Windows-Named-Pipe des Spiels als einziger Prozess und stellt die dekodierten, schreibgeschützten Ereignisse über WebSocket bereit.

Ein Mod- oder Tool-Client verbindet sich also mit dem Hub. Er öffnet die Spiel-Pipe nicht selbst und kann über den Hub keine Spielbefehle senden. Dadurch können mehrere Clients dieselben Daten nutzen, ohne miteinander um die Named Pipe zu konkurrieren.

## Quick start

1. Starte `Anno 117 Pipe Hub`.
2. Starte Anno 117 mit dem Spielstartargument `/pipe`.
3. Verbinde deinen Client mit:

   ```text
   ws://127.0.0.1:8766/ws
   ```

4. Verarbeite zuerst `hub.status`, danach `state.snapshot`.
5. Verarbeite anschließend die Live-Ereignisse `session.start`, `session.end` und `area.production.statistics`.
6. Bei einer Trennung verbinde mit begrenztem Backoff neu. Nach jedem Reconnect kommen Status und ein aktueller Snapshot erneut.

Der Port `8766` ist der Standardwert des Daten-Listeners. Er kann im Dashboard geändert werden. Das Verwaltungs-Dashboard selbst ist ausschließlich lokal unter `http://127.0.0.1:8765/` erreichbar und ist nicht der Daten-WebSocket.

## Voraussetzungen und Verbindung

- Windows mit laufendem Anno 117 und dem Startargument `/pipe`.
- Laufender Anno 117 Pipe Hub.
- Ein Mod oder Tool mit WebSocket-/Netzwerkzugriff.
- Im Standardfall Zugriff auf `127.0.0.1:8766`.

Der Hub unterstützt einen schreibgeschützten WebSocket-Endpunkt:

```text
ws://127.0.0.1:8766/ws
```

Der Endpunkt ist ein WebSocket-Endpunkt. Ein normaler HTTP-Request auf `/ws` wird nicht als Datenverbindung behandelt. Ein Client darf die Named Pipe `\\.\pipe\anno117` nicht als Ersatz selbst öffnen: Das ist die Aufgabe des Hubs.

## Verbindungsablauf

1. WebSocket auf dem konfigurierten Host und Port öffnen.
2. Im Loopback-Modus direkt Textnachrichten empfangen.
3. Im LAN-Modus direkt Nachrichten empfangen; ein Auth-Objekt ist nicht erforderlich.

4. Bei erfolgreicher Verbindung erhält der Client zunächst ein `hub.status`-Ereignis.
5. Danach folgt `state.snapshot` mit dem aktuell im Hub gehaltenen Zustand.
6. Danach folgen neue Live-Ereignisse.

Die Client-Warteschlange ist im Hub pro Verbindung auf 64 Nachrichten begrenzt. Bei Überlast werden ältere Nachrichten verworfen, damit ein langsamer Client den Pipe-Reader nicht blockiert. Ein Client sollte deshalb regelmäßig lesen und darf die Verbindung nicht als verlustfreien Ereignisspeicher behandeln.

## Lokaler Modus und LAN-Modus

### Lokaler Modus

Standardmäßig bindet der Daten-Listener nur an Loopback:

```text
ws://127.0.0.1:8766/ws
```

Es ist keine Authentifizierung erforderlich. Der Hub ist der einzige Leser der Spiel-Pipe und verbindet sich nach einer Trennung erneut; beliebig viele lokale WebSocket-Clients können denselben Datenstrom abonnieren.

### LAN-Modus

Im Dashboard kann eine konkrete private IPv4-Adresse und ein Port ausgewählt werden, zum Beispiel:

```text
ws://192.168.0.50:8767/ws
```

LAN-Clients senden keine Authentifizierungsnachricht. Nach dem Verbinden werden Status und Snapshot direkt gesendet. Der Hub akzeptiert Verbindungen ohne `Origin`-Header sowie mit dem Origin `http://127.0.0.1:8765`. Native Clients ohne `Origin` können sich daher auch im LAN verbinden. Browser-Clients von anderen Origins können mit HTTP 403 abgewiesen werden. Das gilt auch für eine lokal über `file://` geöffnete Seite, die einen anderen oder `null`-Origin senden kann. Diese Origin-Prüfung ersetzt keine Anwendungsauthentifizierung.

LAN verwendet absichtlich unverschlüsseltes `ws://`, nicht TLS-verschlüsseltes `wss://`. Verwende LAN nur in einem vertrauenswürdigen privaten Netzwerk. Der Hub richtet keine Windows-Firewall-Regel ein und konfiguriert keine Router-Portweiterleitung.

## JSON-Grundformat

Jede Hub-Nachricht ist ein JSON-Objekt mit diesen Pflichtfeldern:

```json
{
  "schemaVersion": 1,
  "type": "...",
  "receivedAtUtc": "2026-09-25T19:30:00.0000000+00:00"
}
```

- `schemaVersion`: Integer, aktuell immer `1`.
- `type`: String mit dem Eventtyp.
- `receivedAtUtc`: ISO-8601-Datum/Zeitstring, der den Empfang durch den Hub beschreibt.

Das vollständige maschinenlesbare Schema liegt unter [schema/anno117-event.schema.json](../schema/anno117-event.schema.json). Das Schema erlaubt zusätzliche Eigenschaften. Clients sollten unbekannte Felder tolerieren.

### `hub.status`

Wird beim Verbindungsaufbau und bei Statusänderungen gesendet:

```json
{
  "schemaVersion": 1,
  "type": "hub.status",
  "receivedAtUtc": "2026-09-25T19:30:00.0000000+00:00",
  "hub": {
    "status": {
      "hubStatus": "Ready",
      "pipeStatus": "Connected",
      "connectedClients": 1,
      "lastMessageAtUtc": "2026-09-25T19:29:59.0000000+00:00",
      "protocolVersion": 2,
      "error": null
    },
    "listener": {
      "lanEnabled": false,
      "bindAddress": "127.0.0.1",
      "port": 8766,
      "state": "Listening",
      "connectedClients": 1,
      "error": null
    }
  }
}
```

`hub.status` ist ein Status-Snapshot, kein Spielereignis. `hubStatus` ist der stabile Maschinenwert `Ready`; Übersetzungen gehören nur in die Anzeige. Die im Hub implementierten Werte für `pipeStatus` sind `Waiting`, `Connected`, `Disconnected`, `Timeout`, `NotFound` und `Busy`. `protocolVersion` kann `null` sein, bevor die Pipe-Version bekannt ist. `lastMessageAtUtc` und `error` können `null` sein.

### `state.snapshot`

Die folgenden Werte zeigen nur die vom Schema verlangten Datentypen. Sie sind keine behaupteten Werte aus einer konkreten Spielsession; insbesondere sind GUIDs, Namen und Zeitstempel nicht aus ihnen abzuleiten.

Wird nach `hub.status` bei jeder neuen Clientverbindung gesendet:

```json
{
  "schemaVersion": 1,
  "type": "state.snapshot",
  "receivedAtUtc": "2026-09-25T19:30:00.1000000+00:00",
  "snapshots": [
    {
      "schemaVersion": 1,
      "type": "area.production.statistics",
      "receivedAtUtc": "2026-09-25T19:29:59.9000000+00:00",
      "statistics": {
        "sessionId": 1,
        "islandId": 4,
        "areaIndex": 0,
        "sessionGuid": 1234,
        "areaName": "North Sea",
        "rawTimestamp": 987654321,
        "entries": []
      }
    }
  ]
}
```

`snapshots` ist ein Array von `area.production.statistics`-Objekten. Der Hub hält diesen Zustand nur im Arbeitsspeicher. Bei `session.start` oder `session.end` wird der gehaltene Produktions-Snapshot geleert. Ein Hub-Neustart verwirft den Zustand ebenfalls.

### `session.start`

```json
{
  "schemaVersion": 1,
  "type": "session.start",
  "receivedAtUtc": "2026-09-25T19:30:00.0000000+00:00",
  "session": {
    "sessionName": "My session"
  }
}
```

`session.sessionName` ist ein String und im Schema Pflichtfeld.

### `session.end`

```json
{
  "schemaVersion": 1,
  "type": "session.end",
  "receivedAtUtc": "2026-09-25T20:00:00.0000000+00:00"
}
```

Außer den Grundfeldern enthält dieses Ereignis laut aktuellem Schema keine weiteren Pflichtfelder.

### `area.production.statistics`

Auch dieses Beispiel ist typisiert, nicht repräsentativ für eine bestimmte Spielsession. Die Zahlen, GUIDs und Namen sind Platzhalter zur Darstellung des implementierten JSON-Aufbaus.

```json
{
  "schemaVersion": 1,
  "type": "area.production.statistics",
  "receivedAtUtc": "2026-09-25T19:30:01.0000000+00:00",
  "statistics": {
    "sessionId": 1,
    "islandId": 4,
    "areaIndex": 0,
    "sessionGuid": 1234,
    "areaName": "North Sea",
    "rawTimestamp": 987654321,
    "entries": [
      {
        "productGuid": 2077,
        "productGeneration": 1.0,
        "productConsumption": 0.5,
        "productDelta": 0.5,
        "perfectProductGeneration": 1.2,
        "perfectProductConsumption": 0.5,
        "amountOfBuildings": 2,
        "totalMaintenance": 10,
        "totalIncome": 36.0,
        "totalProfit": 26,
        "summedProductivity": 1.0,
        "averageProductivity": 0.5,
        "workforceGuidToAmount": { "500": 12 },
        "buildingGuidToAmount": { "600": 2 }
      }
    ]
  }
}
```

Die Pflichtfelder von `statistics` sind:

| Feld | Typ | Bekannte Bedeutung |
|---|---|---|
| `sessionId` | Integer, mindestens 0 | Sitzungs-ID aus der Pipe-Dekodierung |
| `islandId` | Integer, mindestens 0 | Insel-ID aus der Pipe-Dekodierung |
| `areaIndex` | Integer, mindestens 0 | Bereichsindex aus der Pipe-Dekodierung |
| `sessionGuid` | Integer | Sitzungs-GUID/Identifier aus der Datenquelle |
| `areaName` | String | Bereichsname |
| `rawTimestamp` | Integer | Rohzeitstempel aus der Datenquelle |
| `entries` | Array | Produktionszeilen des Bereichs |

Jeder Eintrag enthält alle folgenden Pflichtfelder:

| Feld | Typ |
|---|---|
| `productGuid` | Integer |
| `productGeneration` | Number |
| `productConsumption` | Number |
| `productDelta` | Number |
| `perfectProductGeneration` | Number |
| `perfectProductConsumption` | Number |
| `amountOfBuildings` | Integer |
| `totalMaintenance` | Integer |
| `totalIncome` | Number |
| `totalProfit` | Integer |
| `summedProductivity` | Number |
| `averageProductivity` | Number |
| `workforceGuidToAmount` | Objekt mit GUID-String-Schlüsseln und Integer-Mengen |
| `buildingGuidToAmount` | Objekt mit GUID-String-Schlüsseln und Integer-Mengen |

Das Schema legt für diese Zahlen keine Einheiten fest. Insbesondere darf ein Client `rawTimestamp`, Produkt-GUIDs, Gebäude-GUIDs oder Workforce-GUIDs nicht anhand ihrer numerischen Werte erraten. GUIDs sind ohne zusätzliche Produkt-/Gebäudekataloge nur stabile numerische Identifier; eine Zuordnung zu Namen oder Einheiten ist in diesem Hub noch nicht implementiert und muss separat verifiziert werden.

## Browser-JavaScript mit Reconnect

Dieses Beispiel öffnet ausschließlich den Hub-WebSocket. Es liest keine Named Pipe. Es muss von einem erlaubten Origin ausgeführt werden, zum Beispiel `http://127.0.0.1:8765`; Browser-Seiten von anderen Origins können mit HTTP 403 abgewiesen werden. Es akzeptiert nur bekannte Schema-Versionen, verarbeitet Nachrichten defensiv und verbindet mit begrenztem Backoff neu:

```html
<script>
const endpoint = "ws://127.0.0.1:8766/ws";
let retry = 0;
let socket;
let stopped = false;

function handleMessage(message) {
  if (!message || message.schemaVersion !== 1 || typeof message.type !== "string") return;
  switch (message.type) {
    case "hub.status":
      console.log("Hub status", message.hub);
      break;
    case "state.snapshot":
      if (Array.isArray(message.snapshots)) console.log("Snapshot", message.snapshots);
      break;
    case "session.start":
      console.log("Session started", message.session);
      break;
    case "session.end":
      console.log("Session ended");
      break;
    case "area.production.statistics":
      if (message.statistics && Array.isArray(message.statistics.entries)) {
        console.log("Production statistics", message.statistics);
      }
      break;
    default:
      console.debug("Unknown event ignored", message.type);
  }
}

function connect() {
  if (stopped) return;
  socket = new WebSocket(endpoint);
  socket.onopen = () => { retry = 0; console.log("Connected"); };
  socket.onmessage = event => {
    try { handleMessage(JSON.parse(event.data)); }
    catch (error) { console.warn("Invalid JSON ignored", error); }
  };
  socket.onerror = () => socket.close();
  socket.onclose = () => {
    if (stopped) return;
    const delay = Math.min(2000 * 2 ** retry, 10000);
    retry += 1;
    setTimeout(connect, delay);
  };
}

connect();
// Bei einem UI-Abbau: stopped = true; socket?.close();
</script>
```

Im LAN-Modus können Nachrichten direkt nach `socket.onopen` empfangen werden:

```js
socket.onopen = () => {
  console.log("WebSocket verbunden");
};
```

LAN hat keine Anwendungsauthentifizierung. Verwende den LAN-Modus nur in einem vertrauenswürdigen privaten Netzwerk.

## Fehlerbilder und Lösungen

| Problem | Prüfung/Lösung |
|---|---|
| Hub nicht gestartet | `http://127.0.0.1:8765/` öffnen oder `Anno117PipeHub.exe` starten. |
| Spiel ohne `/pipe` | Anno 117 mit dem Startargument `/pipe` starten. Ohne Pipe bleibt der Pipe-Status `Waiting`; der Hub wartet mit Reconnect. |
| Falscher Port | Den Port im Dashboard prüfen. Der Datenport ist nicht der Dashboard-Port. Standard: `8766`. |
| Verbindung abgelehnt | Hub läuft nicht, der Listener startet gerade, falscher Host/Port oder lokale Firewall blockiert. Erst Loopback testen. |
| LAN-Verbindung nicht erreichbar | Ausgewählte private IPv4-Adresse und lokale Firewall prüfen. |
| Keine Statistikdaten | Pipe-Verbindung, `/pipe`, Sitzung und den `state.snapshot` prüfen. Es gibt keine künstlichen Daten und keine garantierten Einheiten. |
| Unbekannter Eventtyp | Nachricht nicht als bekannten Typ verarbeiten, loggen oder ignorieren. Zusätzliche Felder sind erlaubt. |
| Unbekannte Schema-Version | Nachricht kontrolliert ignorieren und keine Annahmen über Felder treffen. Aktuell ist nur `schemaVersion: 1` implementiert. |
| Langsamer Client | Empfangsschleife dauerhaft laufen lassen. Bei Überlast können ältere Nachrichten aus der 64er Client-Warteschlange verworfen werden; nach Reconnect den Snapshot neu einlesen. |

## Einschränkungen für Mods

Nicht jede Mod-Laufzeit erlaubt TCP-, WebSocket- oder HTTP-Zugriff. Das ist eine Eigenschaft der jeweiligen Modding-API und wird vom Hub nicht umgangen.

Wenn die Mod nicht direkt verbinden darf, verwende eine Companion-App oder einen Adapter:

```text
Anno 117 Pipe -> Anno 117 Pipe Hub -> Companion-App -> Mod-kompatibles Format/API
```

Die Companion-App verbindet sich mit dem Hub, validiert und filtert die JSON-Ereignisse und stellt der Mod nur die erlaubte lokale Schnittstelle bereit. Sie sollte die Spiel-Pipe nicht zusätzlich öffnen. Welche IPC-Methode die Mod akzeptiert, ist modding-spezifisch und hier noch zu verifizieren.

## Integrations-Checkliste

- [ ] Hub gestartet und Dashboard erreichbar.
- [ ] Anno 117 mit `/pipe` gestartet.
- [ ] Korrekte WebSocket-URL und Port verwendet.
- [ ] LAN-Modus: private Adresse und fehlendes TLS berücksichtigt.
- [ ] `schemaVersion === 1` und `type` defensiv geprüft.
- [ ] `hub.status` und `state.snapshot` beim Verbindungsaufbau verarbeitet.
- [ ] Alle bekannten Live-Eventtypen verarbeitet.
- [ ] Unbekannte Events, Felder und Schema-Versionen toleriert.
- [ ] JSON-Nachrichten fragmentiert/fehlerhaft behandeln.
- [ ] Reconnect mit begrenztem Backoff und anschließendem Snapshot implementiert.
- [ ] Keine direkte Named-Pipe-Verbindung aus der Mod bzw. dem Client.
- [ ] Keine Annahmen zu GUID-Namen, Zeitstempel-Einheiten oder Zahlen-Einheiten ohne separate Verifikation.

Weitere technische Hinweise stehen in [docs/INTEGRATION.md](INTEGRATION.md), [docs/PROTOCOL.md](PROTOCOL.md) und im [JSON-Schema](../schema/anno117-event.schema.json).
