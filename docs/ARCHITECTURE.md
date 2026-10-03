# Architecture

The game remains authoritative. Generated content is a validated data program, interpreted exclusively through existing game commands. Descriptions never drive behavior.

## Boundaries

| Layer | Responsibility | Dependencies |
| --- | --- | --- |
| `Forge.Core` | Definitions, configuration, prompt assembly, provider protocol, bounds, generation lifecycle, persistence | .NET BCL only |
| `Forge.Mod` | Detached state capture, game callbacks, card implementation, rewards, localization | Game's sts2/Godot/Harmony assemblies |
| `Forge.Tool` | Config scaffolding, provider integration, offline observation replay | Core |
| `Forge.Tests` | Invalid input, transport failures, concurrency and persistence behavior | Core, no live network |
| `Forge.GameSmoke` | Actual mutable card, clone, save, upgrade and Harmony contracts | Mod + user's game DLLs |

Core files are linked into the mod assembly for a single native-loader DLL. They also build as a standalone library for tests/tools. No game assemblies ship in either package.

## Thread and session ownership

Game callbacks capture/detach snapshots on the Godot thread. `GenerationSession` works on JSON values and card definitions on worker tasks. The provider never reads a game model or calls a game command. Each battle owns a cancellation token, request budget, last valid batch and its own journal sink. Only a finished, wholly validated batch replaces the previous batch. Invalid refreshes retain a previous valid batch.

Prefetch begins at turn boundaries and card-play completion, subject to minimum interval, one in-flight request and per-battle request cap. Opening a reward freezes the currently published batch and cancels in-flight work without awaiting it. A subsequent battle/reset closes the prior session. The optional waiting mode awaits a bounded request then returns to Godot's synchronization context before touching game models.

Candidates are all configured before appending to the reward's `_cards`; vanilla choices are retained in original order. Default prefetch mode uses a synchronous prefix, then the original `CardReward.OnSelect` performs normal selection, skip, deck insertion and history bookkeeping with its other Harmony patches intact. Optional waiting mode uses an async wrapper followed by a reverse-patched snapshot of the original method; compatibility with other mods that patch OnSelect needs separate checking in that mode. A per-object marker avoids duplicate insertion on reopening. Populate hooks reattach frozen candidates during an open-screen reroll. No asynchronous task mutates a live reward list.

## Persistence

`NeowGeneratedCard.DefinitionPayload` is a native `SavedProperty` string. Native `ToSerializable`/`FromSerializable` owns deck save, restoration, upgrades, enchantments and cloning. The setter rebuilds canonical fallback caches for energy, dynamic variables and keywords before upgrade restoration. Definitions are not looked up by localized text or a provider-generated ID. The concrete model ID is `NEOW_GENERATED_CARD` (game slug rules); name, effects, type and rarity are per-instance data.

Reward caches are atomically written sidecars, keyed by seed/character/ascension/act/floor/room ID. First shown candidates, including no candidates, are frozen. Loading repopulated rewards uses the sidecar, never a new provider call. Same-seed reruns and multiple rewards in a room share candidates; a future run UUID/store interface can refine this without changing card payloads. Chosen cards remain playable without the sidecar as long as the Mod is present.

Logs serialize detached snapshots through a single background writer. The full journal is separate from limited LLM context. Provider failures persist only exception categories, never response bodies or authorization values.

## Version-sensitive game API surface

- `CombatManager.CombatBegan/TurnStarted/TurnEnded/CombatWon/CombatEnded`; `Reset` postfix closes sessions.
- `CombatHistory.Add(ICombatState, CombatHistoryEntry)` postfix captures events before after-event hooks.
- `CombatHistory.Clear` prefix captures the final state before original cleanup clears history.
- `CardReward.OnSelect` prefix + Harmony reverse patch; private `_cards`, `Options`, `_cardsWereManuallySet`, `_currentlyShownScreen`.
- `CardReward.Populate` postfix preserves generated candidates during reward reroll.
- `CardModel.Description` / `TitleLocString` getter postfixes apply only to `NeowGeneratedCard`.
- CardModel `_dynamicVars`, `_keywords` caches; `LocTable._translations` for per-definition templates.

Runtime localization keys are hashes of definitions and language, so multiple generated cards of the same native model have separate templates. Names/flavor are restricted to plain text. Numeric descriptions use real `DynamicVar` objects and game previews. Card visuals borrow token-pool colorless metadata by overriding `Pool`; nothing is added to native card pools. Generated attacks/skills call native DamageCmd, CreatureCmd, CardPileCmd, PlayerCmd and PowerCmd.

## Extension steps

1. New LLM provider: implement `IContentGenerator<CardBatch>` and select it in a composition root; retain validation and lifecycle.
2. New effect: add the typed effect kind, allowed target/numeric bounds, prompt contract, native execution and text/hovers together; add behavioral checks. Never infer effects from generated prose.
3. New schema version: explicitly migrate saved card payloads before changing field meanings; keep old definitions executable. Future changes must not reinterpret existing saves.
4. New prompt strategy: consume `GenerationContext`, preserving explicit truncation and observation/instruction boundaries.
5. Architect's Gaze: use a distinct versioned monster definition and validator, implement `IContentGenerator<MonsterBatch>`, and add its own native adapter and injection hooks. Reuse journals/transport and the bounded-session pattern. Do not expand card-reward patches to own encounters.

## Current limits

Singleplayer only. No new monsters/powers/mechanics, images, in-game configuration UI, automatic journal retention, full hidden-state capture, replay determinism across providers, multiplayer synchronization or complete balance simulation. Unknown provider fields are deliberately rejected at the card boundary. Optional protocol fields are configurable instead of automatic parameter retries. No current claim of a live game UI/combat pass.
