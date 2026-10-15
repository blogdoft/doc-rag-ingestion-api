using DocRag.Ingestion.Api.Problems;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocRag.Ingestion.Api.Filters;

/// <summary>
/// Turns any binding/validation failure into the single <c>400 application/problem+json</c> shape
/// before the action - and therefore the use case - runs. Actions never build validation problems.
/// </summary>
public sealed class ModelValidationFilter : IActionFilter
{
    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            context.Result = ProblemResults.BadRequestValidation(context.ModelState, context.HttpContext);
        }
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
