# Neow's Company · 涅奥的陪伴

English | [简体中文](README.zh-CN.md)

Adds LLM-generated card rewards to Slay the Spire 2 singleplayer. Neow designs new cards based on your deck and combat history, then adds them to the game's combat card rewards.

Current mod version: `0.1.0`. Target game version: `v0.111.0`. Singleplayer only; compatibility needs to be checked again after game updates. Card artwork uses game placeholders, and no other mods are required.

## Installation

1. Exit the game and place the prebuilt package's `NeowsCompany` folder in the game's `mods` directory. To install from source, see the build instructions below.
2. Copy `config.example.json` in that folder to `config.json`, then set `provider.base_url`, `provider.model`, and your credentials. The first launch also creates a default configuration if needed.
3. Enable Neow's Company (涅奥的陪伴) in the game's mod menu, restart, and begin a singleplayer run.

`base_url` is the root URL of an OpenAI-compatible API; the client appends `/chat/completions`. The default, `http://127.0.0.1:8000/v1`, points to a local service and does not include a model. For a remote service, use its HTTPS API root URL.

Store your key in the local `provider.api_key` field or the `NEOWS_COMPANY_API_KEY` environment variable. A nonempty environment variable takes precedence and should be set before launching Steam or the game. Restart the game after changing the configuration. **Never put real keys in `config.example.json` or commit them to Git.**

## Gameplay

By default, generation starts after the first enemy turn ends, with up to three requests per combat and at least 15 seconds between requests. Valid cards enter the current run's candidate pool. When a reward opens, one available candidate is added alongside the original options. If no candidate is ready, the original reward appears immediately. Rewards never wait for the network or change while you are choosing. Late results can appear after later combats.

New cards use protocol v6: two complete upgrade forms, general card selection and creation/transformation, rule groups, and dynamic expressions. Supported character mechanics include Osty/Souls/Doom, Orbs/Focus, Forge/Stars, and Shivs/Sly. The LLM can combine implemented effects but cannot return executable code. There is no power budget; balance needs to be evaluated through play. Earlier protocols have no migration path, so start a new run when testing.

Generated card definitions are stored with the game's native card saves, so loading them does not require another model call. The candidate pool and frozen rewards are stored separately in `data/runs`; retain this directory to restore rewards for an ongoing run. Keep the mod enabled when loading saves containing generated cards.

## Configuration and privacy

Reward counts, request timing, style instructions, and logging options are configurable. See [Configuration and troubleshooting](docs/CONFIGURATION.md). Generation calls, prompts, and reasoning returned by the provider are logged by default, with no automatic cleanup. Full combat logging has been discontinued; old `data/combats` files can be deleted after exiting the game.

Your game state and custom prompts are sent to the configured provider. API keys are sent in authentication headers rather than prompt fields; still check logs for sensitive information before sharing them. `config.json`, runtime data, and local outputs are covered by `.gitignore`.

## Building from source

Requires the .NET 9 SDK (or a compatible SDK that can build `net9.0`) and a local Windows installation of game version `v0.111.0`. The project has no third-party NuGet dependencies. Game assemblies are not included in release packages.

```powershell
.\build.ps1 -GameDir 'C:\Games\Slay the Spire 2'
.\install.ps1 -GameDir 'C:\Games\Slay the Spire 2'
```

By default, `build.ps1` runs core tests and game API checks, then creates `build/NeowsCompany/` and `build/NeowsCompany-0.1.0.zip`. If the SDK is not on PATH, pass `-DotnetExe '<path to dotnet.exe>'`. Exit the game before running the installation script; it preserves existing configuration and runtime data. Both English and Chinese READMEs are included in the package.

You can test the core and generation pipeline without the game or a live LLM:

```powershell
dotnet run --project tests/Forge.Tests -c Release
# Start the fixture endpoint in a separate terminal; it returns fixed cards:
python tools/mock_provider.py --port 8000
# These commands use the repository's default local-service configuration:
dotnet run --project src/Forge.Tool -- generate config.example.json examples/context.json generated.local.json
dotnet run --project src/Forge.Tool -- validate generated.local.json
# Inspect the actual prompts without calling a provider:
dotnet run --project src/Forge.Tool -- prompt config.example.json examples/context.json prompt.local.json
```

## Viewing generation logs

The local viewer requires Python 3.10+ and no third-party packages. The Windows launcher locates Python and also supports an existing local Codex Python runtime. Pass the game directory to start the server and open your browser automatically:

```powershell
.\view-generation.ps1 -GameDir 'C:\Games\Slay the Spire 2'
# You can also specify a log directory or start the viewer directly with Python:
.\view-generation.ps1 -Directory 'C:\Games\Slay the Spire 2\mods\NeowsCompany\data\generation'
python tools/view_generation.py 'C:\Games\Slay the Spire 2\mods\NeowsCompany\data\generation'
```

Without a path, the launcher uses `STS2_GAME_DIR`, falling back to the repository's `data/generation` directory. The page groups records by combat and request number. Search by card name, model, or failure reason, and filter successful or failed requests. Each record shows base and upgraded card text, effect structures, prompts, reasoning, raw model content, timing, token usage, cache hits, and reward audit events.

New logs include effect text from the shared CardText renderer; the viewer reconstructs text from definitions in older logs. Opening a JSON block expands nested objects and lists, which can still be collapsed manually. The Copy button copies the full contents regardless of their collapsed state. `OBSERVATION_JSON` has its own view; reasoning and raw output start collapsed. Raw output preserves whitespace and code fences, with known credentials and service URLs redacted. Content missing from old logs cannot be recovered. Times use your browser's local timezone, and requests with no completion record are marked pending.

The page refreshes every five seconds by default, so you can keep it open while playing. Invalid or unfinished JSONL lines are skipped with a notice. The viewer only reads logs and listens on `127.0.0.1`; it does not call the model or change game files. Press `Ctrl+C` to stop it. Launcher options include `-Port 0` to choose an available port, `-NoBrowser` to run only the server, and `-PythonExe '<path to python.exe>'`. The corresponding Python options are `--port 0` and `--no-browser`.

## Documentation

Most detailed documentation is in Chinese; the architecture guide is in English:

- [Configuration and troubleshooting](docs/CONFIGURATION.md): settings, data directories, and common failure causes.
- [LLM protocol](docs/LLM_PROTOCOL.md): request context, output, and candidate pool rules.
- [Card protocol](docs/CARD_PROGRAM.md): v6 forms, actions, targets, and rule semantics.
- [Architecture](docs/ARCHITECTURE.md): module boundaries, threading, saves, and game hooks.
- [Validation guide](docs/VALIDATION.md): automated checks and in-game acceptance steps.

Automated tests and assembly checks do not establish correctness of the game UI, effect execution, or compatibility with other mods.
