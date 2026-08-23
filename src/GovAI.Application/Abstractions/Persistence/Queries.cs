using GovAI.Domain.Common;

namespace GovAI.Application.Abstractions.Persistence;

/// <summary>Sayfalama isteği. Üst sınır, tek istekte veritabanını yormamak için 200'dür.</summary>
public record PageRequest
{
    private const int MaxPageSize = 200;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int Skip => (Math.Max(Page, 1) - 1) * Take;

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public static PagedResult<T> Empty(PageRequest request) => new([], 0, request.Page, request.PageSize);
}

/// <summary>Fırsat listesi filtreleri (/api/opportunities).</summary>
public sealed record OpportunityQuery : PageRequest
{
    public string? SearchTerm { get; init; }

    public IReadOnlyCollection<SupportCategory>? Categories { get; init; }

    public IReadOnlyCollection<SourceType>? SourceTypes { get; init; }

    /// <summary>Yalnızca son başvuru tarihi geçmemiş çağrılar.</summary>
    public bool OnlyOpen { get; init; } = true;

    public DateTimeOffset? PublishedAfter { get; init; }

    public DateTimeOffset? DeadlineBefore { get; init; }

    /// <summary>Danışman onayından geçmiş çağrılar; kural kalitesi yüksek olanları filtrelemek için.</summary>
    public bool? OnlyReviewed { get; init; }

    public string? Nuts2Code { get; init; }

    public OpportunitySort Sort { get; init; } = OpportunitySort.DeadlineAscending;
}

public enum OpportunitySort
{
    DeadlineAscending = 0,
    PublishedDescending = 1,
    TitleAscending = 2
}

/// <summary>Firma bazlı değerlendirme listesi filtreleri (/api/eligibility, /api/scoring).</summary>
public sealed record AssessmentQuery : PageRequest
{
    public required Guid CompanyId { get; init; }

    public decimal? MinScore { get; init; }

    public IReadOnlyCollection<EligibilityVerdict>? Verdicts { get; init; }

    public IReadOnlyCollection<SupportCategory>? Categories { get; init; }

    /// <summary>Son başvurusuna bu kadar günden az kalan fırsatlar.</summary>
    public int? DeadlineWithinDays { get; init; }

    public AssessmentSort Sort { get; init; } = AssessmentSort.ScoreDescending;
}

public enum AssessmentSort
{
    ScoreDescending = 0,
    DeadlineAscending = 1,
    EvaluatedAtDescending = 2
}

public sealed record NotificationQuery : PageRequest
{
    /// <summary>
    /// Zorunlu kiracı sınırı. <c>required</c> olması bilinçlidir: yeni bir çağrı yeri
    /// bu alanı atlamak isterse derleme hatası alır. Değer her zaman oturumdan gelir;
    /// istemcinin gönderdiği bir kiracı kimliği kabul edilmez (bkz. NotificationService).
    /// </summary>
    public required Guid TenantId { get; init; }

    public Guid? CompanyId { get; init; }

    public bool? OnlyUnread { get; init; }

    public IReadOnlyCollection<NotificationKind>? Kinds { get; init; }
}

public sealed record AuditLogQuery : PageRequest
{
    public string? Action { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? UserEmail { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}
