# Integration for Tools and Mods

Anno 117 Pipe Hub is a community project and is not an official Ubisoft product. It provides a read-only WebSocket stream. The Hub alone reads the Windows named pipe; clients never open `\\.\pipe\anno117` and cannot send game commands.

## Endpoint

By default:

```text
ws://127.0.0.1:8766/ws
```

The administration dashboard remains local at `http://127.0.0.1:8765/`. The GUI can select the port and, optionally, a specific private IPv4 address. LAN clients might then use `ws://192.168.1.25:8766/ws`.

## Connection

1. Open a WebSocket.
2. In local mode, read JSON messages directly.
3. In LAN mode, send `{"type":"auth","token":"<token>"}` within 10 seconds as the first message.
4. After successful authentication, `hub.status` is sent first, followed by `state.snapshot`.
5. New live events follow.

LAN intentionally uses `ws://`: tokens and game data are not encrypted. The token is authentication, not encryption. Use trusted private networks only and do not configure router port forwarding. A local firewall rule may be required; the Hub does not change the Windows Firewall automatically.

After an interruption, reconnect with bounded backoff. Each reconnect receives a current snapshot again. State is kept in memory only; restarting the Hub discards old session data.

## Compatibility

Handle unknown `schemaVersion` and `type` values defensively by ignoring them or displaying them as unknown. A mod can subscribe only if its mod runtime permits WebSocket/network access. Existing programs that open the Anno pipe directly do not automatically receive Hub data; they need an adaptation or adapter. The Hub supports multiple local clients, while concrete tool/mod integration remains separate.

## Minimal JavaScript client

```js
const socket = new WebSocket("ws://127.0.0.1:8766/ws");
socket.onmessage = event => {
  const message = JSON.parse(event.data);
  if (message.schemaVersion !== 1) return;
  if (message.type === "state.snapshot") consumeSnapshot(message.snapshots);
  else if (message.type === "area.production.statistics") consumeArea(message.statistics);
};
```

For LAN, send the authentication message from the client's `onopen` handler. See [browser-client.html](../examples/browser-client.html) and [dotnet-client](../examples/dotnet-client/).

---

# Integration für Tools und Mods

Anno 117 Pipe Hub ist ein Community-Projekt und kein offizielles Ubisoft-Produkt. Es stellt einen read-only WebSocket-Datenstrom bereit. Der Hub liest die einzige Windows-Named-Pipe selbst; Clients öffnen niemals `\\.\pipe\anno117` und senden keine Spielbefehle.

## Endpunkt

Standardmäßig:

```text
ws://127.0.0.1:8766/ws
```

Das Verwaltungs-Dashboard bleibt immer lokal unter `http://127.0.0.1:8765/`. In der GUI können Port und optional eine konkrete private IPv4-Adresse gewählt werden. LAN-Clients verwenden dann beispielsweise `ws://192.168.1.25:8766/ws`.

## Verbindung

1. WebSocket öffnen.
2. Im lokalen Modus direkt JSON-Nachrichten lesen.
3. Im LAN-Modus innerhalb von 10 Sekunden zuerst senden: `{"type":"auth","token":"<token>"}`.
4. Nach erfolgreicher Authentifizierung kommen zuerst `hub.status`, danach `state.snapshot`.
5. Neue Live-Ereignisse folgen danach.

LAN nutzt absichtlich `ws://`: Token und Spieldaten sind nicht verschlüsselt. Das Token ist keine Verschlüsselung. Nur vertrauenswürdige private Netzwerke verwenden, keine Router-Portweiterleitung einrichten. Eine Firewall-Regel kann lokal erforderlich sein; der Hub ändert die Windows-Firewall nicht automatisch.

Nach einer Unterbrechung kontrolliert neu verbinden. Jeder Reconnect erhält wieder einen aktuellen Snapshot. Der Zustand liegt nur im Arbeitsspeicher; ein Hub-Neustart verwirft alte Sitzungsdaten.

## Kompatibilität

Unbekannte `schemaVersion`-Werte oder `type`-Werte müssen kontrolliert ignoriert oder als unbekannt angezeigt werden. Ein Mod kann nur abonnieren, wenn seine Mod-Laufzeit WebSocket-/Netzwerkzugriff erlaubt. Bestehende Programme, die direkt die Anno-Pipe öffnen, beziehen ihre Daten nicht automatisch vom Hub; sie brauchen eine Anpassung oder einen Adapter. Der Hub unterstützt mehrere lokale Clients, die konkrete Tool-/Mod-Integration erfolgt separat.

## Minimaler JavaScript-Client

```js
const socket = new WebSocket("ws://127.0.0.1:8766/ws");
socket.onmessage = event => {
  const message = JSON.parse(event.data);
  if (message.schemaVersion !== 1) return;
  if (message.type === "state.snapshot") consumeSnapshot(message.snapshots);
  else if (message.type === "area.production.statistics") consumeArea(message.statistics);
};
```

Für LAN ergänzt der Client im `onopen`-Handler die Authentifizierungsnachricht. Siehe [browser-client.html](../examples/browser-client.html) und [dotnet-client](../examples/dotnet-client/).
