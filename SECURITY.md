# Security Policy

## Scope

This policy covers the Anno 117 Pipe Hub source code, dashboard, local WebSocket listener, LAN listener, configuration handling, and release scripts in this repository.

The tool reads a local Windows named pipe and can expose decoded data over unencrypted `ws://`. Do not expose the listener through router port forwarding or an untrusted network.

## Reporting a vulnerability

Please use the repository's private GitHub security reporting feature when it is enabled. Do not publish credentials, personal data, or a complete exploit in a public issue.

This repository does not contain a private email address or promise a response time. Until private reporting is configured on GitHub, use the repository owner or maintainer contact channel configured on the repository page and share only the minimum reproducible details.

## Handling secrets

Never commit `config/hub-settings.json`, runtime logs, published binaries, or copied local credentials. LAN mode has no application-level authentication; `ws://` does not encrypt the connection or game data. Use it only on a trusted private network and do not configure router port forwarding.
