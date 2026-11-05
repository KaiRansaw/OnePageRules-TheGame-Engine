using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;

namespace FDG
{
    /// <summary>
    /// #456 - the Fast-Play Rules (Grimdark Future and Age of Fantasy Advanced Rulebooks 3.6.0, p17).
    ///
    /// <para>
    /// "Fast-Play rules allow you to speed up regular games, as well as play epic battles of massive size
    /// quickly. We highly recommend using these rules when playing games with 4000pts armies or higher" -
    /// 4000pts in Grimdark Future, <b>3000pts</b> in Age of Fantasy; otherwise the two books' wording is
    /// identical. The Firefight and Skirmish Advanced books do not print Fast-Play at all, so it applies to
    /// those two games only (CLAUDE.md: each book is its own game).
    /// </para>
    ///
    /// <para>
    /// "Pick any of the following: Leader-Based Movement, Leader-Based Melee, Leader-Based Shooting,
    /// Simplified Terrain" - each a separate lobby toggle (<see cref="GameSettings"/>). Simplified Terrain is
    /// deferred by Kai's call (2026-11-05). "No matter which combination of Fast-Play rules you choose to use,
    /// all of them use Unit Leaders", so the leaders exist whenever any of them is on.
    /// </para>
    /// </summary>
    public static class FastPlay
    {
        /// <summary>Whether this game's books have Fast-Play at all: Grimdark Future and Age of Fantasy.</summary>
        public static bool BooksHaveIt(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy;

        /// <summary>The army size each book recommends Fast-Play from. Null for a game without it.</summary>
        public static int? RecommendedFromPoints(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => 4000,
            EGameSystem.AgeOfFantasy => 3000,
            _ => null,
        };

        public static bool Movement(GameSettings s) => BooksHaveIt(s.GameSystem) && s.FastPlayMovement;
        public static bool Melee(GameSettings s) => BooksHaveIt(s.GameSystem) && s.FastPlayMelee;
        public static bool Shooting(GameSettings s) => BooksHaveIt(s.GameSystem) && s.FastPlayShooting;

        /// <summary>
        /// How far a model the Fast-Play rules say is placed "in any way you like" - not measured - may be put.
        /// Not infinite only so the placement search keeps finite numbers; no table is this long.
        /// </summary>
        public const float UnmeasuredMoveInches = 1000f;

        /// <summary>"All of them use Unit Leaders" - true when any Fast-Play rule is in play.</summary>
        public static bool UsesLeaders(GameSettings s) => Movement(s) || Melee(s) || Shooting(s);

        /// <summary>
        /// The leader the game picks for a unit, which the player may change before the game (Kai,
        /// 2026-11-05: "Game picks, I can change it"). Must come out identical on every machine - every
        /// player runs this in lockstep - so it depends only on the models and their order, never on
        /// anything random or local.
        /// <para>
        /// <b>The pick:</b> the model carrying the most weapons that the rest of the unit does not, i.e. the
        /// upgraded model - that is the one a player would protect, and the leader is the last model to die.
        /// Ties go to more weapons in total, then to unit order. A unit where every model is the same gets
        /// its first model. A joined Hero is never the leader: it already dies last of all under the Hero
        /// rule, and the leader is the last of the unit's OWN models.
        /// </para>
        /// <para>
        /// Book is silent on how to choose (it is the player's choice at the table), so this is a judgement
        /// call, recorded in claude/house-rules.md.
        /// </para>
        /// </summary>
        public static ModelID? AutoPick(IUnit unit)
        {
            var candidates = unit.Models
                .Where(m => m.GetIsAlive() && !(unit.JoinedHeroModelId is { } hero && m.ID.Equals(hero)))
                .ToList();
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0].ID;

            // How many models carry each weapon name. A weapon most of the unit carries is the standard
            // kit; one carried by fewer than half is an upgrade.
            var carriers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IModel model in candidates)
                foreach (string name in model.Weapons.Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                    carriers[name] = carriers.TryGetValue(name, out int n) ? n + 1 : 1;

            int Special(IModel m) => m.Weapons.Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(name => carriers[name] * 2 < candidates.Count);

            IModel best = candidates[0];
            int bestSpecial = Special(best), bestCount = best.Weapons.Count();
            for (int i = 1; i < candidates.Count; i++)
            {
                IModel m = candidates[i];
                int special = Special(m), count = m.Weapons.Count();
                if (special > bestSpecial || (special == bestSpecial && count > bestCount))
                {
                    best = m;
                    bestSpecial = special;
                    bestCount = count;
                }
            }
            return best.ID;
        }

        /// <summary>
        /// #456 Leader-Based Shooting: the one model a unit's shooting is measured from - "only measure line of
        /// sight and distances from the leader model ... models can freely shoot as if they were in the
        /// leader's position". Null means measure from every model as usual: Leader-Based Shooting is off, the
        /// unit has no leader, or its leader is dead. The last should not happen (the leader is the last model
        /// to die) but a few wounds pick their own model - Dangerous terrain wounds the model that rolled - and
        /// a unit whose leader fell that way simply shoots the ordinary way rather than not at all.
        /// </summary>
        public static ModelID? ShootingOrigin(IUnit unit, GameSettings settings)
        {
            if (!Shooting(settings) || unit.LeaderModelId is not { } id) return null;
            IModel? leader = unit.Models.FirstOrDefault(m => m.ID.Equals(id));
            return leader != null && leader.GetIsAlive() ? id : null;
        }

        /// <summary>
        /// A model described for the leader picker: its position in the unit and what it carries -
        /// "Model 3: Plasma Rifle, Rifle". Game text, so ASCII (weapon names come from the books, which are
        /// already folded on import).
        /// </summary>
        public static string Describe(IUnit unit, IModel model)
        {
            int index = unit.Models.ToList().FindIndex(m => m.ID.Equals(model.ID)) + 1;
            var weapons = model.Weapons
                .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() > 1 ? $"{g.Count()}x {g.Key}" : g.Key);
            string kit = string.Join(", ", weapons);
            return kit.Length > 0 ? $"Model {index}: {kit}" : $"Model {index}";
        }
    }
}
