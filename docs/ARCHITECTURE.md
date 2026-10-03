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

Game callbacks detach observations on the Godot thread. ObservationProjector removes duplicated card descriptions/state reflection metadata from provider input, groups identical deck cards, preserves native star costs, and compacts pile/event references. Full original journals remain unchanged. CombatSummary keeps cumulative card/resource/damage statistics and a fixed first-enemy-turn summary independent of the recent-event window. Generation history identifies candidates and displayed/chosen/skipped designs.

Each battle owns request budgets, its provider session and journals; each run owns a CandidatePool. Initial prefetch waits for the first enemy turn to finish, regardless of damage received; a win before that submits a final-summary request. The adapter remembers the side from TurnStarted because v111 switches CurrentSide before TurnEnded. Winning or opening a reward seals future scheduling without canceling the pending request. Normal combat reset retires a winning session and keeps its journal open until completion. RunStarted/CleanUp/death close the old pool and cancel all active/retired sessions; old callbacks retain only the closed old pool.

Every complete valid batch appends to the pool. Exact executable fingerprints (ignoring name/flavor) deduplicate across batches; oldest candidates expire above capacity. Stable reward selection prefers mechanics not recently displayed, with oldest-first ties. Pool removal and reward freezing are one atomic snapshot transaction. Already shown rewards, including empty rewards, remain stable; a loaded battle with a frozen reward does not start generation again. Waiting mode awaits one bounded request before constructing native models.

Candidates are all configured before appending to the reward's `_cards`; vanilla choices are retained in original order. Default prefetch mode uses a synchronous prefix, then the original `CardReward.OnSelect` performs normal selection, skip, deck insertion and history bookkeeping with its other Harmony patches intact. Optional waiting mode uses an async wrapper followed by a reverse-patched snapshot of the original method; compatibility with other mods that patch OnSelect needs separate checking in that mode. A per-object marker avoids duplicate insertion on reopening. Populate hooks reattach frozen candidates during an open-screen reroll. No asynchronous task mutates a live reward list.

## Persistence

`NeowGeneratedCard.DefinitionPayload` is a native `SavedProperty` string. Native `ToSerializable`/`FromSerializable` owns deck save, restoration, upgrades, enchantments and cloning. The setter rebuilds canonical fallback caches for energy, dynamic variables and keywords before upgrade restoration. Definitions are not looked up by localized text or a provider-generated ID. The concrete model ID is `NEOW_GENERATED_CARD` (game slug rules); name, effects, type and rarity are per-instance data.

CandidatePool snapshots under data/runs are keyed by native RunManager._startTime (persisted by the game), seed, character and ascension. Each snapshot stores candidates, bounded history, executable fingerprints and all frozen room rewards. Late completed requests add only to their captured run pool. Same-seed runs with different native start times are isolated. Pre-pool data/rewards sidecars are imported only when their file time belongs to the current run. Native card payloads remain independent of pool sidecars.

Full combat journals and generation/reward audits use background JsonlJournal writers. generation_response records only explicitly extracted reasoning_content, finish_reason and numeric usage with revision correlation; known credentials/base_url are redacted. Missing reasoning and disabled reasoning recording produce null. Even a malformed final card JSON can have its reasoning recorded first. Headers, HTTP error bodies and arbitrary exception messages are not persisted. CLI generate writes an optional diagnostic sidecar; CLI prompt projects an observation without a provider call.

Generated reward cards inherit Silver Crucible upgrades from the vanilla candidates' `ModifyingRelics` provenance, through a registered clone and native upgrade/finalization. The already-consumed relic counter is not consulted and modification hooks are not repeated. Base card definitions remain unchanged in reward sidecars; an upgraded generated card retains its native serialization state.

## Schema v3

New requests ask for v3. Its validator checks executable structure and integer/sign consistency, but removes the legacy power score, zero-cost exhaust requirement and fixed numerical/repeat/duration ceilings. Event max_per_turn=0 and scaling_cap=0 mean unlimited; v1/v2 validation still rejects those formerly invalid meanings and preserves capped saved behavior. Structural effect/keyword counts remain bounded. Opcode/scaling enums are appended.

Stars use native PlayerCmd.GainStars and CanonicalStarCost/payment. Cost upgrades are explicit nonnegative reductions. Definition setters restore each instance's native star-cost cache; native upgrades/save/downgrade preserve costs. Only slots with a nonzero UpgradeAmount call UpgradeValueBy, avoiding the native WasJustUpgraded=true-on-zero behavior. CardText special-cases duration=1 and omits limit clauses for unlimited v3 effects. See [LLM_PROTOCOL.md](LLM_PROTOCOL.md) and [CARD_PROGRAM.md](CARD_PROGRAM.md).

## Version-sensitive game API surface

Card schema v2 introduced independent trigger, lifetime, per-turn quota, repetition, condition and capped scaling slots to each effect. `EffectRules` and `EffectTriggerRuntime` own game-independent numeric/lifecycle logic. `GeneratedEffectExecutor` resolves current state and uses native commands for immediate and triggered payoffs. `CardText` projects the same program into Chinese/English. Schema v1 keeps immediate defaults and rejects v2-only semantics; enum members are appended.

Every play arms a separate instanced `GeneratedEffectPower` after immediate effects finish. Its versioned `RuntimePayload` native SavedProperty captures the full definition, current base values, upgraded source state, remaining lifetimes, per-turn counters and arming-play exclusion. Clones deep-copy arrays. Native `SavedProperties` round-trips state; actual combat checkpoint persistence remains the game's responsibility.

The power resets quotas before owner turn setup, fires future start effects with `AfterPlayerTurnStart`'s choice context, fires end effects before hand cleanup, and expires finite event effects after cleanup. Event hooks scope to the owning player. Activation is consumed before awaiting effects, including failed conditions. Per-instance re-entry suppression and a shared async-flow depth cap prevent self/cross-power event cycles; random selections use native run RNG streams. Runtime power icons reuse native placeholders through scoped getter patches. See [CARD_PROGRAM.md](CARD_PROGRAM.md) for the DSL, legacy budgets, semantics and unsupported routes.

- `CombatManager.CombatBegan/TurnStarted/TurnEnded/CombatWon/CombatEnded`; `Reset` postfix closes sessions.
- `CombatHistory.Add(ICombatState, CombatHistoryEntry)` postfix captures events before after-event hooks.
- `CombatHistory.Clear` prefix captures the final state before original cleanup clears history.
- `CardReward.OnSelect` prefix + Harmony reverse patch; private `_cards`, `Options`, `_cardsWereManuallySet`, `_currentlyShownScreen`.
- `CardReward.Populate` postfix preserves generated candidates during reward reroll.
- `CardModel.Description` / `TitleLocString` getter postfixes apply only to `NeowGeneratedCard`.
- RunManager `RunStarted/CleanUp`, native `_startTime`; CardModel private `BaseStarCost` setter.
- CardModel `_dynamicVars`, `_keywords` caches; `LocTable._translations` for per-definition templates.

Runtime localization keys are hashes of definitions and language, so multiple generated cards of the same native model have separate templates. Names/flavor are restricted to plain text. Numeric descriptions use real `DynamicVar` objects and game previews. Card visuals borrow token-pool colorless metadata by overriding `Pool`; nothing is added to native card pools. Generated attacks/skills call native DamageCmd, CreatureCmd, CardPileCmd, PlayerCmd and PowerCmd.

## Extension steps

1. New LLM provider: implement `IContentGenerator<CardBatch>` and select it in a composition root; retain validation and lifecycle.
2. New effect: add the typed effect kind, allowed target/numeric bounds, prompt contract, native execution and text/hovers together; add behavioral checks. Never infer effects from generated prose.
3. New schema version: explicitly migrate saved card payloads before changing field meanings; keep old definitions executable. Future changes must not reinterpret existing saves.
4. New prompt strategy: consume `GenerationContext`, preserving explicit truncation and observation/instruction boundaries.

## Current limits

Singleplayer only. No new monsters, arbitrary rule powers, images, in-game configuration UI, automatic journal retention, full hidden-state capture, replay determinism across providers, multiplayer synchronization or complete balance simulation. Unknown provider fields are deliberately rejected at the card boundary. Optional protocol fields are configurable instead of automatic parameter retries. No current claim of a live game UI/combat pass.

`ProviderConfig.ReasoningEffort` is an optional Chat Completions request field. Null/missing values omit it entirely, preserving provider defaults and older configurations. Non-null validated values are sent verbatim, including `none` for providers supporting disabled reasoning, and recorded in request audits. Accepted values are sent without remapping by the client; provider-specific support remains the provider's responsibility. Card decoding reads only `message.content`; `reasoning_content` is separately extracted for diagnostics.
