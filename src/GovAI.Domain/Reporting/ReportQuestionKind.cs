namespace GovAI.Domain.Reporting;

/// <summary>
/// Soru türü. Metin değil <b>anahtar</b> saklanır.
///
/// <para>
/// Kullanıcı soru yazmaz, listeden seçer. Bu bir kolaylık değil <b>tasarım kararıdır</b>:
/// serbest metin, cevabı raporun dışına taşır ve sistemin bilmediği bir şeye cevap
/// uydurmasına zemin hazırlar. Sabit anahtar kümesi, her sorunun rapordaki hangi veriden
/// cevaplanacağının önceden belli olmasını sağlar.
/// </para>
/// </summary>
public enum ReportQuestionKind
{
    /// <summary>Bu hafta en acil ne yapmalıyım?</summary>
    EnAcilIs = 1,

    /// <summary>Hangi eksiğimi kapatırsam en çok çağrı açılır?</summary>
    EnEtkiliEksik = 2,

    /// <summary>Hangi çağrıya önce başvurmalıyım?</summary>
    BasvuruOnceligi = 3,

    /// <summary>Belirli bir çağrıya neden tam uygun değilim?</summary>
    CagriNedenSartli = 4,

    /// <summary>Sektör uyumum neden doğrulanamadı?</summary>
    SektorUyumu = 5,

    /// <summary>Hangi belgeleri temin etmem gerekiyor?</summary>
    EksikBelgeler = 6,

    /// <summary>Bu haftaki mevzuat değişiklikleri beni nasıl etkiliyor?</summary>
    MevzuatEtkisi = 7,

    /// <summary>Geçmiş dönem eksiklerim neden hâlâ duruyor?</summary>
    GecmisDonemEksikleri = 8,

    /// <summary>Profilimdeki eksik bilgiler kararımı nasıl etkiliyor?</summary>
    EksikProfilEtkisi = 9,

    /// <summary>Teknoloji ihalelerinde neden az kayıt var?</summary>
    TeknolojiIhaleleri = 10,
}
