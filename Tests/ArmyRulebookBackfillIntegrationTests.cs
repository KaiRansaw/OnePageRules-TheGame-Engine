using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Tests
{
    /// <summary>
    /// #354 — a compiled <c>.fdgarmy</c> embeds a frozen COPY of its book's rule definitions
    /// (<c>ListCompiler</c>), and army load used to resolve rule names against the core catalog plus
    /// that copy alone. So a list saved before a rule was implemented named the rule with nothing behind
    /// it, and the rule silently did nothing in every game the list was ever fielded in — the shipped
    /// case that opened this item was a Saurian Starhost list saved 2026-07-26 whose Ripjawdactyl Riders
    /// lost Heavy Impact(3), three days after Heavy Impact shipped.
    ///
    /// <para>Load takes the current book's definitions from <see cref="CurrentRulebook"/>: they fill names the
    /// army is too old to define, and (Kai, 2026-11-14) replace the file's frozen copy of a name both define, so
    /// a rule fix reaches saved lists. Was gap-fill only (owner ruling 2026-08-05).</para>
    /// </summary>
    [TestFixture]
    public class ArmyRulebookBackfillIntegrationTests
    {
        private const string Faction = "Eternal Dynasty";
        private const string BackfilledRule = "Vengeance";

        /// <summary>A stub rulebook: one faction's definitions, plus the names it knows about at all.</summary>
        private sealed class StubRulebook : ICurrentRulebook
        {
            private readonly string _faction;
            private readonly IReadOnlyList<SpecialRuleDefinition> _definitions;

            public StubRulebook(string faction, params SpecialRuleDefinition[] definitions)
            {
                _faction = faction;
                _definitions = definitions;
            }

            public IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string faction, string? gameSystem) =>
                string.Equals(faction, _faction, StringComparison.OrdinalIgnoreCase)
                    ? _definitions
                    : Array.Empty<SpecialRuleDefinition>();

            public bool Defines(string ruleName) =>
                _definitions.Any(d => string.Equals(d.Name, ruleName, StringComparison.OrdinalIgnoreCase));
        }

        // Two definitions under the SAME name differing only in their modifier, so an assertion can tell
        // which one won: the rulebook's grants +1 to hit, the army's frozen copy +2.
        private static SpecialRuleDefinition HitBonus(string name, int delta) =>
            new SpecialRuleDefinition(name,
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: delta),
                        ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>());

        private static ArmyListFile StaleList(string faction, params SpecialRuleDefinition[] embedded) =>
            new ArmyListFile
            {
                Name = "Saved Before The Rule Existed",
                Faction = faction,
                RuleDefinitions = embedded.ToList(),
                Units =
                {
                    new UnitFileEntry
                    {
                        Name = "Royal Guard", ModelCount = 3, Quality = 3, Defense = 3,
                        SpecialRules = { new SpecialRuleEntry_Core(BackfilledRule) },
                        Weapons =
                        {
                            new WeaponFileEntry { Name = "Great Mace", Quantity = 3, Attacks = 2 },
                        },
                    },
                },
            };

        [TearDown]
        public void ClearInstalledRulebook() => CurrentRulebook.Installed = null;

        [Test]
        public void StaleList_WithNoRulebookInstalled_StillDropsTheRule()
        {
            // The behavior this item found in the wild, pinned so the fix is visibly a fix: with no
            // rulebook to consult, an old list's reference resolves to nothing.
            List<RuleDrop> drops = Launch(StaleList(Faction), out _);

            Assert.That(drops.Select(d => d.RuleName), Is.EqualTo(new[] { BackfilledRule }));
            Assert.That(drops[0].Reason, Is.EqualTo(ERuleDropReason.Unimplemented));
        }

        [Test]
        public void StaleList_PicksUpTheRulebooksDefinition_AndAttachesIt()
        {
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            List<RuleDrop> drops = Launch(StaleList(Faction), out UnitData unit);

            Assert.That(drops, Is.Empty, "the rulebook defines the rule, so nothing should be dropped.");
            Assert.That(unit.RuleDefinitions.Select(r => r.RequestedName), Does.Contain(BackfilledRule),
                "a list too old to carry the definition must still field the rule.");
        }

        [Test]
        public void TheRulebooksDefinition_ReplacesTheArmysFrozenCopy()
        {
            // Kai, 2026-11-14: a rule fix reaches saved lists. The list froze a +2 version; the book now says +1.
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            ArmyListFile army = StaleList(Faction, HitBonus(BackfilledRule, delta: 2));
            Launch(army, out UnitData unit);

            ResolvedRule attached = unit.RuleDefinitions.Single(r => r.RequestedName == BackfilledRule);
            Effect.RollModifier effect = (Effect.RollModifier)attached.Definition.Passive.Single().Effect;
            Assert.That(effect.Delta, Is.EqualTo(1),
                "the current book's definition replaces the list's old copy of the same rule.");
        }

        [Test]
        public void ARuleOnlyTheArmyDefines_IsKeptAsSaved()
        {
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            ArmyListFile army = StaleList(Faction, HitBonus(BackfilledRule, delta: 1), HitBonus("Homebrew Grit", delta: 2));
            List<SpecialRuleDefinition> effective = ArmyListRuleResolution.EffectiveDefinitions(army);

            Assert.That(effective.Single(d => d.Name == "Homebrew Grit"), Is.Not.Null,
                "a rule the book doesn't define stays exactly as the list saved it.");
        }

        [Test]
        public void NoFactionMatch_ClassifiesTheDropAsAnOutdatedList_NotUnimplemented()
        {
            // A freeform / hand-authored list has no faction to match, so nothing can be backfilled. The
            // rule IS implemented though, and saying "not implemented" is what sent a player hunting for
            // an engine gap that did not exist.
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            List<RuleDrop> drops = Launch(StaleList(faction: string.Empty), out _);

            Assert.That(drops.Select(d => d.Reason), Is.EqualTo(new[] { ERuleDropReason.OutdatedList }));
            Assert.That(drops[0].Message, Does.Contain("rebuild the list"));
        }

        [Test]
        public void ANameTheRulebookDoesNotKnow_StaysUnimplemented()
        {
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus("Something Else", delta: 1));

            List<RuleDrop> drops = Launch(StaleList(Faction), out _);

            Assert.That(drops.Select(d => d.Reason), Is.EqualTo(new[] { ERuleDropReason.Unimplemented }),
                "a name no rulebook implements must keep reporting as unimplemented.");
        }

        [Test]
        public void Audit_AgreesWithLaunch_AboutTheBackfill()
        {
            // ArmyRuleAuditParityTests pins audit/launch parity in general; this pins that the backfill
            // reaches BOTH, so the builder pane can't advertise a rule the launch path drops (or vice versa).
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            Assert.That(ArmyRuleAudit.Audit(StaleList(Faction)).Drops, Is.Empty);
            Assert.That(ArmyRuleAudit.Audit(StaleList(faction: string.Empty)).Drops.Single().Reason,
                Is.EqualTo(ERuleDropReason.OutdatedList));
        }

        [Test]
        public void BackfilledDefinition_SurvivesIntoTheResumeSnapshot()
        {
            // #095: a resumed game rebuilds its resolver from what ArmyData persisted, not from the army
            // file. Persisting only the file's frozen list would leave a backfilled rule dead to every
            // by-name lookup on resume (a RuleGrant token, a unit created mid-game) even though the
            // units carrying it kept their attachments.
            CurrentRulebook.Installed = new StubRulebook(Faction, HitBonus(BackfilledRule, delta: 1));

            ArmyListFile army = StaleList(Faction);
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);

            // What a resume would replay: the store's persisted army rule data, through a fresh resolver.
            RuleResolver resumed = CoreRuleCatalog.CreateResolver();
            GameBootstrap.RestoreArmyRuleData(resumed, store);

            Assert.That(resumed.TryResolve(BackfilledRule, out _), Is.True,
                "the backfilled definition must ride into the resume snapshot with the army's own.");
        }

        /// <summary>Runs the real launch path, capturing structured drops and the built unit.</summary>
        private static List<RuleDrop> Launch(ArmyListFile army, out UnitData unit)
        {
            List<RuleDrop> drops = new List<RuleDrop>();
            void Capture(RuleDrop drop) => drops.Add(drop);

            RuleDiagnostics.OnRuleDropped += Capture;
            try
            {
                GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
                GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);

                unit = store.GetAllValues<UnitData>().Single();
            }
            finally
            {
                RuleDiagnostics.OnRuleDropped -= Capture;
            }

            return drops;
        }
    }
}
