using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using Newtonsoft.Json;
namespace FDG
{
    public class UnitData : IUnit
    {
        public UnitID ID { get; private set; }

        [JsonProperty] private TokenContainer _tokens = new TokenContainer();

        [JsonIgnore] public ITokenContainer Tokens => _tokens;

        public PlayerID PlayerID { get; private set; }

        public string Name { get; set; }

        public int Quality { get; set; }

        public int Defense { get; set; }

        // #329: what this unit cost in its army list, carried into the game so the in-game army list
        // can print it the way the printed list does. Display-only — no rule reads it. A joined
        // hero's cost folds into its host at merge (HeroJoinResolver, mirroring the Forge's combined
        // cost); rule-spawned auxiliaries cost 0 by their spec. Public settable, so it rides the
        // store to clients and survives saves; a pre-#329 save reads back 0 and the display hides it.
        public int PointCost { get; set; }

        /// <summary>
        /// Horde Mode shop: the army-builder choices this unit was bought or last upgraded with (a
        /// <see cref="ArmyBuilding.HordeLoadout"/> as JSON), so the next shop opens its upgrade editor
        /// exactly where it left off. Null for units fielded from the starting list.
        /// </summary>
        public string? HordeLoadoutJson { get; set; }

        /// <summary>Star Quest: this hero's sheet (a <see cref="FDG.Quest.QuestHero"/> as JSON), or null for any
        /// other unit. Its XP / level / stats change during the mission and are written back to the hero file
        /// afterwards.</summary>
        public string? QuestHeroJson { get; set; }

        /// <summary>True for a Star Quest hero.</summary>
        [JsonIgnore] public bool IsQuestHero => QuestHeroJson != null;

        /// <summary>GF: Firefight Campaign: this unit's strike team sheet (a <see cref="FDG.Campaign.StrikeUnit"/> as
        /// JSON), or null. Kills are counted on it during the game; it is written back to the team file afterwards.</summary>
        public string? StrikeUnitJson { get; set; }

        /// <summary>
        /// #454 - what a renamed campaign unit IS in its book ("Assault Grunts" under "Vargas Squad"), or null
        /// when the unit's name already is its type, or it is not a campaign unit. Kai, 2026-11-05: "I need to
        /// know which unit type the unit is." Read from the campaign sheet the unit carries onto the table.
        /// <para>
        /// Cached against the sheet's JSON string, because the hover card and the table labels ask every
        /// frame and the sheet only changes when a kill or an event is recorded on it.
        /// </para>
        /// </summary>
        [JsonIgnore]
        public string? CampaignTypeIfRenamed
        {
            get
            {
                string? json = StrikeUnitJson;
                if (!ReferenceEquals(json, _typeForJson))
                {
                    _typeForJson = json;
                    _typeCache = FDG.Campaign.StrikeUnit.FromJson(json)?.UnitType;
                }
                return string.IsNullOrWhiteSpace(_typeCache)
                       || string.Equals(_typeCache, Name, System.StringComparison.OrdinalIgnoreCase)
                    ? null : _typeCache;
            }
        }

        private string? _typeForJson;
        private string? _typeCache;

        /// <summary>Star Quest: the hero's current power (it starts the mission full).</summary>
        public int QuestPower { get; set; }

        /// <summary>Star Quest: "+X to the next stat roll" waiting to be used (Concentrate, Guidance...).</summary>
        public int QuestStatBonus { get; set; }

        /// <summary>Star Quest: once-per-game class features already used this mission.</summary>
        public List<string> QuestFeatsUsed { get; set; } = new();

        // #029/#150: an Aircraft's fixed flight heading is no longer a unit field — it lives on the models'
        // per-model IModel.Facing (set once toward the table centre, never turned), read back via
        // ForcedAircraftMove.EnsureHeading and gated by the AircraftHeadingSet token.

        private readonly List<ResolvedRule> _ruleDefinitions = new();

        [JsonIgnore] public IReadOnlyList<ResolvedRule> RuleDefinitions => _ruleDefinitions;

        // #095: RuleDefinitions is [JsonIgnore] (the resolved rule graph can't round-trip through the
        // Newtonsoft store/save layer), so its persisted form is this STJ blob, kept current in
        // AttachRuleDefinition and replayed by RehydrateRules on load. See RuleAttachmentPersistence.
        [JsonProperty] private string? _ruleDefinitionsJson;

        // #006: set when a Hero has joined this unit. The hero's model is merged into ModelBindings;
        // this carries the divergent facts (which model, the hero's own Quality/Defense). Serialized so
        // the merge survives save/network round-trips alongside the merged ModelBindings.
        [JsonProperty] private HeroAttachment? _heroAttachment;

        [JsonIgnore] public HeroAttachment? HeroAttachment => _heroAttachment;

        [JsonIgnore] public ModelID? JoinedHeroModelId => _heroAttachment?.HeroModelId;

        /// <summary>
        /// #456 - this unit's leader under the Fast-Play rules: "Before the game begins, you must clearly
        /// define a single model within each unit to be its leader, and which will remain the unit's leader
        /// until the end of the game. Since the leader is essential, it always has to be the last model that
        /// is removed when the unit takes wounds." Null when Fast-Play is not in use - every rule that reads
        /// it treats null as "no leader", which is the ordinary game.
        /// <para>
        /// A model ID rather than an index, like <see cref="JoinedHeroModelId"/>, so it stays valid as other
        /// models die. Serialized, so a saved or networked game keeps the same leader.
        /// </para>
        /// </summary>
        [JsonProperty] public ModelID? LeaderModelId { get; set; }

        public List<DataBinding<ModelData>> ModelBindings;

        [JsonIgnore]
        public float MaxWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds;
                }
                return total;
            }
        }

        [JsonIgnore]
        public float RemainingWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds - model.WoundsDealt;
                }
                return total;
            }
        }

        public event DataValueChangedHandler<float>? OnWoundsDealt;

        // #081: a fresh, pre-sized list per access - down from a Select/Cast/ToList chain (three
        // enumerators + a growing list). Deliberately NOT cached: bindings resolve through the store
        // per call, and #190's networked token sync re-Sets whole ModelData instances, so a cached
        // resolution would serve stale objects on clients (and ModelBindings is externally mutable -
        // the hero join adds to it).
        [JsonIgnore]
        public List<IModel> Models
        {
            get
            {
                var models = new List<IModel>(ModelBindings.Count);
                foreach (DataBinding<ModelData> binding in ModelBindings)
                {
                    models.Add(binding.GetValue());
                }
                return models;
            }
        }

        [JsonConstructor]
        public UnitData(PlayerID playerID, string name, int quality, int defense,
            List<DataBinding<ModelData>> modelBindings,
            UnitID? id = null)
        {
            ID = id ?? new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

            ModelBindings = modelBindings;
        }

        /// <summary>
        /// #396: the whole-store clone's copy (<see cref="FDG.SaveLoad.StoreClone"/>) - the JSON
        /// constructor plus <see cref="RehydrateRules"/>, built directly from the live unit. Like the
        /// JSON path it wires NO wound subscriptions (the clone rewires them once every model exists,
        /// via <see cref="RewireModelWoundSubscriptions"/>) and carries no <see cref="OnWoundsDealt"/>
        /// subscribers.
        /// </summary>
        internal UnitData(UnitData source, List<DataBinding<ModelData>> modelBindings)
        {
            ID = source.ID;
            _tokens = source._tokens.Clone();
            PlayerID = source.PlayerID;
            Name = source.Name;
            Quality = source.Quality;
            Defense = source.Defense;
            PointCost = source.PointCost;
            HordeLoadoutJson = source.HordeLoadoutJson;
            QuestHeroJson = source.QuestHeroJson;
            StrikeUnitJson = source.StrikeUnitJson;
            QuestPower = source.QuestPower;
            QuestStatBonus = source.QuestStatBonus;
            QuestFeatsUsed = source.QuestFeatsUsed.ToList();
            _ruleDefinitions.AddRange(source._ruleDefinitions);
            _ruleDefinitionsJson = source._ruleDefinitionsJson;
            _heroAttachment = source._heroAttachment?.Clone();
            ModelBindings = modelBindings;
        }

        public UnitData(IUnitTemplate unitToCopy, List<DataReference> modelReferences, IReadWriteableGameDataStore gameDataStore)
        {
            ID = new UnitID(System.Guid.NewGuid());

            PlayerID = unitToCopy.PlayerID;
            Name = unitToCopy.Name;
            Quality = unitToCopy.Quality;
            Defense = unitToCopy.Defense;

            ModelBindings = new List<DataBinding<ModelData>>();
            foreach (DataReference model in modelReferences)
            {
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(model);
                ModelBindings.Add(modelBinding);
                ((IModel)modelBinding.GetValue()).OnWoundsDealt += OnModelWoundsDealt;
            }
        }

        public UnitData(PlayerID playerID, UnitFileEntry unitFileEntry, IReadWriteableGameDataStore gameDataStore,
            IRuleResolver? ruleResolver = null,
            string? defaultRangedEffectSet = null, string? defaultMeleeEffectSet = null)
        {
            ID = new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = unitFileEntry.Name;
            Quality = unitFileEntry.Quality;
            Defense = unitFileEntry.Defense;
            PointCost = unitFileEntry.PointCost;
            QuestHeroJson = unitFileEntry.QuestHeroJson;
            StrikeUnitJson = unitFileEntry.StrikeUnitJson;

            ModelBindings = new List<DataBinding<ModelData>>(unitFileEntry.ModelCount);

            List<Weapon> weapons = new List<Weapon>();

            // #392: sort a COPY - the entry belongs to the caller's ArmyListFile, and sorting it in
            // place made army creation mutate its input. Two concurrent games built from one shared
            // file (the FdgLab bench) raced this sort against each other's enumeration and captured
            // different weapon orders, which feeds resolution order and dice consumption - outcomes
            // then depended on which games happened to overlap. Idempotent, so purely sequential
            // reuse never showed it.
            List<WeaponFileEntry> sortedWeaponEntries = new List<WeaponFileEntry>(unitFileEntry.Weapons);
            sortedWeaponEntries.Sort((x, y) => x.Quantity.CompareTo(y.Quantity));

            //Distribute weapons in order of quantity, should be a decent approximate of how they're
            //actually distributed.
            List<Weapon> unitWeapons = new List<Weapon>();

            // #412: rules written onto a weapon whose definition is about the MODEL get collected here
            // and attached to the unit once the weapons are built, rather than being dropped as a
            // scope mismatch. See ResolveWeaponRules.
            List<ResolvedRule> promotedToUnit = new List<ResolvedRule>();
            for (int i = 0; i < sortedWeaponEntries.Count; i++)
            {
                WeaponFileEntry weaponEntry = sortedWeaponEntries[i];

                //Resolved once per entry and shared across the quantity's instances —
                //ResolvedRule is immutable, so duplicates of the same weapon can share it.
                List<ResolvedRule> weaponRuleDefinitions =
                    ResolveWeaponRules(weaponEntry, ruleResolver, promotedToUnit);

                // #239: the entry's explicit effect-set key, else the army default for the weapon's
                // kind. Resolved once here so beat emission just reads Weapon.EffectKey.
                string? effectKey = weaponEntry.EffectSet
                    ?? (weaponEntry.RangeInches > 0 ? defaultRangedEffectSet : defaultMeleeEffectSet);

                for (int q = 0; q < weaponEntry.Quantity; q++)
                {
                    Weapon weapon = new Weapon(weaponEntry.Name, weaponEntry.RangeInches, weaponEntry.Attacks,
                            weaponEntry.ArmorPenetration, effectKey);

                    foreach (ResolvedRule rule in weaponRuleDefinitions)
                    {
                        weapon.AttachRuleDefinition(rule);
                    }

                    unitWeapons.Add(weapon);
                }
            }

            foreach (ResolvedRule promoted in promotedToUnit)
            {
                AttachRuleDefinition(promoted);
            }

            // The unit's base shape (#149), authored per-unit in the army file; all its models share it.
            // Immutable, so one instance is safely shared. Null (e.g. "base": null) → the default circle.
            IBaseShape baseShape = (unitFileEntry.Base ?? new SaveLoad.BaseFileEntry()).ToBaseShape();

            for (int i = 0; i < unitFileEntry.ModelCount; i++)
            {
                List<Weapon> modelWeapons = new List<Weapon>();

                for (int w = i; w < unitWeapons.Count; w += unitFileEntry.ModelCount)
                {
                    modelWeapons.Add(unitWeapons[w]);
                }

                ModelData modelData = new ModelData(baseShape, modelWeapons, new Position(), gameDataStore);
                if (unitFileEntry.ModelLooks is { } looks && i < looks.Count && !string.IsNullOrEmpty(looks[i]))
                    modelData.Look = looks[i];

                DataReference modelReference = gameDataStore.Create(modelData);
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(modelReference);
                ModelBindings.Add(modelBinding);
            }
        }

        /// <summary>
        /// Resolves a weapon file entry's special-rule names into attachable #042 definitions
        /// (#027). Scope-enforced: a unit-scoped rule named on a weapon is skipped with a
        /// warning. A null resolver (e.g. template-copy construction paths that never see an
        /// army file) yields no rules.
        /// </summary>
        /// <summary>
        /// Resolves a weapon entry's rule names. Weapon-scoped ones come back to be attached to the
        /// weapon; UNIT-scoped ones are added to <paramref name="promotedToUnit"/> for the caller to
        /// attach to the unit instead.
        /// <para>
        /// #412: the promotion is the mirror of the unit-level wargear re-homing #197 slice 0 added
        /// (a weapon rule named at unit level is spread across the unit's weapons). OPR routinely
        /// writes a rule about the MODEL onto a piece of its gear - Spell Amplifier on a magic staff,
        /// Brace(X) on a spear - and a weapon's rules are only consulted when that weapon is the one
        /// in the attack context. A defender bracing against a charge, or a hero casting a spell, is
        /// never that, so before this those rules resolved, validated, and then silently never fired.
        /// </para>
        /// </summary>
        private List<ResolvedRule> ResolveWeaponRules(WeaponFileEntry weaponEntry, IRuleResolver? ruleResolver,
            List<ResolvedRule> promotedToUnit)
        {
            List<ResolvedRule> resolved = new List<ResolvedRule>();

            if (ruleResolver == null)
            {
                return resolved;
            }

            foreach (SpecialRuleEntry ruleEntry in weaponEntry.SpecialRules)
            {
                ResolvedRule? rule = ArmyListRuleResolution.ResolveAnyScope(
                    ruleResolver, ruleEntry, $"weapon '{weaponEntry.Name}' of unit '{Name}'");

                if (rule == null)
                {
                    continue;
                }

                if (rule.Definition.Scope == ERuleScope.Unit)
                {
                    // One copy per unit however many weapons or duplicates name it: ResolvedRule is
                    // immutable and the dispatcher dedups by definition and arguments anyway, so a
                    // squad of ten spears must not grant Brace ten times over.
                    bool alreadyPromoted = promotedToUnit.Exists(existing =>
                        existing.Definition == rule.Definition
                        && existing.Arguments.Count == rule.Arguments.Count
                        && !existing.Arguments.Where((a, i) => !a.Equals(rule.Arguments[i])).Any());

                    if (!alreadyPromoted)
                    {
                        promotedToUnit.Add(rule);
                    }

                    continue;
                }

                resolved.Add(rule);
            }

            return resolved;
        }


        /// <summary>
        /// Re-subscribes this unit's wound aggregation to its models' <see cref="IModel.OnWoundsDealt"/>.
        /// The <see cref="JsonConstructorAttribute"/> path deliberately does NOT do this: it must stay
        /// order-independent (a model may not be deserialized yet when the unit is, e.g. during network
        /// full-sync), so it never calls <c>GetValue()</c>. Call this once after a full save/load replay,
        /// when every referenced model is guaranteed present. Idempotent — removes any existing handler
        /// before re-adding.
        /// </summary>
        public void RewireModelWoundSubscriptions()
        {
            foreach (DataBinding<ModelData> modelBinding in ModelBindings)
            {
                IModel model = modelBinding.GetValue();
                model.OnWoundsDealt -= OnModelWoundsDealt;
                model.OnWoundsDealt += OnModelWoundsDealt;
            }
        }

        private void OnModelWoundsDealt(float oldWoundsCount, float newWoundsCount)
        {
            //This is called with an individual models' old and new wound count.
            //So to invoke the same for the unit, we get the new wound count, and do math
            //to find out the old.
            float woundsDealt = oldWoundsCount - newWoundsCount;

            // #263 backstop: no attack path should ever reach a unit that is off the table — wounds
            // landing on one mean a targeting filter upstream is missing (the class of bug where an
            // Ambush reserve's origin-sitting models got charged in round 1). Checked via the
            // off-table TOKENS, not GetIsOnBattlefield(): a unit whose last model just died reads as
            // off-battlefield by position and would warn on every ordinary kill. Warn, don't throw —
            // a live game should degrade loudly, not crash.
            if (woundsDealt > 0f
                && (ReserveRules.IsInReserve(this)
                    || Tokens.HasToken(TokenType.EmbarkedIn)
                    || Tokens.HasToken(TokenType.OffTableFromForcedMove)))
            {
                RuleDiagnostics.WarnOnce($"off-table-wounds-{ID}",
                    $"{Name} took {woundsDealt} wound(s) while off the battlefield (reserve/embarked/off-table). " +
                    "No attack should reach it there - this is an engine targeting bug.");
            }

            float newUnitTotalWounds = RemainingWounds;
            float oldUnitTotalWounds = newUnitTotalWounds + woundsDealt;

            OnWoundsDealt?.Invoke(oldUnitTotalWounds, newUnitTotalWounds);
        }

        /// <summary>
        /// Attaches a resolved special-rule definition to this unit. Attachment is
        /// post-construction (army-load / harness) rather than a constructor param so
        /// the existing constructors stay untouched.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule)
        {
            _ruleDefinitions.Add(rule);
            _ruleDefinitionsJson = RuleAttachmentPersistence.Serialize(_ruleDefinitions);
        }

        /// <summary>
        /// Horde Mode shop upgrades: swaps this unit's unit-level rules for a new set (the upgraded
        /// loadout's), keeping the persisted form in step.
        /// </summary>
        public void ReplaceRuleDefinitions(IEnumerable<ResolvedRule> rules)
        {
            _ruleDefinitions.Clear();
            _ruleDefinitions.AddRange(rules);
            _ruleDefinitionsJson = RuleAttachmentPersistence.Serialize(_ruleDefinitions);
        }

        /// <summary>
        /// #095: replays the persisted rule blob back onto <see cref="RuleDefinitions"/> after a load,
        /// so a resumed game carries the same rules it was saved with. Idempotent and a no-op unless the
        /// live list is empty (a freshly deserialized carrier), so it never double-attaches.
        /// </summary>
        public void RehydrateRules()
        {
            if (_ruleDefinitions.Count == 0 && !string.IsNullOrEmpty(_ruleDefinitionsJson))
            {
                _ruleDefinitions.AddRange(RuleAttachmentPersistence.Deserialize(_ruleDefinitionsJson));
            }
        }

        // A unit (or model) that crossed a network hop arrived with an empty rule list and only the blob:
        // the catch-up snapshot rehydrates (StoreReplay), but every later single update did not, so on a
        // client a unit lost all its special rules the first time it moved or took a wound - the card went
        // blank, Fast stopped counting, rule hovers vanished (network play-through, 2026-11-06). Same fix
        // the weapon got in #325: restore the invariant at the deserialization boundary.
        [System.Runtime.Serialization.OnDeserialized]
        private void OnDeserializedRehydrate(System.Runtime.Serialization.StreamingContext context)
            => RehydrateRules();

        /// <summary>
        /// Merges a Hero into this unit (#006): adds the hero's model binding(s) to this unit's model
        /// list (wiring wound aggregation), and records the <see cref="HeroAttachment"/>. After this the
        /// combined unit is one <see cref="IUnit"/> for all per-unit machinery; only the stat-divergence
        /// seams read <see cref="HeroAttachment"/>. The hero's standalone unit is discarded by the caller.
        /// </summary>
        public void AttachHero(HeroAttachment heroAttachment, IReadOnlyList<DataBinding<ModelData>> heroModelBindings)
        {
            // Add only — deliberately no OnWoundsDealt subscription here. Fresh-game units don't subscribe
            // their rank and file at construction (only the save/load path wires them, via
            // RewireModelWoundSubscriptions, which now sees the merged hero binding too); subscribing only
            // the hero here would make the unit's wound aggregation fire asymmetrically.
            foreach (DataBinding<ModelData> modelBinding in heroModelBindings)
            {
                ModelBindings.Add(modelBinding);
            }

            _heroAttachment = heroAttachment;
        }

        /// <summary> True if a Hero has joined this unit. </summary>
        [JsonIgnore] public bool HasHero => _heroAttachment != null;

        /// <summary>
        /// The joined hero's model, or null if no hero has joined (or its model is gone). Identity is by
        /// <see cref="HeroAttachment.HeroModelId"/>, stable across the model list as models die.
        /// </summary>
        public IModel? GetHeroModel()
        {
            if (_heroAttachment == null)
            {
                return null;
            }

            return Models.FirstOrDefault(model => model.ID == _heroAttachment.HeroModelId);
        }

        public bool GetMobility(out float moveShootDistanceInches, out float chargeDistanceInches)
        {
            //TODO: Process special rules for this.
            moveShootDistanceInches = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES;
            chargeDistanceInches = GameWideConstants.CHARGE_DISTANCE_INCHES;

            return true;
        }


    }
}
