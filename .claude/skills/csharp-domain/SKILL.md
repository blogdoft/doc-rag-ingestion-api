---
name: csharp-domain
description: Coding rules for the C# domain/core layer — entities, value objects, invariants, immutability, Result-pattern usage instead of exceptions for expected failures, and avoiding an anemic domain model. Use this whenever writing, reviewing, or editing code in a Core/Domain project: a new entity or value object, a factory/`Create` method, an invariant check, a domain event, or any type that must have zero dependency on I/O or delivery-mechanism concerns — even for something as small as "add a property to this entity" or "create a value object for X".
---

# C# domain-layer coding standards

Rules for the innermost layer: the domain model. This layer has no dependency on any delivery
mechanism or I/O technology — no HTTP types, no Dapper, no DI container attributes, no logging
framework calls. If a piece of code needs one of those, it doesn't belong here.

## Building Entities

- A domain entity isn't necessarily the same thing as an ORM's notion of an entity. Here, an entity
  is an object with a unique identity that legitimately changes state over time — that's what
  distinguishes it from a value object, whose identity *is* its value (see Immutability below for
  how that state change is controlled).
- Build entities through a Builder or Factory class rather than a public parameterized constructor
  — this is what makes it structurally impossible to end up with a half-valid instance, since every
  required validation rule runs before the entity exists.
  - The builder should — preferably — follow a fluent interface, for a more readable and expressive
    way to construct complex entities.
  - Use FluentValidation to express the rules the builder/factory enforces, and surface the outcome
    as a `Result<T>` (see Result pattern over exceptions, below) from the builder/factory's
    `Build()`/`Create()` method — a rule violation is something the caller branches on, not an
    exception.

## Invariants

- An entity or value object must never be constructible into an invalid state. Enforce invariants
  inside the builder/factory that constructs it (see Building Entities, above), not in a separate
  "validate" step that callers might forget to call.
- Once instantiated, an object should never need to be re-validated by its callers — if you find
  code re-checking something the builder/factory already guaranteed, that's a sign the guarantee
  isn't actually being enforced where it should be.

## Immutability

- Prefer `record` types for value objects — value equality and immutability come for free, which
  is exactly the semantics a value object needs.
  - When is applicable, use implicit operator overloads to convert between a value object and its underlying primitive type (e.g. `public static implicit operator string(MyValueObject vo) => vo.Value;`), so the value object can be used interchangeably with the primitive in most places without losing the invariant enforcement.
- Entities are allowed to be mutable — that's the point of Building Entities, above: their state
  legitimately changes over their lifecycle. But that mutation must go through explicit methods
  that enforce which transitions are legal, never a public property setter anyone can assign from
  outside — expose entity properties as `get`-only (or `private set`) to callers.

## Result pattern over exceptions

- Use `BlogDoFT.Libs.ResultPattern` for failures that are an expected, named outcome of a business
  operation (a validation rule failed, a state transition isn't allowed, a value fell outside its
  valid range). The caller is expected to branch on these, so they should be visible in the return
  type, not hidden behind a `catch`.
- Reserve `throw` for programmer errors and truly exceptional conditions the caller has no
  reasonable way to anticipate or recover from (a broken invariant that should have been
  impossible, a null that a contract guarantees can't happen). If you're about to `throw` for
  something a caller would plausibly want to check with an `if` first, it should be a `Result`
  failure instead.

## Avoid an anemic domain model

- If an entity has data that a rule operates on, the rule belongs on the entity (or a domain
  service, for rules that span multiple entities/aggregates) — not on an application-layer class
  that pulls the data out, does the logic externally, and writes it back in. A domain type that is
  only ever read from and written to, with all its behavior living elsewhere, has lost the point of
  being a domain type.
- A quick self-check while reviewing: if every property on a class has a public setter and no
  method does anything but assign fields, that's anemic — behavior has leaked out to whoever calls
  it, and callers will inevitably diverge on how they enforce the entity's rules.

## Domain events (where used)

- Model a domain event as a plain immutable data record describing what happened, not as
  something that knows how to dispatch itself. The entity raises/records the event (e.g. appends
  to an internal list the entity exposes read-only); the Application layer is responsible for
  collecting and dispatching it after the use case completes. The domain layer never depends on a
  dispatch/messaging mechanism.
