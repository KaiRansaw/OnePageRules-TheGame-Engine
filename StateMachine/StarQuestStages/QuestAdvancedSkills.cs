using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// Star Quest skills heroes learn from level 2 on (tiers 1-3, every class plus the core list). Built from
    /// the same pieces as the starting skills: stat grants, rule grants, table markers, pushes and hits, and
    /// the round flags in <see cref="QuestFlags"/>. <see cref="NotPossible"/> names the few this engine
    /// can't play, with the reason shown in the hero's skill menu.
    /// </summary>
    public static class QuestAdvancedSkills
    {
        /// <summary>Skills that can't work in this game, and why (shown greyed out in the Skill menu).</summary>
        public static readonly IReadOnlyDictionary<string, string> NotPossible = new Dictionary<string, string>
        {
            ["Leap"] = "There are no jumping rolls in this game, so Leap has no effect.",
            ["Confusion"] = "Enemies can't attack their own side in this game.",
            ["Infiltrate"] = "Moving only outside enemy line of sight isn't in this game yet.",
            ["Mockery"] = "Enemies never take stat tests in this game, so this has no effect.",
            ["Spiteful Curse"] = "Enemies never take stat tests in this game, so this has no effect.",
        };

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

        private static string? Always(IGameContext c, UnitData h) => null;
        private static int D3(IGameContext c) => (HordePlacement.RollD6(c) + 1) / 2;
        private static Task Toast(IGameContext c, string text) => QuestActions.Toast(c, text);

        private static Func<IGameContext, UnitData, string?> NeedsEnemy(float range) =>
            (c, h) => QuestActions.Enemies(c, h, range).Count > 0 ? null : $"No enemy within {range:0}\".";

        private static Func<IGameContext, UnitData, string?> NeedsFriend(float range, bool includeSelf, bool heroesOnly) =>
            (c, h) => QuestActions.Friends(c, h, range, includeSelf, heroesOnly).Count > 0 ? null
                : $"No {(heroesOnly ? "hero" : "friendly unit")} within {range:0}\".";

        private static Func<IGameContext, UnitData, string?> Caster => QuestTreasure.IsCaster;

        private static List<UnitData> Far(IGameContext c, UnitData h, float over) =>
            QuestActions.Enemies(c, h, 999f).Where(e => QuestActions.Distance(h, e) > over).ToList();

        private static Task<List<UnitData>> PickEnemies(IGameContext c, UnitData h, string skill, float range, int count) =>
            QuestActions.PickUnits(c, h, skill, QuestActions.Enemies(c, h, range), count);

        private static Task<List<UnitData>> PickFriends(IGameContext c, UnitData h, string skill, float range, int count,
            bool includeSelf, bool heroesOnly = false) =>
            QuestActions.PickUnits(c, h, skill, QuestActions.Friends(c, h, range, includeSelf, heroesOnly), count);

        private static DataBinding<UnitData>? Binding(IGameContext c, UnitData unit) =>
            c.GameDataStore.GetAllDataBindings<UnitData>().FirstOrDefault(b => ReferenceEquals(b.GetValue(), unit));

        private static async Task MakeShaken(IGameContext c, UnitData unit)
        {
            if (Binding(c, unit) is { } b) await MoraleUtilities.ApplyShakenWithPresentation(c, b);
        }

        /// <summary>Asks how much power to spend (0-3) and spends it.</summary>
        private static async Task<int> SpendX(IGameContext c, UnitData h, string skill, string what)
        {
            var request = new StringSelectionRequest(h.PlayerID, $"{skill}: spend how much power? {what} ({h.QuestPower} left)",
                new List<string> { "0", "1", "2", "3" }, Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: skill);
            int x = int.TryParse(await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request), out int v) ? Math.Clamp(v, 0, 3) : 0;
            if (x > 0) await QuestActions.SpendPower(c, h, x, skill);
            return x;
        }

        private static async Task Flag(IGameContext c, UnitData h, string flag, string text, string value = "")
        {
            QuestFlags.Set(c, h, flag, value);
            await Toast(c, QuestSkill.Line(h.Name, text));
        }

        private static async Task GrantSelf(IGameContext c, UnitData h, string rule, ELifetime life, string text)
        {
            QuestTreasure.GrantRule(c, h, h, rule, life);
            await Toast(c, QuestSkill.Line(h.Name, text));
        }

        private static async Task ApPlusOne(IGameContext c, UnitData h)
        {
            QuestTreasure.GrantRule(c, h, h, "AP(+1) in melee", ELifetime.ThisRound);
            QuestTreasure.GrantRule(c, h, h, "AP(1) when shooting", ELifetime.ThisRound);
            await Toast(c, $"{h.Name}: AP(+1) this round.");
        }

        private static async Task Faster(IGameContext c, UnitData target, string rule, ELifetime life, string text)
        {
            QuestTreasure.GrantRule(c, target, target, rule, life);
            await Toast(c, $"{target.Name}: {text}.");
        }

        private static async Task MoraleThen(IGameContext c, UnitData h, string skill, float range, int count,
            Func<UnitData, Task> onFail)
        {
            foreach (UnitData e in await PickEnemies(c, h, skill, range, count))
            {
                if (await QuestActions.Morale(c, e)) { await Toast(c, $"{skill}: {e.Name} holds."); continue; }
                await onFail(e);
            }
        }

        private static Task Push(IGameContext c, UnitData h, UnitData e, float inches) =>
            QuestActions.Shove(c, h, e, inches);

        private static Task Pull(IGameContext c, UnitData h, UnitData e, float inches) =>
            QuestActions.Shove(c, h, e, inches, away: false);

        private static async Task Teleport(IGameContext c, UnitData h, int inches, string skill)
        {
            await Toast(c, $"{skill}: place {h.Name} anywhere within {inches}\".");
            await new GameOperationServices(c).MoveUnit(h, inches, isOptional: true, controller: h.PlayerID);
        }

        private static async Task DebuffEnemy(IGameContext c, UnitData h, string skill, float range, ERollKind roll, int delta, string text)
        {
            foreach (UnitData e in await PickEnemies(c, h, skill, range, 1))
            {
                QuestActions.Grant(h, e, roll, delta, ELifetime.ThisRound, c);
                await Toast(c, $"{skill}: {e.Name} {text} this round.");
            }
        }

        private static async Task Place(IGameContext c, UnitData h, string skill, string kind, float range, int rounds, string text)
        {
            Position p = await QuestTreasure.PickPoint(c, h, skill, range);
            QuestTreasure.PlaceMarkerAt(c, kind, p, rounds);
            await Toast(c, $"{skill}: {text}.");
        }

        private static async Task PlaceOnHero(IGameContext c, UnitData h, string kind, string text)
        {
            Position p = h.Models.FirstOrDefault(m => m.GetIsAlive())?.Position ?? new Position(0f, 0f);
            QuestTreasure.PlaceMarkerAt(c, kind, new Position(p.x, p.z), 1);
            await Toast(c, QuestSkill.Line(h.Name, $"{text} (around where it stands now)"));
        }

        private static async Task Heal(IGameContext c, UnitData u, int wounds, string skill)
        {
            QuestActions.Heal(u, wounds, c);
            await Toast(c, $"{skill}: {u.Name} restores {wounds} wound{(wounds == 1 ? "" : "s")}.");
        }

        private static void LoseCondition(UnitData u)
        {
            string? first = QuestActions.Sheet(u)?.Conditions.FirstOrDefault();
            if (first != null) QuestTreasure.RemoveCondition(u, first);
        }

        private static async Task SpellTokenForPower(IGameContext c, UnitData h, string skill)
        {
            await QuestTreasure.AddSpellTokens(c, h, 1);
            await QuestActions.SpendPower(c, h, D3(c), skill);
        }

        private static async Task HitsX(IGameContext c, UnitData h, string skill, float range, int plus, int ap)
        {
            int x = await SpendX(c, h, skill, $"The target takes X+{plus} hits.");
            foreach (UnitData e in await PickEnemies(c, h, skill, range, 1))
                await QuestActions.DealHits(c, h, e, x + plus, ap, skill);
        }

        private static async Task DefenseX(IGameContext c, UnitData h, string skill)
        {
            int x = await SpendX(c, h, skill, "+X+1 to Defense the next time it takes hits.");
            QuestActions.Grant(h, h, ERollKind.Save, x + 1, ELifetime.NextTrigger, c, skill);
            await Toast(c, $"{h.Name}: +{x + 1} to Defense the next time it takes hits.");
        }

        private static async Task Untargetable(IGameContext c, UnitData h, string skill)
        {
            await Flag(c, h, QuestFlags.Infiltrating, "enemies can't target it this round (until it targets one)");
        }

        private static async Task OnKill(IGameContext c, UnitData h, string flag, string text) =>
            await Flag(c, h, flag, text, QuestTreasure.EnemiesDestroyed(c).ToString());

        private static async Task Charm(IGameContext c, UnitData h, string skill)
        {
            foreach (UnitData e in await PickEnemies(c, h, skill, 6f, 1))
            {
                QuestFlags.AddToList(c, e, QuestFlags.CharmedBy, QuestFlags.Id(h));
                await Toast(c, $"{skill}: {e.Name} can't target {h.Name} this round.");
            }
        }

        private static async Task Revive(IGameContext c, UnitData h, string skill, float range, bool all, Func<int> wounds)
        {
            var down = QuestActions.UnconsciousHeroes(c, h, range);
            var picks = all ? down : await QuestActions.PickUnits(c, h, skill, down, 1);
            foreach (UnitData t in picks)
            {
                int w = wounds();
                QuestActions.Revive(t, w);
                await c.Announce($"{skill}: {t.Name} is back up with {w} wounds!", new TextColor(120, 230, 140, 255), EBannerTier.Notice);
            }
        }

        private static string? DownWithin(IGameContext c, UnitData h, float range) =>
            QuestActions.UnconsciousHeroes(c, h, range).Count > 0 ? null : $"No unconscious hero within {range:0}\".";

        private static string? ConditionWithin(IGameContext c, UnitData h, float range, bool includeSelf) =>
            QuestActions.Friends(c, h, range, includeSelf, false).Any(u => QuestActions.Sheet(u)?.Conditions.Count > 0)
                ? null : $"No friendly unit within {range:0}\" has a condition.";

        // ── The skills ───────────────────────────────────────────────────────────────────────────────────

        public static readonly IReadOnlyList<QuestSkill> All = new List<QuestSkill>
        {
            // ── Barbarian ──
            new("Aimed Strike", 2, "Until the end of the round, the next time the hero charges and deals at least one hit in melee, it deals one extra hit.",
                Always, (c, h) => Flag(c, h, QuestFlags.ChargeHit, "one extra hit on its next charge")),
            new("Feral Instinct", 1, "Until the end of the round, enemies get -1 to hit when attacking the hero in melee or when shooting at it from within 12\".",
                Always, (c, h) => Flag(c, h, QuestFlags.Elude, "enemies get -1 to hit it in melee or from within 12\" this round")),
            new("Blood Frenzy", 0, "Until the end of the round, the next time the hero charges and deals at least one wound in melee, it deals two extra wounds, but takes one wound.",
                Always, (c, h) => Flag(c, h, QuestFlags.BloodFrenzy, "two extra wounds on its next charge (and it takes one)")),
            new("Dazed Attack", 2, "Until the end of the round, the next time the hero charges and deals at least one hit in melee, it deals two extra hits, but takes one wound.",
                Always, (c, h) => Flag(c, h, QuestFlags.DazedAttack, "two extra hits on its next charge (and it takes one wound)")),
            new("Intimidating", 0, "Pick two enemy units within 3\", which must take a morale test. If failed, they are Shaken.",
                NeedsEnemy(3f), (c, h) => MoraleThen(c, h, "Intimidating", 3f, 2, e => MakeShaken(c, e))),
            new("Vicious", 1, "Until the end of the round, whenever the hero attacks in melee or shoots from within 12\" and deals at least one hit, it deals one extra hit, but whenever it takes at least one hit, it takes one extra hit.",
                Always, async (c, h) =>
                {
                    QuestTreasure.GrantRule(c, h, h, "Extra hit in melee or within 12\"", ELifetime.ThisRound);
                    await Flag(c, h, QuestFlags.Vicious, "one extra hit dealt (melee / within 12\") and taken this round");
                }),
            new("Brutal", 0, "The hero's weapons get AP(+1) until the end of the round.", Always, ApPlusOne),
            new("Furious Shout", 1, "Pick two enemy units within 3\", which must take a morale test. If failed, they are pushed 6\" directly away.",
                NeedsEnemy(3f), (c, h) => MoraleThen(c, h, "Furious Shout", 3f, 2, e => Push(c, h, e, 6f))),
            new("Toughen Up", 2, "The hero takes 1 wound, and until the end of the round gets +2 to Def rolls in melee or when being shot at from within 12\".",
                Always, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    if (!h.GetIsAlive()) return;
                    await Flag(c, h, QuestFlags.ToughenUp, "+2 to Defense in melee or against shots from within 12\" this round");
                }),

            // ── Bard ──
            new("Curing Melody", 1, "Pick two other friendly units within 9\", which lose one status condition each.",
                (c, h) => ConditionWithin(c, h, 9f, false), async (c, h) =>
                {
                    var hurt = QuestActions.Friends(c, h, 9f, false, false).Where(u => QuestActions.Sheet(u)?.Conditions.Count > 0).ToList();
                    foreach (UnitData u in await QuestActions.PickUnits(c, h, "Curing Melody", hurt, 2)) LoseCondition(u);
                    await Toast(c, "Curing Melody: conditions eased.");
                }),
            new("Persuade", 2, "Pick one enemy unit within 12\", which must take a morale test. If failed, it gets -2 to hit until the end of the round.",
                NeedsEnemy(12f), (c, h) => MoraleThen(c, h, "Persuade", 12f, 1, async e =>
                {
                    QuestActions.Grant(h, e, ERollKind.Hit, -2, ELifetime.ThisRound, c, "Persuade");
                    await Toast(c, $"Persuade: {e.Name} gets -2 to hit this round.");
                })),
            new("Repelling Song", 0, "Pick two enemy units within 9\", which must take a morale test. If failed, they are pushed 6\" directly away.",
                NeedsEnemy(9f), (c, h) => MoraleThen(c, h, "Repelling Song", 9f, 2, e => Push(c, h, e, 6f))),
            new("Song of Fear", 1, "Pick two enemy units within 9\", which must take a morale test. If failed, they are Shaken.",
                NeedsEnemy(9f), (c, h) => MoraleThen(c, h, "Song of Fear", 9f, 2, e => MakeShaken(c, e))),
            new("Charm", 2, "Pick one enemy unit within 6\", which can't target the hero until the end of the round.",
                NeedsEnemy(6f), (c, h) => Charm(c, h, "Charm")),
            new("Motivate", 0, "Pick one other friendly unit within 9\", which gets +2 to hit rolls next time it attacks.",
                NeedsFriend(9f, false, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Motivate", 9f, 1, false))
                    {
                        QuestActions.Grant(h, u, ERollKind.Hit, 2, ELifetime.NextTrigger, c, "Motivate");
                        await Toast(c, $"Motivate: {u.Name} gets +2 to hit the next time it attacks.");
                    }
                }),
            new("Warding Song", 2, "Pick one other friendly unit within 9\", which gets +2 to Def rolls next time it takes hits.",
                NeedsFriend(9f, false, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Warding Song", 9f, 1, false))
                    {
                        QuestActions.Grant(h, u, ERollKind.Save, 2, ELifetime.NextTrigger, c, "Warding Song");
                        await Toast(c, $"Warding Song: {u.Name} gets +2 to Defense the next time it takes hits.");
                    }
                }),
            new("Song of Rest", 0, "Pick two other friendly heroes within 9\", which restore 1 power each.",
                NeedsFriend(9f, false, true), async (c, h) =>
                {
                    var picks = await PickFriends(c, h, "Song of Rest", 9f, 2, false, true);
                    foreach (UnitData u in picks) QuestActions.RestorePower(u, 1);
                    await Toast(c, $"Song of Rest: {string.Join(", ", picks.Select(p => p.Name))} restore 1 power.");
                }),

            // ── Cleric ──
            new("Cure Wounds", 1, "Pick one other friendly hero within 3\", which loses all status conditions.",
                (c, h) => QuestActions.Friends(c, h, 3f, false, true).Any(u => QuestActions.Sheet(u)?.Conditions.Count > 0) ? null : "No other hero within 3\" has a condition.",
                async (c, h) =>
                {
                    var hurt = QuestActions.Friends(c, h, 3f, false, true).Where(u => QuestActions.Sheet(u)?.Conditions.Count > 0).ToList();
                    foreach (UnitData u in await QuestActions.PickUnits(c, h, "Cure Wounds", hurt, 1))
                    {
                        QuestTreasure.EditSheet(u, s => s.Conditions.Clear());
                        await Toast(c, $"Cure Wounds: {u.Name} loses all status conditions.");
                    }
                }),
            new("Multiply", 2, "Place one multiply marker within 3\". Enemies treat the marker as a hero until the end of the round (it goes if an enemy moves within 1\").",
                Always, (c, h) => Place(c, h, "Multiply", "phantasm", 3f, 1, "a decoy appears - enemies that see it go for it")),
            new("Divine Strike", 0, "The hero's weapons get AP(+1) until the end of the round.", Always, ApPlusOne),
            new("Bless", 1, "Pick one other friendly unit within 6\", which gets +1 to hit until the end of the round.",
                NeedsFriend(6f, false, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Bless", 6f, 1, false))
                    {
                        QuestActions.Grant(h, u, ERollKind.Hit, 1, ELifetime.ThisRound, c, "Bless");
                        await Toast(c, $"Bless: {u.Name} gets +1 to hit this round.");
                    }
                }),
            new("Holy Protection", 0, "Pick one friendly unit within 6\", which can't be targeted by enemies until the end of the round.",
                NeedsFriend(6f, true, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Holy Protection", 6f, 1, true))
                        await Flag(c, u, QuestFlags.Untargetable, "enemies can't target it this round");
                }),
            new("Spirit Guardians", 2, "Place a spirit guardians marker within 6\". The area within 3\" of it is dangerous terrain for enemies until the end of the round.",
                Always, (c, h) => Place(c, h, "Spirit Guardians", "biotrap", 6f, 1, "dangerous for enemies within 3\" this round")),
            new("Bane", 0, "Pick one enemy unit within 12\", which gets -1 to hit until the end of the round.",
                NeedsEnemy(12f), (c, h) => DebuffEnemy(c, h, "Bane", 12f, ERollKind.Hit, -1, "gets -1 to hit")),
            new("Guiding Bolt", 1, "Pick one enemy unit within 6\". All friendly units get +1 to hit against it until the end of the round.",
                NeedsEnemy(6f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Guiding Bolt", 6f, 1))
                    {
                        QuestFlags.Set(c, e, QuestFlags.GuidingBolt, QuestFlags.Id(h));
                        await Toast(c, $"Guiding Bolt: your side gets +1 to hit {e.Name} this round.");
                    }
                }),
            new("Opulent Revival", 2, "Revive all unconscious heroes within 6\", and they restore D3 wounds each.",
                (c, h) => DownWithin(c, h, 6f), (c, h) => Revive(c, h, "Opulent Revival", 6f, true, () => D3(c))),

            // ── Druid ──
            new("Wolf Form", 1, "The hero gets +1\" on Advance, and +2\" on Rush/Charge until the end of the round.",
                Always, (c, h) => Faster(c, h, HordeExperience.Agile, ELifetime.ThisRound, "+1\" Advance, +2\" Rush/Charge this round")),
            new("Natural Recovery", 2, "The hero gets one spell token, and loses D3 power.",
                Caster, (c, h) => SpellTokenForPower(c, h, "Natural Recovery")),
            new("Spike Growth", 0, "Place a spike growth marker within 9\". The area within 3\" of it is difficult terrain for enemies until the end of the round.",
                Always, (c, h) => Place(c, h, "Spike Growth", "spikes", 9f, 1, "difficult ground for enemies within 3\" this round")),
            new("Power Berry", 0, "Pick one friendly unit within 3\", which restores 1 wound and loses one status condition.",
                NeedsFriend(3f, true, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Power Berry", 3f, 1, true))
                    {
                        LoseCondition(u);
                        await Heal(c, u, 1, "Power Berry");
                    }
                }),
            new("Panther Form", 2, "Until the end of the round, the next time the hero charges and deals at least one hit in melee, it deals one extra hit.",
                Always, (c, h) => Flag(c, h, QuestFlags.ChargeHit, "one extra hit on its next charge")),
            new("Insect Plague", 1, "Pick two enemy units within 9\", which take 1 hit each.",
                NeedsEnemy(9f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Insect Plague", 9f, 2))
                        await QuestActions.DealHits(c, h, e, 1, 0, "Insect Plague");
                }),
            new("Circle of Spores", 2, "Place one spore marker on two enemy units within 9\". When those units are activated, roll one die for each marker: on a 4+ remove the marker, else the unit takes 1 hit.",
                NeedsEnemy(9f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Circle of Spores", 9f, 2))
                        QuestTreasure.AddUnitMarker(c, e, "spores", rounds: 99, source: QuestFlags.Id(h));
                    await Toast(c, "Circle of Spores: spore markers placed.");
                }),
            new("Wall of Thorns", 1, "Place a wall of thorns marker within 9\". The area within 3\" of it is dangerous terrain for enemies until the end of the round.",
                Always, (c, h) => Place(c, h, "Wall of Thorns", "biotrap", 9f, 1, "dangerous for enemies within 3\" this round")),
            new("Bear Form", 0, "Until the end of the round, whenever the hero takes hits, those hits count as having AP(-1), to a min. of AP(0).",
                Always, (c, h) => GrantSelf(c, h, "Fortified", ELifetime.ThisRound, "incoming hits get AP(-1) this round")),

            // ── Fighter ──
            new("Disarming Attack", 0, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from within 12\", that enemy gets -1 to hit until the end of the round.",
                Always, (c, h) => Flag(c, h, QuestFlags.Disarming, "its next close attack gives the target -1 to hit this round")),
            new("Sweeping Attack", 1, "Until the end of the round, the next time the hero charges an enemy unit, after combat is resolved, it may immediately make another Charge action against another enemy unit within 3\".",
                Always, (c, h) => Flag(c, h, QuestFlags.Sweeping, "after its next charge it gets a free action to charge again")),
            new("Taunt", 2, "Pick two enemy units within 9\", which must take a morale test. If failed, they are pulled up to 6\" directly toward the hero.",
                NeedsEnemy(9f), (c, h) => MoraleThen(c, h, "Taunt", 9f, 2, e => Pull(c, h, e, 6f))),
            new("Relentless Attack", 2, "Until the end of the round, the next time the hero destroys an enemy unit in melee, it may immediately make another Charge action against another enemy unit.",
                Always, (c, h) => OnKill(c, h, QuestFlags.Relentless, "destroy an enemy to get a free action to charge again")),
            new("Indomitable", 0, "Until the end of the round, whenever the hero takes hits, those hits count as having AP(-1), to a min. of AP(0).",
                Always, (c, h) => GrantSelf(c, h, "Fortified", ELifetime.ThisRound, "incoming hits get AP(-1) this round")),
            new("Menacing Attack", 1, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from within 12\", that enemy must take a morale test at the end of that activation. If failed, it is Shaken.",
                Always, (c, h) => Flag(c, h, QuestFlags.Menacing, "its next close attack makes the target test morale")),
            new("Pushing Attack", 2, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from within 12\", after combat is resolved, the enemy unit is pushed 6\" directly away.",
                Always, (c, h) => Flag(c, h, QuestFlags.Pushing, "its next close target is pushed 6\" away")),
            new("Surge Charge", 0, "The hero may immediately take a Charge action against a target within 6\".",
                NeedsEnemy(6f), (c, h) => Flag(c, h, QuestFlags.BonusAction, "it may take a free action now - charge a target within 6\"",
                    "Surge Charge: charge a target within 6\"")),
            new("Second Wind", 1, "The hero restores D3+1 wounds, and loses 1 power.",
                Always, async (c, h) =>
                {
                    await Heal(c, h, D3(c) + 1, "Second Wind");
                    await QuestActions.SpendPower(c, h, 1, "Second Wind");
                }),

            // ── Monk ──
            new("Cloak of Shadow", 1, "Until the end of the round, enemies can't target the hero as long as it hasn't targeted any enemies.",
                Always, (c, h) => Untargetable(c, h, "Cloak of Shadow")),
            new("Elemental Rest", 2, "The hero may spend X power (max. 3) to restore X+2 wounds.",
                Always, async (c, h) =>
                {
                    int x = await SpendX(c, h, "Elemental Rest", "It restores X+2 wounds.");
                    await Heal(c, h, x + 2, "Elemental Rest");
                }),
            new("Athletics", 0, "The hero gets +1\" on Advance, and +2\" on Rush/Charge until the end of the round.",
                Always, (c, h) => Faster(c, h, HordeExperience.Agile, ELifetime.ThisRound, "+1\" Advance, +2\" Rush/Charge this round")),
            new("Suppress", 2, "Pick one enemy unit within 9\", which gets -2 to spell casting rolls until the end of the round.",
                NeedsEnemy(9f), (c, h) => DebuffEnemy(c, h, "Suppress", 9f, ERollKind.Cast, -2, "gets -2 to casting rolls")),
            new("Topple", 1, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from within 12\", that enemy must take a morale test at the end of that activation. If failed, it is Shaken.",
                Always, (c, h) => Flag(c, h, QuestFlags.Menacing, "its next close attack makes the target test morale")),
            new("Elemental Strike", 0, "The hero may spend X power (max. 3). Pick one enemy unit within 12\", which takes X+1 hits.",
                NeedsEnemy(12f), (c, h) => HitsX(c, h, "Elemental Strike", 12f, 1, 0)),
            new("Elemental Protection", 1, "The hero may spend X power (max. 3) to get +X+1 to Def rolls next time it takes hits until the end of the round.",
                Always, (c, h) => DefenseX(c, h, "Elemental Protection")),
            new("Shadow Step", 2, "The hero may be placed anywhere within 2D3+1\".",
                Always, (c, h) => Teleport(c, h, D3(c) + D3(c) + 1, "Shadow Step")),

            // ── Paladin ──
            new("Spiteful Control", 0, "Pick two enemy units within 3\", which can't move until the end of the round.",
                NeedsEnemy(3f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Spiteful Control", 3f, 2))
                    {
                        QuestTreasure.GrantRule(c, h, e, "Immobile", ELifetime.ThisRound);
                        await Toast(c, $"Spiteful Control: {e.Name} can't move this round.");
                    }
                }),
            new("Ancient Speed", 1, "Pick one other friendly unit within 6\". The unit gets +1\" on Advance, and +2\" on Rush/Charge until the end of the round.",
                NeedsFriend(6f, false, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Ancient Speed", 6f, 1, false))
                        await Faster(c, u, HordeExperience.Agile, ELifetime.ThisRound, "+1\" Advance, +2\" Rush/Charge this round");
                }),
            new("Spiteful Vow", 1, "Pick one enemy unit within 6\". Until the end of the round, the next time a friendly unit deals at least one wound to it, it deals one extra wound.",
                NeedsEnemy(6f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Spiteful Vow", 6f, 1))
                    {
                        QuestFlags.Set(c, e, QuestFlags.Vow, QuestFlags.Id(h));
                        await Toast(c, $"Spiteful Vow: the next wound your side deals to {e.Name} deals one more.");
                    }
                }),
            new("Vengeful Might", 2, "The hero's weapons get AP(+1) until the end of the round.", Always, ApPlusOne),
            new("Devoted Rebuke", 0, "Pick one friendly unit within 6\". Until the end of the round, the next time an enemy attacks it, the enemy unit takes D3 hits.",
                NeedsFriend(6f, true, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Devoted Rebuke", 6f, 1, true))
                    {
                        QuestFlags.Set(c, u, QuestFlags.Rebuke, QuestFlags.Id(h));
                        await Toast(c, $"Devoted Rebuke: the next enemy to attack {u.Name} takes D3 hits.");
                    }
                }),
            new("Vengeful Haste", 1, "The hero gets +D3\" on Advance, and +2D3\" on Rush/Charge next time it moves until the end of the round.",
                Always, async (c, h) =>
                {
                    int d = D3(c);
                    if (d != 2) QuestTreasure.GrantRule(c, h, h, HordeExperience.Agile, ELifetime.NextTrigger);
                    if (d >= 2) QuestTreasure.GrantRule(c, h, h, "Fast", ELifetime.NextTrigger);
                    await Toast(c, $"Vengeful Haste (rolled {d}): +{d}\" Advance, +{2 * d}\" Rush/Charge on its next move.");
                }),
            new("Devoted Aura", 2, "Pick one friendly unit within 6\". Until the end of the round, the next time that it restores wounds, it restores two extra wounds.",
                NeedsFriend(6f, true, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Devoted Aura", 6f, 1, true))
                        await Flag(c, u, QuestFlags.DevotedAura, "restores two extra wounds the next time it heals");
                }),
            new("Ancient Aura", 0, "Pick one other friendly unit within 6\". Until the end of the round, the next time that unit attacks and deals at least one hit, it deals one extra hit.",
                NeedsFriend(6f, false, false), async (c, h) =>
                {
                    foreach (UnitData u in await PickFriends(c, h, "Ancient Aura", 6f, 1, false))
                    {
                        QuestTreasure.GrantRule(c, h, u, "Extra hit", ELifetime.NextTrigger);
                        await Toast(c, $"Ancient Aura: {u.Name} deals one extra hit on its next attack.");
                    }
                }),

            // ── Ranger ──
            new("Hunter's Mark", 2, "Pick one enemy unit over 12\" away. The hero gets +D3 to hit when attacking it until the end of the round.",
                (c, h) => Far(c, h, 12f).Count > 0 ? null : "No enemy over 12\" away.", async (c, h) =>
                {
                    foreach (UnitData e in await QuestActions.PickUnits(c, h, "Hunter's Mark", Far(c, h, 12f), 1))
                    {
                        int d = D3(c);
                        QuestFlags.Set(c, h, QuestFlags.HunterMark, $"{QuestFlags.Id(e)}:{d}");
                        await Toast(c, $"Hunter's Mark: {h.Name} gets +{d} to hit {e.Name} this round.");
                    }
                }),
            new("Stalking Pet", 1, "Until the end of the round, the next time the hero attacks an enemy in melee or shoots at it from over 12\" away, if it deals at least one hit, it deals one extra hit.",
                Always, (c, h) => Flag(c, h, QuestFlags.StalkingPet, "one extra hit on its next melee attack or long shot")),
            new("Stalker's Flurry", 0, "Until the end of the round, the next time the hero destroys an enemy unit by shooting, it may immediately shoot at another enemy unit.",
                Always, (c, h) => OnKill(c, h, QuestFlags.StalkerFlurry, "destroy an enemy to get a free action to shoot again")),
            new("Surge Shot", 1, "The hero may immediately take a Hold action and shoot at a target within 12\".",
                NeedsEnemy(12f), (c, h) => Flag(c, h, QuestFlags.BonusAction, "it may take a free action now - hold and shoot a target within 12\"",
                    "Surge Shot: hold and shoot a target within 12\"")),
            new("Steel Will", 0, "Heroes may use this skill action even whilst Shaken. The hero stops being Shaken.", Always, async (c, h) =>
                {
                    h.Tokens.RemoveTokens(TokenType.Shaken);
                    await Toast(c, $"{h.Name} steels itself and is no longer Shaken.");
                }),
            new("Umbral Shroud", 1, "Until the end of the round, enemies can't target the hero as long as it hasn't targeted any enemies.",
                Always, (c, h) => Untargetable(c, h, "Umbral Shroud")),
            new("Volley", 2, "Until the end of the round, the next time the hero shoots at an enemy unit, after the attack is resolved, it may immediately shoot at another enemy unit within 3\" of the first unit.",
                Always, (c, h) => Flag(c, h, QuestFlags.Volley, "after its next shot it gets a free action to shoot again")),
            new("Giant Killer", 0, "The hero's weapons get AP(+1) until the end of the round.", Always, ApPlusOne),

            // ── Rogue ──
            new("Hide", 2, "Until the end of the round, enemies can't target the hero as long as it hasn't targeted any enemies.",
                Always, (c, h) => Untargetable(c, h, "Hide")),
            new("Elude", 1, "Until the end of the round, enemies get -1 to hit when attacking the hero in melee or when shooting at it from within 12\".",
                Always, (c, h) => Flag(c, h, QuestFlags.Elude, "enemies get -1 to hit it in melee or from within 12\" this round")),
            new("Sneak Attack", 0, "Until the end of the round, the hero gets +1 to hit when charging enemies that had no line of sight to the hero when it activated.",
                Always, (c, h) => Flag(c, h, QuestFlags.Sneak, "+1 to hit when charging enemies that can't see it now", QuestFlags.EnemiesThatCantSee(c, h))),
            new("Hit & Run", 2, "Until the end of the round, the next time the hero attacks an enemy, after the attack is resolved, the hero may move by up to 6\".",
                Always, (c, h) => Flag(c, h, QuestFlags.HitRun, "after its next attack it may move up to 6\"")),
            new("Dash", 0, "The hero gets +1\" on Advance, and +2\" on Rush/Charge until the end of the round.",
                Always, (c, h) => Faster(c, h, HordeExperience.Agile, ELifetime.ThisRound, "+1\" Advance, +2\" Rush/Charge this round")),
            new("Blurred Step", 1, "The hero may be placed anywhere within 2D3+1\".",
                Always, (c, h) => Teleport(c, h, D3(c) + D3(c) + 1, "Blurred Step")),
            new("Dodge Target", 0, "Pick one enemy unit within 12\", which gets -1 to hit until the end of the round.",
                NeedsEnemy(12f), (c, h) => DebuffEnemy(c, h, "Dodge Target", 12f, ERollKind.Hit, -1, "gets -1 to hit")),
            new("Assassination", 1, "Until the end of the round, the next time the hero shoots at an enemy that had no line of sight to the hero when it activated and deals at least one hit, it deals one extra hit.",
                Always, (c, h) => Flag(c, h, QuestFlags.Assassin, "one extra hit on its next shot at an enemy that can't see it now", QuestFlags.EnemiesThatCantSee(c, h))),

            // ── Sorcerer ──
            new("Twin Spell", 1, "The hero spends D3+2 power, and the next spell it casts until the end of the round may target one additional enemy target.",
                Caster, async (c, h) =>
                {
                    await QuestActions.SpendPower(c, h, D3(c) + 2, "Twin Spell");
                    if (h.GetIsAlive()) await Flag(c, h, QuestFlags.ExtraFoeTarget, "its next spell may target one more enemy");
                }),
            new("Practical Resilience", 0, "The hero may spend X power (max. 3) to get +X+1 to Def rolls next time it takes hits until the end of the round.",
                Always, (c, h) => DefenseX(c, h, "Practical Resilience")),
            new("Lightning Strike", 2, "The hero may spend X power (max. 3). Pick one enemy unit within 6\", which takes X+1 hits with AP(3).",
                NeedsEnemy(6f), (c, h) => HitsX(c, h, "Lightning Strike", 6f, 1, 3)),
            new("Storm Fury", 1, "The hero may spend X power (max. 3). Pick one enemy unit within 9\", which takes X+1 hits with AP(1).",
                NeedsEnemy(9f), (c, h) => HitsX(c, h, "Storm Fury", 9f, 1, 1)),
            new("Heightened Spell", 0, "The hero loses X power, and the next spell it casts until the end of the round gets AP(+X).",
                Caster, async (c, h) =>
                {
                    int x = await SpendX(c, h, "Heightened Spell", "Its next spell gets AP(+X).");
                    if (x > 0) { QuestFlags.Add(c, h, QuestFlags.Pact, x); await Toast(c, $"{h.Name}: its next spell gets AP(+{x})."); }
                }),
            new("Laborious Healing", 2, "Until the end of the round, the hero restores D3+1 wounds the next time it successfully casts a spell, or the next time it counters an enemy spell and that spell fails.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Laborious, "restores D3+1 wounds on its next successful spell or counter")),
            new("Thunderwave", 0, "The hero may spend X power (max. 3). Pick one enemy unit within 3\", which takes X+2 hits.",
                NeedsEnemy(3f), (c, h) => HitsX(c, h, "Thunderwave", 3f, 2, 0)),
            new("Crafted Wings", 2, "The hero may spend X power (max. 3), and may then be placed anywhere within 5+X\".",
                Always, async (c, h) =>
                {
                    int x = await SpendX(c, h, "Crafted Wings", "It may then be placed within 5+X\".");
                    await Teleport(c, h, 5 + x, "Crafted Wings");
                }),
            new("Tinkered Luck", 1, "Until the end of the round, the next time the hero fails to cast a spell, or counters an enemy spell and the enemy still casts it, it may re-roll that casting roll.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Tinkered, "re-rolls its next failed cast (or an enemy cast it failed to stop)")),

            // ── Warlock ──
            new("Pact of Wisdom", 2, "The hero takes 1 wound, and until the end of the round gets +1 to spell casting rolls, and if it spends tokens to modify enemy casting rolls the enemy gets an additional -1.",
                Caster, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    if (!h.GetIsAlive()) return;
                    QuestActions.Grant(h, h, ERollKind.Cast, 1, ELifetime.ThisRound, c, "Pact of Wisdom");
                    await Flag(c, h, QuestFlags.Wisdom, "+1 to casting, and its counter-spells give the enemy an extra -1, this round");
                }),
            new("Fey Curse", 1, "Pick one enemy unit within 12\", which must take a morale test. If failed, it gets -2 to hit until the end of the round.",
                NeedsEnemy(12f), (c, h) => MoraleThen(c, h, "Fey Curse", 12f, 1, async e =>
                {
                    QuestActions.Grant(h, e, ERollKind.Hit, -2, ELifetime.ThisRound, c, "Fey Curse");
                    await Toast(c, $"Fey Curse: {e.Name} gets -2 to hit this round.");
                })),
            new("Dark Sacrifice", 0, "The hero takes 1 wound, and then deals 1 wound to one enemy unit within 6\".",
                NeedsEnemy(6f), async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    var t = await PickEnemies(c, h, "Dark Sacrifice", 6f, 1);
                    if (t.Count > 0) await CasualtyPresentation.ApplyUnitWounds(c, t[0], 1f);
                }),
            new("Dark Transfer", 1, "The hero takes 1 wound, and one other hero within 3\" restores D3+1 wounds.",
                NeedsFriend(3f, false, true), async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, 1f);
                    foreach (UnitData u in await PickFriends(c, h, "Dark Transfer", 3f, 1, false, true))
                        await Heal(c, u, D3(c) + 1, "Dark Transfer");
                }),
            new("Fey Whispers", 2, "Pick two enemy units within 9\", which must take a morale test. If failed, they are Shaken.",
                NeedsEnemy(9f), (c, h) => MoraleThen(c, h, "Fey Whispers", 9f, 2, e => MakeShaken(c, e))),
            new("Fiendish Cantrip", 0, "Until the end of the round, the next time the hero destroys an enemy unit, it gets one spell token and loses 1 power.",
                Caster, (c, h) => OnKill(c, h, QuestFlags.Cantrip, "destroy an enemy unit this round to get a spell token")),
            new("Pact of Skill", 1, "The hero takes D3 wounds, and the next spell it casts until the end of the round may target one additional enemy target.",
                Caster, async (c, h) =>
                {
                    await CasualtyPresentation.ApplyUnitWounds(c, h, D3(c));
                    if (h.GetIsAlive()) await Flag(c, h, QuestFlags.ExtraFoeTarget, "its next spell may target one more enemy");
                }),
            new("Fey Domination", 0, "Pick one enemy unit within 6\", which can't target the hero until the end of the round.",
                NeedsEnemy(6f), (c, h) => Charm(c, h, "Fey Domination")),
            new("Fiendish Blessing", 2, "Until the end of the round, the next time the hero destroys an enemy unit, it restores D3 wounds.",
                Always, (c, h) => OnKill(c, h, QuestFlags.FiendBless, "destroy an enemy unit this round to restore D3 wounds")),

            // ── Wizard ──
            new("Divination", 0, "Until the end of the round, the next time the hero successfully casts a spell, the target restores D3+1 power.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Divination, "its next successful spell restores D3+1 power to a hero it targets")),
            new("Transmutation", 2, "Until the end of the round, the next time the hero successfully casts a spell, one of the targets restores D3 wounds.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Transmutation, "its next successful spell heals one target D3 wounds")),
            new("Abjuration", 1, "The hero restores D3+1 wounds, and loses 1 power.",
                Always, async (c, h) =>
                {
                    await Heal(c, h, D3(c) + 1, "Abjuration");
                    await QuestActions.SpendPower(c, h, 1, "Abjuration");
                }),
            new("Recovery", 0, "The hero restores D3+1 power.", Always, async (c, h) =>
                {
                    int d = D3(c) + 1;
                    QuestActions.RestorePower(h, d);
                    await Toast(c, $"Recovery: {h.Name} restores {d} power ({h.QuestPower}).");
                }),
            new("Evocation", 1, "Until the end of the round, the next time the hero casts a spell that targets enemies, that spell gets AP(+D3).",
                Caster, async (c, h) =>
                {
                    int d = D3(c);
                    QuestFlags.Add(c, h, QuestFlags.Pact, d);
                    await Toast(c, $"Evocation: {h.Name}'s next spell gets AP(+{d}).");
                }),
            new("Illusion", 2, "Until the end of the round, the next time the hero successfully casts a spell that targets enemies, pick one of them to get -1 to hit until the end of the round.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Illusion, "its next spell on enemies gives one of them -1 to hit")),
            new("Preparation", 2, "The hero gets one spell token, and loses D3 power.",
                Caster, (c, h) => SpellTokenForPower(c, h, "Preparation")),
            new("Duplication", 1, "The hero loses D3 power, and the next spell it casts until the end of the round may target one additional friendly target.",
                Caster, async (c, h) =>
                {
                    await QuestActions.SpendPower(c, h, D3(c), "Duplication");
                    if (h.GetIsAlive()) await Flag(c, h, QuestFlags.ExtraFriendTarget, "its next spell may target one more friend");
                }),
            new("Necromancy", 0, "Until the end of the round, the next time the hero successfully casts a spell that targets enemies, pick one of them to take D3 hits.",
                Caster, (c, h) => Flag(c, h, QuestFlags.Necromancy, "its next spell on enemies deals D3 hits to one of them")),

            // ── Core skills (every class) ──
            new("Trapper", 1, "The area within 3\" of the hero counts as difficult terrain for enemies until the end of the round.",
                Always, (c, h) => PlaceOnHero(c, h, "spikes", "difficult ground for enemies within 3\" this round")),
            new("Sprint", 0, "The hero gets +2\" on Advance, and +4\" on Rush/Charge next time it moves until the end of the round.",
                Always, (c, h) => Faster(c, h, "Fast", ELifetime.NextTrigger, "+2\" Advance, +4\" Rush/Charge on its next move")),
            new("Distraction", 1, "Place one distraction marker within 12\". Enemies treat the nearest marker as the AI Goal. Remove the marker at the end of 3 rounds, or when an enemy moves within 1\".",
                Always, (c, h) => Place(c, h, "Distraction", "noise", 12f, 3, "enemies that can't see a hero head for the marker")),
            new("Shockwave", 0, "Pick one enemy unit within 3\", which is pushed 6\" directly away.",
                NeedsEnemy(3f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Shockwave", 3f, 1)) await Push(c, h, e, 6f);
                }),
            new("Resurrect", 2, "Revive one unconscious hero within 3\", and it restores D3+1 wounds.",
                (c, h) => DownWithin(c, h, 3f), (c, h) => Revive(c, h, "Resurrect", 3f, false, () => D3(c) + 1)),
            new("Pacify", 2, "Pick one enemy unit within 6\", which gets -1 to hit until the end of the round.",
                NeedsEnemy(6f), (c, h) => DebuffEnemy(c, h, "Pacify", 6f, ERollKind.Hit, -1, "gets -1 to hit")),
            new("Sanctuary", 2, "The area within 3\" of the hero counts as cover terrain for friendly units until the end of the round.",
                Always, (c, h) => PlaceOnHero(c, h, "smoke", "cover within 3\" this round")),
            new("Fortitude", 2, "The hero automatically passes all morale tests until the end of the round.",
                Always, async (c, h) =>
                {
                    QuestActions.Grant(h, h, ERollKind.Morale, 6, ELifetime.ThisRound, c, "Fortitude");
                    await Toast(c, $"{h.Name} passes all morale tests this round.");
                }),
            new("Unstoppable", 0, "The hero doesn't get fatigued until the end of the round.", Always, async (c, h) =>
                {
                    QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
                    q.Adrenaline.Add(h.ID.ID.ToString());
                    GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                    await Toast(c, $"{h.Name} won't get fatigued this round.");
                }),
            new("Mirror Image", 1, "Place one mirror image marker within 3\". Enemies treat the marker as a hero. Remove the marker at the end of 3 rounds, or when an enemy moves within 1\".",
                Always, (c, h) => Place(c, h, "Mirror Image", "phantasm", 3f, 3, "a mirror image appears - enemies that see it go for it")),
            new("Transfix", 0, "Pick one enemy unit within 9\", which can't move until the end of the round.",
                NeedsEnemy(9f), async (c, h) =>
                {
                    foreach (UnitData e in await PickEnemies(c, h, "Transfix", 9f, 1))
                    {
                        QuestTreasure.GrantRule(c, h, e, "Immobile", ELifetime.ThisRound);
                        await Toast(c, $"Transfix: {e.Name} can't move this round.");
                    }
                }),
            new("Longshot", 1, "The hero gets +6\" range until the end of the round.",
                Always, (c, h) => GrantSelf(c, h, "Increased Shooting Range", ELifetime.ThisRound, "+6\" range this round")),
            new("Shift", 1, "The hero may be placed anywhere within D3+3\".",
                Always, (c, h) => Teleport(c, h, D3(c) + 3, "Shift")),
            new("Silence", 2, "Pick one enemy unit within 18\", which gets -1 to spell casting rolls until the end of the round.",
                NeedsEnemy(18f), (c, h) => DebuffEnemy(c, h, "Silence", 18f, ERollKind.Cast, -1, "gets -1 to casting rolls")),
            new("Terrify", 0, "Pick one enemy unit within 3\", which must take a morale test. If failed, it is Shaken.",
                NeedsEnemy(3f), (c, h) => MoraleThen(c, h, "Terrify", 3f, 1, e => MakeShaken(c, e))),
            new("Swinging Strike", 0, "The area within 3\" of the hero counts as dangerous terrain for enemies until the end of the round.",
                Always, (c, h) => PlaceOnHero(c, h, "biotrap", "dangerous for enemies within 3\" this round")),
        };
    }
}
