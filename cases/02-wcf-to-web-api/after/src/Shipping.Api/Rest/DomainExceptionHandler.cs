using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shipping.Core;

namespace Shipping.Api.Rest;

/// <summary>
/// The REST counterpart of the typed SOAP faults: each domain error becomes an HTTP status code and a
/// ProblemDetails body with a stable machine-readable "code".
/// </summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            ShipmentNotFoundException e => Problem(StatusCodes.Status404NotFound, "shipment_not_found", e.Message),
            ValidationFailedException e => Problem(StatusCodes.Status400BadRequest, "validation_failed", e.Message, ("errors", e.Errors)),
            InvalidTransitionException e => Problem(StatusCodes.Status409Conflict, "invalid_transition", e.Message, ("from", e.From.ToString()), ("to", e.To.ToString())),
            BadHttpRequestException e => Problem(StatusCodes.Status400BadRequest, "bad_request", e.Message),
            _ => null
        };
        if (problem is null) return false;

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Problem(int status, string code, string detail, params (string Key, object Value)[] extensions)
    {
        var problem = new ProblemDetails { Status = status, Title = detail };
        problem.Extensions["code"] = code;
        foreach (var (key, value) in extensions) problem.Extensions[key] = value;
        return problem;
    }
}
