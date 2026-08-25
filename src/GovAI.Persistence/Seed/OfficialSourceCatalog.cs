using GovAI.Domain.Common;
using GovAI.Domain.Sources;

namespace GovAI.Persistence.Seed;

/// <summary>
/// Resmî kaynak kataloğunun <b>tek</b> tanım yeri (Faz 2).
///
/// Bu dosya yalnızca kayıtları <b>oluşturur</b>; çalışma zamanında hiçbir yerde
/// okunmaz. Tarayıcı, seçicileri ve sınırları veritabanındaki kaynak kaydından alır —
/// böylece yeni bir kurum eklemek ya da bozulan bir seçiciyi düzeltmek kod değişikliği
/// değil, veri değişikliğidir.
///
/// <b>Hepsi doğrulanmamış (Unverified) ve kapalı olarak açılır.</b> Bir kaynağın
/// taranabilmesi için seçicisinin canlı olarak çalıştığının kanıtlanması gerekir
/// (bkz. <c>SourceVerificationService</c>). Doğrulanmamış kaynak asla taranmaz;
/// "aktifmiş gibi" gösterilmez.
///
/// Yalnızca resmî kurum alan adları kullanılır.
/// </summary>
public static class OfficialSourceCatalog
{
    public sealed record Definition(
        string Name,
        SourceType Type,
        SourceCategory Category,
        string Authority,
        string Jurisdiction,
        string OfficialDomain,
        string BaseUrl,
        string CronExpression,
        string? StartUrl,
        string? ListSelector,
        string? ContentSelector,
        string? UrlPattern,
        int MaxPages,
        string Language,
        string DocumentTypes,
        string Note);

    /// <summary>Türkiye ve AB pilot kaynakları.</summary>
    public static IReadOnlyList<Definition> All { get; } =
    [
        // ─────────────────── Türkiye: mevzuat ───────────────────
        new("Resmî Gazete", SourceType.OfficialGazette, SourceCategory.Regulation,
            "T.C. Cumhurbaşkanlığı", "TR", "resmigazete.gov.tr",
            "https://www.resmigazete.gov.tr", "0 7 * * *",
            StartUrl: "/", ListSelector: "a.html-doc-button, a[href*='eskiler']",
            ContentSelector: "#divResmiGazete, .html-content",
            UrlPattern: @"eskiler|/\d{8}/", MaxPages: 5, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Günlük yayımlanan mevzuatın birincil kaynağı."),

        new("Gelir İdaresi Başkanlığı", SourceType.Ministry, SourceCategory.Tax,
            "Gelir İdaresi Başkanlığı", "TR", "gib.gov.tr",
            "https://www.gib.gov.tr", "0 8 * * *",
            StartUrl: "/", ListSelector: "a[href*='teblig'], a[href*='sirküler'], a[href*='sirkuler']",
            ContentSelector: "#content, .icerik",
            UrlPattern: "teblig|sirkuler|genelge|ozelge", MaxPages: 5, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Vergi tebliğ, sirküler ve genelgeleri."),

        new("Sosyal Güvenlik Kurumu", SourceType.Ministry, SourceCategory.SocialSecurity,
            "Sosyal Güvenlik Kurumu", "TR", "sgk.gov.tr",
            "https://www.sgk.gov.tr", "0 8 * * *",
            StartUrl: "/", ListSelector: "a[href*='genelge'], a[href*='duyuru']",
            ContentSelector: "#content, .icerik",
            UrlPattern: "genelge|duyuru|teblig", MaxPages: 5, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "SGK genelge ve duyuruları."),

        new("Çalışma ve Sosyal Güvenlik Bakanlığı", SourceType.Ministry, SourceCategory.LabourLaw,
            "Çalışma ve Sosyal Güvenlik Bakanlığı", "TR", "csgb.gov.tr",
            "https://www.csgb.gov.tr", "0 8 * * *",
            StartUrl: "/", ListSelector: "a[href*='duyuru'], a[href*='mevzuat']",
            ContentSelector: "#content, .icerik",
            UrlPattern: "duyuru|mevzuat|yonetmelik", MaxPages: 5, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "İş hukuku düzenlemeleri ve duyuruları."),

        // ─────────────────── Türkiye: destek ───────────────────
        new("KOSGEB", SourceType.KosgebOrSimilar, SourceCategory.Incentive,
            "KOSGEB", "TR", "kosgeb.gov.tr",
            "https://www.kosgeb.gov.tr", "0 9 * * *",
            StartUrl: "/site/tr/genel/destekler/3/destekler",
            ListSelector: "a[href*='/destekler/']",
            ContentSelector: "#content, .destek-detay",
            UrlPattern: "/destek", MaxPages: 10, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "KOBİ destek programları."),

        new("TÜBİTAK TEYDEB", SourceType.KosgebOrSimilar, SourceCategory.Fund,
            "TÜBİTAK", "TR", "tubitak.gov.tr",
            "https://www.tubitak.gov.tr", "0 9 * * *",
            StartUrl: "/tr/destekler",
            ListSelector: "a[href*='/destekler/']",
            ContentSelector: "#content, .icerik",
            UrlPattern: "destek|cagri|program", MaxPages: 10, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Ar-Ge ve yenilik destek çağrıları."),

        new("Sanayi ve Teknoloji Bakanlığı", SourceType.Ministry, SourceCategory.Incentive,
            "Sanayi ve Teknoloji Bakanlığı", "TR", "sanayi.gov.tr",
            "https://www.sanayi.gov.tr", "0 9 * * *",
            StartUrl: "/", ListSelector: "a[href*='duyuru'], a[href*='destek']",
            ContentSelector: "#content, .icerik",
            UrlPattern: "duyuru|destek|tesvik", MaxPages: 5, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Yatırım teşvik ve sanayi destekleri."),

        new("Çukurova Kalkınma Ajansı", SourceType.DevelopmentAgency, SourceCategory.Grant,
            "Çukurova Kalkınma Ajansı", "TR", "cka.org.tr",
            "https://www.cka.org.tr", "0 9 * * 1-5",
            StartUrl: "/", ListSelector: "a[href*='destek'], a[href*='cagri'], a[href*='duyuru']",
            ContentSelector: "#content, .entry-content, article",
            UrlPattern: "destek|cagri|program|duyuru", MaxPages: 10, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Kalkınma ajansı pilotu (TR62 – Adana, Mersin)."),

        // ─────────────────── Türkiye: ihale ───────────────────
        new("EKAP – Elektronik Kamu Alımları Platformu", SourceType.TenderPortal, SourceCategory.Tender,
            "Kamu İhale Kurumu", "TR", "ekap.kik.gov.tr",
            "https://ekap.kik.gov.tr", "0 */6 * * *",
            StartUrl: "/EKAP/Ortak/IhaleArama/index.html",
            ListSelector: "a[href*='IhaleDetay'], a[href*='ilan']",
            ContentSelector: "#content, .ihale-detay",
            UrlPattern: "ihale|ilan", MaxPages: 10, Language: "tr",
            DocumentTypes: "text/html,application/pdf",
            Note: "Kamu ihale ilanları. Erişim koşulları kuruma aittir."),

        // ─────────────────── Avrupa Birliği ───────────────────
        new("EU Funding & Tenders Portal", SourceType.EuOrInternational, SourceCategory.EuProgramme,
            "European Commission", "EU", "ec.europa.eu",
            "https://ec.europa.eu", "0 10 * * *",
            // Portalın kendisi Angular ile çizilir ve statik HTML'de bağlantı yoktur.
            // Kurum, tarayıcılar için sunucuda üretilen bu listeyi ayrıca yayımlar.
            StartUrl: "/info/funding-tenders/opportunities/data/topic-list.html",
            ListSelector: "a[href*='/topic-details/']",
            ContentSelector: "main, .eui-page-content",
            UrlPattern: "topic-details|call", MaxPages: 10, Language: "en",
            DocumentTypes: "text/html,application/pdf",
            Note: "AB fon ve ihale çağrıları."),

        new("EUR-Lex", SourceType.EuOrInternational, SourceCategory.Regulation,
            "Publications Office of the European Union", "EU", "eur-lex.europa.eu",
            "https://eur-lex.europa.eu", "0 10 * * *",
            StartUrl: "/oj/direct-access.html",
            ListSelector: "a[href*='legal-content']",
            ContentSelector: "#docHtml, .panel-body",
            UrlPattern: "legal-content|TXT", MaxPages: 10, Language: "en",
            DocumentTypes: "text/html,application/pdf",
            Note: "AB mevzuatının resmî kaynağı."),

        new("European Innovation Council", SourceType.EuOrInternational, SourceCategory.Fund,
            "European Innovation Council", "EU", "eic.ec.europa.eu",
            "https://eic.ec.europa.eu", "0 10 * * *",
            StartUrl: "/eic-funding-opportunities_en",
            ListSelector: "a[href*='funding-opportunities'], a[href*='calls']",
            ContentSelector: "main, .ecl-container",
            UrlPattern: "funding|call|opportunit", MaxPages: 10, Language: "en",
            DocumentTypes: "text/html,application/pdf",
            Note: "Derin teknoloji ve ölçeklenme fonları."),

        new("EISMEA", SourceType.EuOrInternational, SourceCategory.EuProgramme,
            "European Innovation Council and SMEs Executive Agency", "EU", "eismea.ec.europa.eu",
            "https://eismea.ec.europa.eu", "0 10 * * *",
            StartUrl: "/funding-opportunities_en",
            ListSelector: "a[href*='funding'], a[href*='call']",
            ContentSelector: "main, .ecl-container",
            UrlPattern: "funding|call|programme", MaxPages: 10, Language: "en",
            DocumentTypes: "text/html,application/pdf",
            Note: "KOBİ ve inovasyon programları."),
    ];

    /// <summary>Tanımı, doğrulanmamış ve kapalı bir kaynak kaydına çevirir.</summary>
    public static Source ToSource(Definition definition)
    {
        var source = new Source(definition.Name, definition.Type, definition.BaseUrl, definition.CronExpression);

        source.Describe(
            definition.Category,
            new SourceProfile(
                definition.Authority,
                definition.Jurisdiction,
                definition.OfficialDomain,
                definition.Language));

        source.PlanCrawl(new SourceCrawlPlan(
            definition.StartUrl,
            definition.ListSelector,
            definition.ContentSelector,
            definition.UrlPattern,
            definition.MaxPages,
            AllowedDomains: definition.OfficialDomain,
            DocumentTypes: definition.DocumentTypes));

        // Doğrulanana kadar KAPALI. Aktifmiş gibi gösterilmez.
        source.Disable();

        return source;
    }
}
