using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #423 - the Caster clause of <b>Solo &amp; Co-Op Rules v3.6.0</b>, quoted:
    ///
    /// <para>
    /// "AI units always cast spells <b>after moving</b>, selecting a <b>random spell by rolling D3+X</b>,
    /// where X is their caster level. If there is no valid target for that spell, or they don't have enough
    /// tokens to cast it, they must <b>cycle through the list</b> until there is a valid spell, or else
    /// <b>don't cast anything</b>."
    /// </para>
    /// <para>
    /// These cases are the selection half. The window half - "after moving", and the larger fault that the
    /// official bot never picked Cast AT ALL - is in <see cref="SoloCoOpResolverTests"/>.
    /// </para>
    /// <para>
    /// The D3 is injected, so each case states the roll instead of hoping for it. OPR numbers spell lists
    /// 1-6, so D3+X is a spell number: the roll and the caster level together pick where in the list to
    /// start looking.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiCasterClauseTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
        }

        // D3 = 2 with Caster(1) is spell number 3, which is the third spell: index 2.
        [Test]
        public void TheRollPicksTheSpellNumber_NotTheFirstCastableOne()
        {
            var resolver = new SoloCoOpSpellResolver(() => 2);

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 1,
                castable: new[] { true, true, true, true, true, true });

            Assert.That(reply.SpellIndex, Is.EqualTo(2),
                "D3(2) + Caster(1) = spell 3, the third in the list - where the baseline resolver would "
                + "have taken the first castable spell every single time.");
        }

        // The caster level is half the roll, so two casters rolling the same die pick different spells.
        [Test]
        public void TheCasterLevelMovesTheRoll()
        {
            var resolver = new SoloCoOpSpellResolver(() => 1);

            ChooseSpellReply low = Resolve(resolver, casterLevel: 1,
                castable: new[] { true, true, true, true, true, true });
            ChooseSpellReply high = Resolve(resolver, casterLevel: 3,
                castable: new[] { true, true, true, true, true, true });

            Assert.Multiple(() =>
            {
                Assert.That(low.SpellIndex, Is.EqualTo(1), "D3(1) + Caster(1) = spell 2.");
                Assert.That(high.SpellIndex, Is.EqualTo(3), "D3(1) + Caster(3) = spell 4.");
            });
        }

        // "...they must cycle through the list until there is a valid spell": the rolled spell is not
        // castable, so the next one that is gets cast.
        [Test]
        public void AnUncastableRolledSpell_CyclesForward()
        {
            var resolver = new SoloCoOpSpellResolver(() => 2);   // spell 3 with Caster(1) -> index 2

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 1,
                castable: new[] { true, true, false, false, true, true });

            Assert.That(reply.SpellIndex, Is.EqualTo(4),
                "index 2 and 3 are not castable, so the cycle lands on 4 - forward through the list, not "
                + "back to the start.");
        }

        // The cycle wraps. Without the wrap a Caster(1) rolling 3 could never reach spell 1, even as the
        // only castable spell in the book - and "cycle through the list" plainly means the whole list.
        [Test]
        public void TheCycleWrapsPastTheEndOfTheList()
        {
            var resolver = new SoloCoOpSpellResolver(() => 3);   // spell 4 with Caster(2) -> index 5

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 2,
                castable: new[] { true, false, false, false, false, false });

            Assert.That(reply.SpellIndex, Is.EqualTo(0),
                "the only castable spell is the first one, and the cycle has to come round to it.");
        }

        // "...or else don't cast anything."
        [Test]
        public void NothingCastable_CastsNothing()
        {
            var resolver = new SoloCoOpSpellResolver(() => 1);

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 1,
                castable: new[] { false, false, false, false, false, false });

            Assert.That(reply.SpellIndex, Is.EqualTo(ChooseSpellReply.Cancel.SpellIndex),
                "no valid spell anywhere in the list means no cast - not a forced one.");
        }

        // A shorter list than the roll can reach wraps rather than failing: some factions print four
        // spells, and D3+Caster(3) is a spell number up to 6.
        [Test]
        public void AShortSpellList_WrapsRatherThanFailing()
        {
            var resolver = new SoloCoOpSpellResolver(() => 3);   // spell 6 with Caster(3)

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 3,
                castable: new[] { true, true, true, true });

            Assert.That(reply.SpellIndex, Is.EqualTo(1),
                "spell 6 of a four-spell list wraps to the second entry; a caster with four spells still "
                + "casts.");
        }

        // The clause says nothing about spending extra tokens on the roll, so nothing is spent. Star
        // Quest's resolver boosts because its own flowchart says to; inventing it here would be house AI
        // wearing the book bot's clothes.
        [Test]
        public void NoTokensAreSpentOnBoostingTheRoll()
        {
            var resolver = new SoloCoOpSpellResolver(() => 1);

            ChooseSpellReply reply = Resolve(resolver, casterLevel: 1,
                castable: new[] { true, true, true }, availableTokens: 6);

            Assert.That(reply.TotalBoost, Is.Zero);
        }

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        private ChooseSpellReply Resolve(SoloCoOpSpellResolver resolver, int casterLevel, bool[] castable,
            int availableTokens = 3)
        {
            DataBinding<UnitData> caster = Caster(casterLevel);

            List<ChooseSpellRequest.SpellOption> spells = castable
                .Select((can, i) => new ChooseSpellRequest.SpellOption(
                    $"Spell {i + 1}", $"Does something {i + 1}", Cost: 1, Castable: can,
                    can ? null : "no valid target"))
                .ToList();

            var request = new ChooseSpellRequest(_ai, caster, availableTokens,
                baseThreshold: 4, hinderTokensInRange: 0, spells);

            return resolver.Resolve(request).GetAwaiter().GetResult();
        }

        private DataBinding<UnitData> Caster(int level)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_ai, "Wizard", quality: 4, defense: 4, modelBindings: bindings);
            unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(level) }));

            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
