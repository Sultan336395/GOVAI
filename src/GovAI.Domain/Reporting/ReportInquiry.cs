using GovAI.Domain.Common;

namespace GovAI.Domain.Reporting;

/// <summary>
/// Bir rapora sorulmuş soru ve verilen cevap.
///
/// <para>
/// Kayıt hem <b>hak sayacının</b> hem de <b>geri dönüşün</b> dayanağıdır: kullanıcı
/// cevabı okuyup listeye döndüğünde, daha önce sorduğu soruyu yeniden açabilmeli ve bu
/// ikinci okuma hak harcamamalıdır. Cevabı saklamadan bu mümkün olmazdı — cevap yeniden
/// üretilseydi rapor değişmiş olsa farklı bir metin çıkardı.
/// </para>
/// </summary>
public class ReportInquiry : AggregateRoot, IAuditable, ITenantScoped
{
    private ReportInquiry()
    {
    }

    public ReportInquiry(
        Guid tenantId,
        Guid companyId,
        Guid weeklyReportId,
        ReportQuestionKind kind,
        string questionKey,
        string questionText,
        string answerText,
        Guid? opportunityId,
        Guid? parentInquiryId,
        DateTimeOffset askedAt,
        string askedBy)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(questionKey), "Soru anahtarı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(questionText), "Soru metni zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(answerText), "Cevap boş olamaz.");

        TenantId = tenantId;
        CompanyId = companyId;
        WeeklyReportId = weeklyReportId;
        Kind = kind;
        QuestionKey = questionKey;
        QuestionText = questionText;
        AnswerText = answerText;
        OpportunityId = opportunityId;
        ParentInquiryId = parentInquiryId;
        AskedAt = askedAt;
        AskedBy = askedBy;
    }

    public Guid TenantId { get; set; }

    public Guid CompanyId { get; private set; }

    public Guid WeeklyReportId { get; private set; }

    public ReportQuestionKind Kind { get; private set; }

    /// <summary>Tür + varsa çağrı kimliği. Aynı raporda aynı soru iki kez sorulamaz.</summary>
    public string QuestionKey { get; private set; } = string.Empty;

    /// <summary>Sorunun kullanıcıya gösterildiği hâli; sonradan metin değişse bile kayıt sabit kalır.</summary>
    public string QuestionText { get; private set; } = string.Empty;

    /// <summary>Cevabın o andaki hâli. Yeniden üretilmez.</summary>
    public string AnswerText { get; private set; } = string.Empty;

    public Guid? OpportunityId { get; private set; }

    /// <summary>Bu soru bir cevabın ardından açıldıysa, onu açan sorunun kimliği.</summary>
    public Guid? ParentInquiryId { get; private set; }

    public DateTimeOffset AskedAt { get; private set; }

    public string AskedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Rapor başına soru hakkı.
///
/// <para>
/// Hak <b>rapor bazında</b> tanımlıdır ve rapor bir firmaya ait olduğu için sayaç
/// doğrudan firma–rapor çiftini sayar. Firma düzeyinde tek bir havuz tutmak, yoğun bir
/// haftada hakkın tükenip sonraki haftanın raporunu sorusuz bırakması demek olurdu.
/// </para>
/// </summary>
public static class ReportInquiryQuota
{
    /// <summary>Her rapor için verilen soru hakkı.</summary>
    public const int PerReport = 5;

    public static int Remaining(int asked) => Math.Max(0, PerReport - asked);

    public static bool HasRemaining(int asked) => Remaining(asked) > 0;
}
