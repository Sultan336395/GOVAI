namespace GovAI.Domain.Tenders;

/// <summary>
/// Bir ihaleye başvuru sürecinin bulunduğu aşama.
///
/// <para>
/// Dört aşama kullanıcının kendi anlattığı akıştır. <see cref="Vazgecildi"/> beşinci
/// değer olarak eklendi çünkü onsuz bırakılan bir ihale ya silinmek zorunda kalırdı
/// (geçmiş kaybolur) ya da "Hazırlanıyor" durumunda sonsuza kadar takılı kalıp listeyi
/// kirletirdi.
/// </para>
/// </summary>
public enum TenderPursuitStatus
{
    /// <summary>İlgileniliyor, şartname ve koşullar okunuyor.</summary>
    Inceleniyor = 1,

    /// <summary>Karar verildi; belge ve teklif dosyası hazırlanıyor.</summary>
    Hazirlaniyor = 2,

    /// <summary>Teklif kuruma sunuldu; sonuç bekleniyor.</summary>
    TeklifVerildi = 3,

    /// <summary>Kurum kararını açıkladı. Sonuç ayrıca <c>TenderOutcome</c> ile yazılır.</summary>
    Sonuclandi = 4,

    /// <summary>Takip bırakıldı. Kayıt silinmez; neden bırakıldığı notta kalır.</summary>
    Vazgecildi = 5
}

/// <summary>
/// Sonuçlanmış bir ihalenin kurum kararı.
///
/// <para>
/// "Sonuçlandı" tek başına bir bilgi taşımaz; kazanılan ile kaybedilen ihale aynı
/// satırda görünürse liste ne raporlanabilir ne de öğrenmeye yarar. Bu yüzden
/// <see cref="TenderPursuitStatus.Sonuclandi"/> sonucu <b>zorunlu</b> kılar.
/// </para>
/// </summary>
public enum TenderOutcome
{
    /// <summary>İhale kazanıldı.</summary>
    Kazanildi = 1,

    /// <summary>Başka istekliye verildi.</summary>
    Kaybedildi = 2,

    /// <summary>Kurum ihaleyi iptal etti veya yeniledi.</summary>
    Iptal = 3
}
