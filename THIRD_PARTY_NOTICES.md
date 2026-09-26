# Third-Party Notices

This file separates project-owned code from reference material and platform/runtime components. It does not relicense third-party material under the project MIT License.

## .NET and ASP.NET Core

The application targets .NET 8 and ASP.NET Core and uses the shared framework supplied by Microsoft. The project has no explicit `PackageReference` entries in its `.csproj` files. The .NET and ASP.NET Core runtime and SDK remain subject to their own Microsoft licenses and notices. The self-contained Windows publish includes runtime components; their notices are not replaced by this repository's `LICENSE` file.

## Fonts

No third-party font files or font packages are included. The dashboard uses fonts available from the operating system and does not bundle a webfont.

## Ubisoft Anno 117 pipe reference

The directory `references/ubisoft-anno117-pipe/` contains reference material from the repository identified in its local `REFERENCE.md`:

- Source: `https://github.com/UbisoftMainzAnno/anno117_pipe_example`
- Recorded commit: `08b502bacc53e460a6f36b489f4e7227c2ae9cd4`
- Local files: `README.md`, `REFERENCE.md`, `LICENSE`, `src/pipe.h`, and `src/pipe.cpp`
- Purpose: documentation/reference material only; it is not a build input for Anno 117 Pipe Hub
- License: the included `references/ubisoft-anno117-pipe/LICENSE` states the Unlicense

The original reference license and notices are preserved in that directory. The presence of this reference does not imply Ubisoft affiliation, endorsement, or an official SDK.

## Open questions

- The exact license and notice requirements for the .NET runtime components distributed by a self-contained publish should be reviewed against the final release artifact and Microsoft's current distribution guidance before redistribution.
- No other copied third-party source, font, image, or package was identified in the workspace during this repository-preparation pass.
