using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Options;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Application.Idempotency;

/// <summary>What to do after consulting the idempotency store.</summary>
/// <param name="Request">The reservation request, or <see langword="null"/> when the client sent no key.</param>
/// <param name="ReplayResourceId">Resource produced by the original request, when this is a replay.</param>
/// <param name="Failure">A <c>409</c> failure when the key cannot be used.</param>
public sealed record IdempotencyDecision(IdempotencyRequest? Request, Guid? ReplayResourceId, Failure? Failure)
{
    /// <summary>Gets a value indicating whether the original result must be returned instead of executing again.</summary>
    public bool IsReplay => ReplayResourceId.HasValue;
}

/// <summary>Applies the shared idempotency decision rules in front of every creating operation.</summary>
public sealed class IdempotencyGate(IIdempotencyStore store, IClock clock, IOptions<UploadOptions> options)
{
    /// <summary>Builds the request descriptor for a key; <see langword="null"/> when no key was sent.</summary>
    /// <param name="operation">Operation scope including the target resource.</param>
    /// <param name="key">Client key, if any.</param>
    /// <param name="fingerprint">Request fingerprint when known upfront.</param>
    /// <param name="ttl">Reservation lifetime; defaults to the configured record TTL.</param>
    /// <returns>The descriptor or <see langword="null"/>.</returns>
    public IdempotencyRequest? Describe(string operation, string? key, string? fingerprint, TimeSpan? ttl = null) =>
        string.IsNullOrEmpty(key)
            ? null
            : new IdempotencyRequest(
                operation,
                key,
                fingerprint,
                clock.UtcNow,
                clock.UtcNow + (ttl ?? TimeSpan.FromHours(options.Value.IdempotencyTtlHours)),
                clock.UtcNow + TimeSpan.FromHours(options.Value.IdempotencyTtlHours));

    /// <summary>Reserves the key or classifies the existing record. Must run inside the caller's transaction.</summary>
    /// <param name="request">The descriptor, or <see langword="null"/> to proceed without idempotency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision.</returns>
    public async Task<IdempotencyDecision> EnterAsync(IdempotencyRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return new IdempotencyDecision(null, null, null);
        }

        var reservation = await store.TryReserveAsync(request, cancellationToken);
        if (reservation.Reserved)
        {
            return new IdempotencyDecision(request, null, null);
        }

        var existing = reservation.Existing!;
        if (!existing.Completed)
        {
            return new IdempotencyDecision(
                request, null, new Failure("409-idempotency-key-in-progress", "A request with this Idempotency-Key is still being processed."));
        }

        if (request.Fingerprint is not null && existing.Fingerprint != request.Fingerprint)
        {
            return new IdempotencyDecision(request, null, KeyReused());
        }

        return new IdempotencyDecision(request, existing.ResourceId, null);
    }

    /// <summary>Builds the failure used when a key is reused with different content.</summary>
    /// <returns>A <c>409</c> failure.</returns>
    public static Failure KeyReused() =>
        new("409-idempotency-key-reused", "This Idempotency-Key was already used with a different request.");
}
