# Repository Guidelines

## Project Structure & Module Organization

Neow's Company adds LLM-generated card rewards to Slay the Spire 2 singleplayer.

- `src/Forge.Core/`: game-independent configuration, contracts, validation, provider transport, generation sessions, and persistence; .NET BCL only.
- `src/Forge.Mod/`: Godot state capture, Harmony hooks, generated cards, and mod manifest. Core sources are linked into its single DLL.
- `src/Forge.Tool/`: CLI for configuration, generation, and validation.
- `tests/Forge.Tests/` and `tests/Forge.GameSmoke/`: core behavior tests and installed-game API checks.
- `examples/`: JSON fixtures; `tools/mock_provider.py`: local HTTP fixture provider.
- `docs/`: architecture and validation notes. `build/` contains generated packages; card visuals use game placeholders.

## Build, Test, and Development Commands

Run from the repository root. Use .NET 9 SDK or newer compatible SDK. Mod builds require game installation `v0.111.0`.

- `.\build.ps1 -GameDir 'C:\Games\Slay the Spire 2'`: runs both test harnesses, builds the mod, regenerates `config.example.json`, and packages into `build/`. Use `-DotnetExe '<path>'` if needed.
- `dotnet run --project tests/Forge.Tests -c Release`: runs core tests without the game or live network.
- Set `STS2_GAME_DIR`, then run `dotnet run --project tests/Forge.GameSmoke -c Release`: checks real game assemblies without starting the game.
- `python tools/mock_provider.py --port 8000`: starts the fixture endpoint.
- `dotnet run --project src/Forge.Tool -- generate config.example.json examples/context.json generated.local.json`: exercises generation against the configured provider.
- `dotnet run --project src/Forge.Tool -- validate generated.local.json`: validates saved cards.

## Coding Style & Naming Conventions

Follow existing C# 13 style: four-space indentation, file-scoped namespaces, PascalCase types/members, camelCase locals/parameters, and `_camelCase` private fields. Use two-space indentation in project XML. Nullable checks and warnings-as-errors are enabled; no dedicated formatter or linter is configured.

## Testing Guidelines

Tests are custom console harnesses, without xUnit/NUnit or a coverage threshold. Add descriptive `Test("behavior", ...)` cases to the core harness and native contract checks to GameSmoke. Cover validation, failure, cancellation, and persistence behavior. Follow `docs/VALIDATION.md` for live reward, gameplay, and save/load checks; smoke tests do not establish UI correctness.

## Commit & Pull Request Guidelines

Use `main` and imperative commits, such as `Fix reward cache restoration`. Review `git diff --cached` before committing. Respect `.gitignore` for build output, local configuration, and runtime data. PRs should describe behavior changes, link issues, report checks/game version, and include screenshots for UI changes.

## Security & Architecture Constraints

Keep credentials in ignored `config.json` or `NEOWS_COMPANY_API_KEY`; never commit logs or game assemblies. Capture game state on the Godot thread, keep provider work detached, validate generated definitions, and preserve saved-card schema compatibility.
