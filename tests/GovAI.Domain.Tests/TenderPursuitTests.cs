using GovAI.Domain.Common;
using GovAI.Domain.Tenders;

namespace GovAI.Domain.Tests;

/// <summary>
/// İhale takibinin alan modeli.
///
/// <para>
/// Takip firmanın kendi beyanıdır; burada korunan şey doğruluk değil
/// <b>izlenebilirliktir</b>: her aşama değişikliği kim ve ne zaman diye kaydedilir,
/// hiçbir geçiş sessizce kaybolmaz.
/// </para>
/// </summary>
public class TenderPursuitTests
{
    private static readonly DateTimeOffset Baslangic = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static TenderPursuit Olustur(string? not = null) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Baslangic, "kullanici@firma.test", not);

    [Fact(DisplayName = "İT1. Yeni takip incelemede başlar ve geçmişe ilk satırı yazar")]
    public void Yeni_takip_incelemede_baslar()
    {
        var takip = Olustur("Şartname indirildi.");

        Assert.Equal(TenderPursuitStatus.Inceleniyor, takip.Status);
        Assert.Null(takip.Outcome);
        Assert.False(takip.IsClosed);
        Assert.Equal("Şartname indirildi.", takip.Note);

        var ilk = Assert.Single(takip.Events);

        // İlk satırda "önceki durum" yoktur; takip o anda açılmıştır.
        Assert.Null(ilk.FromStatus);
        Assert.Equal(TenderPursuitStatus.Inceleniyor, ilk.ToStatus);
        Assert.Equal("kullanici@firma.test", ilk.By);
    }

    [Fact(DisplayName = "İT2. Aşama değişikliği geçmişe önceki ve yeni durumu yazar")]
    public void Asama_degisikligi_kaydedilir()
    {
        var takip = Olustur();

        takip.ChangeStatus(
            TenderPursuitStatus.Hazirlaniyor, null, "Teklif dosyası açıldı.",
            Baslangic.AddDays(2), "uzman@firma.test");

        Assert.Equal(TenderPursuitStatus.Hazirlaniyor, takip.Status);
        Assert.Equal(Baslangic.AddDays(2), takip.StatusChangedAt);

        var son = takip.Events.Last();

        Assert.Equal(TenderPursuitStatus.Inceleniyor, son.FromStatus);
        Assert.Equal(TenderPursuitStatus.Hazirlaniyor, son.ToStatus);
        Assert.Equal("uzman@firma.test", son.By);
    }

    [Fact(DisplayName = "İT3. Sonuçlanan ihalede SONUÇ zorunludur")]
    public void Sonuclanan_ihale_sonuc_ister()
    {
        // "Sonuçlandı" tek başına kazanılan ile kaybedilen ihaleyi aynı satırda gösterirdi.
        var takip = Olustur();

        var hata = Assert.Throws<DomainException>(() => takip.ChangeStatus(
            TenderPursuitStatus.Sonuclandi, null, null, Baslangic.AddDays(30), "x@y.test"));

        Assert.Contains("sonuc", hata.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TenderPursuitStatus.Inceleniyor, takip.Status);
    }

    [Fact(DisplayName = "İT4. Sonuçlanmamış ihaleye sonuç yazılamaz")]
    public void Sonuclanmamis_ihaleye_sonuc_yazilamaz()
    {
        var takip = Olustur();

        Assert.Throws<DomainException>(() => takip.ChangeStatus(
            TenderPursuitStatus.Hazirlaniyor, TenderOutcome.Kazanildi, null,
            Baslangic.AddDays(1), "x@y.test"));
    }

    [Fact(DisplayName = "İT5. Sonuçlanan ihale kapanır ve sonucu taşır")]
    public void Sonuclanan_ihale_kapanir()
    {
        var takip = Olustur();

        takip.ChangeStatus(
            TenderPursuitStatus.Sonuclandi, TenderOutcome.Kazanildi, "Sözleşme imzalandı.",
            Baslangic.AddDays(40), "x@y.test");

        Assert.True(takip.IsClosed);
        Assert.Equal(TenderOutcome.Kazanildi, takip.Outcome);
    }

    [Fact(DisplayName = "İT6. Vazgeçilen takip kapanır ama SİLİNMEZ")]
    public void Vazgecilen_takip_kapanir()
    {
        // Kayıt silinseydi "bu ihaleye neden girmedik" sorusunun cevabı da kaybolurdu.
        var takip = Olustur();

        takip.ChangeStatus(
            TenderPursuitStatus.Vazgecildi, null, "Teminat tutarı bütçeyi aşıyor.",
            Baslangic.AddDays(3), "x@y.test");

        Assert.True(takip.IsClosed);
        Assert.Equal("Teminat tutarı bütçeyi aşıyor.", takip.Note);
        Assert.Equal(2, takip.Events.Count);
    }

    [Fact(DisplayName = "İT7. Aynı aşamaya yeniden geçmek geçmişe satır EKLEMEZ")]
    public void Ayni_asama_tekrar_yazilmaz()
    {
        // Arayüzde iki kez tıklamak kaydı aynı durumun tekrarlarıyla doldururdu.
        var takip = Olustur("not");

        takip.ChangeStatus(
            TenderPursuitStatus.Inceleniyor, null, "not", Baslangic.AddDays(1), "x@y.test");

        Assert.Single(takip.Events);
    }

    [Fact(DisplayName = "İT8. Aşama aynı kalsa da NOT değiştiyse kaydedilir")]
    public void Not_degisirse_kaydedilir()
    {
        var takip = Olustur("ilk not");

        takip.ChangeStatus(
            TenderPursuitStatus.Inceleniyor, null, "kurum ek süre verdi",
            Baslangic.AddDays(1), "x@y.test");

        Assert.Equal(2, takip.Events.Count);
        Assert.Equal("kurum ek süre verdi", takip.Note);
    }

    [Fact(DisplayName = "İT9. Geriye dönüş SERBESTTİR")]
    public void Geriye_donus_serbesttir()
    {
        // Katı bir boru hattı sahayla çatışırdı: hazırlık aşamasından incelemeye dönmek,
        // iptal edilip yenilenen bir ihaleyi yeniden açmak olağan durumlardır.
        var takip = Olustur();

        takip.ChangeStatus(
            TenderPursuitStatus.Hazirlaniyor, null, null, Baslangic.AddDays(1), "x@y.test");
        takip.ChangeStatus(
            TenderPursuitStatus.Inceleniyor, null, null, Baslangic.AddDays(2), "x@y.test");

        Assert.Equal(TenderPursuitStatus.Inceleniyor, takip.Status);
        Assert.Equal(3, takip.Events.Count);
    }

    [Fact(DisplayName = "İT10. Kapanmış takip yeniden açılabilir ve sonuç temizlenir")]
    public void Kapanan_takip_yeniden_acilabilir()
    {
        var takip = Olustur();

        takip.ChangeStatus(
            TenderPursuitStatus.Sonuclandi, TenderOutcome.Iptal, null,
            Baslangic.AddDays(30), "x@y.test");

        // Kurum ihaleyi yenilerse süreç baştan başlar; eski sonucun kalması yanıltırdı.
        takip.ChangeStatus(
            TenderPursuitStatus.Inceleniyor, null, null, Baslangic.AddDays(35), "x@y.test");

        Assert.False(takip.IsClosed);
        Assert.Null(takip.Outcome);
    }

    [Fact(DisplayName = "İT11. Boş not null sayılır")]
    public void Bos_not_null_sayilir()
    {
        var takip = Olustur("   ");

        Assert.Null(takip.Note);
    }

    [Fact(DisplayName = "İT12. Takibi yürüten kişi geçmişe satır AÇMAZ")]
    public void Sorumlu_atamasi_gecmise_yazilmaz()
    {
        // Aşama değişmemiştir; her atamayı geçmişe yazmak süreç izini gürültüye boğardı.
        var takip = Olustur();

        takip.AssignOwner("Ayşe Yılmaz");

        Assert.Equal("Ayşe Yılmaz", takip.Owner);
        Assert.Single(takip.Events);
    }

    [Fact(DisplayName = "İT13. Firma ve ihale kimliği zorunludur")]
    public void Kimlikler_zorunludur()
    {
        Assert.Throws<DomainException>(() => new TenderPursuit(
            Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), Baslangic, "x@y.test"));

        Assert.Throws<DomainException>(() => new TenderPursuit(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty, Baslangic, "x@y.test"));
    }
}
