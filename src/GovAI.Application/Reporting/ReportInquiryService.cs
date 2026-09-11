using GovAI.Application.Abstractions.Persistence;
using GovAI.Application.Abstractions.Services;
using GovAI.Application.Common;
using GovAI.Domain.Common;
using GovAI.Domain.Reporting;
using Microsoft.Extensions.Logging;

namespace GovAI.Application.Reporting;

/// <summary>
/// Cevaplanacak sorunun seçimi.
///
/// <para>
/// Yalnızca <b>anahtar</b> alınır; serbest metin kabul edilmez. Kullanıcının soru
/// yazamaması bir kısıt değil, cevabın raporun dışına taşmasını engelleyen tasarım
/// kararıdır.
/// </para>
/// </summary>
public sealed record AnswerQuestionRequest
{
    public required string QuestionKey { get; init; }

    /// <summary>Bu soru bir cevabın ardından açıldıysa, onu açan sorunun kimliği.</summary>
    public Guid? ParentInquiryId { get; init; }
}

public sealed record ReportQuestionDto(
    ReportQuestionKind Kind,
    string Key,
    string Text,
    Guid? OpportunityId);

public sealed record ReportInquiryDto(
    Guid Id,
    ReportQuestionKind Kind,
    string QuestionKey,
    string QuestionText,
    string AnswerText,
    Guid? OpportunityId,
    Guid? ParentInquiryId,
    DateTimeOffset AskedAt,
    string AskedBy);

/// <summary>Soru ekranının tek seferde ihtiyaç duyduğu her şey.</summary>
public sealed record ReportInquiryStateDto(
    Guid ReportId,
    /// <summary>Henüz sorulmamış, sorulabilir sorular.</summary>
    IReadOnlyList<ReportQuestionDto> Available,
    /// <summary>Daha önce sorulanlar ve cevapları; geri dönüp yeniden okumak hak harcamaz.</summary>
    IReadOnlyList<ReportInquiryDto> Asked,
    int UsedCount,
    int RemainingCount,
    int TotalQuota);

public sealed record AnswerQuestionResultDto(
    ReportInquiryDto Inquiry,
    /// <summary>Bu cevabın ardından açılan sorular.</summary>
    IReadOnlyList<ReportQuestionDto> FollowUps,
    int RemainingCount);

/// <summary>
/// Rapora sorulan soruların yönetimi.
///
/// <para>
/// Kullanıcı soru <b>yazmaz</b>, listeden seçer. Bu bir kolaylık değil güvenlik ve
/// doğruluk kararıdır: serbest metin cevabı raporun dışına taşır, sistemin bilmediği bir
/// şeye cevap uydurmasına zemin hazırlar ve modele yönlendirme (prompt injection) kapısı
/// açardı. Sabit soru kümesinde her sorunun rapordaki hangi veriden cevaplanacağı
/// önceden bellidir.
/// </para>
///
/// <para>
/// Hak <b>cevap üretilince</b> harcanır, soruya bakınca değil. Daha önce sorulmuş bir
/// soruyu yeniden açmak da harcamaz: kullanıcı listeye dönüp okuduğunu tekrar
/// görebilmelidir.
/// </para>
/// </summary>
public sealed class ReportInquiryService(
    IReportInquiryRepository inquiries,
    IWeeklyReportRepository reports,
    IUnitOfWork unitOfWork,
    CompanyAccessGuard access,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<ReportInquiryService> logger)
{
    public async Task<ReportInquiryStateDto> GetStateAsync(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var (report, content) = await LoadAsync(reportId, CompanyPermission.Read, cancellationToken);

        var sorulanlar = await inquiries.ListForReportAsync(reportId, cancellationToken);
        var sorulanAnahtarlar = sorulanlar.Select(i => i.QuestionKey).ToHashSet();

        var acik = ReportQuestionCatalog.Build(content)
            .Where(s => !sorulanAnahtarlar.Contains(s.Key))
            .Select(ToDto)
            .ToList();

        return new ReportInquiryStateDto(
            report.Id,
            acik,
            sorulanlar.Select(ToDto).ToList(),
            sorulanlar.Count,
            ReportInquiryQuota.Remaining(sorulanlar.Count),
            ReportInquiryQuota.PerReport);
    }

    /// <summary>Seçilen soruyu cevaplar ve bir hak harcar.</summary>
    public async Task<AnswerQuestionResultDto> AnswerAsync(
        Guid reportId,
        string questionKey,
        Guid? parentInquiryId,
        CancellationToken cancellationToken = default)
    {
        var (report, content) = await LoadAsync(reportId, CompanyPermission.Operate, cancellationToken);

        var sorulanlar = await inquiries.ListForReportAsync(reportId, cancellationToken);

        // Daha önce sorulmuşsa hak HARCANMAZ; kayıtlı cevap geri verilir.
        var mevcut = sorulanlar.FirstOrDefault(i => i.QuestionKey == questionKey);

        if (mevcut is not null)
        {
            return new AnswerQuestionResultDto(
                ToDto(mevcut),
                ReportQuestionCatalog
                    .FollowUps(mevcut.Kind, content, sorulanlar.Select(i => i.QuestionKey).ToList())
                    .Select(ToDto).ToList(),
                ReportInquiryQuota.Remaining(sorulanlar.Count));
        }

        if (!ReportInquiryQuota.HasRemaining(sorulanlar.Count))
        {
            throw new DomainException(
                $"Bu rapor için {ReportInquiryQuota.PerReport} soru hakkının tamamı kullanıldı. "
                + "Sorulmuş soruların cevapları okunmaya devam edilebilir.");
        }

        var soru = ReportQuestionCatalog.Build(content).FirstOrDefault(s => s.Key == questionKey)
            ?? throw new NotFoundException("Soru", questionKey);

        var cevap = ReportAnswerBuilder.Build(soru, content);

        var kayit = new ReportInquiry(
            access.RequireTenant(),
            report.CompanyId,
            report.Id,
            soru.Kind,
            soru.Key,
            soru.Text,
            cevap,
            soru.OpportunityId,
            parentInquiryId,
            clock.UtcNow,
            currentUser.Email ?? currentUser.UserId?.ToString() ?? "bilinmiyor");

        await inquiries.AddAsync(kayit, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var kullanilan = sorulanlar.Count + 1;

        logger.LogInformation(
            "Rapora soru soruldu. ReportId={ReportId} Soru={Kind} Kalan={Remaining}",
            report.Id, soru.Kind, ReportInquiryQuota.Remaining(kullanilan));

        var sonrakiler = ReportQuestionCatalog
            .FollowUps(
                soru.Kind,
                content,
                sorulanlar.Select(i => i.QuestionKey).Append(soru.Key).ToList())
            .Select(ToDto)
            .ToList();

        return new AnswerQuestionResultDto(
            ToDto(kayit), sonrakiler, ReportInquiryQuota.Remaining(kullanilan));
    }

    /// <summary>Raporu yükler, yetkiyi doğrular ve gövdesini çözer.</summary>
    private async Task<(WeeklyReport Report, WeeklyReportContent Content)> LoadAsync(
        Guid reportId,
        CompanyPermission permission,
        CancellationToken cancellationToken)
    {
        var report = await reports.GetAsync(reportId, cancellationToken)
            ?? throw new NotFoundException("Haftalık rapor", reportId);

        await access.LoadAccessibleAsync(report.CompanyId, permission, cancellationToken);

        var content = System.Text.Json.JsonSerializer.Deserialize<WeeklyReportContent>(
            report.ContentJson, WeeklyReportJson.Options)
            ?? throw new DomainException($"Rapor gövdesi okunamadı. ReportId={report.Id}");

        return (report, content);
    }

    private static ReportQuestionDto ToDto(ReportQuestion soru) =>
        new(soru.Kind, soru.Key, soru.Text, soru.OpportunityId);

    private static ReportInquiryDto ToDto(ReportInquiry kayit) =>
        new(
            kayit.Id,
            kayit.Kind,
            kayit.QuestionKey,
            kayit.QuestionText,
            kayit.AnswerText,
            kayit.OpportunityId,
            kayit.ParentInquiryId,
            kayit.AskedAt,
            kayit.AskedBy);
}
