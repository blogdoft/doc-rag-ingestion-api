---
name: csharp-unit-test
description: Coding rules for C# unit tests written with xUnit, Shouldly, NSubstitute, and Bogus — naming convention, Given-When-Then structure, one assertion focus per test, test-data generation, and where to mock vs. exercise real code. Use this whenever writing, reviewing, or editing an xUnit test class or method, a test fixture, a mock/substitute setup, or test data builders — even for something as small as "add a test for this edge case" or "write tests for this repository".
---

# C# unit test coding standards

Rules for tests in this stack: **xUnit** (framework), **Shouldly** (assertions), **NSubstitute**
(mocking), **Bogus** (test data). Consistency across test files matters more here than almost
anywhere else in the codebase, since tests are read far more often than they're written, usually
while someone is trying to understand *why* something broke.

## Naming

- Name test methods `Should_ExpectedResult_When_Scenario` (e.g.
  `Should_ReturnFailure_When_NameIsNullOrEmpty`). Pick this convention and use it uniformly across the
  project — a mixed bag of naming styles makes it harder to scan a test class and know what's
  covered at a glance.
- When a class under test has only one public method, create a single test class named after it
  with the suffix "Tests". E.g.: `DocumentProcessor` -> `DocumentProcessorTests`.
- When a class under test has multiple public methods, create a `<class-name>Tests` folder. Inside
  it, create an abstract base class `Base<class-name>Tests` plus one test class per public method,
  each suffixed "Tests". E.g.: `DocumentProcessor` -> `DocumentProcessorTests` folder ->
  `BaseDocumentProcessorTests` abstract class -> `ProcessDocumentTests` class.
  - The abstract class can hold common setup code, reusable test data builders, and shared helpers.
- Whichever shape applies, the test class name must trace back unambiguously to the type (and, for
  the per-method shape, the method) under test — never a generic catch-all test class shared across
  multiple unrelated types.

## Structure

- Given / When / Then, in that order, with a blank line between each section so the shape is
  visible without reading closely (add comments specifying the section if you want, but the blank
  line is the real visual cue).
- No branching or loops inside a test (`if`, `for`, `foreach` over assertions). If a test needs a
  loop to check multiple inputs, that's what `[Theory]` + `[InlineData]`/`[MemberData]` is for —
  the framework's parameterization, not hand-rolled iteration.
- One behavior under test per test method. If the test name would need "and" to describe what it
  checks, split it into separate tests — each failure should point at exactly one broken behavior.

## Assertions

- Use Shouldly (`result.ShouldBe(expected)`, `action.ShouldThrow<T>()`, `collection.ShouldContain(x)`)
  instead of `Assert.*` — the fluent form reads closer to the requirement it's checking and gives a
  clearer failure message.
- For code that returns `Result`/`Result<T>` (`BlogDoFT.Libs.ResultPattern`), assert on both the
  success/failure flag *and* the meaningful payload — `result.IsSuccess.ShouldBeTrue()` alone
  without also asserting on the value, or `result.IsFailure.ShouldBeTrue()` without asserting on
  the error content, leaves the actual behavior unverified.

## Test data

- Build test data with a `Faker<T>` (Bogus) instead of hand-writing repetitive object literals
  full of magic strings/numbers — one Faker builder per type that needs realistic variation, reused
  across the tests that need instances of it.
- Only override the specific field(s) a given test cares about from the Faker default; leave the
  rest to the generator. If a test hardcodes every field, it's not using Bogus for anything and a
  plain literal would be more honest about what actually matters to that test.
- Seed the Faker (or otherwise make data generation deterministic) for any test whose assertion
  depends on the exact generated value, so a re-run can't flip a passing test to failing.

## Mocking boundaries

- Mock only at the Application layer's ports (the interfaces to infrastructure — repositories,
  embedding generators, external clients) with NSubstitute. That's the actual seam between "code
  under test" and "the outside world."
- Never mock the domain/Core layer. Domain entities and value objects have no I/O dependency by
  construction (see `csharp-domain`), so there's no reason to fake them — exercise the real type.
  A test that mocks an entity is usually a sign the entity has picked up a dependency it shouldn't
  have, more than a sign the test needs a mock.
- Don't substitute a type just because it's convenient — substitute it because the real
  implementation is something the test genuinely shouldn't exercise (it hits a database, the
  network, the clock, or is slow/non-deterministic).
