# Anno 117 Pipe Hub

## English

Anno 117 Pipe Hub is a community project and is not an official Ubisoft product. It reads the local Windows named pipe `\\.\pipe\anno117` and distributes decoded Anno 117 statistics to any number of programs through a local WebSocket server. No MQTT broker is required.

### Why

Anno 117 exposes statistics through a local named pipe. The Hub makes the decoded data available as WebSocket messages, so multiple tools can use the same live feed at once. With LAN mode enabled, clients on other computers in the selected private network can connect to that feed too.

### Requirements

- Windows x64
- Go 1.26 or newer for development runs (CI tests Go 1.26.x and 1.27.x)
- Anno 117 started with the `/pipe` launch argument. The tool does not start Anno automatically.

### Run a release

Download the finished ZIP release package from the [GitHub Releases](https://github.com/CommanderLeander/Anno-117-Pipe-Hub/releases) page. It contains the standalone Go executable `Anno117PipeHub.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md` in the ZIP root. Distribute the complete ZIP contents together.

Start `Anno117PipeHub.exe` whenever convenient; the Hub can start before Anno 117 and waits for the pipe. When you start Anno 117, it must use the `/pipe` launch argument. The Hub does not open the browser automatically. While it is running, open the dashboard manually at `http://127.0.0.1:8765/`. Keep the Hub open while using the dashboard or WebSocket.

The data WebSocket defaults to `ws://127.0.0.1:8766/ws` and can be configured in the dashboard. By default it binds to loopback only. LAN mode keeps the loopback bind and additionally binds to a selected private IPv4 address; LAN mode has no application-level authentication. Port, LAN mode, and the selected address are stored in `config\hub-settings.json` relative to the executable. Because the stream uses unencrypted `ws://`, use LAN mode only on a trusted private network and do not configure router port forwarding.

If Anno is not running yet, the Hub stays open and retries the pipe connection with bounded backoff.

### Run from source

This workflow is intended for development and for running the Hub from the source tree. It requires Go 1.26 or newer. Run the start script in PowerShell from the project directory:

```powershell
.\Start-AnnoPipeHub.ps1
```

The start script starts the Hub from source, waits until `http://127.0.0.1:8765/` responds, and then opens the dashboard in the browser. The Hub can start before Anno 117; when you start Anno 117, it must use the `/pipe` launch argument. Keep the Hub open while using it. The script does not start Anno automatically. To explicitly start an existing published EXE with the script, use `.\Start-AnnoPipeHub.ps1 -UsePublished`.

### Settings path

The settings file is resolved relative to the executable directory as `config\hub-settings.json`. With `go run .`, this is the temporary executable directory; with the published EXE it is beside the EXE: `publish\win-x64\config\hub-settings.json`. The repository's `config\hub-settings.example.json` is only a template.

### Diagnostics and file logging

The local debug console shows received pipe events, decoded messages, connection attempts, reconnects, and decoder errors. It keeps at most 200 entries and follows the selected language. File logging is off by default. Enable it at runtime with `Save log locally: On`; new events are then written beside the started EXE, for example `Anno117PipeHub-2026-09-25.log`. Queues and files are bounded; at most seven log files are retained in total, and a daily file is rotated when it reaches the 5 MiB rotation threshold.

Raw data in hexadecimal is not written to files by default. Explicit raw logging can be enabled by starting the process with `ANNO117PIPEHUB_RAW_LOG=1`. Disabling logging flushes and closes the file cleanly; existing log files are preserved. If the Hub cannot write beside the EXE, the dashboard shows a localized warning and does not silently use a fallback path. Credentials are never logged.

### Create a release from source

```powershell
.\Publish-Windows.ps1
```

The output is placed in `publish\win-x64`, and the release archive is `publish\Anno117PipeHub-windows-x64.zip`. The ZIP contains exactly the standalone Go single-file Windows x64 application `Anno117PipeHub.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md` in its root. No Go installation is required to run the EXE; distribute the ZIP contents together.

### Message format

WebSocket messages are versioned JSON objects with `schemaVersion`, `type`, and `receivedAtUtc`. Supported events are `hub.status`, `state.snapshot`, `session.start`, `session.end`, and `area.production.statistics`. A new connection receives status and the current snapshot first. Client queues are limited to 64 messages; under load, older messages may be dropped so a slow client cannot block the pipe reader. See the [Modder integration guide](docs/MODDER-INTEGRATION.md), [full integration guide](docs/INTEGRATION.md), [protocol reference](docs/PROTOCOL.md), and [JSON schema](schema/anno117-event.schema.json).

In LAN mode, read the WebSocket messages directly after opening the connection. `ws://` encrypts neither game data nor the connection; use LAN mode only on a trusted private network and do not configure router port forwarding. The Hub does not change the Windows Firewall automatically.

The protocol reference under `references\ubisoft-anno117-pipe` is documentation only and is not a build input. Its copied source files and license remain under the original Unlicense; the local reference README contains a small metadata correction. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The pipe is experimental and not guaranteed; compatibility with later game versions remains open. Tests use synthetic frames and do not test a live game connection.

### License and project policies

Project-owned code is released under the [MIT License](LICENSE). The bundled pipe reference is separate third-party material and remains under its original license; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Contributions should follow [CONTRIBUTING.md](CONTRIBUTING.md). Security reports should follow [SECURITY.md](SECURITY.md).

### Tests

```powershell
go test ./...
```

---

## Deutsch

Anno 117 Pipe Hub ist ein Community-Projekt und kein offizielles Ubisoft-Produkt. Es liest die lokale Windows-Named-Pipe `\\.\pipe\anno117` und verteilt die dekodierten Anno-117-Statistikdaten über einen lokalen WebSocket-Server an beliebig viele Programme. Es wird kein MQTT-Broker benötigt.

### Warum

Anno 117 stellt Statistikdaten über eine lokale Named Pipe bereit. Der Hub stellt die dekodierten Daten als WebSocket-Nachrichten zur Verfügung, sodass mehrere Tools denselben Live-Datenstrom gleichzeitig nutzen können. Bei aktiviertem LAN-Modus können sich auch Clients auf anderen Rechnern im ausgewählten privaten Netzwerk damit verbinden.

### Voraussetzungen

- Windows x64
- Für den Entwicklungsstart: Go 1.26 oder neuer (CI testet Go 1.26.x und 1.27.x)
- Anno 117 muss mit dem Startargument `/pipe` gestartet werden. Das Tool startet Anno nicht automatisch.

### Release herunterladen und starten

Lade das fertige ZIP-Release-Paket von der Seite [GitHub Releases](https://github.com/CommanderLeander/Anno-117-Pipe-Hub/releases) herunter. Es enthält die eigenständige Go-Einzeldatei `Anno117PipeHub.exe`, `LICENSE` und `THIRD_PARTY_NOTICES.md` im ZIP-Hauptverzeichnis. Der vollständige ZIP-Inhalt muss zusammen verteilt werden.

Starte `Anno117PipeHub.exe`, wann es passt; der Hub kann vor Anno 117 gestartet werden und wartet auf die Pipe. Wenn du Anno 117 startest, muss es das Startargument `/pipe` verwenden. Der Hub öffnet den Browser derzeit nicht automatisch. Öffne während des Betriebs das Dashboard manuell unter `http://127.0.0.1:8765/`. Der Hub muss während der Nutzung des Dashboards oder WebSockets geöffnet bleiben.

Der Daten-WebSocket ist standardmäßig `ws://127.0.0.1:8766/ws` und wird in der Dashboard-GUI konfiguriert. Standardmäßig bindet er nur an Loopback. Im LAN-Modus bleibt der Loopback-Bind erhalten und zusätzlich wird an eine ausgewählte private IPv4-Adresse gebunden; der LAN-Modus hat keine Anwendungsauthentifizierung. Port, LAN-Modus und Adresse liegen in `config\hub-settings.json` relativ zur ausführbaren Datei. Da `ws://` unverschlüsselt ist, den LAN-Modus nur im vertrauenswürdigen privaten Netzwerk und ohne Router-Portweiterleitung verwenden. Der Hub ändert die Windows-Firewall nicht automatisch.

Wenn Anno noch nicht läuft, bleibt der Hub geöffnet und versucht die Pipe mit begrenztem Backoff erneut zu verbinden.

### Aus dem Quellcode starten

Dieser Weg ist für die Entwicklung und den Start des Hubs aus dem Quellcode gedacht. Dafür wird Go 1.26 oder neuer benötigt. Führe das Startskript in PowerShell aus dem Projektordner aus:

```powershell
.\Start-AnnoPipeHub.ps1
```

Das Startskript startet den Hub aus dem Quellcode, wartet auf eine Antwort unter `http://127.0.0.1:8765/` und öffnet dann das Dashboard im Browser. Der Hub kann vor Anno 117 gestartet werden; wenn du Anno 117 startest, muss es das Startargument `/pipe` verwenden. Der Hub muss während der Nutzung geöffnet bleiben. Das Skript startet Anno nicht automatisch. Eine vorhandene veröffentlichte EXE kann ausdrücklich mit `.\Start-AnnoPipeHub.ps1 -UsePublished` gestartet werden.

### Konfigurationspfad

Die Einstellungsdatei wird relativ zum Verzeichnis der ausführbaren Datei als `config\hub-settings.json` aufgelöst. Bei `go run .` ist das ein temporäres Ausgabeverzeichnis; bei der veröffentlichten EXE liegt sie neben der EXE unter `publish\win-x64\config\hub-settings.json`. `config\hub-settings.example.json` im Repository ist nur eine Vorlage.

### Diagnose- und Datei-Log

Die lokale Debug-Konsole zeigt die tatsächlich empfangenen Pipe-Ereignisse, dekodierte Nachrichten, Verbindungsversuche, Reconnects und Decoderfehler. Sie hält höchstens 200 Einträge und folgt der ausgewählten Sprache. Das Datei-Log ist standardmäßig aus. Über `Log lokal speichern: An` beziehungsweise `Save log locally: On` wird es zur Laufzeit aktiviert; dann schreibt der Hub neue Ereignisse direkt neben die gestartete EXE, zum Beispiel `Anno117PipeHub-2026-09-25.log`. Die Queue und die Dateien sind begrenzt; insgesamt werden höchstens sieben Logdateien aufbewahrt, und eine Tagesdatei wird beim Erreichen der Rotationsschwelle von 5 MiB in nummerierte Teil-Dateien aufgeteilt.

Rohdaten als Hex werden standardmäßig nicht in die Datei geschrieben. Für ausdrücklich gewünschtes Raw-Logging kann der Prozess mit `ANNO117PIPEHUB_RAW_LOG=1` gestartet werden. Beim Ausschalten wird die Datei sauber geflusht und geschlossen; bereits geschriebene Logs bleiben erhalten. Kann der Hub neben der EXE nicht schreiben, zeigt das Dashboard eine lokalisierte Warnung und verwendet keinen stillen Fallback-Pfad. Zugangsdaten werden nicht protokolliert.

### Release aus dem Quellcode erstellen

```powershell
.\Publish-Windows.ps1
```

Die Ausgabe liegt unter `publish\win-x64`; das Release-ZIP liegt unter `publish\Anno117PipeHub-windows-x64.zip`. Das ZIP enthält genau die eigenständige Go-Einzeldatei `Anno117PipeHub.exe`, `LICENSE` und `THIRD_PARTY_NOTICES.md` im Hauptverzeichnis. Zur Ausführung der EXE ist keine Go-Installation erforderlich; der vollständige ZIP-Inhalt muss zusammen verteilt werden.

### Nachrichtenformat

WebSocket-Nachrichten sind versionierte JSON-Objekte mit `schemaVersion`, `type` und `receivedAtUtc`. Unterstützt werden `hub.status`, `state.snapshot`, `session.start`, `session.end` und `area.production.statistics`. Bei einer neuen Verbindung werden Status und der aktuelle Snapshot zuerst gesendet. Client-Warteschlangen sind auf 64 Nachrichten begrenzt; bei Überlast werden alte Nachrichten verworfen, damit ein langsamer Client die Pipe nicht blockiert. Die [Modder-Integrationsanleitung](docs/MODDER-INTEGRATION.md) erklärt Verbindung, JSON-Verarbeitung und Reconnect. Die vollständige Anleitung liegt in `docs\INTEGRATION.md`, das Protokoll in `docs\PROTOCOL.md` und das Schema in `schema\anno117-event.schema.json`.

Im LAN-Modus können Clients nach dem WebSocket-Verbindungsaufbau direkt Nachrichten empfangen. `ws://` verschlüsselt weder Verbindung noch Spieldaten; keine Router-Portweiterleitung einrichten. Der Hub ändert die Windows-Firewall nicht automatisch.

Die Protokollreferenz liegt unter `references\ubisoft-anno117-pipe`. Die kopierten Quelldateien und die Lizenz bleiben unter der dort enthaltenen Unlicense; die lokale Referenz-README enthält eine kleine Metadatenkorrektur. Sie ist Dokumentation und kein Build-Eingang und wird in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) getrennt aufgeführt. Die Pipe ist laut Referenz eine experimentelle, nicht garantierte Schnittstelle; Kompatibilität mit späteren Spielversionen ist daher offen. Die Tests verwenden synthetische Frames und testen keine echte Verbindung zu einem laufenden Spiel.

### Lizenz und Projektregeln

Projekt-eigener Code steht unter der [MIT-Lizenz](LICENSE). Die enthaltene Pipe-Referenz ist getrenntes Drittanbietermaterial und bleibt unter ihrer ursprünglichen Lizenz; siehe [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Beiträge sollten [CONTRIBUTING.md](CONTRIBUTING.md) folgen. Sicherheitsmeldungen gehören nach [SECURITY.md](SECURITY.md).

### Tests

```powershell
go test ./...
node --check src\AnnoPipeHub\wwwroot\app.js
```