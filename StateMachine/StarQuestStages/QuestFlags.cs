using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// Star Quest skills that change one attack, one wound, one spell or who can be targeted, for the rest of
    /// the round. They are stored as "this round" flags on a unit in <see cref="QuestProgress.Flags"/>
    /// ("unit-guid|Flag|value"), read by the engine at the moment they matter, and cleared at the end of
    /// the round. Outside Star Quest games every check returns straight away.
    /// </summary>
    public static class QuestFlags
    {
        // ── Attacks ──
        public const string Reckless = "Reckless";             // +1 wound dealt (melee / within 12") and taken
        public const string Vengeful = "Vengeful";             // +1 to hit Shaken enemies (melee / within 12")
        public const string Dread = "Dread";                   // next melee or shot from over 12": target tests morale
        public const string Menacing = "Menacing";             // next melee or shot from within 12": target tests morale
        public const string DreadTarget = "DreadTarget";
        public const string Martial = "Martial";               // next melee: target pushed 6"
        public const string Pushing = "Pushing";               // next melee or shot from within 12": target pushed 6"
        public const string MartialTarget = "MartialTarget";
        public const string Disarming = "Disarming";           // next close attack: target -1 to hit this round
        public const string Elude = "Elude";                   // enemies -1 to hit it (melee / within 12")
        public const string ToughenUp = "ToughenUp";           // +2 Def against melee / shots from within 12"
        public const string Vicious = "Vicious";               // takes one extra hit whenever hit
        public const string ChargeHit = "ChargeHit";           // next charge with a hit: +1 hit (Aimed Strike, Panther Form)
        public const string DazedAttack = "DazedAttack";       // next charge with a hit: +2 hits, takes 1 wound
        public const string BloodFrenzy = "BloodFrenzy";       // next charge with a wound: +2 wounds, takes 1 wound
        public const string StalkingPet = "StalkingPet";       // next melee or shot from over 12" with a hit: +1 hit
        public const string HunterMark = "HunterMark";         // "target-guid:bonus" - +bonus to hit it
        public const string GuidingBolt = "GuidingBolt";       // on an enemy: hero-guid - the hero's side gets +1 to hit it
        public const string Vow = "Vow";                       // on an enemy: hero-guid - next wound from that side +1
        public const string Rebuke = "Rebuke";                 // on a friend: hero-guid - next enemy attacker takes D3 hits
        public const string RebukeHits = "RebukeHits";
        public const string CharmedBy = "CharmedBy";           // on an enemy: hero guids it can't target
        public const string Untargetable = "Untargetable";     // Holy Protection
        public const string Infiltrating = "Infiltrating";     // can't be targeted until it targets an enemy
        public const string Sneak = "Sneak";                   // enemy guids that couldn't see the hero: +1 when charging them
        public const string Assassin = "Assassin";             // enemy guids that couldn't see the hero: next shot +1 hit
        public const string HitRun = "HitRun";                 // after the next attack, may move 6"
        public const string HitRunMove = "HitRunMove";
        public const string Sweeping = "Sweeping";             // after the next charge: a free action (charge another enemy)
        public const string Volley = "Volley";                 // after the next shot: a free action (shoot another enemy)
        public const string Relentless = "Relentless";         // destroyed-count: a free action after destroying an enemy in melee
        public const string StalkerFlurry = "StalkerFlurry";   // destroyed-count: a free action after destroying an enemy by shooting
        public const string BonusAction = "BonusAction";       // value = why ("Sweeping Attack: ...")
        public const string SelfWound = "SelfWound";           // wounds the hero takes at the end of the activation
        public const string Cantrip = "Cantrip";               // destroyed-count: +1 spell token, -1 power on a kill
        public const string FiendBless = "FiendBless";         // destroyed-count: restore D3 wounds on a kill
        public const string DevotedAura = "DevotedAura";       // next time it restores wounds: +2
        // ── Spells ──
        /// <summary>Conjuration / Distant Spell: extra inches for the next spell or counter-spell.</summary>
        public const string SpellRange = "SpellRange";
        public const string Pact = "Pact";                     // AP bonus for the next spell (Pact of Strength, Heightened Spell, Evocation)
        public const string Enchant = "Enchant";
        public const string Tides = "Tides";
        public const string Laborious = "Laborious";
        public const string Tinkered = "Tinkered";
        public const string Wisdom = "Wisdom";
        public const string Divination = "Divination";
        public const string Transmutation = "Transmutation";
        public const string Illusion = "Illusion";
        public const string Necromancy = "Necromancy";
        public const string ExtraFoeTarget = "ExtraFoeTarget";
        public const string ExtraFriendTarget = "ExtraFriendTarget";

        public static string Id(IUnit unit) => unit.ID.ID.ToString();
        private static string Prefix(IUnit unit, string flag) => $"{Id(unit)}|{flag}|";

        private static QuestProgress? Quest(IGameContext c) =>
            QuestActions.IsQuestGame(c) ? GameProgressUtilities.GetQuestProgress(c.GameDataStore) : null;

        private static void Save(IGameContext c, QuestProgress q) => GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);

        /// <summary>The flag's value, or null when the unit doesn't have it.</summary>
        public static string? Get(QuestProgress? q, IUnit unit, string flag)
        {
            if (q == null || q.Flags.Count == 0) return null;
            string prefix = Prefix(unit, flag);
            string? entry = q.Flags.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal));
            return entry?[prefix.Length..];
        }

        public static bool Has(QuestProgress? q, IUnit unit, string flag) => Get(q, unit, flag) != null;

        public static bool Has(IGameContext c, IUnit unit, string flag) => Has(Quest(c), unit, flag);

        /// <summary>Sets the flag (replacing any earlier value).</summary>
        public static void Set(IGameContext c, IUnit unit, string flag, string value = "")
        {
            QuestProgress? q = Quest(c);
            if (q == null) return;
            string prefix = Prefix(unit, flag);
            q.Flags.RemoveAll(f => f.StartsWith(prefix, StringComparison.Ordinal));
            q.Flags.Add(prefix + value);
            Save(c, q);
        }

        /// <summary>Removes the flag and returns its value (null when it wasn't set).</summary>
        public static string? Take(IGameContext c, IUnit unit, string flag)
        {
            QuestProgress? q = Quest(c);
            string? value = Get(q, unit, flag);
            if (q == null || value == null) return null;
            string prefix = Prefix(unit, flag);
            q.Flags.RemoveAll(f => f.StartsWith(prefix, StringComparison.Ordinal));
            Save(c, q);
            return value;
        }

        /// <summary>Adds a number to the flag's value (stacking bonuses).</summary>
        public static void Add(IGameContext c, IUnit unit, string flag, int amount)
        {
            int now = int.TryParse(Get(Quest(c), unit, flag), out int v) ? v : 0;
            Set(c, unit, flag, (now + amount).ToString());
        }

        /// <summary>Adds a unit guid to a comma-separated list flag.</summary>
        public static void AddToList(IGameContext c, IUnit unit, string flag, string id)
        {
            string now = Get(Quest(c), unit, flag) ?? "";
            if (!now.Split(',').Contains(id)) Set(c, unit, flag, now.Length == 0 ? id : now + "," + id);
        }

        private static bool ListHas(string? list, IUnit unit) => list != null && list.Split(',').Contains(Id(unit));

        /// <summary>End of round: this round's skills wear off.</summary>
        public static void ClearRound(QuestProgress q) => q.Flags.Clear();

        /// <summary>The flags a hero has, as short names for the hero card and the Game Master window.</summary>
        public static List<string> Describe(QuestProgress? q, IUnit unit)
        {
            var list = new List<string>();
            if (q == null) return list;
            string id = Id(unit) + "|";
            foreach (string f in q.Flags.Where(f => f.StartsWith(id, StringComparison.Ordinal)))
            {
                string[] p = f.Split('|');
                string name = p.Length > 1 ? p[1] : f;
                string value = p.Length > 2 ? p[2] : "";
                string? label = name switch
                {
                    Reckless => "Reckless",
                    Vengeful => "Vengeful Strike",
                    Dread or Menacing => "Morale attack ready",
                    Martial or Pushing => "Push attack ready",
                    Disarming => "Disarming Attack ready",
                    Elude => "-1 to be hit (close)",
                    ToughenUp => "+2 Def (close)",
                    Vicious => "Vicious",
                    ChargeHit => "Extra hit on next charge",
                    DazedAttack => "+2 hits on next charge",
                    BloodFrenzy => "+2 wounds on next charge",
                    StalkingPet => "Stalking Pet ready",
                    HunterMark => "Hunter's Mark",
                    GuidingBolt => "Marked by Guiding Bolt",
                    Vow => "Spiteful Vow on it",
                    Rebuke => "Devoted Rebuke",
                    CharmedBy => "Charmed",
                    Untargetable => "Can't be targeted",
                    Infiltrating => "Hidden (can't be targeted)",
                    Sneak => "Sneak Attack",
                    Assassin => "Assassination ready",
                    HitRun => "Hit & Run ready",
                    Sweeping => "Sweeping Attack ready",
                    Volley => "Volley ready",
                    Relentless => "Relentless Attack ready",
                    StalkerFlurry => "Stalker's Flurry ready",
                    BonusAction => "Free action ready",
                    Cantrip => "Fiendish Cantrip ready",
                    FiendBless => "Fiendish Blessing ready",
                    DevotedAura => "+2 on next heal",
                    SpellRange => $"+{value}\" next spell",
                    Pact => $"next spell AP+{value}",
                    Enchant => "Enchantment ready",
                    Tides => "Tides of Chaos ready",
                    Laborious => "Laborious Healing ready",
                    Tinkered => "Tinkered Luck ready",
                    Wisdom => "Pact of Wisdom",
                    Divination => "Divination ready",
                    Transmutation => "Transmutation ready",
                    Illusion => "Illusion ready",
                    Necromancy => "Necromancy ready",
                    ExtraFoeTarget => "+1 enemy spell target",
                    ExtraFriendTarget => "+1 friendly spell target",
                    _ => null,
                };
                if (label != null) list.Add(label);
            }
            return list;
        }

        // ── Targeting (Infiltrating Step, Hide, Holy Protection, Charm) ─────────────────────────────────

        /// <summary>
        /// True when <paramref name="target"/> can't be targeted this round: Infiltrating Step / Hide / Cloak of
        /// Shadow / Umbral Shroud (until it targets an enemy), Holy Protection, or - when the
        /// <paramref name="attacker"/> is known - Charm / Fey Domination on that attacker.
        /// </summary>
        public static bool CannotBeTargeted(QuestProgress? q, IUnit target, IUnit? attacker = null)
        {
            if (q == null || q.Flags.Count == 0) return false;
            if (Has(q, target, Infiltrating) || Has(q, target, Untargetable)) return true;
            return attacker != null && ListHas(Get(q, attacker, CharmedBy), target);
        }

        public const string HiddenReason = "Can't be targeted this round (Star Quest skill).";

        /// <summary>The hero targeted an enemy: Infiltrating Step ends.</summary>
        public static void TargetedAnEnemy(IGameContext c, IUnit unit)
        {
            QuestProgress? q = Quest(c);
            if (!Has(q, unit, Infiltrating)) return;
            Take(c, unit, Infiltrating);
            c.Log($"{unit.Name} targets an enemy - it can be targeted again.");
        }

        private static bool Allied(IGameContext c, PlayerID a, PlayerID b) =>
            a.Equals(b) || ITeamExtensions.AreAllied(c.TableState.Teams.Objects, a, b);

        private static UnitData? Find(IGameContext c, string id) =>
            c.GameDataStore.GetAllValues<UnitData>().FirstOrDefault(u => Id(u) == id);

        private static DataBinding<UnitData>? Binding(IGameContext c, string id) =>
            c.GameDataStore.GetAllDataBindings<UnitData>().FirstOrDefault(b => Id(b.GetValue()) == id);

        // ── Attacks ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// An attack is about to roll to hit: returns the to-hit change from Star Quest skills (both sides),
        /// grants this attack's extra hits / Defense, and notes targets and follow-ups that resolve later.
        /// </summary>
        /// <summary>
        /// The Star Quest flags that bear on an attack in flight, applied as it resolves.
        ///
        /// <para>#407: returns its to-hit contributions ITEMISED rather than pre-summed. They used to come
        /// back as one <c>int</c>, which DetermineHitRollStage folded into its generic "buff" chip - so
        /// Vengeful Strike's +1 was indistinguishable from any other buff, and Kai's QA note ("i am unsure
        /// if the modifiers are correctly being applied") had no way to be answered from play. Each entry
        /// names the skill that produced it; the caller can still sum them with
        /// <see cref="QuestAttackModifiers.Net"/>.</para>
        /// </summary>
        public static QuestAttackModifiers OnAttack(IGameContext c, IUnit attacker, IUnit defender,
            bool melee, float distance, bool charging = false)
        {
            QuestProgress? q = Quest(c);
            if (q == null || q.Flags.Count == 0) return QuestAttackModifiers.None;
            TargetedAnEnemy(c, attacker);
            q = Quest(c);
            var hitModifiers = new List<Rules.Dispatch.GrantedModifier>();
            bool close = melee || distance <= 12f;
            bool far = melee || distance > 12f;
            string target = Id(defender);

            // To-hit changes.
            if (Has(q, attacker, Vengeful) && close && defender.Tokens.HasToken(TokenType.Shaken))
            {
                hitModifiers.Add(new Rules.Dispatch.GrantedModifier("Vengeful Strike", +1));
                c.Log($"Vengeful Strike: {attacker.Name} gets +1 to hit the Shaken {defender.Name}.");
            }
            if (Get(q, attacker, HunterMark) is { } mark && mark.Split(':') is { Length: 2 } m && m[0] == target
                && int.TryParse(m[1], out int markBonus))
            {
                hitModifiers.Add(new Rules.Dispatch.GrantedModifier("Hunter's Mark", markBonus));
                c.Log($"Hunter's Mark: {attacker.Name} gets +{markBonus} to hit {defender.Name}.");
            }
            if (Get(q, defender, GuidingBolt) is { } bolter && Find(c, bolter) is { } bolterUnit && Allied(c, attacker.PlayerID, bolterUnit.PlayerID))
            {
                hitModifiers.Add(new Rules.Dispatch.GrantedModifier("Guiding Bolt", +1));
                c.Log($"Guiding Bolt: +1 to hit {defender.Name}.");
            }
            if (charging && ListHas(Get(q, attacker, Sneak), defender))
            {
                hitModifiers.Add(new Rules.Dispatch.GrantedModifier("Sneak Attack", +1));
                c.Log($"Sneak Attack: {attacker.Name} gets +1 to hit {defender.Name}.");
            }
            if (Has(q, defender, Elude) && close)
            {
                hitModifiers.Add(new Rules.Dispatch.GrantedModifier("Elude", -1));
                c.Log($"{defender.Name} is hard to hit: -1 to hit.");
            }

            // Follow-ups at the end of the activation.
            if ((far && Take(c, attacker, Dread) != null) || (close && Take(c, attacker, Menacing) != null))
            {
                Set(c, attacker, DreadTarget, target);
                c.Log($"{defender.Name} must test morale at the end of this activation.");
            }
            if ((melee && Take(c, attacker, Martial) != null) || (close && Take(c, attacker, Pushing) != null))
            {
                Set(c, attacker, MartialTarget, target);
                c.Log($"{defender.Name} will be pushed 6\" away after the combat.");
            }
            if (close && Take(c, attacker, Disarming) != null)
            {
                QuestActions.Grant(attacker, defender, ERollKind.Hit, -1, ELifetime.ThisRound, c, "Disarming Attack");
                c.Log($"Disarming Attack: {defender.Name} gets -1 to hit this round.");
            }
            if (Take(c, attacker, HitRun) != null) Set(c, attacker, HitRunMove);
            if (melee && charging && Take(c, attacker, Sweeping) != null)
                Set(c, attacker, BonusAction, "Sweeping Attack: charge another enemy within 3\"");
            if (!melee && Take(c, attacker, Volley) != null)
                Set(c, attacker, BonusAction, "Volley: shoot another enemy within 3\" of the first");

            // Extra hits and Defense for this attack.
            if (melee && charging && Take(c, attacker, ChargeHit) != null)
                QuestTreasure.GrantRule(c, attacker, attacker, "Extra hit in melee", ELifetime.NextTrigger);
            if (melee && charging && Take(c, attacker, DazedAttack) != null)
            {
                QuestTreasure.GrantRule(c, attacker, attacker, "Two extra hits", ELifetime.NextTrigger);
                Add(c, attacker, SelfWound, 1);
            }
            if (far && Take(c, attacker, StalkingPet) != null)
                QuestTreasure.GrantRule(c, attacker, attacker, "Extra hit", ELifetime.NextTrigger);
            if (!melee && ListHas(Get(q, attacker, Assassin), defender) && Take(c, attacker, Assassin) != null)
                QuestTreasure.GrantRule(c, attacker, attacker, "Extra hit", ELifetime.NextTrigger);
            if (Has(q, defender, Vicious))
                QuestTreasure.GrantRule(c, defender, attacker, "Extra hit", ELifetime.NextTrigger);
            if (Has(q, defender, ToughenUp) && close)
                QuestActions.Grant(defender, defender, ERollKind.Save, 2, ELifetime.NextTrigger, c, "Toughen Up");
            if (Get(q, defender, Rebuke) is { } rebuker && !Allied(c, attacker.PlayerID, defender.PlayerID))
            {
                Take(c, defender, Rebuke);
                Set(c, attacker, RebukeHits, rebuker);
                c.Log($"Devoted Rebuke: {attacker.Name} will take D3 hits.");
            }
            return new QuestAttackModifiers(hitModifiers);
        }

        /// <summary>
        /// Extra wounds from Star Quest skills once an attack's wounds are known: Reckless (dealt and taken),
        /// Blood Frenzy (next charge: +2) and Spiteful Vow (next wound from the hero's side: +1).
        /// </summary>
        public static float ExtraWounds(IGameContext c, IUnit attacker, IUnit defender, bool melee, bool charging,
            bool spell, float distance, float wounds)
        {
            if (wounds <= 0f) return 0f;
            QuestProgress? q = Quest(c);
            if (q == null || q.Flags.Count == 0) return 0f;
            float extra = 0f;
            if (!spell && (melee || distance <= 12f) && Has(q, attacker, Reckless))
            {
                extra += 1f;
                c.Log($"Reckless: {attacker.Name} deals one extra wound.");
            }
            if (Has(q, defender, Reckless))
            {
                extra += 1f;
                c.Log($"Reckless: {defender.Name} takes one extra wound.");
            }
            if (melee && charging && Take(c, attacker, BloodFrenzy) != null)
            {
                extra += 2f;
                Add(c, attacker, SelfWound, 1);
                c.Log($"Blood Frenzy: {attacker.Name} deals two extra wounds (and takes one).");
            }
            if (Get(q, defender, Vow) is { } vower && Find(c, vower) is { } vowUnit && Allied(c, attacker.PlayerID, vowUnit.PlayerID))
            {
                Take(c, defender, Vow);
                extra += 1f;
                c.Log($"Spiteful Vow: {defender.Name} takes one extra wound.");
            }
            return extra;
        }

        /// <summary>Kept for the S2a callers and tests: <see cref="ExtraWounds"/> without the charge flag.</summary>
        public static float RecklessExtraWounds(IGameContext c, IUnit attacker, IUnit defender, bool melee,
            bool spell, float distance, float wounds) =>
            ExtraWounds(c, attacker, defender, melee, charging: false, spell, distance, wounds);

        /// <summary>
        /// Any activation ends: Dread / Menacing targets test morale, Martial / Pushing targets are pushed,
        /// Devoted Rebuke hits land, self-wounds (Dazed Attack, Blood Frenzy) are taken, Hit &amp; Run moves, and
        /// the activated hero's "when it destroys an enemy" skills check for a kill.
        /// </summary>
        public static async Task OnActivationEnd(IGameContext c, UnitData? activated = null)
        {
            QuestProgress? q = Quest(c);
            if (q == null || q.Flags.Count == 0) return;
            string[] followUps = { DreadTarget, MartialTarget, RebukeHits, SelfWound, HitRunMove };
            foreach (string entry in q.Flags.Where(f => followUps.Any(k => f.Contains($"|{k}|"))).ToList())
            {
                string[] p = entry.Split('|');
                if (p.Length < 3) continue;
                QuestProgress now = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
                now.Flags.Remove(entry);
                Save(c, now);
                UnitData? owner = Find(c, p[0]);
                if (owner == null) continue;

                switch (p[1])
                {
                    case DreadTarget:
                        if (Binding(c, p[2]) is not { } dread || !dread.GetValue().GetIsAlive() || !dread.GetValue().GetIsOnBattlefield()) break;
                        if (await QuestActions.Morale(c, dread.GetValue())) await QuestActions.Toast(c, $"{dread.GetValue().Name} holds its nerve.");
                        else
                        {
                            await MoraleUtilities.ApplyShakenWithPresentation(c, dread);
                            await QuestActions.Toast(c, $"{dread.GetValue().Name} is Shaken.");
                        }
                        break;
                    case MartialTarget:
                        if (Find(c, p[2]) is not { } pushed || !pushed.GetIsAlive() || !pushed.GetIsOnBattlefield()) break;
                        await QuestActions.Shove(c, owner, pushed, 6f);
                        break;
                    case RebukeHits:
                        if (Find(c, p[2]) is not { } rebuker || !owner.GetIsAlive()) break;
                        int d3 = (HordePlacement.RollD6(c) + 1) / 2;
                        await QuestActions.Toast(c, $"Devoted Rebuke: {owner.Name} takes {d3} hit{(d3 == 1 ? "" : "s")}.");
                        await QuestActions.DealHits(c, rebuker, owner, d3, 0, "Devoted Rebuke");
                        break;
                    case SelfWound:
                        if (!owner.GetIsAlive() || !int.TryParse(p[2], out int self) || self <= 0) break;
                        await QuestActions.Toast(c, $"{owner.Name} takes {self} wound{(self == 1 ? "" : "s")} from its own fury.");
                        await CasualtyPresentation.ApplyUnitWounds(c, owner, self);
                        break;
                    case HitRunMove:
                        if (!owner.GetIsAlive()) break;
                        await QuestActions.Toast(c, $"Hit & Run: {owner.Name} may move up to 6\".");
                        await new GameOperationServices(c).MoveUnit(owner, 6f, isOptional: true, controller: owner.PlayerID);
                        break;
                }
            }

            if (activated is not { IsQuestHero: true } || !activated.GetIsAlive()) return;
            int destroyed = QuestTreasure.EnemiesDestroyed(c);
            if (int.TryParse(Get(Quest(c), activated, Cantrip), out int cantripAt) && destroyed > cantripAt)
            {
                Take(c, activated, Cantrip);
                await QuestTreasure.AddSpellTokens(c, activated, 1);
                await QuestActions.SpendPower(c, activated, 1, "Fiendish Cantrip");
            }
            if (int.TryParse(Get(Quest(c), activated, FiendBless), out int blessAt) && destroyed > blessAt)
            {
                Take(c, activated, FiendBless);
                int d = (HordePlacement.RollD6(c) + 1) / 2;
                QuestActions.Heal(activated, d, c);
                await QuestActions.Toast(c, $"Fiendish Blessing: {activated.Name} restores {d} wounds.");
            }
        }

        /// <summary>
        /// Before a hero picks its next action: Relentless Attack / Stalker's Flurry turn into a free action
        /// once the hero has destroyed an enemy unit since using them. Returns the free action's reason, if any.
        /// </summary>
        public static string? FreeAction(IGameContext c, UnitData hero)
        {
            QuestProgress? q = Quest(c);
            if (q == null || q.Flags.Count == 0) return null;
            int destroyed = QuestTreasure.EnemiesDestroyed(c);
            if (int.TryParse(Get(q, hero, Relentless), out int r) && destroyed > r)
            {
                Take(c, hero, Relentless);
                Set(c, hero, BonusAction, "Relentless Attack: charge another enemy");
            }
            if (int.TryParse(Get(Quest(c), hero, StalkerFlurry), out int s) && destroyed > s)
            {
                Take(c, hero, StalkerFlurry);
                Set(c, hero, BonusAction, "Stalker's Flurry: shoot at another enemy");
            }
            return Get(Quest(c), hero, BonusAction);
        }

        /// <summary>Enemy units on the table that can't see the hero right now (Sneak Attack, Assassination).</summary>
        public static string EnemiesThatCantSee(IGameContext c, UnitData hero)
        {
            var terrain = c.TableState.Terrain.Objects.ToList();
            var ids = QuestActions.Units(c)
                .Where(u => u.GetIsAlive() && u.GetIsOnBattlefield() && !Allied(c, u.PlayerID, hero.PlayerID))
                .Where(u => !Ai.StarQuest.StarQuestAiBrain.Sees(u, hero, terrain))
                .Select(Id);
            return string.Join(",", ids);
        }

        // ── Spells ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Extra spell range in inches (Conjuration / Distant Spell), without using it up.</summary>
        public static float SpellRangeBonus(QuestProgress? q, IUnit caster) =>
            int.TryParse(Get(q, caster, SpellRange), out int inches) ? inches : 0f;

        /// <summary>Adds to the next spell's extra range (Conjuration and Distant Spell stack).</summary>
        public static void AddSpellRange(IGameContext c, IUnit caster, int inches) => Add(c, caster, SpellRange, inches);

        /// <summary>Twin Spell / Pact of Skill (+1 enemy target) and Duplication (+1 friendly target).</summary>
        public static int ExtraTargets(QuestProgress? q, IUnit caster, ETargetAffinity affinity) => affinity switch
        {
            ETargetAffinity.Foe => Has(q, caster, ExtraFoeTarget) ? 1 : 0,
            ETargetAffinity.Friend => Has(q, caster, ExtraFriendTarget) ? 1 : 0,
            ETargetAffinity.Any => Has(q, caster, ExtraFoeTarget) || Has(q, caster, ExtraFriendTarget) ? 1 : 0,
            _ => 0,
        };
    }

    /// <summary>The Star Quest caster skills that trigger on a spell.</summary>
    public static class QuestSpells
    {
        private static int D3(IGameContext c) => (HordePlacement.RollD6(c) + 1) / 2;

        /// <summary>
        /// A spell was cast successfully. Tides of Chaos / Laborious Healing restore power / wounds;
        /// Enchantment, Illusion and Necromancy hit one enemy target; Divination and Transmutation help one
        /// friendly target; returns the AP bonus for this spell (Pact of Strength, Heightened Spell, Evocation).
        /// </summary>
        public static async Task<int> OnCastSucceeded(IGameContext c, IUnit caster, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            if (!QuestActions.IsQuestGame(c) || caster is not UnitData hero || !hero.IsQuestHero) return 0;
            if (QuestFlags.Take(c, hero, QuestFlags.Tides) != null)
            {
                int d = D3(c) + 2;
                QuestActions.RestorePower(hero, d);
                await QuestActions.Toast(c, $"Tides of Chaos: {hero.Name} restores {d} power ({hero.QuestPower}).");
            }
            if (QuestFlags.Take(c, hero, QuestFlags.Laborious) != null)
            {
                int d = D3(c) + 1;
                QuestActions.Heal(hero, d, c);
                await QuestActions.Toast(c, $"Laborious Healing: {hero.Name} restores {d} wounds.");
            }

            bool Ally(DataBinding<UnitData> t) => t.GetValue().PlayerID.Equals(hero.PlayerID)
                || ITeamExtensions.AreAllied(c.TableState.Teams.Objects, hero.PlayerID, t.GetValue().PlayerID);
            List<UnitData> enemies = targets.Where(t => !Ally(t) && t.GetValue().GetIsAlive()).Select(t => t.GetValue()).ToList();
            List<UnitData> friends = targets.Where(t => Ally(t) && t.GetValue().GetIsAlive()).Select(t => t.GetValue()).ToList();

            async Task<UnitData?> PickOne(string skill, List<UnitData> from) =>
                from.Count == 0 ? null : from.Count == 1 ? from[0] : (await QuestActions.PickUnits(c, hero, skill, from, 1)).FirstOrDefault();

            if (enemies.Count > 0 && QuestFlags.Take(c, hero, QuestFlags.Enchant) != null && await PickOne("Enchantment", enemies) is { } enchanted)
            {
                if (await QuestActions.Morale(c, enchanted)) await QuestActions.Toast(c, $"Enchantment: {enchanted.Name} resists.");
                else if (c.GameDataStore.GetAllDataBindings<UnitData>().FirstOrDefault(b => ReferenceEquals(b.GetValue(), enchanted)) is { } eb)
                {
                    await MoraleUtilities.ApplyShakenWithPresentation(c, eb);
                    await QuestActions.Toast(c, $"Enchantment: {enchanted.Name} is Shaken.");
                }
            }
            if (enemies.Count > 0 && QuestFlags.Take(c, hero, QuestFlags.Illusion) != null && await PickOne("Illusion", enemies) is { } fooled)
            {
                QuestActions.Grant(hero, fooled, ERollKind.Hit, -1, ELifetime.ThisRound, c, "Illusion");
                await QuestActions.Toast(c, $"Illusion: {fooled.Name} gets -1 to hit this round.");
            }
            if (enemies.Count > 0 && QuestFlags.Take(c, hero, QuestFlags.Necromancy) != null && await PickOne("Necromancy", enemies) is { } drained)
            {
                int d = D3(c);
                await QuestActions.DealHits(c, hero, drained, d, 0, "Necromancy");
            }
            List<UnitData> heroes = friends.Where(f => f.IsQuestHero).ToList();
            if (heroes.Count > 0 && QuestFlags.Take(c, hero, QuestFlags.Divination) != null && await PickOne("Divination", heroes) is { } seer)
            {
                int d = D3(c) + 1;
                QuestActions.RestorePower(seer, d);
                await QuestActions.Toast(c, $"Divination: {seer.Name} restores {d} power.");
            }
            if (targets.Count > 0 && QuestFlags.Take(c, hero, QuestFlags.Transmutation) != null
                && await PickOne("Transmutation", targets.Select(t => t.GetValue()).Where(t => t.GetIsAlive()).ToList()) is { } mended)
            {
                int d = D3(c);
                QuestActions.Heal(mended, d, c);
                await QuestActions.Toast(c, $"Transmutation: {mended.Name} restores {d} wounds.");
            }
            if (QuestFlags.Take(c, hero, QuestFlags.Pact) is { } pact && int.TryParse(pact, out int ap) && ap > 0)
            {
                await QuestActions.Toast(c, $"{hero.Name}'s spell gets AP(+{ap}).");
                return ap;
            }
            return 0;
        }

        /// <summary>A hero's counter-spell helped make an enemy cast fail: Tides of Chaos / Laborious Healing.</summary>
        public static async Task OnCounterSucceeded(IGameContext c, IUnit hinderer)
        {
            if (!QuestActions.IsQuestGame(c) || hinderer is not UnitData hero || !hero.IsQuestHero) return;
            if (QuestFlags.Take(c, hero, QuestFlags.Tides) != null)
            {
                int d = D3(c) + 2;
                QuestActions.RestorePower(hero, d);
                await QuestActions.Toast(c, $"Tides of Chaos: the counter-spell works - {hero.Name} restores {d} power ({hero.QuestPower}).");
            }
            if (QuestFlags.Take(c, hero, QuestFlags.Laborious) != null)
            {
                int d = D3(c) + 1;
                QuestActions.Heal(hero, d, c);
                await QuestActions.Toast(c, $"Laborious Healing: {hero.Name} restores {d} wounds.");
            }
        }
    }
}
