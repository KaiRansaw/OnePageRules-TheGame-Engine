namespace FDG.StageResolution
{
    /// <summary>
    /// Reply type for stage requests where the player can cancel/back out.
    /// Stages match on the concrete subtype:
    ///   <c>Selected&lt;T&gt;</c>  — player made a choice; .Value carries it.
    ///   <c>Cancelled&lt;T&gt;</c> — player wants to back out of this decision.
    /// Non-cancellable requests should keep returning their plain reply type
    /// rather than wrapping it.
    /// </summary>
    public abstract record CancellableResult<T>;

    public sealed record Selected<T>(T Value) : CancellableResult<T>;

    public sealed record Cancelled<T> : CancellableResult<T>;

    /// <summary>
    /// Deployment-level undo: pick up the unit placed BEFORE this one and place it again. Only sent when
    /// the request offered it (<c>PlaceObjectsRequest.CanUndoUnit</c>) - Horde Mode's defender deployment.
    /// </summary>
    public sealed record UndoUnitRequested<T> : CancellableResult<T>;

    /// <summary>
    /// Deployment-level redo: put this (just-undone) unit back exactly where it was. Only sent when the
    /// request offered it (<c>PlaceObjectsRequest.CanRedoUnit</c>).
    /// </summary>
    public sealed record RedoUnitRequested<T> : CancellableResult<T>;
}
