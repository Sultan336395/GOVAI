using GovAI.Domain.Eligibility;

namespace GovAI.Domain.Companies;

/// <summary>Tek bir profil alanının bilinip bilinmediği.</summary>
/// <param name="Field">Kural motorundaki alan adı (ör. <c>Workforce.WomenEmployeeCount</c>).</param>
/// <param name="Label">Kullanıcıya gösterilecek ad.</param>
/// <param name="IsKnown">Motor bu alanı okuduğunda bir değer buluyor mu?</param>
public readonly record struct ProfileFieldState(string Field, string Label, bool IsKnown);

/// <summary>
/// Profil doluluğunun ölçüsü.
///
/// <para>
/// Ölçü, doldurulmuş form alanlarını saymaz; <b>kural motorunun okuduğu</b> alanlardan
/// kaçının değer döndürdüğünü sayar. Aradaki fark önemlidir: kullanıcıya "profiliniz
/// %80 dolu" demek, o %20'nin hangi çağrılarda karar veremediğini anlatmıyorsa bir işe
/// yaramaz. Buradaki her eksik alan, o alana bakan her kuralda <c>Unknown</c> demektir
/// (CLAUDE.md §2.2) — yani karar "belirsiz" çıkar, firma elenmez ama uygun da sayılmaz.
/// </para>
///
/// <para>
/// Sertifika listesi ölçüye <b>girmez</b>: boş sertifika kümesi "belgemiz yok" demektir
/// ve bu geçerli bir cevaptır (ADR-0003). Eksik sayılsaydı kullanıcı hiç sahip olmadığı
/// bir belgeyi girmeye çalışırdı.
/// </para>
///
/// <para>
/// Hesap deterministiktir: girdisi firma ile <paramref name="asOf"/>'tur, içeride tarih
/// okunmaz.
/// </para>
/// </summary>
public static class ProfileCompleteness
{
    /// <summary>Boş kümesi geçerli bir cevap olan alanlar ölçünün dışındadır.</summary>
    private static readonly string[] OlcuDisi = ["Company.Certificates"];

    public static IReadOnlyList<ProfileFieldState> Evaluate(Company company, DateOnly asOf) =>
        CompanyFieldResolver.SupportedFields.Keys
            .Where(f => !OlcuDisi.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Select(f => new ProfileFieldState(
                f,
                CompanyFieldResolver.Label(f),
                CompanyFieldResolver.Resolve(company, f, asOf).IsKnown))
            .ToList();

    /// <summary>Bilinen alan yüzdesi (0–100). Alan yoksa 0 döner, sıfıra bölünmez.</summary>
    public static int Percentage(IReadOnlyList<ProfileFieldState> states) =>
        states.Count == 0
            ? 0
            : (int)Math.Round(states.Count(s => s.IsKnown) * 100.0 / states.Count);
}
