using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// GF: Star Quest search tokens and treasure (Horde Mode v2.0.0), status conditions (Core Rules v2.0.1),
    /// items used with a skill action, and the between-waves shop.
    ///
    /// <para>Search tokens live in the mission progress (<see cref="QuestProgress.Tokens"/>); items, credits
    /// and conditions on the hero sheet (<see cref="UnitData.QuestHeroJson"/>), so they ride the save and go
    /// home with the hero.</para>
    /// </summary>
    public static class QuestTreasure
    {
        public const string Afflicted = "Afflicted";
        public const string Crippled = "Crippled";
        public const string Diseased = "Diseased";
        public const string Impaired = "Impaired";
        public const string ItemPrefix = "Item: ";

        private static readonly TextColor Gold = new(255, 215, 0, 255);
        private static readonly TextColor Purple = new(200, 150, 255, 255);
        private static readonly TextColor Red = new(255, 120, 90, 255);

        // ── Hero sheet ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>Changes the hero's sheet and stores it back on the unit.</summary>
        public static void EditSheet(UnitData hero, Action<QuestHero> change)
        {
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null) return;
            change(sheet);
            hero.QuestHeroJson = sheet.ToJson();
        }

        public static bool Has(UnitData hero, string condition) =>
            QuestHero.FromJson(hero.QuestHeroJson)?.Conditions.Contains(condition) == true;

        // ── Search tokens on the table ───────────────────────────────────────────────────────────────────

        public static List<QuestToken> Tokens(IGameContext context) =>
            GameProgressUtilities.GetQuestProgress(context.GameDataStore).Tokens;

        /// <summary>"One in each table quarter, placed randomly at the centre of that table quarter or within 6"
        /// of its table corner." (A token rolled for the quarter's centre sits just off it, beside the objective.)</summary>
        public static void PlaceSearchTokens(IGameContext context, string why)
        {
            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            var rng = new Random(unchecked((context.Settings.DiceSeed ?? 0) * 7349 + quest.NextTokenId * 104729));
            for (int qx = 0; qx < 2; qx++)
                for (int qz = 0; qz < 2; qz++)
                {
                    float sx = qx == 0 ? 1f : -1f, sz = qz == 0 ? 1f : -1f;
                    float cornerX = qx * w, cornerZ = qz * h;
                    float x, z;
                    if (rng.Next(2) == 0)
                    {
                        x = (qx * 2 + 1) * w / 4f - sx * 2f;
                        z = (qz * 2 + 1) * h / 4f - sz * 2f;
                    }
                    else
                    {
                        double r = 2.0 + rng.NextDouble() * 4.0;
                        double a = (10.0 + rng.NextDouble() * 70.0) * Math.PI / 180.0;
                        x = cornerX + sx * (float)(r * Math.Cos(a));
                        z = cornerZ + sz * (float)(r * Math.Sin(a));
                    }
                    quest.Tokens.Add(new QuestToken { Id = quest.NextTokenId++, X = x, Z = z });
                }
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, quest);
            context.Log($"Star Quest: four search tokens are placed ({why}).");
        }

        /// <summary>Places tokens at chosen quarters (1-4): at the quarter's centre, or within 6" of its corner.</summary>
        public static void PlaceTokensAt(IGameContext context, IEnumerable<(int Quarter, bool AtCorner)> spots, string why)
        {
            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            var rng = new Random(unchecked((context.Settings.DiceSeed ?? 0) * 7349 + quest.NextTokenId * 104729));
            int count = 0;
            foreach ((int quarter, bool atCorner) in spots)
            {
                int qx = (quarter == 2 || quarter == 4) ? 1 : 0, qz = quarter >= 3 ? 1 : 0;
                float sx = qx == 0 ? 1f : -1f, sz = qz == 0 ? 1f : -1f;
                float x, z;
                if (!atCorner)
                {
                    // Beside the quarter's centre (objective markers can sit on the exact centre).
                    double a = rng.NextDouble() * Math.PI * 2.0;
                    x = (qx * 2 + 1) * w / 4f + 2.5f * (float)Math.Cos(a);
                    z = (qz * 2 + 1) * h / 4f + 2.5f * (float)Math.Sin(a);
                }
                else
                {
                    double r = 2.0 + rng.NextDouble() * 4.0;
                    double a = (10.0 + rng.NextDouble() * 70.0) * Math.PI / 180.0;
                    x = qx * w + sx * (float)(r * Math.Cos(a));
                    z = qz * h + sz * (float)(r * Math.Sin(a));
                }
                quest.Tokens.Add(new QuestToken { Id = quest.NextTokenId++, X = x, Z = z });
                count++;
            }
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, quest);
            context.Log($"Star Quest: {count} search tokens are placed ({why}).");
        }

        public static bool AnySearchTokensLeft(IGameContext context) => Tokens(context).Any(t => !t.IsTreasure);

        /// <summary>Distance from the hero's base edge to a token.</summary>
        public static float DistanceTo(UnitData hero, QuestToken token)
        {
            float best = float.MaxValue;
            foreach (IModel model in hero.Models)
            {
                if (!model.GetIsAlive()) continue;
                float dx = model.Position.x - token.X, dz = model.Position.z - token.Z;
                best = MathF.Min(best, MathF.Max(0f, MathF.Sqrt(dx * dx + dz * dz) - model.BaseRadiusInches));
            }
            return best;
        }

        public static bool CanSee(IGameContext context, UnitData hero, QuestToken token)
        {
            var terrain = context.TableState.Terrain.Objects.ToList();
            var at = new Position(token.X, 0.5f, token.Z);
            return hero.Models.Any(m => m.GetIsAlive()
                && LineOfSightUtilities.HasLineOfSight(m.Position, at, terrain));
        }

        /// <summary>Search tokens within range and in sight (or any token within 1" when <paramref name="pickUpTreasure"/>).</summary>
        public static List<QuestToken> TokensFor(IGameContext context, UnitData hero, float range, bool pickUpTreasure)
        {
            var list = new List<QuestToken>();
            foreach (QuestToken t in Tokens(context))
            {
                float d = DistanceTo(hero, t);
                if (t.IsTreasure) { if (pickUpTreasure && d <= 1f) list.Add(t); continue; }
                if (d <= range && (d <= 1f || CanSee(context, hero, t))) list.Add(t);
            }
            return list.OrderBy(t => DistanceTo(hero, t)).ToList();
        }

        public static async Task<QuestToken?> PickToken(IGameContext context, UnitData hero, string skill, List<QuestToken> tokens)
        {
            if (tokens.Count == 0) return null;
            if (tokens.Count == 1) return tokens[0];
            var labels = tokens.ToDictionary(t => $"{t.Label} - {DistanceTo(hero, t):0.#}\" away", t => t);
            var request = new StringSelectionRequest(hero.PlayerID, $"{skill}: which token?", labels.Keys.ToList(),
                Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: $"{hero.Name}: {skill}");
            string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            return labels.TryGetValue(pick, out QuestToken? t) ? t : tokens[0];
        }

        private static void Update(IGameContext context, Action<QuestProgress> change)
        {
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            change(quest);
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, quest);
        }

        // ── Revealing ────────────────────────────────────────────────────────────────────────────────────

        public static string TreasureName(int roll) => roll switch
        {
            <= 2 => "Hazard",
            3 => "Injector Pouch",
            4 => "Grenade Bag",
            5 => "Kit Box",
            6 => "Credits Stash",
            _ => "Treasure Trove",
        };

        private static readonly string[] TroveChoices =
        {
            "Courage Injector", "Frenzy Injector", "Power Injector", "Noise Grenade", "Grease Grenade",
            "Stun Grenade", "Curing Kit", "Teleport Kit", "Med Kit", "15c",
        };

        /// <summary>
        /// Uses a token: a treasure marker is picked up; a search token is revealed (roll on the Hidden Treasure
        /// table, which the hero may shift by up to <paramref name="maxShift"/>). An item or credits found within
        /// 1" is picked up, otherwise the token becomes a treasure marker. A hazard only hurts a hero within 1".
        /// </summary>
        public static async Task Reveal(IGameContext context, UnitData hero, QuestToken token, int maxShift)
        {
            bool close = DistanceTo(hero, token) <= 1f;
            if (token.IsTreasure)
            {
                await Give(context, hero, token.Contents);
                Update(context, q => q.Tokens.RemoveAll(t => t.Id == token.Id));
                return;
            }

            int roll = HordePlacement.RollD6(context);
            int result = roll;
            if (maxShift > 0)
            {
                var options = new Dictionary<string, int> { [$"Keep {roll}: {TreasureName(roll)}"] = roll };
                for (int shift = -maxShift; shift <= maxShift; shift++)
                {
                    int r = roll + shift;
                    if (shift == 0 || r < 1 || r > 7) continue;
                    options[$"{(shift > 0 ? "+" : "")}{shift} = {r}: {TreasureName(r)}"] = r;
                }
                var request = new StringSelectionRequest(hero.PlayerID,
                    $"{token.Label}: rolled {roll} ({TreasureName(roll)}). Change the roll?", options.Keys.ToList(),
                    Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: $"{hero.Name}: search roll");
                string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                if (options.TryGetValue(pick, out int chosen)) result = chosen;
            }
            context.Log($"{hero.Name} reveals {token.Label}: rolled {roll}" + (result != roll ? $", changed to {result}" : "")
                + $" - {TreasureName(result)}.");

            if (result <= 2)
            {
                Update(context, q => q.Tokens.RemoveAll(t => t.Id == token.Id));
                string? condition = close ? HordePlacement.RollD6(context) switch
                {
                    1 => Afflicted, 2 => Diseased, 3 => Impaired, 4 => Crippled, _ => null,
                } : null;
                await context.Announce($"{token.Label}: a hazard!" + (condition != null ? $" {hero.Name} is {condition}." : ""),
                    Red, EBannerTier.Notice);
                if (condition != null) AddCondition(hero, condition);
                return;
            }

            List<string> contents = result switch
            {
                3 => new() { HordePlacement.RollD6(context) switch { <= 3 => "Courage Injector", <= 5 => "Frenzy Injector", _ => "Power Injector" } },
                4 => new() { HordePlacement.RollD6(context) switch { <= 3 => "Noise Grenade", <= 5 => "Grease Grenade", _ => "Stun Grenade" } },
                5 => new() { HordePlacement.RollD6(context) switch { <= 3 => "Curing Kit", <= 5 => "Teleport Kit", _ => "Med Kit" } },
                6 => new() { HordePlacement.RollD6(context) switch { <= 3 => "5c", <= 5 => "10c", _ => "15c" } },
                _ => await ChooseTrove(context, hero),
            };
            // Kai, QA 3.14/3.15: when the hero is close enough to take it there and then, the
            // discovery line and the pickup line say the same thing twice - all that matters is who
            // ended up holding what, so only the pickup below is announced. A find made at range
            // leaves a marker on the table, so THAT is worth announcing, by what it is rather than
            // by which token it came out of.
            if (!close)
            {
                await context.Announce($"{string.Join(" + ", contents)} has been discovered.", Gold, EBannerTier.Notice);
            }

            if (close)
            {
                await Give(context, hero, contents);
                Update(context, q => q.Tokens.RemoveAll(t => t.Id == token.Id));
            }
            else
            {
                Update(context, q =>
                {
                    QuestToken? t = q.Tokens.FirstOrDefault(x => x.Id == token.Id);
                    if (t == null) return;
                    t.IsTreasure = true;
                    t.Contents = contents;
                });
                context.Log($"  It stays on the table as a treasure marker - move within 1\" and Discover to pick it up.");
            }
        }

        /// <summary>Prophetic Vision: up to <paramref name="count"/> random hidden search tokens show what they hold
        /// (hazards vanish; finds become treasure markers).</summary>
        public static void ForeseeTokens(IGameContext context, int count)
        {
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            var hidden = quest.Tokens.Where(t => !t.IsTreasure).ToList();
            for (int i = 0; i < count && hidden.Count > 0; i++)
            {
                QuestToken t = hidden[(HordePlacement.RollD6(context) - 1) % hidden.Count];
                hidden.Remove(t);
                int roll = HordePlacement.RollD6(context);
                if (roll <= 2) { quest.Tokens.Remove(t); continue; }
                int sub = HordePlacement.RollD6(context);
                t.IsTreasure = true;
                t.Contents = roll switch
                {
                    3 => new() { sub <= 3 ? "Courage Injector" : sub <= 5 ? "Frenzy Injector" : "Power Injector" },
                    4 => new() { sub <= 3 ? "Noise Grenade" : sub <= 5 ? "Grease Grenade" : "Stun Grenade" },
                    5 => new() { sub <= 3 ? "Curing Kit" : sub <= 5 ? "Teleport Kit" : "Med Kit" },
                    _ => new() { sub <= 3 ? "5c" : sub <= 5 ? "10c" : "15c" },
                };
            }
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, quest);
        }

        private static async Task<List<string>> ChooseTrove(IGameContext context, UnitData hero)
        {
            var picks = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                var request = new StringSelectionRequest(hero.PlayerID, $"Treasure Trove: pick {(i == 0 ? "the first" : "the second")} item or credits",
                    TroveChoices.ToList(), Array.Empty<StringSelectionRequest.InvalidOption>(),
                    optionDescriptions: TroveChoices.ToDictionary(c => c, c => QuestItems.Find(c)?.Description ?? "15 credits."),
                    displayName: $"{hero.Name}: Treasure Trove");
                string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                picks.Add(TroveChoices.Contains(pick) ? pick : TroveChoices[0]);
            }
            return picks;
        }

        /// <summary>Puts items and credits into the hero's pack.</summary>
        public static async Task Give(IGameContext context, UnitData hero, IEnumerable<string> contents)
        {
            var list = contents.ToList();
            EditSheet(hero, s =>
            {
                foreach (string c in list)
                {
                    if (QuestItems.Credits(c) is int credits) s.Credits += credits;
                    else s.Items.Add(c);
                }
            });
            await context.Announce($"{hero.Name} picked up {string.Join(" + ", list)}.", Gold, EBannerTier.Toast);
        }

        // ── Status conditions ────────────────────────────────────────────────────────────────────────────

        public static void AddCondition(UnitData hero, string condition) =>
            EditSheet(hero, s => { if (!s.Conditions.Contains(condition)) s.Conditions.Add(condition); });

        public static void RemoveCondition(UnitData hero, string condition) =>
            EditSheet(hero, s => s.Conditions.Remove(condition));

        /// <summary>"When activated" checks for each condition the hero has.</summary>
        public static async Task OnActivated(IGameContext context, UnitData hero)
        {
            await ActivatedInDanger(context, hero);
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null || sheet.Conditions.Count == 0 || !hero.GetIsAlive()) return;
            foreach (string condition in sheet.Conditions.ToList())
            {
                if (!hero.GetIsAlive()) return;
                switch (condition)
                {
                    case Afflicted:
                        if (await QuestActions.StatTest(context, hero, 0, "shake off Afflicted", offerPower: true))
                            await Cured(context, hero, condition);
                        else
                        {
                            await QuestActions.Toast(context, $"{hero.Name} is Afflicted and takes 1 wound.");
                            await CasualtyPresentation.ApplyUnitWounds(context, hero, 1f);
                        }
                        break;
                    case Crippled:
                        if (await QuestActions.StatTest(context, hero, 1, "shake off Crippled", offerPower: true))
                            await Cured(context, hero, condition);
                        else
                            await QuestActions.Toast(context, $"{hero.Name} is Crippled: half movement.");
                        break;
                    case Diseased:
                        if (await QuestActions.StatTest(context, hero, 2, "shake off Diseased", offerPower: true))
                            await Cured(context, hero, condition);
                        break;
                    case Impaired:
                        bool pay = await context.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(hero.PlayerID,
                            $"{hero.Name} is Impaired (-1 to stat rolls). Spend D3 power to remove it? ({hero.QuestPower} power)",
                            defaultAnswer: hero.QuestPower >= 3, displayName: $"{hero.Name}: Impaired"));
                        if (pay)
                        {
                            int d = (HordePlacement.RollD6(context) + 1) / 2;
                            await QuestActions.SpendPower(context, hero, d, "to remove Impaired");
                            await Cured(context, hero, condition);
                        }
                        break;
                }
            }
        }

        private static async Task Cured(IGameContext context, UnitData hero, string condition)
        {
            RemoveCondition(hero, condition);
            await QuestActions.Toast(context, $"{hero.Name} is no longer {condition}.");
        }

        public static void GrantRule(IGameContext context, IUnit bearer, IUnit target, string rule, ELifetime lifetime)
        {
            var invocation = new RuleInvocation(Hook: null, Bearer: bearer, Arguments: Array.Empty<RuleArgument>(),
                Target: target, DiceRoller: context.DiceRoller);
            var ops = new List<RuleOperation>();
            new Effect.AddRule(rule, lifetime).Apply(invocation, ops);
            OperationApplier.ApplyTokenOperations(ops);
        }

        // ── Shop ─────────────────────────────────────────────────────────────────────────────────────────

        public const string ShopDone = "Done shopping";

        /// <summary>End of wave: the hero buys and sells items. Items unlock by tier as the hero levels up.</summary>
        public static async Task Shop(IGameContext context, UnitData hero, bool canBuyAndSell = true)
        {
            for (int guard = 0; guard < 40; guard++)
            {
                QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
                if (sheet == null) return;
                int tier = QuestProgression.TierUnlocked(sheet.Level);
                var options = new List<string> { ShopDone };
                var descriptions = new Dictionary<string, string> { [ShopDone] = "Leave the shop." };
                var actions = new Dictionary<string, Action<QuestHero>>();
                foreach (QuestItemInfo item in QuestItems.All.Where(i => canBuyAndSell && i.Tier <= tier && i.Cost <= sheet.Credits))
                {
                    string o = $"Buy {item.Name} ({item.Cost}c)";
                    options.Add(o);
                    descriptions[o] = item.Description;
                    actions[o] = s => { s.Credits -= item.Cost; s.Items.Add(item.Name); };
                }
                foreach (string held in sheet.Items.Distinct().Where(_ => canBuyAndSell))
                {
                    QuestItemInfo? info = QuestItems.Find(held);
                    if (info == null) continue;
                    string o = $"Sell {held} (+{info.SellValue}c)";
                    options.Add(o);
                    descriptions[o] = $"You have {sheet.Items.Count(i => i == held)}. The shop pays full price.";
                    actions[o] = s => { s.Items.Remove(held); s.Credits += info.SellValue; };
                }
                // Heroes may hand items and credits to each other.
                var gifts = new Dictionary<string, (UnitData To, string What)>();
                foreach (UnitData other in StarQuestHordeRules.Heroes(context).Where(o => !ReferenceEquals(o, hero)))
                {
                    string otherName = QuestHero.FromJson(other.QuestHeroJson)?.Name ?? other.Name;
                    foreach (string held in sheet.Items.Distinct())
                    {
                        string o = $"Give {held} to {otherName}";
                        options.Add(o);
                        descriptions[o] = $"Hand one {held} to {otherName}.";
                        gifts[o] = (other, held);
                    }
                    foreach (int amount in new[] { 5, 10 }.Where(a => a <= sheet.Credits))
                    {
                        string o = $"Give {amount}c to {otherName}";
                        options.Add(o);
                        descriptions[o] = $"Hand {amount} credits to {otherName}.";
                        gifts[o] = (other, $"{amount}c");
                    }
                }
                if (options.Count == 1) return;

                string bag = sheet.Items.Count == 0 ? "nothing" : string.Join(", ", sheet.Items.GroupBy(i => i)
                    .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
                var request = new StringSelectionRequest(hero.PlayerID,
                    $"{(canBuyAndSell ? "Shop" : "Share loot")} - {sheet.Name}: {sheet.Credits}c. Carrying: {bag}.", options,
                    Array.Empty<StringSelectionRequest.InvalidOption>(), optionDescriptions: descriptions,
                    displayName: $"{sheet.Name}: shop");
                string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                if (gifts.TryGetValue(pick, out var gift))
                {
                    if (QuestItems.Credits(gift.What) is int c) EditSheet(hero, s => s.Credits -= c);
                    else EditSheet(hero, s => s.Items.Remove(gift.What));
                    EditSheet(gift.To, s =>
                    {
                        if (QuestItems.Credits(gift.What) is int credits) s.Credits += credits;
                        else s.Items.Add(gift.What);
                    });
                    context.Log($"  {sheet.Name}: {pick}.");
                    continue;
                }
                if (!actions.TryGetValue(pick, out Action<QuestHero>? act)) return;
                EditSheet(hero, act);
                context.Log($"  {sheet.Name}: {pick}.");
            }
        }

        // ── Item markers (grenades) and timed injectors ──────────────────────────────────────────────────

        public static readonly string[] ItemMarkerKinds = { "noise", "grease", "smoke", "fire", "phantasm", "silence", "poison", "biotrap", "spikes", "spores" };

        /// <summary>A marker kind as the player should read it ("biotrap" -> "bio-trap").</summary>
        public static string MarkerName(string kind) => kind switch
        {
            "biotrap" => "bio-trap",
            "phantasm" => "phantasm",
            "spikes" => "spike growth",
            "spores" => "circle of spores",
            _ => kind,
        };

        /// <summary>Puts an area marker on the table for <paramref name="rounds"/> rounds (1 = until the end of this round).</summary>
        public static void PlaceMarkerAt(IGameContext c, string kind, Position p, int rounds)
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            q.Markers.Add(new QuestMarker { Id = q.NextMarkerId++, Kind = kind, X = p.x, Z = p.z, RoundsLeft = rounds, Action = "" });
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
            RefreshSmoke(c);
        }

        /// <summary>A dangerous terrain test: one die per wound the unit can take, a wound for each 1.</summary>
        public static async Task DangerousTest(IGameContext c, UnitData u, string source)
        {
            int dice = u.Models.Where(m => m.GetIsAlive()).Sum(m => Math.Max(1, (int)MathF.Round(m.TotalWounds - m.WoundsDealt)));
            int ones = 0;
            for (int i = 0; i < dice; i++) if (HordePlacement.RollD6(c) == 1) ones++;
            if (ones == 0) return;
            await QuestActions.Toast(c, $"{source}: {u.Name} takes {ones} wound{(ones == 1 ? "" : "s")}.");
            await CasualtyPresentation.ApplyUnitWounds(c, u, ones);
        }

        /// <summary>A unit activating in dangerous terrain (a fire marker; a Bio-Trap for enemies) takes a test.</summary>
        public static async Task ActivatedInDanger(IGameContext c, UnitData u)
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            if (NearMarker(q, u, "fire")) await DangerousTest(c, u, "Fire");
            else if (!u.IsQuestHero && NearMarker(q, u, "biotrap")) await DangerousTest(c, u, "Dangerous ground");
        }

        public static string? IsCaster(IGameContext c, UnitData h) =>
            (QuestHero.FromJson(h.QuestHeroJson)?.Caster ?? 0) > 0 ? null : "Only casters can use it.";

        public static async Task AddSpellTokens(IGameContext c, UnitData h, int n)
        {
            int have = h.Tokens.GetTokenCount(TokenType.SpellTokens);
            int add = Math.Max(0, Math.Min(n, 6 - have));
            if (add > 0) h.Tokens.AddToken(new Token(TokenType.SpellTokens, add, new TokenClearTrigger.ManualOnly(), OwnerUnitID: h.ID));
            await QuestActions.Toast(c, $"{h.Name} gets {add} spell token{(add == 1 ? "" : "s")} ({have + add}).");
        }

        public static int EnemiesDestroyed(IGameContext c) =>
            StarQuestHordeRules.Enemies(c).Count(u => !u.GetIsAlive() && u.Models.Any(m => m.Position.x != 0f || m.Position.z != 0f));

        /// <summary>The heroes' player clicks a spot within <paramref name="range"/>" of the hero.</summary>
        public static async Task<Position> PickPoint(IGameContext c, UnitData h, string label, float range)
        {
            Position at = h.Models.FirstOrDefault(m => m.GetIsAlive())?.Position ?? StarQuestMissionRules.Centre;
            float w = HordePlacement.TableWidth, ht = HordePlacement.TableHeight;
            var band = new RectangularZone(Math.Max(0.5f, at.x - range), Math.Min(w - 0.5f, at.x + range),
                Math.Max(0.5f, at.z - range), Math.Min(ht - 0.5f, at.z + range));
            var request = new PlaceObjectiveRequest(h.PlayerID, $"Marker: {label} (within {range:0}\" of {h.Name})", 1, 0, band, 0f);
            Position p = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(request);
            float dx = p.x - at.x, dz = p.z - at.z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > range && d > 0f) p = new Position(at.x + dx / d * range, at.z + dz / d * range);
            return new Position(Math.Clamp(p.x, 0.5f, w - 0.5f), Math.Clamp(p.z, 0.5f, ht - 0.5f));
        }

        private static async Task PlaceItemMarker(IGameContext c, UnitData h, string item, string kind, float range)
        {
            Position p = await PickPoint(c, h, item, range);
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            q.Markers.Add(new QuestMarker { Id = q.NextMarkerId++, Kind = kind, X = p.x, Z = p.z, RoundsLeft = 3, Action = "" });
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
            RefreshSmoke(c);
            string what = kind switch
            {
                "noise" => "enemies treat it as their AI Goal",
                "grease" => "within 3\" counts as difficult terrain",
                "smoke" => "within 3\" counts as cover",
                "fire" => "within 3\" counts as dangerous terrain",
                _ => "enemies treat it as a hero",
            };
            await QuestActions.Toast(c, $"{item} marker placed: {what} (3 rounds).");
        }

        public static void AddUnitMarker(IGameContext c, UnitData unit, string kind, int rounds = 3, string source = "")
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            Position p = unit.Models.FirstOrDefault(m => m.GetIsAlive())?.Position ?? StarQuestMissionRules.Centre;
            q.Markers.Add(new QuestMarker { Id = q.NextMarkerId++, Kind = kind, X = p.x, Z = p.z, RoundsLeft = rounds, Action = source, OnUnit = unit.ID.ID.ToString() });
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
        }

        /// <summary>Where item markers on the table are (live, for the AI and the cover check).</summary>
        public static List<Position> MarkerSpots(QuestProgress? q, string kind) =>
            q == null ? new() : q.Markers.Where(m => m.Kind == kind).Select(m => new Position(m.X, m.Z)).ToList();

        /// <summary>Smoke areas for the cover check (which has no game context): updated whenever markers change.</summary>
        public static volatile Position[] SmokeSpots = Array.Empty<Position>();

        public static void RefreshSmoke(IGameContext c) =>
            SmokeSpots = MarkerSpots(GameProgressUtilities.GetQuestProgress(c.GameDataStore), "smoke").ToArray();

        /// <summary>True when the unit stands within 3" of a marker of this kind.</summary>
        public static bool NearMarker(QuestProgress? q, IUnit unit, string kind, float inches = 3f) =>
            MarkerSpots(q, kind).Any(p => QuestMission.DistanceToPoint(unit, p) <= inches);

        /// <summary>End of round: item markers count down (3 rounds); noise / phantasm markers go when an enemy
        /// is within 1"; this round's Frenzy / Adrenaline wear off.</summary>
        public static async Task TickItemMarkers(IGameContext c)
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            var enemies = StarQuestHordeRules.Enemies(c).Where(e => e.GetIsAlive() && e.GetIsOnBattlefield()).ToList();
            var gone = new List<string>();
            foreach (QuestMarker m in q.Markers.Where(m => ItemMarkerKinds.Contains(m.Kind)).ToList())
            {
                bool touched = (m.Kind is "noise" or "phantasm")
                    && enemies.Any(e => QuestMission.DistanceToPoint(e, new Position(m.X, m.Z)) <= 1f);
                m.RoundsLeft--;
                if (m.RoundsLeft > 0 && !touched) continue;
                q.Markers.Remove(m);
                gone.Add(m.Kind);
            }
            q.Frenzy.Clear();
            q.Adrenaline.Clear();
            q.Respite.Clear();
            QuestFlags.ClearRound(q);
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
            RefreshSmoke(c);
            // Say which one, and say it even when several go at once - a marker quietly disappearing from
            // the table with no line in the report is indistinguishable from a bug.
            if (gone.Count > 0)
                await QuestActions.Toast(c, gone.Count == 1
                    ? $"The {MarkerName(gone[0])} marker wears off."
                    : $"{gone.Count} markers wear off: {string.Join(", ", gone.Select(MarkerName))}.");
        }

        /// <summary>Start of round: fire markers burn (dangerous terrain test for units that start within 3"),
        /// silenced units get -2 to casting this round.</summary>
        public static async Task ItemMarkerRoundEffects(IGameContext c)
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            RefreshSmoke(c);
            var units = QuestActions.Units(c).Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();
            foreach (QuestMarker m in q.Markers.Where(m => m.Kind == "silence"))
                if (units.FirstOrDefault(u => u.ID.ID.ToString() == m.OnUnit) is { } s)
                    QuestActions.Grant(s, s, ERollKind.Cast, -2, ELifetime.ThisRound, c);
        }

        /// <summary>A poisoned unit activates: on a 4+ it takes 1 wound, otherwise the poison wears off.</summary>
        public static async Task OnEnemyActivated(IGameContext c, UnitData unit)
        {
            await ActivatedInDanger(c, unit);
            if (!unit.GetIsAlive()) return;
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            // Circle of Spores: on a 4+ the spore marker goes, otherwise the unit takes 1 hit.
            foreach (QuestMarker spore in q.Markers.Where(m => m.Kind == "spores" && m.OnUnit == unit.ID.ID.ToString()).ToList())
            {
                int sporeRoll = HordePlacement.RollD6(c);
                if (sporeRoll >= 4)
                {
                    q.Markers.Remove(spore);
                    GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                    await QuestActions.Toast(c, $"Spores: {unit.Name} rolls {sporeRoll} and shakes them off.");
                }
                else
                {
                    UnitData? druid = StarQuestHordeRules.Heroes(c).FirstOrDefault(h => h.ID.ID.ToString() == spore.Action) ?? StarQuestHordeRules.Heroes(c).FirstOrDefault();
                    await QuestActions.Toast(c, $"Spores: {unit.Name} rolls {sporeRoll} and takes 1 hit.");
                    if (druid != null) await QuestActions.DealHits(c, druid, unit, 1, 0, "Circle of Spores");
                }
                if (!unit.GetIsAlive()) return;
                q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            }
            QuestMarker? poison = q.Markers.FirstOrDefault(m => m.Kind == "poison" && m.OnUnit == unit.ID.ID.ToString());
            if (poison == null) return;
            int roll = HordePlacement.RollD6(c);
            if (roll >= 4)
            {
                // Kai, QA 3.19: say what HAPPENED, not the mechanism. The roll itself is on the
                // hover, where it belongs.
                await QuestActions.Toast(c, $"{unit.Name} fails to fight off Poison.");
                await CasualtyPresentation.ApplyUnitWounds(c, unit, 1f);
                return;
            }
            q.Markers.Remove(poison);
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
            await QuestActions.Toast(c, $"{unit.Name} fights off Poison.");
        }

        /// <summary>Any activation ends: Adrenaline heroes shed fatigue; a Frenzy hero that destroyed an enemy
        /// unit may move up to 6".</summary>
        public static async Task OnAnyActivationEnd(IGameContext c, UnitData activated)
        {
            await QuestFlags.OnActivationEnd(c, activated);
            QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
            foreach (UnitData h in StarQuestHordeRules.Heroes(c).Where(h => q.Adrenaline.Contains(h.ID.ID.ToString())))
                if (h.Tokens.GetTokenCount(TokenType.Fatigued) > 0) h.Tokens.RemoveTokens(TokenType.Fatigued, h.Tokens.GetTokenCount(TokenType.Fatigued));
            if (!activated.IsQuestHero || !activated.GetIsAlive()) return;
            string? respite = q.Respite.FirstOrDefault(f => f.StartsWith(activated.ID.ID + ":"));
            if (respite != null && int.TryParse(respite.Split(':')[1], out int before) && EnemiesDestroyed(c) > before)
            {
                q.Respite.Remove(respite);
                GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                int d = (HordePlacement.RollD6(c) + 1) / 2 + 1;
                QuestActions.RestorePower(activated, d);
                await QuestActions.Toast(c, $"Fiendish Respite: {activated.Name} restores {d} power.");
            }
            string? entry = q.Frenzy.FirstOrDefault(f => f.StartsWith(activated.ID.ID + ":"));
            if (entry == null || !int.TryParse(entry.Split(':')[1], out int was) || EnemiesDestroyed(c) <= was) return;
            q.Frenzy.Remove(entry);
            GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
            await QuestActions.Toast(c, $"Frenzy! {activated.Name} may move up to 6\".");
            await new GameOperationServices(c).MoveUnit(activated, 6f, isOptional: true, controller: activated.PlayerID);
        }

        // ── Skills that use search tokens ────────────────────────────────────────────────────────────────

        private static Func<IGameContext, UnitData, string?> NeedsToken(float range, bool pickUpTreasure) =>
            (c, h) => TokensFor(c, h, range, pickUpTreasure).Count > 0 ? null
                : range <= 1f ? "No search token within 1\"." : $"No search token in sight within {range:0}\".";

        private static Func<IGameContext, UnitData, Task> RevealSkill(string name, float range, int shift) => async (c, h) =>
        {
            QuestToken? t = await PickToken(c, h, name, TokensFor(c, h, range, false));
            if (t != null) await Reveal(c, h, t, shift);
        };

        public static readonly IReadOnlyList<QuestSkill> SearchSkills = new List<QuestSkill>
        {
            new(QuestSkill.DiscoverName, -1,
                "Reveal one search token in sight within 6\" by passing a Wil test, or reveal and pick it up (or pick up a treasure marker) within 1\" without a roll.",
                NeedsToken(6f, true), async (c, h) =>
                {
                    QuestToken? t = await PickToken(c, h, QuestSkill.DiscoverName, TokensFor(c, h, 6f, true));
                    if (t == null) return;
                    if (DistanceTo(h, t) > 1f && !await QuestActions.StatTest(c, h, 2, QuestSkill.DiscoverName, offerPower: true))
                    {
                        await QuestActions.Toast(c, $"{h.Name} finds nothing at {t.Label}.");
                        return;
                    }
                    await Reveal(c, h, t, 0);
                }, common: true),
            new("Inspect", 2, "Reveal one search token in sight within 9\", and change the treasure roll by +1/-1.",
                NeedsToken(9f, false), RevealSkill("Inspect", 9f, 1)),
            new("Tracking Bird", 2, "Reveal one search token in sight within 18\".",
                NeedsToken(18f, false), RevealSkill("Tracking Bird", 18f, 0)),
            new("Sleight of Hand", 1, "Pick up one search token within 1\", and change the treasure roll by up to +2/-2.",
                NeedsToken(1f, false), RevealSkill("Sleight of Hand", 1f, 2)),
            new("Mind Vision", 0, "Pick up one search token within 1\", and change the treasure roll by up to +2/-2.",
                NeedsToken(1f, false), RevealSkill("Mind Vision", 1f, 2)),
        };

        // ── Items (used with a skill action, then removed) ───────────────────────────────────────────────

        private static Func<IGameContext, UnitData, string?> EnemyWithin(float range) =>
            (c, h) => QuestActions.Enemies(c, h, range).Count > 0 ? null : $"No enemy within {range:0}\".";

        private static string? Always(IGameContext c, UnitData h) => null;

        private static async Task<UnitData?> SelfOrHero(IGameContext c, UnitData h, string item)
        {
            var picks = await QuestActions.PickUnits(c, h, item, QuestActions.Friends(c, h, 3f, true, true), 1);
            return picks.Count > 0 ? picks[0] : null;
        }

        private static async Task<UnitData?> OneEnemy(IGameContext c, UnitData h, string item, float range)
        {
            var picks = await QuestActions.PickUnits(c, h, item, QuestActions.Enemies(c, h, range), 1);
            return picks.Count > 0 ? picks[0] : null;
        }

        /// <summary>Items a hero lobs rather than applies - they read as "throws", not "uses".</summary>
        private static bool IsThrown(string name) => name.EndsWith("Grenade", StringComparison.Ordinal);

        private static QuestSkill Item(string name, Func<IGameContext, UnitData, string?> unavailable,
            Func<IGameContext, UnitData, Task> use) =>
            new(ItemPrefix + name, -1, QuestItems.Find(name)?.Description ?? name, unavailable, async (c, h) =>
            {
                EditSheet(h, s => s.Items.Remove(name));
                // One standard form for every item, so the report reads the same way each time
                // (Kai, QA 3.17-3.20): a grenade is THROWN, everything else is used. No "a"/"an"
                // guessing, and the item keeps its own name so it can be looked up.
                c.Log(IsThrown(name) ? $"{h.Name} throws {name}!" : $"{h.Name} uses {name}.");
                await use(c, h);
            });

        private static QuestSkill HealItem(string name, int wounds) => Item(name, Always, async (c, h) =>
        {
            if (await SelfOrHero(c, h, name) is { } u)
            {
                QuestActions.Heal(u, wounds, c);
                await QuestActions.Toast(c, $"{name}: {u.Name} restores {wounds} wounds.");
            }
        });

        private static QuestSkill PowerItem(string name, int power) => Item(name, Always, async (c, h) =>
        {
            if (await SelfOrHero(c, h, name) is { } u)
            {
                QuestActions.RestorePower(u, power);
                await QuestActions.Toast(c, $"{name}: {u.Name} restores {power} power ({u.QuestPower}).");
            }
        });

        private static QuestSkill SelfBuff(string name, ERollKind roll, int delta, string text) => Item(name, Always, async (c, h) =>
        {
            QuestActions.Grant(h, h, roll, delta, ELifetime.ThisRound, c);
            await QuestActions.Toast(c, QuestSkill.Line(h.Name, text));
        });

        public static readonly IReadOnlyList<QuestSkill> ItemSkills = new List<QuestSkill>
        {
            HealItem("Med Kit", 3),
            HealItem("Advanced Med Kit", 6),
            PowerItem("Resting Kit", 3),
            PowerItem("Advanced Resting Kit", 6),
            Item("Curing Kit", Always, async (c, h) =>
            {
                if (await SelfOrHero(c, h, "Curing Kit") is { } u)
                {
                    EditSheet(u, s => s.Conditions.Clear());
                    await QuestActions.Toast(c, $"Curing Kit: {u.Name} loses all status conditions.");
                }
            }),
            Item("Reviving Kit", (c, h) => QuestActions.UnconsciousHeroes(c, h, 3f).Count > 0 ? null : "No unconscious hero within 3\".",
                async (c, h) =>
                {
                    // Always ask, even with one hero down - the player should see who they are reviving.
                    var down = QuestActions.UnconsciousHeroes(c, h, 3f);
                    var who = new StringSelectionRequest(h.PlayerID, "Which hero does the Reviving Kit wake?",
                        down.Select(d => d.Name).ToList(), Array.Empty<StringSelectionRequest.InvalidOption>(),
                        displayName: "Choosing a hero to revive");
                    string picked = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(who);
                    UnitData target = down.FirstOrDefault(d => d.Name == picked) ?? down[0];
                    QuestActions.Revive(target, 6);
                    await c.Announce($"{h.Name} revives {target.Name} with a Reviving Kit!", new TextColor(120, 230, 140, 255), EBannerTier.Notice);
                }),
            Item("Power Injector", Always, async (c, h) =>
            {
                h.QuestStatBonus += 2;
                await QuestActions.Toast(c, $"{h.Name}: +2 to the next stat roll.");
            }),
            SelfBuff("Courage Injector", ERollKind.Morale, 6, "passes morale tests this round"),
            SelfBuff("Nanoplate Injector", ERollKind.Save, 1, "+1 to Defense rolls this round"),
            SelfBuff("Advanced Nanoplate Injector", ERollKind.Save, 2, "+2 to Defense rolls this round"),
            Item("Speed Injector", Always, async (c, h) =>
            {
                GrantRule(c, h, h, "Fast", ELifetime.ThisRound);
                await QuestActions.Toast(c, $"{h.Name}: +2\" Advance, +4\" Rush/Charge this round.");
            }),
            Item("Teleport Kit", Always, async (c, h) =>
            {
                await QuestActions.Toast(c, $"{h.Name} teleports up to 6\".");
                await new GameOperationServices(c).MoveUnit(h, 6f, isOptional: true, controller: h.PlayerID);
            }),
            Item("Recon Kit", NeedsToken(12f, false), RevealSkill("Recon Kit", 12f, 1)),
            Item("Flash Grenade", EnemyWithin(9f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Flash Grenade", 9f) is not { } e) return;
                QuestActions.Grant(h, e, ERollKind.Hit, -1, ELifetime.ThisRound, c);
                await QuestActions.Toast(c, $"{e.Name}: -1 to hit this round.");
            }),
            Item("Web Grenade", EnemyWithin(9f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Web Grenade", 9f) is not { } e) return;
                GrantRule(c, h, e, "Slow", ELifetime.ThisRound);
                await QuestActions.Toast(c, $"{e.Name}: -2\" Advance, -4\" Rush/Charge this round.");
            }),
            Item("Stun Grenade", EnemyWithin(6f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Stun Grenade", 6f) is not { } e) return;
                if (await QuestActions.Morale(c, e)) return;
                DataBinding<UnitData>? binding = c.GameDataStore.GetAllDataBindings<UnitData>().FirstOrDefault(b => ReferenceEquals(b.GetValue(), e));
                if (binding != null) await MoraleUtilities.ApplyShakenWithPresentation(c, binding);
            }),
            Item("Concussive Grenade", EnemyWithin(6f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Concussive Grenade", 6f) is not { } e) return;
                await QuestActions.Shove(c, h, e, 6f);
            }),
            Item("Optical Injector", Always, async (c, h) =>
            {
                GrantRule(c, h, h, "Increased Shooting Range", ELifetime.ThisRound);
                await QuestActions.Toast(c, $"{h.Name}: +6\" range this round.");
            }),
            Item("Advanced Speed Injector", Always, async (c, h) =>
            {
                GrantRule(c, h, h, "Very Fast", ELifetime.ThisRound);
                await QuestActions.Toast(c, $"{h.Name}: +4\" Advance, +8\" Rush/Charge this round.");
            }),
            Item("Psychic Kit", IsCaster, (c, h) => AddSpellTokens(c, h, 1)),
            Item("Advanced Psychic Kit", IsCaster, (c, h) => AddSpellTokens(c, h, 2)),
            Item("Frenzy Injector", Always, async (c, h) =>
            {
                QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
                q.Frenzy.Add($"{h.ID.ID}:{EnemiesDestroyed(c)}");
                GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                await QuestActions.Toast(c, $"{h.Name}: destroy an enemy unit this round to move up to 6\" afterwards.");
            }),
            Item("Adrenaline Injector", Always, async (c, h) =>
            {
                QuestProgress q = GameProgressUtilities.GetQuestProgress(c.GameDataStore);
                q.Adrenaline.Add(h.ID.ID.ToString());
                GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
                await QuestActions.Toast(c, $"{h.Name} won't get fatigued this round.");
            }),
            Item("Noise Grenade", Always, (c, h) => PlaceItemMarker(c, h, "Noise Grenade", "noise", 12f)),
            Item("Grease Grenade", Always, (c, h) => PlaceItemMarker(c, h, "Grease Grenade", "grease", 12f)),
            Item("Smoke Grenade", Always, (c, h) => PlaceItemMarker(c, h, "Smoke Grenade", "smoke", 12f)),
            Item("Fire Grenade", Always, (c, h) => PlaceItemMarker(c, h, "Fire Grenade", "fire", 12f)),
            Item("Phantasm Grenade", Always, (c, h) => PlaceItemMarker(c, h, "Phantasm Grenade", "phantasm", 6f)),
            Item("Silence Grenade", EnemyWithin(9f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Silence Grenade", 9f) is not { } e) return;
                AddUnitMarker(c, e, "silence");
                QuestActions.Grant(h, e, ERollKind.Cast, -2, ELifetime.ThisRound, c);
                await QuestActions.Toast(c, $"{e.Name} is silenced: -2 to casting rolls (marker lasts 3 rounds).");
            }),
            Item("Poison Grenade", EnemyWithin(9f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Poison Grenade", 9f) is not { } e) return;
                AddUnitMarker(c, e, "poison");
                await QuestActions.Toast(c, $"{e.Name} is poisoned: when it activates, a 4+ deals 1 wound (else the poison wears off).");
            }),
            Item("Frag Grenade", EnemyWithin(9f), async (c, h) =>
            {
                if (await OneEnemy(c, h, "Frag Grenade", 9f) is not { } e) return;
                int roll = HordePlacement.RollD6(c);
                if (roll < 4) { await QuestActions.Toast(c, $"Frag Grenade: rolled {roll} - it misses."); return; }
                int hits = Math.Min(3, e.Models.Count(m => m.GetIsAlive()));
                await QuestActions.DealHits(c, h, e, hits, 1, "Frag Grenade");
            }),
        };
    }
}
