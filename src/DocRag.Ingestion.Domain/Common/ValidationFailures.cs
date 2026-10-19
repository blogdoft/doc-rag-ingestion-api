using BlogDoFT.Libs.ResultPattern;
using FluentValidation.Results;

namespace DocRag.Ingestion.Domain.Common;

/// <summary>Converts FluentValidation outcomes into the Result pattern's <see cref="Failure"/>.</summary>
internal static class ValidationFailures
{
    /// <summary>Builds a failure whose code carries the intended HTTP status as its leading digits.</summary>
    /// <param name="code">Failure code, e.g. <c>400-document-invalid</c>.</param>
    /// <param name="result">The failed validation result.</param>
    /// <returns>A failure joining every error message.</returns>
    public static Failure From(string code, ValidationResult result) =>
        new(code, string.Join(" ", result.Errors.Select(error => error.ErrorMessage)));
}
