namespace Forge.Core;

// Reviewed against the installed v0.111.0 models and native localization. These are
// design notes, not a simulation of combat. HP ranges come from native capture.
public static class GameKnowledge
{
    public static string CharacterBackground(CharacterMechanics mechanics)
    {
        var notes = new List<string>
        {
            "Attack and defense / 攻防: Direct damage, Block and efficient costs are valid core designs. Strength increases powered Attack damage and benefits repeated hits; Dexterity improves card Block. Weak reduces attack output, Vulnerable increases incoming attack damage, and Frail reduces card Block. Immediate defense, burst damage and combat-long scaling solve different problems.",
            "Card flow and resources / 运转与资源: Draw improves access to the deck; energy enables plays. Discard moves cards out of Hand without removing them from combat, allowing later redraws; it does not itself draw replacements. Exhaust removes a card from the normal draw/discard cycle for this combat and can support deck thinning or exhaust triggers. Retain keeps a card through ordinary end-of-turn discard, enabling timing and setup.",
            "Synergy and tradeoffs / 协同与取舍: Simple attacks, defensive skills, draw, energy, card manipulation, debuffs and persistent powers are equally valid building blocks. HP loss and healing trade immediate survival against payoff; permanent HP or gold changes affect the whole run. Use observed cards and relics to judge support for an engine; a mechanic being available alone does not establish synergy."
        };
        if (mechanics.Poison)
            notes.Add("Silent / 静默猎手: Shivs are temporary attack cards that support repeated attack plays and Strength scaling. Poison damages at the start of the affected creature's turn and then decreases; it supports sustained damage without relying on Attack modifiers. Draw/discard can improve card access and trigger existing discard payoffs, while Dexterity and Weak support defense. Sly (奇巧) plays a card for free when it is discarded from Hand before the end of your turn. Discard, draw and Sly can work together.");
        if (mechanics.Stars)
            notes.Add("Regent / 储君: Stars (星) are separate from energy, persist between turns within combat, and are not automatically refilled each turn. Star costs need a source of Stars. Forge (铸造) adds damage to the native Sovereign Blade (君王之剑); the first Forge each combat adds the Blade to Hand. The Blade has Retain and can be played again after drawing it back. This is combat scaling, not a permanent deck upgrade. Minion cards (仆从牌) are temporary playable cards, not allied creatures. Native base forms: minion_strike (仆从打击) costs 0, deals 6 damage and draws 1; minion_sacrifice (仆从捐躯) costs 0 and gains 7 Block; minion_dive (仆从俯冲) costs 0 and deals 13 damage. All three Exhaust.");
        if (mechanics.Necrobinder)
            notes.Add("Necrobinder / 亡灵契约师: Summon (召唤) brings back Osty (奥斯提) with the summoned HP, or increases his Max HP when already present, for this combat. Osty is an allied creature, not a card or orb; Summon is not an attack. His attacks need an explicit actor:osty damage action. Native base Souls (灵魂) cost 0, draw 2 and Exhaust. Doom (灾厄) is a death threshold: at the end of the enemy turn an enemy dies if its Doom is at least its current HP; Doom is not Poison or immediate damage.");
        if (mechanics.Defect)
            notes.Add("Defect / 故障机器人: Orbs occupy slots and have native passive and evoke effects. Channeling into full slots evokes the oldest orb. Lightning (闪电, lightning) passively damages a random enemy at turn end and damages a random enemy on evoke. Frost (冰霜, frost) passively gains Block at turn end and gains Block on evoke. Dark (黑暗, dark) increases its stored damage at turn end and deals it to the lowest-HP enemy on evoke. Plasma (等离子, plasma) gains energy at turn start and on evoke, and is unaffected by Focus. Glass (玻璃, glass) damages ALL enemies with its passive and evoke; each positive passive activation reduces its base value by 1, to a minimum of 0, including extra orb_passive activations. Its normal turn-end trigger activates this passive. Glass evoke deals twice its current Focus-modified passive value to ALL enemies, so it decays rather than accumulating like Dark. Channel, evoke and orb_passive are different actions.");
        return string.Join("\n", notes);
    }

    // Missing ascension in older/offline captures leaves variable quantities unspecified.
    public static string? MonsterMechanics(string id, int? ascension = null) => id switch
    {
        "LAGAVULIN_MATRIARCH" => "Has a sleep/setup window and Plating; later siphons player Strength/Dexterity while gaining Strength. Consider setup and sustained damage before debuffs accumulate.",
        "SOUL_FYSH" => "Applies Vulnerable and has an Intangible phase that limits each hit to 1 damage. Adds Beckon (呼唤) status cards to draw/discard piles: costs 1 to play, and loses 6 HP bypassing Block if held at turn end. Needs reliable defense and status-card handling.",
        "WATERFALL_GIANT" => "Builds Steam Eruption, which deals delayed damage after it is killed. Winning the damage race does not remove the need for defense.",
        "CEREMONIAL_BEAST" => $"Plow repeatedly deals high damage and gains Strength until remaining HP reaches its threshold{(ascension is null ? "" : $" ({(ascension >= 9 ? 160 : 150)} HP)")}, triggering a stun and removing Strength. Later applies Ringing to restrict card plays and continues scaling. Needs fast damage development, sustained defense and high value per card play.",
        "KIN_PRIEST" => "Fights with followers, applies Weak/Frail and scales Strength. Needs multi-target output and defense under stat debuffs.",
        "KIN_FOLLOWER" => "An enemy Minion that can gain Strength.",
        "VANTOM" => $"Starts with {(ascension is null ? "" : $"{(ascension >= 8 ? 9 : 8)} ")}Slippery stacks: each damaging hit that gets past Block loses one stack and has HP loss capped at 1 while stacks remain. Adds 3 Wounds to discard and gains Strength. Needs repeated damage instances, status tolerance and sustained output.",
        "AEONGLASS" => "Has Artifact, scales Strength, and Withering Presence adds Wither after repeated card plays. Wither damages the player if held at turn end. Consider efficient output and hand pollution rather than unlimited low-impact plays.",
        "QUEEN" => "Fights alongside one Torch Head Amalgam. Chains of Binding afflicts the first 3 eligible cards drawn each turn with Bound: only one Bound card can be played per turn; Bound clears at turn end. Extra draw provides unbound options beyond those 3 cards. Also applies Weak/Frail/Vulnerable. Needs draw capacity, sustained defense and output despite hand constraints.",
        "TORCH_HEAD_AMALGAM" => "An attacking enemy Minion accompanying the Queen, adding damage pressure.",
        "TEST_SUBJECT" => $"Revives through 3 phases, each with a separate HP pool{(ascension is null ? "" : $": {(ascension >= 8 ? "111/212/313" : "100/200/300")} HP")}. Phase 1 has Enrage, gaining Strength from player Skill plays. Phase 2 has rapidly growing multi-hit attacks; Painful Stabs adds Wounds to discard for hits with unblocked damage. Phase 3 introduces alternating Intangible protection and Burns. Needs sustained damage across the first two phases, multi-hit defense and status tolerance.",
        "PHANTASMAL_GARDENER" => "The elite encounter contains 4 gardeners together. Skittish gains Block on the first hit each turn; they also scale Strength. Consider multi-target damage or high damage to eliminate enemies quickly, plus defense against continually growing combined attack pressure.",
        "SKULKING_COLONY" => "Hardened Shell caps HP lost per turn; also scales Strength. One-turn burst cannot bypass the per-turn cap; plan repeated damage and defense.",
        "TERROR_EEL" => "Has a half-HP stun threshold, applies Vulnerable and gains Vigor. Needs burst capacity and defense against amplified attacks.",
        "BYGONE_EFFIGY" => "Does not attack for its first 2 turns, then attacks every turn with high damage. Slow increases attack damage received with each card played that turn. Needs to use the opening window to set up quickly and deal damage.",
        "BYRDONIS" => "Territorial gains Strength each turn, creating continually increasing incoming damage. Needs fast setup into substantial damage and/or Block; slow development faces escalating survival pressure.",
        "PHROG_PARASITE" => "Infested summons 4 Wrigglers on death. Adds 3 Infection cards to discard: unplayable statuses that deal 3 blockable damage each if held at turn end. Needs fast damage to limit deck pollution and status handling; multi-target damage is useful for clearing the later summons.",
        "WRIGGLER" => "A follow-up enemy that can gain Strength.",
        "FLAIL_KNIGHT" => "An attacking member of the knight group that can gain Strength. Needs ways to handle high incoming damage or eliminate enemies quickly.",
        "SPECTRAL_KNIGHT" => "A member of the three-knight group. While alive, Hex makes the player's cards Ethereal, threatening loss of unplayed key cards. Needs ways to eliminate enemies quickly or tolerate Ethereal cards.",
        "MAGI_KNIGHT" => "A member of the three-knight group. While alive, Dampen downgrades the player's cards. Needs ways to eliminate enemies quickly or tolerate downgraded cards.",
        "MECHA_KNIGHT" => "Has Artifact and gains Strength. Debuff-dependent builds need ways to strip Artifact, and must prepare for high single-turn damage and Burns added to the deck.",
        "SOUL_NEXUS" => "Applies Weak and Vulnerable, pressuring both attack output and defense.",
        "CRUSHER" => "Part of a surrounding pair with Rocket; applies Weak/Frail. Crab Rage grants Strength and Block when its ally dies. Needs multi-target output and defense through the surviving ally's buff.",
        "ROCKET" => "Part of a surrounding pair with Crusher; gains Strength and has a high-damage single-turn attack. Crab Rage grants Strength and Block when its ally dies. Needs substantial burst defense and strong output.",
        "KNOWLEDGE_DEMON" => "Offers 3 successive two-choice penalties: take recurring end-of-turn damage (6, then 7, then 8 per turn for each accepted penalty), or respectively draw 1 fewer card, play at most 3 cards per turn, or have 1 less energy. Damage is blockable. Also heals and gains Strength with single/multi-hit attacks. Needs substantial consistent Block, or card/energy efficiency under the chosen penalties.",
        "THE_INSATIABLE" => "Introduces Frantic Escape cards. Needs encounter-specific hand management, with energy and draw capacity reserved for the forced response.",
        "ENTOMANCER" => "Personal Hive adds Dazed to the draw pile when hit by an Attack. More attack hits can pollute the deck; it also gains Strength.",
        "INFESTED_PRISM" => "Vital Spark afflicts Skills so playing them gives Tainted, increasing attack damage taken that turn. Block from non-Skill sources can reduce HP losses without triggering this penalty. Needs defense that limits reliance on repeated Skill plays.",
        "DECIMILLIPEDE_SEGMENT_FRONT" or "DECIMILLIPEDE_SEGMENT_MIDDLE" or "DECIMILLIPEDE_SEGMENT_BACK" => "One of 3 simultaneous segments (front, middle, back). Needs multi-target output and defense against combined pressure.",
        _ => null
    };

    public sealed record EncounterMember(string Id, int Count, string Role);

    // Reviewed starting lineups, never inferred from AllPossibleMonsters (which
    // also includes later summons). Omit unknown encounters instead of guessing.
    public static EncounterMember[]? InitialLineup(string id) => id switch
    {
        "KNIGHTS_ELITE" => [new("FLAIL_KNIGHT", 1, "attacker"), new("SPECTRAL_KNIGHT", 1, "ethereal debuff"), new("MAGI_KNIGHT", 1, "downgrade debuff")],
        "QUEEN_BOSS" => [new("QUEEN", 1, "boss"), new("TORCH_HEAD_AMALGAM", 1, "attacking minion")],
        "PHANTASMAL_GARDENERS_ELITE" => [new("PHANTASMAL_GARDENER", 4, "scaling attacker")],
        "THE_KIN_BOSS" => [new("KIN_PRIEST", 1, "leader"), new("KIN_FOLLOWER", 2, "minion")],
        "KAISER_CRAB_BOSS" => [new("CRUSHER", 1, "surrounding attacker"), new("ROCKET", 1, "surrounding attacker")],
        "DECIMILLIPEDE_ELITE" => [new("DECIMILLIPEDE_SEGMENT_FRONT", 1, "front"), new("DECIMILLIPEDE_SEGMENT_MIDDLE", 1, "middle"), new("DECIMILLIPEDE_SEGMENT_BACK", 1, "back")],
        "PHROG_PARASITE_ELITE" => [new("PHROG_PARASITE", 1, "initial enemy; 4 Wrigglers spawn on death")],
        _ => null
    };
}
