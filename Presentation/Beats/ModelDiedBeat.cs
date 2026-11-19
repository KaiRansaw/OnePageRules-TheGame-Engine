using System;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A single model destroyed. Its authoritative state is already dead when this beat is
    /// presented; the front-end plays a death animation at <see cref="Position"/> over
    /// <see cref="NominalDuration"/> before it stops drawing the model. The engine never removes
    /// dead models, so the front-end fully owns when the model visually disappears.
    /// </summary>
    [Serializable]
    public sealed class ModelDiedBeat : PresentationBeat
    {
        public ModelID Model { get; }
        public UnitID Unit { get; }
        public string UnitName { get; }
        public Position Position { get; }

        /// <summary>
        /// #232 casualty cascade: when true this death overlaps the next casualty beat - the presenter
        /// paces only <see cref="PresentationDurations.CasualtyStagger"/> before moving on, while the
        /// front-end still plays the full-length animation concurrently. ApplyWoundsStage sets this on
        /// every casualty of a volley except the last, so a multi-kill reads as a rapid-fire cascade
        /// with only the final death playing out on its own.
        /// </summary>
        public bool Overlap { get; }

        /// <summary>The wounds that landed on this model in the blow that killed it (Kai, 2026-11-18: the battle report
        /// totals the wounds an attack actually dealt - a Tough model can take several before it dies). 0 = not known.</summary>
        public float Wounds { get; }

        public ModelDiedBeat(ModelID model, UnitID unit, string unitName, Position position,
            bool overlap = false, float wounds = 0f)
        {
            Model = model;
            Unit = unit;
            UnitName = unitName;
            Position = position;
            Overlap = overlap;
            Wounds = wounds;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ModelDeath;
        public override bool Held => Overlap;
        public override TimeSpan HoldLeadIn => PresentationDurations.CasualtyStagger;
        public override string? Text => $"{UnitName}: a model is destroyed.";
    }
}
