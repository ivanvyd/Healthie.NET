namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// A stored state together with the version it was read at.
/// </summary>
/// <remarks>
/// <para>
/// The version is what makes a later write conditional: hand it back to
/// <see cref="IStateProvider.TrySetStateAsync"/> and the write only lands if nothing else has
/// changed the state since. It is the same shape Orleans gives grain state, an ETag alongside the
/// value, and the same thing an <c>If-Match</c> header carries in the Azure SDKs.
/// </para>
/// <para>
/// Opaque on purpose. One provider's version is a row's transaction id, another's is a document
/// ETag, another's is a counter -- nothing outside the provider that produced it should read
/// meaning into it, or compare two of them for anything but equality.
/// </para>
/// </remarks>
/// <typeparam name="TState">The type of the stored state.</typeparam>
/// <param name="Value">The stored state.</param>
/// <param name="Version">
/// The version it was read at, or <c>null</c> from a provider that does not version its writes.
/// </param>
public sealed record StateEntry<TState>(TState Value, string? Version)
{
    /// <summary>
    /// Whether this entry can be used for a conditional write.
    /// </summary>
    /// <remarks>
    /// False when the provider does not version. Writing such an entry back conditionally would be
    /// asking for a guarantee nothing can give, so it is worth being able to tell.
    /// </remarks>
    public bool IsVersioned => Version is not null;
}
