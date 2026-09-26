# Third-Party Notices

This file separates project-owned code from reference material and platform/runtime components. It does not relicense third-party material under the project MIT License.

## Go dependencies

The Go implementation uses these permissively licensed dependencies:

- `github.com/Microsoft/go-winio` v0.6.2, MIT License, for Windows named pipes.
- `github.com/gorilla/websocket` v1.5.3, BSD-2-Clause License, for WebSocket framing and upgrades.

Their licenses and notices remain those of the respective upstream projects. The Go toolchain is not redistributed by this repository.

## Fonts

No third-party font files or font packages are included. The dashboard uses fonts available from the operating system and does not bundle a webfont.

## Ubisoft Anno 117 pipe reference

The directory `references/ubisoft-anno117-pipe/` contains reference material from the repository identified in its local `REFERENCE.md`:

- Source: `https://github.com/UbisoftMainzAnno/anno117_pipe_example`
- Recorded commit: `08b502bacc53e460a6f36b489f4e7227c2ae9cd4`
- Local files: `README.md` (adapted metadata), `REFERENCE.md` (local provenance note), `LICENSE`, `src/pipe.h`, and `src/pipe.cpp`
- Purpose: documentation/reference material only; it is not a build input for Anno 117 Pipe Hub
- License: the included `references/ubisoft-anno117-pipe/LICENSE` states the Unlicense; the copied source files and license remain separate from the MIT-licensed project code

The original reference license and notices are preserved in that directory. The presence of this reference does not imply Ubisoft affiliation, endorsement, or an official SDK.

## Open questions

- The exact license and notice requirements of the final Windows distribution should be reviewed against the dependency versions resolved in `go.sum` before redistribution.
- No other copied third-party source, font, image, or package was identified in the workspace during this repository-preparation pass.
