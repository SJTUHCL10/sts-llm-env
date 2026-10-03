# Validation · 2026-10-02

Target: Slay the Spire 2 `v0.111.0`, commit `41cef1ea`, installed game assemblies. .NET SDK `9.0.318`.

Completed:

- Release compilation against installed `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`: zero warnings/errors.
- 28 core behavior tests: invalid definitions/markup/targets/enums, schema, bounds, config, prompt size/old-event trimming, OpenAI-compatible HTTP shape, keys, fenced JSON, sanitized HTTP errors, timeout, byte limits, truncated completion, deduplication/cooldown/budget, frozen late-result handling, failed-refresh retention, wrong-count rejection, atomic cache and concurrent JSONL writing.
- 5 managed checks against the real game DLL: fallback-cache reset, native card clone isolation, native `ToSerializable` / `FromSerializable` with upgraded per-card SavedProperty definition, downgrade preservation, and Harmony patch installation/field compatibility. The test bootstraps only the model's serialization metadata; it does not start Godot or touch live saves.
- Local HTTP integration with the bundled Python mock provider: CLI sent an actual Chat Completions request, received one structured card, validated it, wrote it and revalidated from disk. This verifies transport, not an actual LLM's generation quality.

Pending live verification:

1. Enable the Mod with a configured endpoint or `tools/mock_provider.py` and start a new singleplayer run.
2. Spend at least one turn long enough for generation to finish; verify vanilla reward choices plus the generated card.
3. Verify a fast combat/slow provider gives immediate vanilla-only rewards; late results do not appear on an already open screen.
4. Pick and play a generated attack/skill; verify native strength/weak/vulnerable/block hooks, AoE, draw/energy, poison and keywords.
5. Upgrade a generated card, save/reload and verify identical definition/cost/values.
6. Save/reload an unclaimed reward, close/reopen it, skip it, and test a reroll relic; verify stable options/no duplicates.
7. Test game over, restart, a new run and singleplayer coexistence with the user's usual Mods; ensure stale generation never attaches to another battle.
8. Confirm multiplayer generation is skipped and original rewards remain normal.

The running game was left untouched. No real provider credentials were used. Full live combat and visual layout have not been validated.
