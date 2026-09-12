using GovAI.Domain.Evidence;

namespace GovAI.Domain.Tests;

/// <summary>
/// Kanıt eskime motoru.
///
/// <para>
/// Motorun tek iddiası şu: "belge var" ile "belge hâlâ güvenilir" farklı şeylerdir.
/// Testler bu farkın kaybolmadığını ve farkı ölçerken ürünün üçüncü iddiasının
/// çiğnenmediğini sabitler — tarihi bilinmeyen bir kanıt <b>çürük sayılmaz</b>,
/// ölçülemez sayılır.
/// </para>
/// </summary>
public class EvidenceHalfLifeTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 12);

    private static EvidenceSnapshot Kanit(
        EvidenceKind tur = EvidenceKind.WorkforceDeclaration,
        DateOnly? gozlem = null,
        DateOnly? gecerlilik = null,
        EvidenceAssurance guvence = EvidenceAssurance.SelfDeclared,
        bool metinDegisti = false) =>
        new()
        {
            Kind = tur,
            Label = "Örnek kanıt",
            ObservedOn = gozlem,
            ValidUntil = gecerlilik,
            Assurance = guvence,
            SourceSuperseded = metinDegisti,
        };

    private static EvidenceReliability Olc(EvidenceSnapshot kanit) =>
        EvidenceHalfLife.Evaluate(kanit, Bugun);

    // ── Eksik veri kanıtı çürütmez ──────────────────────────────────────────

    [Fact(DisplayName = "EY1. Tarihi BİLİNMEYEN kanıt güvenilmez sayılmaz")]
    public void Tarihsiz_kanit_bilinmiyor()
    {
        // Çürük saysaydık, belgesi olan ama tarihini girmemiş firmayı elinde olmayan
        // bir eksikten cezalandırırdık. Doğru cevap "tarihini girin"dir.
        var sonuc = Olc(Kanit(gozlem: null));

        Assert.Equal(EvidenceStatus.Bilinmiyor, sonuc.Status);
        Assert.Null(sonuc.Score);
        Assert.NotEqual(EvidenceStatus.Guvenilmez, sonuc.Status);
    }

    [Fact(DisplayName = "EY2. Bilinmeyen kanıt karar dayanağı da SAYILMAZ")]
    public void Bilinmeyen_dayanak_olmaz()
    {
        // "Çürük değil" ile "güvenilir" aynı şey değildir; ikisini karıştırmak
        // ölçülemeyen kanıta tam güven vermek olurdu.
        Assert.False(Olc(Kanit(gozlem: null)).IsDependable);
    }

    [Fact(DisplayName = "EY3. Ortalama skor BİLİNMEYENLERİ dışarıda bırakır")]
    public void Ortalama_bilinmeyeni_saymaz()
    {
        // Sıfır sayılsaydı, tarihi girilmemiş her kanıt portföy skorunu aşağı çekerdi.
        var taze = Olc(Kanit(gozlem: Bugun));
        var bilinmeyen = Olc(Kanit(gozlem: null));

        var ozet = EvidenceHalfLife.Summarize([taze, bilinmeyen]);

        Assert.Equal(2, ozet.Total);
        Assert.Equal(1, ozet.Unknown);
        Assert.Equal(1m, ozet.AverageScore);
    }

    [Fact(DisplayName = "EY4. Hiçbiri ölçülemiyorsa ortalama YOKTUR, sıfır değil")]
    public void Olculemeyen_portfoyde_ortalama_yok()
    {
        var ozet = EvidenceHalfLife.Summarize([Olc(Kanit(gozlem: null))]);

        Assert.Null(ozet.AverageScore);
    }

    // ── Eskime ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "EY5. Bugün elde edilen kanıt tam güvenilirdir")]
    public void Bugunku_kanit_tam()
    {
        var sonuc = Olc(Kanit(gozlem: Bugun));

        Assert.Equal(1m, sonuc.Score);
        Assert.Equal(EvidenceStatus.Guvenilir, sonuc.Status);
    }

    [Fact(DisplayName = "EY6. Yarı ömür kadar eskiyen kanıtın skoru YARIYA iner")]
    public void Yari_omurde_yariya_iner()
    {
        // Modelin tanımı budur; sapması tüm eşikleri anlamsızlaştırır.
        var yariOmur = EvidenceHalfLife.HalfLifeFor(
            EvidenceKind.WorkforceDeclaration, EvidenceAssurance.SelfDeclared);

        var sonuc = Olc(Kanit(gozlem: Bugun.AddDays(-yariOmur)));

        Assert.Equal(0.5m, sonuc.Score);
    }

    [Fact(DisplayName = "EY7. Kanıt eskidikçe skor DÜŞER, artmaz")]
    public void Eskidikce_duser()
    {
        var taze = Olc(Kanit(gozlem: Bugun.AddDays(-10)));
        var orta = Olc(Kanit(gozlem: Bugun.AddDays(-120)));
        var eski = Olc(Kanit(gozlem: Bugun.AddDays(-500)));

        Assert.True(taze.Score > orta.Score);
        Assert.True(orta.Score > eski.Score);
    }

    [Fact(DisplayName = "EY8. Çok eski kanıt SIFIRA inmez")]
    public void Cok_eski_sifir_olmaz()
    {
        // İki yıllık bir beyan bile hiç beyan olmamasından iyidir; sıfırlamak
        // "hiç bilgi yok" demek olurdu ve bilgiyi kaybederdik.
        var sonuc = Olc(Kanit(gozlem: Bugun.AddDays(-2000)));

        Assert.True(sonuc.Score > 0m);
        Assert.Equal(EvidenceStatus.Guvenilmez, sonuc.Status);
    }

    [Fact(DisplayName = "EY9. GELECEK tarihli gözlem kanıtı ödüllendirmez")]
    public void Gelecek_tarih_odullendirmez()
    {
        // Veri hatası ya da yanlış giriş; negatif yaş üstel modelde skoru 1'in
        // üstüne çıkarırdı.
        var sonuc = Olc(Kanit(gozlem: Bugun.AddDays(60)));

        Assert.Equal(1m, sonuc.Score);
    }

    // ── Güvence düzeyi ──────────────────────────────────────────────────────

    [Fact(DisplayName = "EY10. Onaylı mali veri, firma beyanından YAVAŞ eskir")]
    public void Onayli_veri_yavas_eskir()
    {
        var beyan = Olc(Kanit(EvidenceKind.FinancialStatement, Bugun.AddDays(-365)));
        var onayli = Olc(Kanit(
            EvidenceKind.FinancialStatement, Bugun.AddDays(-365), guvence: EvidenceAssurance.Verified));

        Assert.True(onayli.Score > beyan.Score);
    }

    [Fact(DisplayName = "EY11. ERP anlık görüntüsü en hızlı eskir")]
    public void Erp_hizli_eskir()
    {
        // Sürekli değişen bir sistemin fotoğrafıdır; bir aylık ERP verisi bir aylık
        // sertifikayla aynı güveni taşıyamaz.
        var erp = EvidenceHalfLife.HalfLifeFor(EvidenceKind.ErpSnapshot, EvidenceAssurance.SelfDeclared);
        var beyan = EvidenceHalfLife.HalfLifeFor(EvidenceKind.WorkforceDeclaration, EvidenceAssurance.SelfDeclared);

        Assert.True(erp < beyan);
    }

    // ── Süresi yazılı belgeler ──────────────────────────────────────────────

    [Fact(DisplayName = "EY12. Süresi DOLMUŞ belge kesin geçersizdir")]
    public void Suresi_dolmus_gecersiz()
    {
        var sonuc = Olc(Kanit(
            EvidenceKind.Certificate, gozlem: Bugun.AddDays(-400), gecerlilik: Bugun.AddDays(-1)));

        Assert.Equal(EvidenceStatus.Gecersiz, sonuc.Status);
        Assert.Equal(0m, sonuc.Score);
        Assert.False(sonuc.IsDependable);
    }

    [Fact(DisplayName = "EY13. DÜN alınmış ama yarın bitecek belge taze SAYILMAZ")]
    public void Yeni_ama_bitmek_uzere_olan_taze_degil()
    {
        // Ölçüt yaş değil KALAN SÜREDİR. Yaşa bakılsaydı bu belge "bir günlük, tertemiz"
        // görünür ve firma yenileme fırsatını kaçırırdı.
        var sonuc = Olc(Kanit(
            EvidenceKind.Certificate, gozlem: Bugun.AddDays(-1), gecerlilik: Bugun.AddDays(1)));

        Assert.NotEqual(EvidenceStatus.Guvenilir, sonuc.Status);
        Assert.True(sonuc.Score < 0.1m);
    }

    [Fact(DisplayName = "EY14. Yenileme penceresinin dışındaki belge tam güvenilirdir")]
    public void Pencere_disinda_tam_guven()
    {
        var sonuc = Olc(Kanit(
            EvidenceKind.Certificate,
            gozlem: Bugun.AddDays(-30),
            gecerlilik: Bugun.AddDays(EvidenceHalfLife.CertificateRenewalWindowDays + 10)));

        Assert.Equal(1m, sonuc.Score);
        Assert.Equal(EvidenceStatus.Guvenilir, sonuc.Status);
    }

    [Fact(DisplayName = "EY15. Yenileme penceresine girince skor düşmeye başlar")]
    public void Pencerede_skor_duser()
    {
        // Bitiş gününde uyarmak, uyarmamakla aynı kapıya çıkardı: denetim randevusu ve
        // kurum yanıtı haftalar sürer.
        var pencere = EvidenceHalfLife.CertificateRenewalWindowDays;

        var disinda = Olc(Kanit(EvidenceKind.Certificate, gecerlilik: Bugun.AddDays(pencere + 1)));
        var icinde = Olc(Kanit(EvidenceKind.Certificate, gecerlilik: Bugun.AddDays(pencere / 2)));

        Assert.Equal(1m, disinda.Score);
        Assert.True(icinde.Score < 1m);
    }

    [Fact(DisplayName = "EY16. Bitiş günü hâlâ geçerlidir")]
    public void Bitis_gunu_gecerli()
    {
        // Sınır hatası: son gün "dolmuş" sayılsaydı firma geçerli belgesiyle elenirdi.
        var sonuc = Olc(Kanit(EvidenceKind.Certificate, gecerlilik: Bugun));

        Assert.NotEqual(EvidenceStatus.Gecersiz, sonuc.Status);
    }

    // ── Mevzuat değişikliği ─────────────────────────────────────────────────

    [Fact(DisplayName = "EY17. Dayandığı metin değişen kanıt YAŞINDAN BAĞIMSIZ geçersizdir")]
    public void Metin_degisince_gecersiz()
    {
        // Bu bir eskime değil olaydır. Dün bağlanmış olması kanıtı kurtarmaz.
        var sonuc = Olc(Kanit(EvidenceKind.RegulationClause, gozlem: Bugun, metinDegisti: true));

        Assert.Equal(EvidenceStatus.Gecersiz, sonuc.Status);
        Assert.Equal(0m, sonuc.Score);
    }

    [Fact(DisplayName = "EY18. Metin değişikliği süresi dolmamış belgeyi de geçersiz kılar")]
    public void Metin_degisikligi_gecerliligi_yener()
    {
        // Sıra önemli: önce kesin geçersizlik sebepleri bakılır. Ters sırada geçerli
        // görünen bir belge, dayanağı kalkmış olmasına rağmen yeşil kalırdı.
        var sonuc = Olc(Kanit(
            EvidenceKind.Certificate, gozlem: Bugun, gecerlilik: Bugun.AddYears(3), metinDegisti: true));

        Assert.Equal(EvidenceStatus.Gecersiz, sonuc.Status);
    }

    // ── Açıklanabilirlik ────────────────────────────────────────────────────

    [Fact(DisplayName = "EY19. Her sonuç bir GEREKÇE taşır")]
    public void Her_sonuc_gerekceli()
    {
        // §2'nin ikinci iddiası: skor "model böyle dedi" diye açıklanamaz.
        EvidenceSnapshot[] ornekler =
        [
            Kanit(gozlem: null),
            Kanit(gozlem: Bugun),
            Kanit(gozlem: Bugun.AddDays(-900)),
            Kanit(EvidenceKind.Certificate, gecerlilik: Bugun.AddDays(-5)),
            Kanit(EvidenceKind.Certificate, gecerlilik: Bugun.AddDays(10)),
            Kanit(metinDegisti: true),
        ];

        foreach (var ornek in ornekler)
        {
            Assert.False(string.IsNullOrWhiteSpace(Olc(ornek).Reason));
        }
    }

    [Fact(DisplayName = "EY20. Aynı kanıt aynı gün için AYNI skoru verir")]
    public void Deterministik()
    {
        // §2.1: alan katmanında rastgelelik ve DateTime.Now yoktur.
        var kanit = Kanit(gozlem: Bugun.AddDays(-77));

        Assert.Equal(
            EvidenceHalfLife.Evaluate(kanit, Bugun).Score,
            EvidenceHalfLife.Evaluate(kanit, Bugun).Score);
    }

    [Fact(DisplayName = "EY21. Zayıflama günü gelecekteyse bildirilir, geçmişteyse bildirilmez")]
    public void Zayiflama_gunu()
    {
        // Geçmiş bir tarih "şu gün zayıflayacak" diye gösterilseydi anlamsız olurdu.
        var taze = Olc(Kanit(gozlem: Bugun));
        var eski = Olc(Kanit(gozlem: Bugun.AddDays(-900)));

        Assert.NotNull(taze.WeakensOn);
        Assert.True(taze.WeakensOn > Bugun);
        Assert.Null(eski.WeakensOn);
    }

    // ── Portföy özeti ───────────────────────────────────────────────────────

    [Fact(DisplayName = "EY22. Portföy özeti durumları ayrı ayrı sayar")]
    public void Portfoy_ozeti()
    {
        var kayitlar = new[]
        {
            Olc(Kanit(gozlem: Bugun)),
            Olc(Kanit(gozlem: Bugun.AddDays(-150))),
            Olc(Kanit(gozlem: Bugun.AddDays(-900))),
            Olc(Kanit(EvidenceKind.Certificate, gecerlilik: Bugun.AddDays(-1))),
            Olc(Kanit(gozlem: null)),
        };

        var ozet = EvidenceHalfLife.Summarize(kayitlar);

        Assert.Equal(5, ozet.Total);
        Assert.Equal(1, ozet.Expired);
        Assert.Equal(1, ozet.Unknown);
        Assert.Equal(1, ozet.Undependable);
        Assert.Equal(2, ozet.NeedsAttention);
    }

    [Fact(DisplayName = "EY23. Boş portföy çökmez")]
    public void Bos_portfoy()
    {
        var ozet = EvidenceHalfLife.Summarize([]);

        Assert.Equal(0, ozet.Total);
        Assert.Null(ozet.AverageScore);
    }
}
