---
name: csharp-api
description: Coding rules for C# Web API driving adapters (ASP.NET Core) — controllers, minimal API endpoints, request/response DTOs, validation, error-to-HTTP-status mapping, OpenAPI. Use this whenever writing, reviewing, or editing anything in an ASP.NET Core Web API project: adding or changing an endpoint/controller/route handler, request or response DTOs, model validation, exception or Result-to-HTTP mapping, or Swagger/OpenAPI annotations — even if the request is as short as "add an endpoint" or "create a controller for X" without naming this skill explicitly.
---

# C# Web API coding standards

Rules for the HTTP driving-adapter layer: the thin translation between wire format and the
Application layer's use cases. This is not about architecture placement (see the project's own
CLAUDE.md for where the API project sits) — it's about how code inside that layer should look.

## DTOs vs domain/application models

- Never return a domain entity or Core-layer type directly from an endpoint. Define a
  request/response DTO per operation (or a small shared set when payloads are genuinely
  identical) and map explicitly.
- Mapping code lives at the edge (endpoint/controller or a dedicated mapper next to it), not
  sprinkled into the domain or application layer — those layers must stay unaware that HTTP
  exists.
- DTOs are plain data: no behavior, no validation logic beyond attribute-based annotations.
- DTOs can have static methods for mapping to/from domain types, but avoid putting mapping logic
  in the domain or application layer.

## Validation

- Validate at the boundary, before the Application-layer use case is invoked. Prefer
  attribute/model validation (or a validation library already in the project) over hand-rolled
  `if` chains scattered through the handler body.
- A validation failure returns `400 Bad Request` as a `ProblemDetails` response — never a raw string
  or an unstructured JSON error shape.
  - Use an HTTP filter (`IEndpointFilter` for minimal APIs, an equivalent action filter for
    controllers) to translate validation failures into that `ProblemDetails` response — endpoints
    should not construct or return `ValidationProblemDetails` directly.
- Don't re-validate things the domain already guarantees (e.g., an invariant enforced by a value
  object's constructor). Validate wire-format concerns (required fields, shape, ranges) at the API
  layer; trust the domain for business-rule invariants.

## Error handling

- The Application layer returns `Result`/`Result<T>` (see `BlogDoFT.Libs.ResultPattern`) for
  expected failure paths. The API layer's job is to translate a failure `Result` into the right
  HTTP status — do this translation in one central place (a filter, middleware, or a small mapping
  helper), not repeated ad hoc in every endpoint.
- Reserve unhandled-exception middleware for truly unexpected failures (bugs, infrastructure
  outages). It must not leak exception details (stack traces, connection strings, internal type
  names) to the client.
- Status code guide: `400` malformed/invalid request, conflicting state, or semantically invalid
  but well-formed request; `404` resource not found; `500` unexpected/unhandled.
- Errors `401`, `403`, `404`, and `5xx` return no response body — only application logs are
  generated for them. When the error is `403`, log the username.

## Endpoint shape

- Prefer minimal API route handlers for straightforward CRUD-shaped operations; reach for
  controllers when you need shared filters, model binding conventions, or a large enough group of
  related routes that a controller's grouping earns its ceremony. Don't mix styles within the same
  feature without a reason.
- Keep the handler body thin: bind/validate request → call one Application-layer use case → map
  result to response. If a handler is doing real logic (branching business rules, multiple
  sequential calls to different ports), that logic belongs in the Application layer, not the
  endpoint.
- Thread `CancellationToken` from the endpoint parameter through every downstream async call.
- When an endpoint represents a verb that isn't `GET`/`POST`/`PUT`/`DELETE`, prefer adding a
  `:<action-verb>` suffix to the resource name. E.g.: `/clients:report` or `/sales:summary`.

## Dependency injection

- Group service registrations behind an extension method per feature/module
  (`AddXServices(this IServiceCollection ...)`) rather than letting `Program.cs` grow into a long
  flat list — this keeps registration colocated with the feature it wires up.

## OpenAPI

- Annotate endpoints with a summary and the concrete response types/status codes they can produce
  (`Produces<T>(StatusCodes...)` or equivalent), so the generated spec reflects reality instead of
  a generic `200`.
- OpenApi documentation should be as rich as possible, providing clear descriptions and examples for each endpoint and its responses. Always ensure that accepted request formats and response formats are well-documented, including any custom headers or query parameters or format (application/json, application/xml, etc.) that the endpoint supports.
- Ensure it's possible to log in to the API via Swagger UI (if the API requires authentication) and
  that the authentication mechanism is clearly documented in the OpenAPI spec.
- Use Swashbuckle — instead of Scalar or any other OpenAPI generator — for generating and managing
  OpenAPI documentation. Ensure it's configured correctly to include all necessary endpoints,
  request/response examples, and security schemes if required.
- Use an extension method class to configure Swagger/OpenAPI.
- Tag names are kebab-case. E.g.: `user-management`, `order-processing`, `inventory-control`.
- Json should follow camelCase naming convention for properties, and use `System.Text.Json` as the default serializer. Avoid using Newtonsoft.Json unless absolutely necessary.
- Use `ProducesResponseType` attributes to specify the expected response types and status codes for each endpoint, ensuring that the OpenAPI documentation accurately reflects the API's behavior.

## Observability

- Use `BlogDoFT.Libs.Api.OpenTelemetry` (built on OpenTelemetry) as the observability stack —
  logging, metrics, and tracing. Avoid bringing in a separate third-party observability library on
  top of it unless there's a concrete gap it doesn't cover.
- Use structured logging with clear and consistent log messages, including relevant context
  information (e.g., request IDs, user IDs, correlation IDs) to facilitate troubleshooting and
  monitoring.
- Use distributed tracing to track requests across services and components, ensuring that trace IDs
  are propagated correctly through the system.
- Configure observability (logging, metrics, tracing setup) through an extension method class, same
  as the Swagger/OpenAPI setup above.