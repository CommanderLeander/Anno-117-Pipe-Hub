# Anno 117 Pipe Hub

## English

Anno 117 Pipe Hub is a community project and is not an official Ubisoft product. It reads the local Windows named pipe `\\.\pipe\anno117` and distributes decoded Anno 117 statistics to any number of programs through a local WebSocket server. No MQTT broker is required.

### Requirements

- Windows x64
- .NET 8 SDK for development runs
- Anno 117 started with the `/pipe` launch argument. The tool does not start Anno automatically.

### Run a release

Download the finished `Anno117PipeHub.exe` from the [GitHub Releases](https://github.com/CommanderLeander/Anno-117-Pipe-Hub/releases) page. The release EXE is self-contained; using it requires neither the .NET 8 SDK nor PowerShell.

Start Anno 117 with the `/pipe` launch argument, then start `Anno117PipeHub.exe`. The Hub does not open the browser automatically. While it is running, open the dashboard manually at `http://127.0.0.1:8765/`. Keep the Hub open while using the dashboard or WebSocket.

The data WebSocket defaults to `ws://127.0.0.1:8766/ws` and can be configured in the dashboard. By default it binds to loopback only. LAN mode binds exclusively to a selected private IPv4 address and requires a token. Port, LAN mode, and the selected address are stored in `config\hub-settings.json` relative to `AppContext.BaseDirectory`; the token exists only in process memory and is newly generated after every restart. The token is not written to files, logs, URLs, cookies, or browser storage. Regenerating it immediately invalidates the old token.

If Anno is not running yet, the Hub stays open and retries the pipe connection with bounded backoff.

### Run from source

This workflow is intended for development and for running the Hub from the source tree. It requires the .NET 8 SDK. Run the start script in PowerShell from the project directory:

```powershell
.\Start-AnnoPipeHub.ps1
```

The start script starts the Hub and opens `http://127.0.0.1:8765/` in the browser. Keep the Hub open while using it. The script does not start Anno automatically.

### Settings path

The settings file is always resolved as `Path.Combine(AppContext.BaseDirectory, "config", "hub-settings.json")`. With `dotnet run`, `AppContext.BaseDirectory` is typically `src\AnnoPipeHub\bin\Debug\net8.0\` (or `Release\net8.0\`), so development settings belong in that output directory's `config\` subdirectory. For the published single-file EXE, the path is beside the EXE: `publish\win-x64\config\hub-settings.json`. The repository's `config\hub-settings.example.json` is only a template.

### Diagnostics and file logging

The local debug console shows received pipe events, decoded messages, connection attempts, reconnects, and decoder errors. It keeps at most 200 entries and follows the selected language. File logging is off by default. Enable it at runtime with `Save log locally: On`; new events are then written beside the started EXE, for example `Anno117PipeHub-2026-09-25.log`. Queues and files are bounded; up to seven old log files are retained, and a daily file is split into numbered parts at 5 MiB.

Raw data in hexadecimal is not written to files by default. Explicit raw logging can be enabled by starting the process with `ANNO117PIPEHUB_RAW_LOG=1`. Disabling logging flushes and closes the file cleanly; existing log files are preserved. If the Hub cannot write beside the EXE, the dashboard shows a localized warning and does not silently use a fallback path. Tokens and credentials are never logged.

### Create a release from source

```powershell
.\Publish-Windows.ps1
```

The output is placed in `publish\win-x64`. The script creates a self-contained single-file Windows x64 application and removes the PDB and IIS configuration file that are not needed for local Kestrel hosting. The resulting output consists of `Anno117PipeHub.exe`; the dashboard is embedded in the application.

### Message format

WebSocket messages are versioned JSON objects with `schemaVersion`, `type`, and `receivedAtUtc`. Supported events are `hub.status`, `state.snapshot`, `session.start`, `session.end`, and `area.production.statistics`. A new connection receives status and the current snapshot first. Client queues are limited to 64 messages; under load, older messages may be dropped so a slow client cannot block the pipe reader. See the [Modder integration guide](docs/MODDER-INTEGRATION.md), [full integration guide](docs/INTEGRATION.md), [protocol reference](docs/PROTOCOL.md), and [JSON schema](schema/anno117-event.schema.json).

In LAN mode, send `{"type":"auth","token":"..."}` after opening the WebSocket. `ws://` encrypts neither the token nor game data; do not configure router port forwarding. The Hub does not change the Windows Firewall automatically.

The protocol reference under `references\ubisoft-anno117-pipe` is documentation only and is not a build input. It remains under its original Unlicense; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The pipe is experimental and not guaranteed; compatibility with later game versions remains open. Tests use synthetic frames and do not test a live game connection.

### License and project policies

Project-owned code is released under the [MIT License](LICENSE). The bundled pipe reference is separate third-party material and remains under its original license; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Contributions should follow [CONTRIBUTING.md](CONTRIBUTING.md). Security reports should follow [SECURITY.md](SECURITY.md).

### Tests

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.local\dotnet-home"
$env:NUGET_PACKAGES = "$PWD\.local\nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = "$PWD\.local\nuget\http-cache"
$env:TEMP = "$PWD\.local\tmp"
$env:TMP = $env:TEMP
dotnet run --project tests\AnnoPipeHub.Tests\AnnoPipeHub.Tests.csproj
```

---

## Deutsch

Anno 117 Pipe Hub ist ein Community-Projekt und kein offizielles Ubisoft-Produkt. Es liest die lokale Windows-Named-Pipe `\\.\pipe\anno117` und verteilt die dekodierten Anno-117-Statistikdaten über einen lokalen WebSocket-Server an beliebig viele Programme. Es wird kein MQTT-Broker benötigt.

## Voraussetzungen

- Windows x64
- Für den Entwicklungsstart: .NET 8 SDK
- Anno 117 muss mit dem Startargument `/pipe` gestartet werden. Das Tool startet Anno nicht automatisch.

### Release herunterladen und starten

Lade die fertige `Anno117PipeHub.exe` von der Seite [GitHub Releases](https://github.com/CommanderLeander/Anno-117-Pipe-Hub/releases) herunter. Die Release-EXE ist self-contained; zum Verwenden werden weder das .NET 8 SDK noch PowerShell benötigt.

Starte Anno 117 mit dem Startargument `/pipe` und danach `Anno117PipeHub.exe`. Der Hub öffnet den Browser derzeit nicht automatisch. Öffne während des Betriebs das Dashboard manuell unter `http://127.0.0.1:8765/`. Der Hub muss während der Nutzung des Dashboards oder WebSockets geöffnet bleiben.

Der Daten-WebSocket ist standardmäßig `ws://127.0.0.1:8766/ws` und wird in der Dashboard-GUI konfiguriert. Standardmäßig bindet er nur an Loopback. LAN-Modus bindet ausschließlich an eine ausgewählte private IPv4-Adresse und verlangt ein Token. Port, LAN-Modus und Adresse liegen in `config\hub-settings.json` relativ zu `AppContext.BaseDirectory`; das Token existiert nur im Arbeitsspeicher und wird nach jedem Neustart neu erzeugt. Es wird nicht in Dateien, Logs, URLs, Cookies oder Browser-Speicher geschrieben. Eine Neugenerierung macht das alte Token sofort ungültig.

Wenn Anno noch nicht läuft, bleibt der Hub geöffnet und versucht die Pipe mit begrenztem Backoff erneut zu verbinden.

### Aus dem Quellcode starten

Dieser Weg ist für die Entwicklung und den Start des Hubs aus dem Quellcode gedacht. Dafür wird das .NET 8 SDK benötigt. Führe das Startskript in PowerShell aus dem Projektordner aus:

```powershell
.\Start-AnnoPipeHub.ps1
```

Das Startskript startet den Hub und öffnet `http://127.0.0.1:8765/` im Browser. Der Hub muss während der Nutzung geöffnet bleiben. Das Skript startet Anno nicht automatisch.

### Konfigurationspfad

Die Einstellungsdatei wird immer als `Path.Combine(AppContext.BaseDirectory, "config", "hub-settings.json")` aufgelöst. Bei `dotnet run` ist `AppContext.BaseDirectory` typischerweise `src\AnnoPipeHub\bin\Debug\net8.0\` (oder `Release\net8.0\`); die Entwicklungsdatei liegt daher in dessen Unterordner `config\`. Bei der veröffentlichten Single-File-EXE liegt sie neben der EXE unter `publish\win-x64\config\hub-settings.json`. `config\hub-settings.example.json` im Repository ist nur eine Vorlage.

## Diagnose- und Datei-Log

Die lokale Debug-Konsole zeigt die tatsächlich empfangenen Pipe-Ereignisse, dekodierte Nachrichten, Verbindungsversuche, Reconnects und Decoderfehler. Sie hält höchstens 200 Einträge und folgt der ausgewählten Sprache. Das Datei-Log ist standardmäßig aus. Über `Log lokal speichern: An` beziehungsweise `Save log locally: On` wird es zur Laufzeit aktiviert; dann schreibt der Hub neue Ereignisse direkt neben die gestartete EXE, zum Beispiel `Anno117PipeHub-2026-09-25.log`. Die Queue und die Dateien sind begrenzt; alte Logdateien werden aufbewahrt, solange höchstens sieben Dateien vorhanden sind, und eine Tagesdatei wird bei 5 MB in nummerierte Teil-Dateien aufgeteilt.

Rohdaten als Hex werden standardmäßig nicht in die Datei geschrieben. Für ausdrücklich gewünschtes Raw-Logging kann der Prozess mit `ANNO117PIPEHUB_RAW_LOG=1` gestartet werden. Beim Ausschalten wird die Datei sauber geflusht und geschlossen; bereits geschriebene Logs bleiben erhalten. Kann der Hub neben der EXE nicht schreiben, zeigt das Dashboard eine lokalisierte Warnung und verwendet keinen stillen Fallback-Pfad. Tokens und Zugangsdaten werden nicht protokolliert.

### Release aus dem Quellcode erstellen

```powershell
.\Publish-Windows.ps1
```

Die Ausgabe liegt unter `publish\win-x64`. Das Skript verwendet Self-contained und Single-file für Windows x64 und entfernt die für den lokalen Kestrel-Betrieb nicht benötigte PDB- und IIS-Konfigurationsdatei. Die resultierende Ausgabe besteht damit aus `Anno117PipeHub.exe`; das Dashboard ist in der Anwendung enthalten.

## Nachrichtenformat

WebSocket-Nachrichten sind versionierte JSON-Objekte mit `schemaVersion`, `type` und `receivedAtUtc`. Unterstützt werden `hub.status`, `state.snapshot`, `session.start`, `session.end` und `area.production.statistics`. Bei einer neuen Verbindung werden Status und der aktuelle Snapshot zuerst gesendet. Client-Warteschlangen sind auf 64 Nachrichten begrenzt; bei Überlast werden alte Nachrichten verworfen, damit ein langsamer Client die Pipe nicht blockiert. Die [Modder-Integrationsanleitung](docs/MODDER-INTEGRATION.md) erklärt Verbindung, Authentifizierung, JSON-Verarbeitung und Reconnect. Die vollständige Anleitung liegt in `docs\INTEGRATION.md`, das Protokoll in `docs\PROTOCOL.md` und das Schema in `schema\anno117-event.schema.json`.

Im LAN-Modus wird das Token nach dem WebSocket-Verbindungsaufbau als `{"type":"auth","token":"..."}` gesendet. `ws://` verschlüsselt weder Token noch Spieldaten; keine Router-Portweiterleitung einrichten. Der Hub ändert die Windows-Firewall nicht automatisch.

Die Protokollreferenz liegt unverändert unter `references\ubisoft-anno117-pipe`. Sie ist Dokumentation und kein Build-Eingang, bleibt unter der dort enthaltenen Unlicense und wird in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) getrennt aufgeführt. Die Pipe ist laut Referenz eine experimentelle, nicht garantierte Schnittstelle; Kompatibilität mit späteren Spielversionen ist daher offen. Die Tests verwenden synthetische Frames und testen keine echte Verbindung zu einem laufenden Spiel.

### Lizenz und Projektregeln

Projekt-eigener Code steht unter der [MIT-Lizenz](LICENSE). Die enthaltene Pipe-Referenz ist getrenntes Drittanbietermaterial und bleibt unter ihrer ursprünglichen Lizenz; siehe [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Beiträge sollten [CONTRIBUTING.md](CONTRIBUTING.md) folgen. Sicherheitsmeldungen gehören nach [SECURITY.md](SECURITY.md).

## Tests

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.local\dotnet-home"
$env:NUGET_PACKAGES = "$PWD\.local\nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = "$PWD\.local\nuget\http-cache"
$env:TEMP = "$PWD\.local\tmp"
$env:TMP = $env:TEMP
dotnet run --project tests\AnnoPipeHub.Tests\AnnoPipeHub.Tests.csproj
```