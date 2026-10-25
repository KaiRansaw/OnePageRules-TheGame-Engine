namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that knows how many hits the attack scored (successful hit rolls,
/// before extra-hit rules). Star Quest's "if it deals at least one hit, it deals one extra hit" skills read it.
/// </summary>
public interface IHasHitsScored : ICapability
{
    public float HitsScored { get; }
}
