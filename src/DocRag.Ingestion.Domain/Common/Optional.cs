namespace DocRag.Ingestion.Domain.Common;

/// <summary>
/// Distinguishes "the caller did not mention the field" from "the caller explicitly set it to
/// <see langword="null"/>", which JSON Merge Patch (RFC 7396) treats differently.
/// </summary>
/// <typeparam name="T">Wrapped value type.</typeparam>
/// <param name="IsSet">Whether the field was present in the patch.</param>
/// <param name="Value">The provided value; meaningful only when <paramref name="IsSet"/> is true.</param>
public readonly record struct Optional<T>(bool IsSet, T? Value)
{
    /// <summary>Gets the "field omitted" value.</summary>
    public static Optional<T> Unset => default;

    /// <summary>Wraps an explicitly provided value (possibly <see langword="null"/>).</summary>
    /// <param name="value">The provided value.</param>
    /// <returns>A set optional.</returns>
    public static Optional<T> Of(T? value) => new(true, value);
}
