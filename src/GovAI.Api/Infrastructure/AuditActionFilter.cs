using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Domain.Auditing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace GovAI.Api.Infrastructure;

/// <summary>
/// Bir isteğin denetim kaydına yazılacağını ve hangi eylem adıyla yazılacağını belirtir.
/// Teknik doküman 5.4: "Her skor ve kullanıcı aksiyonu zaman damgası ile kaydedilmelidir."
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditedAttribute(string action, string entityType = "") : Attribute
{
    public string Action { get; } = action;

    public string EntityType { get; } = entityType;

    /// <summary>Kaydedilecek varlık kimliğinin okunacağı route parametresi adı.</summary>
    public string RouteKey { get; init; } = "id";
}

/// <summary>
/// <see cref="AuditedAttribute"/> ile işaretlenmiş eylemleri <b>başarıyla tamamlandıklarında</b>
/// audit log'a yazar: istisna atmamış <b>ve</b> 2xx dönmüş olmalıdır. Başarısız istekler zaten
/// merkezî hata loguna düşer; denetim kaydı yalnızca gerçekleşen değişiklikleri izler.
/// </summary>
public sealed class AuditActionFilter(
    IAuditLogRepository auditLog,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<AuditActionFilter> logger) : IAsyncActionFilter
{
    /// <summary>
    /// Eylemin sonucu başarılı mı?
    ///
    /// <para>
    /// Yanıtın kendi durum koduna BAKILAMAZ: bu filtre sonuç henüz yazılmadan çalışır,
    /// <c>Response.StatusCode</c> o anda hâlâ 200'dür. Karar sonucun türünden verilir.
    /// </para>
    ///
    /// <para>
    /// <see cref="ForbidResult"/> ve <see cref="ChallengeResult"/> durum kodu taşımaz —
    /// kodu kimlik doğrulama işleyicisi yazar — bu yüzden ayrıca ele alınır. Durum kodu
    /// hiç belirtilmemiş bir sonuç (düz <c>ObjectResult</c>, <c>EmptyResult</c>) başarı
    /// sayılır; MVC o hâlde 200 döner.
    /// </para>
    /// </summary>
    private static bool IsSuccessful(ActionExecutedContext executed) => executed.Result switch
    {
        ForbidResult or ChallengeResult => false,
        IStatusCodeActionResult { StatusCode: { } code } => code is >= 200 and < 300,
        _ => true,
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor.EndpointMetadata.OfType<AuditedAttribute>().FirstOrDefault();

        var executed = await next();

        if (descriptor is null || executed.Exception is { } && !executed.ExceptionHandled)
        {
            return;
        }

        // Gerçekleşmemiş eylem kayda geçmez.
        //
        // Filtre yalnızca istisnaya bakıyordu; 404 ya da 400 dönen bir eylem de
        // "yapıldı" diye yazılıyordu. Somut sonucu: kapalı olan platform hesabı açma
        // ucuna yapılan ve 404 alan denemeler denetim kaydına "aktivasyon oluşturuldu"
        // olarak düştü — veritabanında 6 kayıt vardı, gerçekte 3 bağlantı üretilmişti.
        // Denetlenebilirlik bu ürünün üç iddiasından biridir; olmayan bir işlemin kaydı
        // kaydın tamamını şüpheli yapar.
        if (!IsSuccessful(executed))
        {
            return;
        }

        try
        {
            var entityId = context.RouteData.Values.TryGetValue(descriptor.RouteKey, out var value)
                ? value?.ToString()
                : null;

            var entry = new AuditLogEntry(
                currentUser.TenantId,
                descriptor.Action,
                descriptor.EntityType,
                entityId,
                currentUser.UserId?.ToString(),
                currentUser.Email,
                clock.UtcNow);

            entry.SetRequestContext(currentUser.IpAddress, currentUser.UserAgent, currentUser.CorrelationId);

            await auditLog.AddAsync(entry, context.HttpContext.RequestAborted);
            await unitOfWork.SaveChangesAsync(context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Denetim kaydı yazılamazsa kullanıcı isteği başarısız sayılmaz, ancak bu durum
            // operasyonel bir sorundur ve hata seviyesinde loglanır.
            logger.LogError(ex, "Denetim kaydı yazılamadı. Eylem={Action}", descriptor.Action);
        }
    }
}
