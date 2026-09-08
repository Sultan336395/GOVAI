using GovAI.Domain.Common;

namespace GovAI.Domain.Companies;

/// <summary>Yıllık mali verinin nereden geldiği. Kaynağı bilinmeyen sayı doğrulanamaz.</summary>
public enum FinancialDataSource
{
    /// <summary>Kaynağı beyan edilmedi.</summary>
    Unspecified = 0,

    /// <summary>Firma yetkilisi panelden girdi.</summary>
    SelfDeclared = 1,

    /// <summary>Muhasebe/ERP entegrasyonundan geldi.</summary>
    ErpIntegration = 2,

    /// <summary>Mali müşavir onaylı beyanname veya mizandan alındı.</summary>
    AccountantStatement = 3,

    /// <summary>Bağımsız denetim raporundan alındı.</summary>
    AuditedStatement = 4
}

/// <summary>Mali verinin doğrulanma durumu; puanı değil <b>güven seviyesini</b> etkiler.</summary>
public enum FinancialVerificationStatus
{
    /// <summary>Beyan edildi, doğrulanmadı.</summary>
    Unverified = 0,

    /// <summary>Belgeyle karşılaştırıldı.</summary>
    DocumentChecked = 1,

    /// <summary>Mali müşavir veya bağımsız denetim onayladı.</summary>
    Verified = 2
}

/// <summary>
/// Firmanın bir mali yıla ait <b>toplu</b> finansal verisi (Faz 3).
///
/// <para>
/// Mevcut <see cref="Financials"/> tek bir dönemi taşır ve yıl karşılaştırması yapamaz.
/// Ciro eşiği arayan çağrılarda "hangi yılın cirosu" sorusunun cevabı gerekiyor; ayrıca
/// mali verinin ne kadar eski olduğu güven seviyesini doğrudan etkiliyor. Bu kayıt
/// mevcut alanların <b>yerine geçmez</b>, yanına eklenir — eski değerlendirmeler ve
/// eski profiller bozulmadan çalışmaya devam eder.
/// </para>
///
/// <para>
/// <b>Kişisel veri içermez.</b> Burada yalnızca şirketin toplu mali büyüklükleri durur;
/// çalışan ücreti, ortak bilgisi, banka hesabı veya kimlik verisi tutulmaz ve modele
/// gönderilmez.
/// </para>
/// </summary>
public class AnnualFinancialRecord : Entity
{
    private AnnualFinancialRecord()
    {
    }

    public AnnualFinancialRecord(
        int fiscalYear,
        string currency,
        decimal? annualRevenue,
        decimal? annualIncome,
        decimal? annualExpense,
        decimal? netProfitOrLoss,
        decimal? balanceTotal,
        FinancialDataSource dataSource,
        FinancialVerificationStatus verificationStatus,
        DateTimeOffset updatedAt)
    {
        DomainException.ThrowIf(fiscalYear is < 1990 or > 2100, "Mali yıl 1990–2100 aralığında olmalıdır.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(currency), "Para birimi zorunludur.");
        DomainException.ThrowIf(annualRevenue < 0, "Yıllık ciro negatif olamaz.");
        DomainException.ThrowIf(annualIncome < 0, "Yıllık gelir negatif olamaz.");
        DomainException.ThrowIf(annualExpense < 0, "Yıllık gider negatif olamaz.");
        DomainException.ThrowIf(balanceTotal < 0, "Bilanço toplamı negatif olamaz.");

        FiscalYear = fiscalYear;
        Currency = currency.Trim().ToUpperInvariant();
        AnnualRevenue = annualRevenue;
        AnnualIncome = annualIncome;
        AnnualExpense = annualExpense;
        NetProfitOrLoss = netProfitOrLoss;
        BalanceTotal = balanceTotal;
        DataSource = dataSource;
        VerificationStatus = verificationStatus;
        UpdatedAt = updatedAt;
    }

    public Guid CompanyId { get; private set; }

    public int FiscalYear { get; private set; }

    public string Currency { get; private set; } = "TRY";

    /// <summary>
    /// Yıllık ciro. <c>null</c> "girilmedi" demektir, sıfır değil — eksik mali veri
    /// kriteri <see cref="Analysis.CriterionOutcome.Unknown"/> bırakır, firmayı elemez.
    /// </summary>
    public decimal? AnnualRevenue { get; private set; }

    public decimal? AnnualIncome { get; private set; }

    public decimal? AnnualExpense { get; private set; }

    /// <summary>Net kâr veya zarar; zarar negatif değerdir.</summary>
    public decimal? NetProfitOrLoss { get; private set; }

    /// <summary>Bilanço (aktif) toplamı.</summary>
    public decimal? BalanceTotal { get; private set; }

    public FinancialDataSource DataSource { get; private set; }

    public FinancialVerificationStatus VerificationStatus { get; private set; }

    /// <summary>Verinin son güncellenme zamanı.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bu yıl için hiç sayı girilmemişse kayıt bilgi taşımaz.</summary>
    public bool IsEmpty =>
        AnnualRevenue is null && AnnualIncome is null && AnnualExpense is null
        && NetProfitOrLoss is null && BalanceTotal is null;

    public void Update(
        string currency,
        decimal? annualRevenue,
        decimal? annualIncome,
        decimal? annualExpense,
        decimal? netProfitOrLoss,
        decimal? balanceTotal,
        FinancialDataSource dataSource,
        FinancialVerificationStatus verificationStatus,
        DateTimeOffset updatedAt)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(currency), "Para birimi zorunludur.");
        DomainException.ThrowIf(annualRevenue < 0, "Yıllık ciro negatif olamaz.");
        DomainException.ThrowIf(annualIncome < 0, "Yıllık gelir negatif olamaz.");
        DomainException.ThrowIf(annualExpense < 0, "Yıllık gider negatif olamaz.");
        DomainException.ThrowIf(balanceTotal < 0, "Bilanço toplamı negatif olamaz.");

        Currency = currency.Trim().ToUpperInvariant();
        AnnualRevenue = annualRevenue;
        AnnualIncome = annualIncome;
        AnnualExpense = annualExpense;
        NetProfitOrLoss = netProfitOrLoss;
        BalanceTotal = balanceTotal;
        DataSource = dataSource;
        VerificationStatus = verificationStatus;
        UpdatedAt = updatedAt;
    }
}
