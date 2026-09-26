# Integration for Tools and Mods

Anno 117 Pipe Hub is a community project and is not an official Ubisoft product. It provides a read-only WebSocket stream. The Hub alone reads the Windows named pipe; clients never open `\\.\pipe\anno117` and cannot send game commands.

## Endpoint

By default:

```text
ws://127.0.0.1:8766/ws
```

The administration dashboard remains local at `http://127.0.0.1:8765/`. The GUI can select the port and, optionally, a specific private IPv4 address. LAN mode keeps the local `127.0.0.1` endpoint and adds the selected private IPv4 endpoint, for example `ws://192.168.1.25:8766/ws`.

The settings file is resolved from the executable directory as `config\hub-settings.json`. With `go run .`, this is a temporary executable directory; for the published single-binary EXE, it is `publish\win-x64\config\hub-settings.json` beside the EXE. The repository example file is a template and is not read from the repository root.

## Connection

1. Open a WebSocket.
2. In local mode, read JSON messages directly.
3. In LAN mode, receive messages directly; no authentication message is required.
4. `hub.status` is sent first, followed by `state.snapshot`.
5. New live events follow.

LAN intentionally uses unencrypted `ws://`; use trusted private networks only and do not configure router port forwarding. A local firewall rule may be required; the Hub does not change the Windows Firewall automatically.

The WebSocket accepts the dashboard origin `http://127.0.0.1:8765` and native clients without an `Origin` header. Other browser origins are rejected. Dashboard POST requests for settings, debug-log clearing, and file logging use the same origin rule; no broad CORS policy is enabled.

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

For LAN, receive messages directly after the client's `onopen` handler. With the Hub running, open the browser example at `http://127.0.0.1:8765/examples/browser-client.html`; it is served by the dashboard origin.

---

# Integration für Tools und Mods

Anno 117 Pipe Hub ist ein Community-Projekt und kein offizielles Ubisoft-Produkt. Es stellt einen read-only WebSocket-Datenstrom bereit. Der Hub liest die einzige Windows-Named-Pipe selbst; Clients öffnen niemals `\\.\pipe\anno117` und senden keine Spielbefehle.

## Endpunkt

Standardmäßig:

```text
ws://127.0.0.1:8766/ws
```

Das Verwaltungs-Dashboard bleibt immer lokal unter `http://127.0.0.1:8765/`. In der GUI können Port und optional eine konkrete private IPv4-Adresse gewählt werden. Im LAN-Modus bleibt `ws://127.0.0.1:8766/ws` erreichbar und zusätzlich können LAN-Clients beispielsweise `ws://192.168.1.25:8766/ws` verwenden.

Die Einstellungsdatei wird relativ zum Verzeichnis der ausführbaren Datei unter `config\hub-settings.json` aufgelöst. Bei `go run .` liegt sie im temporären Go-Ausgabeverzeichnis; bei der veröffentlichten EXE liegt sie neben der EXE unter `publish\win-x64\config\hub-settings.json`. Die Beispiel-Datei im Repository ist nur eine Vorlage und wird nicht aus dem Repository-Stamm gelesen.

## Verbindung

1. WebSocket öffnen.
2. Im lokalen Modus direkt JSON-Nachrichten lesen.
3. Im LAN-Modus direkt Nachrichten empfangen; eine Authentifizierungsnachricht ist nicht erforderlich.
4. Zuerst kommen `hub.status`, danach `state.snapshot`.
5. Neue Live-Ereignisse folgen danach.

LAN nutzt absichtlich unverschlüsseltes `ws://`. Nur vertrauenswürdige private Netzwerke verwenden, keine Router-Portweiterleitung einrichten. Eine Firewall-Regel kann lokal erforderlich sein; der Hub ändert die Windows-Firewall nicht automatisch.

Der WebSocket akzeptiert den Dashboard-Origin `http://127.0.0.1:8765` und native Clients ohne `Origin`-Header. Andere Browser-Origins werden abgelehnt. Dashboard-POSTs für Einstellungen, Debug-Log-Leeren und Datei-Logging verwenden dieselbe Origin-Regel; es gibt kein breites CORS.

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

Für LAN empfängt der Client nach `onopen` direkt Nachrichten. Öffne das Browser-Beispiel bei laufendem Hub unter `http://127.0.0.1:8765/examples/browser-client.html`; es wird vom Dashboard-Origin bereitgestellt.
