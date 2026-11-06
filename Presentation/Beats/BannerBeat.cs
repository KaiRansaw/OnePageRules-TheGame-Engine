using System;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// How loudly a <see cref="BannerBeat"/> announces itself (#275). One banner size for everything
    /// meant every announcement stopped the game dead, which made the big ones cheap and discouraged
    /// announcing anything else at all. The tier drives size, sound, and -- via <see cref="PresentationBeat.Held"/>
    /// -- whether the engine actually waits for it. Serializes so a networked client tiers identically.
    /// </summary>
    public enum EBannerTier
    {
        /// <summary>
        /// The game's structural spine -- phase changes, the round number, the result. Big centered
        /// letters, and the only tier that stops play for its full duration. Rare by design: default
        /// zero so a beat that somehow arrives without a tier reads as the pre-#275 behavior.
        /// </summary>
        Headline = 0,

        /// <summary>
        /// Something the player should read but that shouldn't halt the game -- a roll-off result, a
        /// cast, a failed morale test. Mid-size, its own band below the headline band. Paces a short
        /// lead-in and then lets play continue while it finishes on screen.
        /// </summary>
        Notice = 1,

        /// <summary>
        /// A routine happening worth surfacing but not worth a pause -- an embark, a spell assist, a
        /// unit held in reserve. Small, stacks in the top ticker, and paces nothing at all: it plays
        /// entirely concurrently with whatever beat comes next.
        /// </summary>
        Toast = 2,
    }

    /// <summary>
    /// An on-screen announcement (e.g. "Round 3", "Deployment", a winner). Emitted manually via
    /// <c>context.Announce(...)</c> — never automatically per stage — which also writes the same text to
    /// the regular log. How prominently the front-end flashes it, and whether the engine waits for it,
    /// come from <see cref="Tier"/>.
    /// </summary>
    /// <summary>
    /// A word in a battle-report line that explains itself on hover (Kai's Battle Report rework, #464):
    /// an item's name and what it does, a random event's name and its effect. Unlike
    /// <see cref="PresentationBeat.ReferencedRules"/>, whose hover is looked up from the books, the text
    /// travels with the beat - items and events are not special rules.
    /// </summary>
    [Serializable]
    public sealed record ReportTerm(string Name, string Text);

    [Serializable]
    public sealed class BannerBeat : PresentationBeat
    {
        public string BannerText { get; }
        public TextColor Color { get; }

        /// <summary>Prominence of this announcement (#275) — size, sound, and whether play pauses for it.</summary>
        public EBannerTier Tier { get; }

        /// <inheritdoc cref="PresentationBeat.ReferencedRules"/>
        public override IReadOnlyList<string> ReferencedRules { get; }

        /// <summary>#464: words in the text with their own hover (items, events). See <see cref="ReportTerm"/>.</summary>
        public IReadOnlyList<ReportTerm> Terms { get; }

        /// <summary>
        /// #464: what the line's hover shows - the rolls behind it, what caused it, the models lost. The
        /// text itself says what HAPPENED in plain words (Kai, QA 3.19); the mechanism lives here.
        /// </summary>
        public IReadOnlyList<string> Details { get; }

        public BannerBeat(string bannerText, TextColor color, EBannerTier tier = EBannerTier.Headline,
            IReadOnlyList<string>? referencedRules = null, IReadOnlyList<ReportTerm>? terms = null,
            IReadOnlyList<string>? details = null)
        {
            BannerText = bannerText;
            Color = color;
            Tier = tier;
            ReferencedRules = referencedRules ?? Array.Empty<string>();
            Terms = terms ?? Array.Empty<ReportTerm>();
            Details = details ?? Array.Empty<string>();
        }

        /// <summary>
        /// Everything below <see cref="EBannerTier.Headline"/> is held: it transfers to the front-end's
        /// own concurrent track the frame it is dequeued, so the next beat starts immediately and the
        /// announcement finishes over the top of it.
        /// </summary>
        public override bool Held => Tier != EBannerTier.Headline;

        /// <summary>
        /// A Notice buys a brief hitch — long enough to register that something was said — before play
        /// resumes. A Toast buys none: it is additive, never interruptive.
        /// </summary>
        public override TimeSpan HoldLeadIn => Tier switch
        {
            EBannerTier.Notice => PresentationDurations.BannerNoticeLeadIn,
            EBannerTier.Toast  => TimeSpan.Zero,
            _                  => NominalDuration,
        };

        public override TimeSpan NominalDuration => Tier switch
        {
            EBannerTier.Notice => PresentationDurations.BannerNotice,
            EBannerTier.Toast  => PresentationDurations.BannerToast,
            _                  => PresentationDurations.Banner,
        };

        public override string? Text => BannerText;
    }
}
