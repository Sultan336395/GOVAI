using GovAI.Application.Common;
using GovAI.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GovAI.Api.Infrastructure;

/// <summary>
/// Uygulama ve domain istisnalarını RFC 7807 ProblemDetails yanıtlarına çevirir.
/// Beklenmeyen hatalarda istemciye ayrıntı sızdırılmaz; ayrıntı yalnızca loga yazılır.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Map(exception, httpContext);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "İşlenmeyen hata. Yol={Path} CorrelationId={CorrelationId}",
                httpContext.Request.Path, httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogInformation("İstek reddedildi ({Status}). Yol={Path} Sebep={Message}",
                problem.Status, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        // Tip BİLE BİLE çalışma zamanından alınır: doğrulama hatalarında nesne aslında
        // ValidationProblemDetails'tir ve statik tiple yazılırsa System.Text.Json türetilmiş
        // "errors" sözlüğünü hiç yazmaz — panel hangi alanın hatalı olduğunu göremezdi.
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);

        return true;
    }

    private static ProblemDetails Map(Exception exception, HttpContext httpContext)
    {
        var problem = exception switch
        {
            NotFoundException notFound => new ProblemDetails
            {
                Title = "Kayıt bulunamadı",
                Detail = notFound.Message,
                Status = StatusCodes.Status404NotFound
            },

            ForbiddenException forbidden => new ProblemDetails
            {
                Title = "Erişim reddedildi",
                Detail = forbidden.Message,
                Status = StatusCodes.Status403Forbidden
            },

            AuthenticationFailedException auth => new ProblemDetails
            {
                Title = "Kimlik doğrulama başarısız",
                Detail = auth.Message,
                Status = StatusCodes.Status401Unauthorized
            },

            ValidationException validation => BuildValidationProblem(validation),

            DomainException domain => new ProblemDetails
            {
                Title = "İş kuralı ihlali",
                Detail = domain.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            },

            // 499 "Client Closed Request" — standart sabiti yoktur, nginx yaygın kullanımından alınmıştır.
            OperationCanceledException => new ProblemDetails
            {
                Title = "İstek iptal edildi",
                Status = 499
            },

            _ => new ProblemDetails
            {
                Title = "Beklenmeyen bir hata oluştu",
                Detail = "İşlem tamamlanamadı. Destek ekibiyle iletişime geçerken korelasyon kimliğini paylaşın.",
                Status = StatusCodes.Status500InternalServerError
            }
        };

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;

        return problem;
    }

    private static ProblemDetails BuildValidationProblem(ValidationException exception)
    {
        var problem = new ValidationProblemDetails(exception.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Title = "Girdi doğrulaması başarısız",
            Status = StatusCodes.Status400BadRequest
        };

        return problem;
    }
}
