# Architecture

The game remains authoritative. Generated content is a validated data program, interpreted exclusively through existing game commands. Descriptions never drive behavior.

## Boundaries

| Layer | Responsibility | Dependencies |
| --- | --- | --- |
| `Forge.Core` | Definitions, configuration, prompt assembly, provider protocol, validation, generation lifecycle, persistence | .NET BCL only |
| `Forge.Mod` | Detached state capture, game callbacks, card implementation, rewards, localization | Game's sts2/Godot/Harmony assemblies |
| `Forge.Tool` | Config scaffolding, provider integration, offline observation replay | Core |
| `Forge.Tests` | Invalid input, transport failures, concurrency and persistence behavior | Core, no live network |
| `Forge.GameSmoke` | Actual mutable card, clone, save, upgrade and Harmony contracts | Mod + user's game DLLs |

Core files are linked into the mod assembly for a single native-loader DLL. They also build as a standalone library for tests/tools. No game assemblies ship in either package.

## Thread and session ownership

Game callbacks detach observations on the Godot thread. ObservationProjector removes duplicated card descriptions/state reflection metadata from provider input, groups identical deck cards, preserves native star costs, and projects compact combat statistics. CombatSummary keeps cumulative card/resource/damage statistics and a fixed first-enemy-turn summary independent of the recent-event window. Generation history identifies candidates and displayed/chosen/skipped designs. Events and summaries stay in memory; full combat journals and per-event state snapshot capture have been removed.

Each battle owns request budgets, its provider session and generation journal; each run owns a CandidatePool. Initial prefetch waits for the first enemy turn to finish, regardless of damage received; a win before that submits a final-summary request. The adapter remembers the side from TurnStarted because v111 switches CurrentSide before TurnEnded. Winning or opening a reward seals future scheduling without canceling the pending request. Normal combat reset retires a winning session and keeps its journal open until completion. RunStarted/CleanUp/death close the old pool and cancel all active/retired sessions; old callbacks retain only the closed old pool.

Every complete valid batch appends to the pool. Exact executable fingerprints (ignoring name/flavor) deduplicate across batches; oldest candidates expire above capacity. Stable reward selection prefers mechanics not recently displayed, with oldest-first ties. Pool removal and reward freezing are one atomic snapshot transaction. Already shown rewards, including empty rewards, remain stable; a loaded battle with a frozen reward does not start generation again. Waiting mode awaits one bounded request before constructing native models.

Candidates are all configured before appending to the reward's `_cards`; vanilla choices are retained in original order. Default prefetch mode uses a synchronous prefix, then the original `CardReward.OnSelect` performs normal selection, skip, deck insertion and history bookkeeping with its other Harmony patches intact. Optional waiting mode uses an async wrapper followed by a reverse-patched snapshot of the original method; compatibility with other mods that patch OnSelect needs separate checking in that mode. A per-object marker avoids duplicate insertion on reopening. Populate hooks reattach frozen candidates during an open-screen reroll. No asynchronous task mutates a live reward list.

## Persistence

`NeowGeneratedCard.DefinitionPayload` is a native `SavedProperty` string. Native `ToSerializable`/`FromSerializable` owns deck save, restoration, upgrades, enchantments and cloning. The setter rebuilds canonical fallback caches for energy, dynamic variables and keywords before upgrade restoration. Definitions are not looked up by localized text or a provider-generated ID. The concrete model ID is `NEOW_GENERATED_CARD` (game slug rules); name, effects, type and rarity are per-instance data.

CandidatePool snapshots under data/runs are keyed by native RunManager._startTime (persisted by the game), seed, character and ascension. Each snapshot stores candidates, bounded history, executable fingerprints and all frozen room rewards. Late completed requests add only to their captured run pool. Same-seed runs with different native start times are isolated. Native card payloads remain independent of pool sidecars.

Generation/reward audits use background JsonlJournal writers. The legacy record_combat config field is accepted but ignored, even when true. generation_response records only explicitly extracted model content, reasoning_content, finish_reason and numeric usage with revision correlation; known credentials/base_url are redacted. Missing output fields and disabled reasoning recording produce null. Model content is captured before trimming, fence removal, finish-reason checks or card decoding, so malformed or truncated card JSON remains inspectable. generation_ready includes card_texts snapshots from the shared CardText renderer for each card and form, outside the saved-card schema. Headers, HTTP error bodies and arbitrary exception messages are not persisted. CLI generate writes an optional diagnostic sidecar; CLI prompt projects an observation without a provider call.

Generated reward cards inherit Silver Crucible upgrades from the vanilla candidates' `ModifyingRelics` provenance, through a registered clone and native upgrade/finalization. The already-consumed relic counter is not consulted and modification hooks are not repeated. Base card definitions remain unchanged in reward sidecars; an upgraded generated card retains its native serialization state.

## Card protocol v5

Card families hold two complete forms. The native card reads the active form for cost, keywords, targets and dynamic variables; upgrade/downgrade rebuild these caches instead of applying numeric deltas. Runtime-only InstanceKey is a native SavedProperty; new copies receive a new identity, and rule snapshots retain their original source identity. Definitions have no LLM schema or instance metadata. Old definitions are not migrated.

Card actions share typed selectors, card sources, expressions and conditions. NativeMechanics translates registered card/power/orb aliases; GeneratedEffectExecutor uses native commands for selection, batched discard, creation, transformation, AutoPlay, Osty, Forge and orb resolution. No provider-returned type name is reflected or executed. Structural validation and character gates run before publication; runtime operation/depth guards prevent unbounded effect chains without imposing a balance budget.

GeneratedEffectPower captures the active form, native amount variables, actual paid resources and per-rule counters. A rule owns a group of effects, so a quota is consumed once after its condition succeeds. Occurrence counts use native combat history, including events before the rule was armed; event-time card facts are captured before later mutations. Native rule payloads clone their counters independently. The exact arming CardPlay is ignored, rather than ignoring the next unrelated play.

Immediate attack damage uses native attack commands; triggered damage uses Unpowered damage commands. Previews evaluate numeric expressions and deterministic resource/pile changes without choosing cards, channeling, consuming RNG or mutating state. Immediate expression amounts in the hand/play pile render native dynamic-variable previews, including target modifiers; elsewhere and in listeners they retain formula text. Random chains and future events are not fully forecast. Literal resource gains use native sprite-font icons.

The provider sees a design projection containing grouped deck descriptions, meaningful resources and compact combat statistics. Session identifiers, schema envelopes, reflection trees, complete piles and raw event logs stay client-side. Generation journals remain available for diagnostics. Candidate snapshots and generation audits record card_protocol:5 outside the LLM request.

See CARD_PROGRAM.md, LLM_PROTOCOL.md and MECHANIC_COVERAGE.md for the supported contracts and deliberate boundaries.
