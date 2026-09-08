using GovAI.Domain.Common;

namespace GovAI.Domain.Opportunities;

/// <summary>
/// Çağrı metninden çıkarılmış tek bir makine-değerlendirilebilir koşul.
/// Örnek: "asgari 10 çalışan" → <c>Field=Workforce.EmployeeCount, Operator=GreaterThanOrEqual, Value=10</c>.
/// <see cref="SourceExcerpt"/> her koşulun metindeki dayanağını taşır; açıklanabilirliğin temelidir.
/// </summary>
public class OpportunityRule : Entity
{
    private readonly List<OpportunityRuleEvidence> _evidence = [];

    private OpportunityRule()
    {
    }

    public OpportunityRule(
        string field,
        RuleOperator @operator,
        string value,
        RuleDimension dimension,
        RuleSeverity severity,
        string humanReadable,
        string? sourceExcerpt = null,
        decimal confidence = 1m)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(field), "Kural alanı zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(humanReadable), "Kuralın okunabilir açıklaması zorunludur.");
        DomainException.ThrowIf(confidence is < 0m or > 1m, "Kural güven değeri 0 ile 1 arasında olmalıdır.");

        Field = field.Trim();
        Operator = @operator;
        Value = value;
        Dimension = dimension;
        Severity = severity;
        HumanReadable = humanReadable.Trim();
        SourceExcerpt = sourceExcerpt;
        Confidence = confidence;
    }

    public Guid OpportunityId { get; private set; }

    /// <summary>Firma profilindeki hedef alanın yol ifadesi (ör. <c>Workforce.EmployeeCount</c>).</summary>
    public string Field { get; private set; } = string.Empty;

    public RuleOperator Operator { get; private set; }

    /// <summary>Karşılaştırma değeri. Liste operatörlerinde virgülle ayrılmış olarak saklanır.</summary>
    public string Value { get; private set; } = string.Empty;

    public RuleDimension Dimension { get; private set; }

    public RuleSeverity Severity { get; private set; }

    /// <summary>Kullanıcıya gösterilen Türkçe koşul metni.</summary>
    public string HumanReadable { get; private set; } = string.Empty;

    /// <summary>Koşulun çıkarıldığı orijinal metin parçası; denetlenebilirlik için saklanır.</summary>
    public string? SourceExcerpt { get; private set; }

    /// <summary>AI/parser'ın bu koşulu doğru çıkardığına dair güveni (0..1).</summary>
    public decimal Confidence { get; private set; }

    /// <summary>Danışman kuralı elle düzelttiyse otomatik yeniden çıkarımda korunur.</summary>
    public bool IsManuallyOverridden { get; private set; }

    /// <summary>
    /// Kuralın dayandığı resmî kanıt parçaları. Bir kural birden çok parçaya
    /// dayanabilir; bu yüzden tek bir kolon değil ilişki tutulur.
    /// </summary>
    public IReadOnlyCollection<OpportunityRuleEvidence> Evidence => _evidence.AsReadOnly();

    /// <summary>
    /// Bu kural hakkında yapay zekâ resmî kaynağa dayalı iddia üretebilir mi?
    ///
    /// <para>
    /// Kanıtsız eski kurallar deterministik motorda kullanılmaya devam eder — onları
    /// silmek, doğrulanmış bilgiyi kaybetmek olurdu. Ama kanıt parçası olmadan modelin
    /// "belgede şöyle yazıyor" demesi doğrulanamaz; bu yüzden o kural yapay zekâ
    /// iddiasına kapalıdır.
    /// </para>
    /// </summary>
    public bool SupportsAiClaims => _evidence.Any(e => e.IsCitable);

    /// <summary>
    /// Kanıt bağlantısı ekler. Aynı parça aynı rolle iki kez bağlanmaz: yeniden
    /// ayrıştırma mükerrer satır üretmemelidir.
    /// </summary>
    public void AttachEvidence(OpportunityRuleEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var zatenVar = _evidence.Any(e =>
            e.EvidenceChunkId == evidence.EvidenceChunkId && e.Role == evidence.Role);

        if (zatenVar)
        {
            return;
        }

        _evidence.Add(evidence);
    }

    /// <summary>
    /// Bir kanıt bağlantısını kaldırır (toplu bağlamanın geri alınması).
    ///
    /// <para>
    /// Yalnızca <b>tam eşleşen</b> üçlü kaldırılır: parça ve rol birlikte verilir.
    /// Rolü yok saymak, aynı parçaya farklı rolle bağlı ve geri alma kapsamında
    /// olmayan bir satırı da silerdi.
    /// </para>
    /// </summary>
    /// <returns>Bir satır kaldırıldıysa <c>true</c>.</returns>
    public bool RemoveEvidence(Guid evidenceChunkId, RuleEvidenceRole role)
    {
        var kayit = _evidence.FirstOrDefault(e => e.EvidenceChunkId == evidenceChunkId && e.Role == role);

        return kayit is not null && _evidence.Remove(kayit);
    }

    public void OverrideManually(RuleOperator @operator, string value, RuleSeverity severity, string humanReadable)
    {
        Operator = @operator;
        Value = value;
        Severity = severity;
        HumanReadable = humanReadable;
        Confidence = 1m;
        IsManuallyOverridden = true;
    }

    /// <summary>Liste tipli operatörler için değeri parçalar.</summary>
    public IReadOnlyList<string> ValueList() =>
        Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Kural karşılaştırma operatörleri.</summary>
public enum RuleOperator
{
    Equals = 1,
    NotEquals = 2,
    GreaterThan = 3,
    GreaterThanOrEqual = 4,
    LessThan = 5,
    LessThanOrEqual = 6,
    /// <summary>Firma değeri, verilen listede yer almalı.</summary>
    In = 7,
    NotIn = 8,
    /// <summary>Firmanın koleksiyonu, verilen değerlerin tamamını içermeli (ör. zorunlu sertifikalar).</summary>
    ContainsAll = 9,
    /// <summary>Firmanın koleksiyonu, verilen değerlerden en az birini içermeli.</summary>
    ContainsAny = 10,
    /// <summary>NACE önek eşleşmesi; <see cref="Companies.NaceCode"/> mantığını kullanır.</summary>
    NaceMatch = 11,
    IsTrue = 12,
    IsFalse = 13
}

/// <summary>Başvuru dosyasında istenen tek bir evrak.</summary>
public class DocumentRequirement : Entity
{
    private DocumentRequirement()
    {
    }

    public DocumentRequirement(string code, string name, bool isMandatory, string? issuingAuthority = null, string? notes = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(code), "Belge kodu zorunludur.");

        Code = code.Trim().ToUpperInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? Code : name.Trim();
        IsMandatory = isMandatory;
        IssuingAuthority = issuingAuthority?.Trim();
        Notes = notes?.Trim();
    }

    public Guid OpportunityId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsMandatory { get; private set; }

    /// <summary>Belgeyi düzenleyen kurum (ör. Ticaret Odası, SGK).</summary>
    public string? IssuingAuthority { get; private set; }

    public string? Notes { get; private set; }
}
