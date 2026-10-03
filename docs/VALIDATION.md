# Validation · 2026-10-04

Target: installed Slay the Spire 2 v0.111.0, SDK 9.0.318.

Current implementation checks:

- 82 core behavior tests, including schema v3 unconstrained balance parameters versus legacy capped semantics; unlimited-event persistence/finite expiration; star/cost structure validation; bilingual duration-one and unlimited text; provider reasoning before malformed final content, missing reasoning, null effort omission and credential redaction; compact grouped cards/star costs/generated origins; full combat summary statistics; durable multi-batch pool deduplication, capacity expiry, stable empty/nonempty rewards, late results for later rewards, closure/stale-result rejection and failed-write rollback.
- 11 native game API smoke checks, including unchanged slot not marked WasJustUpgraded/green, native star-cost fallback-cache replacement, energy/star upgrade reductions, native save/load/downgrade, alongside all previous native card/Power/reward/Harmony checks.
- Release compilation without warnings/errors. No live Godot process, game save or installed mod was changed by these checks.
- Offline replay of all 2,207 recorded combat events and reconstruction of all 35 requests, restoring the full 60-event recent window and cumulative summaries: new prompts range from 14,251 to 43,627 characters, median 25,994 (old recorded request median 52,189). No provider calls; these replays omit newly available star-cost fields and generation history, so they do not establish future live request sizes or latency.
- Local mock HTTP v3 star Power generation -> final JSON validation -> CLI diagnostic sidecar: fixture reasoning_content was retained with stop finish reason, absent token usage stayed null. Three new star examples and five legacy v2 examples pass CLI validation.

Required live follow-up: enemy-first-turn timing including fully blocked/non-attacking openings; first-turn wins; cross-combat late completions; reward reload/reroll stability; run exit/reload cancellation; actual reasoning returned by the configured gateway; star income/payment/scaling and UI icons; multi-instance triggered effects without quotas. The mocked transport and native managed tests do not establish actual UI layout, gameplay balance or arbitrary mid-combat persistence.

## Earlier checks and historical notes (2026-10-03)


Target: Slay the Spire 2 `v0.111.0`, commit `41cef1ea`, installed game assemblies. .NET SDK `9.0.318`.

Completed:

- Release compilation against installed `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`: zero warnings/errors.
- 71 core behavior tests: invalid definitions/markup/targets/enums, schema, bounds, config, prompt size/old-event trimming, OpenAI-compatible HTTP shape, keys, fenced JSON, sanitized HTTP errors, timeout, byte limits, truncated completion, deduplication/cooldown/budget, frozen late-result handling, failed-refresh retention, wrong-count rejection, atomic cache and concurrent JSONL writing. Additional diagnostics checks cover metadata with prompts disabled, provider/validation stage and reason, HTTP status, malformed/truncated/filtered response redaction, and freeze cancellation versus timeout. Reasoning effort checks cover old/missing/null config compatibility, supported values/aliases, invalid-value rejection, exact outgoing parameter values and omission by default, audit metadata, and decoding final card JSON alongside unrelated reasoning content.
- 10 managed checks against the real game DLL: fallback-cache reset, native card clone isolation, native `ToSerializable` / `FromSerializable` with upgraded per-card SavedProperty definition, downgrade preservation, Silver Crucible third-charge provenance with no extra counter consumption or repeated upgrade, and Harmony patch installation/field compatibility. The test bootstraps only the model's serialization metadata; it does not start Godot or touch live saves.
- Local HTTP integration with the bundled Python mock provider: CLI sent an actual Chat Completions request, received one structured card, validated it, wrote it and revalidated from disk. This verifies transport, not an actual LLM's generation quality.

- Schema v2 checks cover legacy immediate defaults, unknown/illegal structured slots, trigger/lifetime/condition/scaling/repetition bounds, worst-case budgets, random hand routes, future start lifetimes, finite/combat-long expiration, quota consumption/reset/persistence, corrupted state rejection, bilingual descriptions, minimum prompt budget and atomic batch retention after an invalid complex refresh.
- Native v2 checks cover upgraded Power card restoration, multi-hit/condition/scaling/delay card serialization, captured upgraded values in an instanced Power, native SavedProperties restoration, clone isolation, corrupted payload rejection without mutation and placeholder icon routing. These establish managed contracts, not live effect execution.
- `examples/complex-cards.json`: all five card programs validated by the CLI. Actual local mock HTTP -> CLI generation -> file revalidation produced three v2 combat-long Power definitions. No external provider or credentials used in this check.

- Relaxed bounds: four-cost 52→63 and five-cost 65→80 attacks pass; all effect families test upgraded maximum, over-limit upgrade and scaled-total rejection. Low-cost oversized and repeated high-cost payloads remain rejected. Native high-cost cost/value upgrade, save and downgrade contracts pass.
- Opening request timing: default/missing config threshold 2, explicit 0–10 round trip, invalid threshold rejection, no requests/cooldown consumption before opening plays, latest opening observations, turn-end fallback with 0/1 plays, later refresh cooldown/budget, wait-on-reward bypass, fresh combat gating and freeze-before-threshold behavior pass.

Pending live verification:

1. Enable the Mod with a configured endpoint or `tools/mock_provider.py` and start a new singleplayer run.
2. Spend at least one turn long enough for generation to finish; verify vanilla reward choices plus the generated card.
3. Verify a fast combat/slow provider gives immediate vanilla-only rewards; late results do not appear on an already open screen.
4. Pick and play a generated attack/skill; verify native strength/weak/vulnerable/block hooks, AoE, draw/energy, poison and keywords.
5. Upgrade a generated card, save/reload and verify identical definition/cost/values.
6. Save/reload an unclaimed reward, close/reopen it, skip it, and test a reroll relic; verify stable options/no duplicates.
7. Test game over, restart, a new run and singleplayer coexistence with the user's usual Mods; ensure stale generation never attaches to another battle.
8. Confirm multiplayer generation is skipped and original rewards remain normal.
9. With Silver Crucible, verify generated candidates upgrade alongside vanilla candidates on each of the first three rewards, including reward save/load and reroll. Later randomly upgraded vanilla candidates must not force generated upgrades.
10. Disable combat and prompt recording; verify compact `data/generation` request/results still appear, no new combat journal is written, and a canceled request has its final diagnostic recorded across the next combat/reset.
11. Configure DeepSeek reasoning effort as `low` or `none` and restart the game; verify the request audit records that value and assess live latency/card validity. This change was checked with a fake HTTP handler; no additional paid provider calls were made to validate these settings.
12. Use `tools/mock_provider.py --fixture examples/complex-cards.json --card-index N` (N=0..4) to obtain/play each fixture. Verify conditional multi-hit damage and capped exhaust-pile scaling, future draw/energy, skill-play random damage over two turns, exhaust-event Power quotas and random hand/return commands. Confirm generated effects cannot recursively trigger themselves and duplicate Power instances retain separate counts.
13. Check Power title/description/placeholder icons, card description wrapping, all-enemy conditions, upgrades and downgrades. End a turn with Ethereal cards to verify exhaust events happen before finite effects expire; verify normal hand draw events count after the next owner-turn quota reset. Reload at native supported checkpoints and verify consistent definitions; do not assume arbitrary mid-combat persistence.

The running game and live saves were left untouched. The user's four recorded combats show valid early batches retained across failed refreshes in the first two battles, only FormatException failures in the third/fourth battles, and one in-flight request canceled by reward freezing. With explicit permission, one recorded failed context was replayed against the configured provider with its existing 1800-token limit and reproduced `completion_token_limit`. Old logs cannot prove every historical failure had this cause. New configurations default to 4096 tokens; the larger budget has not been verified with another live call. Full live combat and visual layout have not been validated.
