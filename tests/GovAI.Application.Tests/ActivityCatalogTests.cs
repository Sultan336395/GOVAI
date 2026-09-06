using GovAI.Application.Common;
using GovAI.Application.Reference;

namespace GovAI.Application.Tests;

/// <summary>
/// Sektör ve NACE referans kataloğu (Faz 2).
///
/// Sektör ile NACE kodu serbest metin olarak giriliyordu ve sahada çelişti: bir firmanın
/// sektörü "inşaat" yazarken NACE kodu <c>2562</c> (metal işleme) kalmıştı. Motor NACE
/// koduna bakar; firma kendi sektöründeki ihalelerde "sektör uyumsuz" görünüyordu.
///
/// Katalog tek kaynaktır — arayüzün önerdiği liste ile sunucunun kabul ettiği liste
/// aynıdır. Bu testler o sözü ve arama davranışını sabitler.
/// </summary>
public sealed class ActivityCatalogTests
{
    [Fact(DisplayName = "AC1. Katalog boş değildir ve NACE bölümlerinin tamamını içerir")]
    public void Katalog_bolumlerin_tamamini_icerir()
    {
        // NACE Rev. 2'de 88 bölüm vardır (04, 34, 40, 44, 48, 54, 57, 67, 76, 83, 89 yoktur).
        var bolumler = ActivityCatalog.NaceCodes
            .Where(n => n.Code.Length == 2)
            .Select(n => n.Code)
            .ToList();

        Assert.Equal(88, bolumler.Count);
        Assert.Equal(bolumler.Count, bolumler.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "AC2. Her NACE kaydı bir sektöre bağlıdır")]
    public void Her_kod_bir_sektore_baglidir()
    {
        var sektorAdlari = ActivityCatalog.Sectors.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(ActivityCatalog.NaceCodes, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Title), n.Code);
            Assert.Contains(n.Sector, sektorAdlari);
        });
    }

    [Fact(DisplayName = "AC3. Aynı kod iki kez listelenmez")]
    public void Kodlar_benzersizdir()
    {
        var kodlar = ActivityCatalog.NaceCodes.Select(n => n.Code).ToList();

        Assert.Equal(kodlar.Count, kodlar.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "AC4. Her sektör en az bir NACE bölümü kapsar ve bölümler paylaşılmaz")]
    public void Sektor_bolum_eslemesi_tutarlidir()
    {
        var gorulen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sektor in ActivityCatalog.Sectors)
        {
            Assert.NotEmpty(sektor.Divisions);

            foreach (var bolum in sektor.Divisions)
            {
                // Bir bölüm iki sektöre bağlanırsa kodun sektörü sıraya bağlı kalır.
                Assert.False(gorulen.ContainsKey(bolum), $"{bolum}: {gorulen.GetValueOrDefault(bolum)} / {sektor.Name}");
                gorulen[bolum] = sektor.Name;
            }
        }
    }

    [Theory(DisplayName = "AC5. NACE araması koda göre çalışır")]
    [InlineData("256", "25.62")]
    [InlineData("25.6", "25.62")]
    [InlineData("620", "62.01")]
    [InlineData("41", "41.20")]
    public void Kod_aramasi_calisir(string sorgu, string beklenenKod)
    {
        var sonuc = ActivityCatalog.SearchNace(sorgu);

        Assert.Contains(sonuc, n => n.Code == beklenenKod);
    }

    [Theory(DisplayName = "AC6. NACE araması tanıma göre de çalışır")]
    [InlineData("yazılım")]
    [InlineData("inşaat")]
    [InlineData("nakliye")]
    [InlineData("temizlik")]
    public void Tanim_aramasi_calisir(string sorgu)
    {
        Assert.NotEmpty(ActivityCatalog.SearchNace(sorgu));
    }

    [Fact(DisplayName = "AC7. Türkçe büyük/küçük harf farkı aramayı bozmaz")]
    public void Turkce_harf_farki_aramayi_bozmaz()
    {
        // "İNŞAAT" ile "inşaat" ToLower ile eşleşmez; katlama olmadan kullanıcı kendi
        // yazdığı kelimeyi listede bulamaz.
        var kucuk = ActivityCatalog.SearchNace("inşaat").Select(n => n.Code).ToList();
        var buyuk = ActivityCatalog.SearchNace("İNŞAAT").Select(n => n.Code).ToList();

        Assert.NotEmpty(kucuk);
        Assert.Equal(kucuk, buyuk);
    }

    [Fact(DisplayName = "AC8. Üç karakterden kısa metin sorgusu öneri getirmez")]
    public void Kisa_sorgu_oneri_getirmez()
    {
        Assert.Empty(ActivityCatalog.SearchNace("ya"));
        Assert.Empty(ActivityCatalog.SearchNace(""));
        Assert.Empty(ActivityCatalog.SearchNace(null));
    }

    [Fact(DisplayName = "AC9. İki haneli kod sorgusu öneri getirir")]
    public void Iki_haneli_kod_oneri_getirir()
    {
        // Kod araması harf sayısına takılmamalı: "62" geçerli bir NACE bölümüdür.
        Assert.NotEmpty(ActivityCatalog.SearchNace("62"));
    }

    [Fact(DisplayName = "AC10. Kod eşleşmesi tanım eşleşmesinden önce gelir")]
    public void Kod_eslesmesi_once_gelir()
    {
        var sonuc = ActivityCatalog.SearchNace("620");

        Assert.StartsWith("62.0", sonuc[0].Code, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "AC11. Sektör verilirse liste o sektörle SINIRLANIR")]
    public void Sektor_verilirse_liste_sinirlanir()
    {
        // Sahada görülen hata: sektörü "İnşaat ve taahhüt" seçilmiş firmaya beton
        // ürünleri kodu (23.61) atandı. Kullanıcı tutmayan kodu göremezse seçemez de.
        var sonuc = ActivityCatalog.SearchNace("imalat", ["Makine ve ekipman imalatı"]);

        Assert.NotEmpty(sonuc);
        Assert.All(sonuc, n => Assert.Equal("Makine ve ekipman imalatı", n.Sector));
    }

    [Fact(DisplayName = "AC11b. Birden çok sektör verilebilir; birleşimleri döner")]
    public void Birden_cok_sektor_birlestirilir()
    {
        // Bir firma birden fazla alanda faaliyet gösterebilir; alt sektörünü beyan
        // ettiyse o alanın kodları da seçilebilir olmalıdır.
        var sonuc = ActivityCatalog.SearchNace("imalat", ["Makine ve ekipman imalatı", "Yapı malzemeleri ve cam"]);

        Assert.Contains(sonuc, n => n.Sector == "Makine ve ekipman imalatı");
        Assert.Contains(sonuc, n => n.Sector == "Yapı malzemeleri ve cam");
        Assert.DoesNotContain(sonuc, n => n.Sector == "Bilişim ve yazılım");
    }

    [Fact(DisplayName = "AC11c. Sektör verilmezse kısıt yoktur")]
    public void Sektor_verilmezse_kisit_yoktur()
    {
        var hepsi = ActivityCatalog.SearchNace("imalat");

        Assert.True(hepsi.Select(n => n.Sector).Distinct().Count() > 1);
    }

    [Fact(DisplayName = "AC11d. Sektör dışı kod aranınca hiç sonuç dönmez")]
    public void Sektor_disi_kod_bulunamaz()
    {
        // 23.61 "Yapı malzemeleri ve cam" sektörüne aittir; inşaat sektörü seçiliyken
        // aranarak da bulunamamalıdır, aksi hâlde filtre yalnızca gösterişten ibaret olur.
        var sonuc = ActivityCatalog.SearchNace("2361", ["İnşaat ve taahhüt"]);

        Assert.Empty(sonuc);
    }

    [Fact(DisplayName = "AC11h. Sektör verilince arama yapmadan tüm kodları listelenir")]
    public void Sektorun_tum_kodlari_listelenir()
    {
        // Kullanıcı alana tıkladığı anda listeyi görmeli; aranacak kelimeyi bilmek
        // zorunda kalmak, listeyi sektörle sınırlamanın kolaylığını geri alırdı.
        var liste = ActivityCatalog.ListNace(["İnşaat ve taahhüt"]);

        Assert.NotEmpty(liste);
        Assert.All(liste, n => Assert.Equal("İnşaat ve taahhüt", n.Sector));
        Assert.Contains(liste, n => n.Code == "41.20");
        Assert.Contains(liste, n => n.Code == "43.21");
    }

    [Fact(DisplayName = "AC11i. Liste koda göre sıralıdır ve kırpılmaz")]
    public void Liste_sirali_ve_kirpilmaz()
    {
        var liste = ActivityCatalog.ListNace(["İnşaat ve taahhüt"]);

        // Üst sınır uygulanmaz: bir sektörün kodları zaten sınırlı sayıdadır ve
        // kullanıcı tamamını görmelidir.
        var beklenen = ActivityCatalog.NaceCodes.Count(n => n.Sector == "İnşaat ve taahhüt");

        Assert.Equal(beklenen, liste.Count);
        Assert.Equal(liste.Select(n => n.Code).Order(StringComparer.Ordinal), liste.Select(n => n.Code));
    }

    [Fact(DisplayName = "AC11j. Sektör verilmezse tüm katalog dökülmez")]
    public void Sektorsuz_liste_bostur()
    {
        Assert.Empty(ActivityCatalog.ListNace(null));
        Assert.Empty(ActivityCatalog.ListNace([]));
    }

    [Fact(DisplayName = "AC11k. Birden çok sektörün kodları birleşir")]
    public void Listede_sektorler_birlesir()
    {
        var liste = ActivityCatalog.ListNace(["İnşaat ve taahhüt", "Bilişim ve yazılım"]);

        Assert.Contains(liste, n => n.Sector == "İnşaat ve taahhüt");
        Assert.Contains(liste, n => n.Sector == "Bilişim ve yazılım");
        Assert.DoesNotContain(liste, n => n.Sector == "Taşımacılık ve lojistik");
    }

    [Theory(DisplayName = "AC11e. Kod sektöre ait mi sorusu doğru cevaplanır")]
    [InlineData("23.61", "İnşaat ve taahhüt", false)]
    [InlineData("23.61", "Yapı malzemeleri ve cam", true)]
    [InlineData("41.20", "İnşaat ve taahhüt", true)]
    [InlineData("25.62", "Metal sanayi ve fabrikasyon", true)]
    [InlineData("25.62", "Makine ve ekipman imalatı", false)]
    [InlineData("62.01", "Bilişim ve yazılım", true)]
    [InlineData("9999", "Bilişim ve yazılım", false)]
    public void Kod_sektore_ait_mi(string kod, string sektor, bool beklenen)
    {
        Assert.Equal(beklenen, ActivityCatalog.NaceBelongsToSectors(kod, [sektor]));
    }

    [Theory(DisplayName = "AC11f. Kodun gerçek sektörü bulunabilir")]
    [InlineData("23.61", "Yapı malzemeleri ve cam")]
    [InlineData("2361", "Yapı malzemeleri ve cam")]
    [InlineData("41.20", "İnşaat ve taahhüt")]
    [InlineData("62.01", "Bilişim ve yazılım")]
    public void Kodun_gercek_sektoru_bulunur(string kod, string beklenen)
    {
        // Hata mesajı kullanıcıya "bu kod aslında şu sektöre ait" diyebilmelidir.
        Assert.Equal(beklenen, ActivityCatalog.SectorOfNace(kod));
    }

    [Fact(DisplayName = "AC11g. Katalogdaki her kod kendi sektörüyle uyumludur")]
    public void Her_kod_kendi_sektoruyle_uyumludur()
    {
        // Katalog kendi içinde çelişirse doğrulama seçilebilir bir kodu reddeder.
        Assert.All(
            ActivityCatalog.NaceCodes,
            n => Assert.True(
                ActivityCatalog.NaceBelongsToSectors(n.Code, [n.Sector]),
                $"{n.Code} kodu kendi sektörü '{n.Sector}' ile uyumsuz görünüyor."));
    }

    [Fact(DisplayName = "AC12. Öneri sayısı sınırlıdır")]
    public void Oneri_sayisi_sinirlidir()
    {
        Assert.True(ActivityCatalog.SearchNace("imalat").Count <= ActivityCatalog.MaxResults);
    }

    [Fact(DisplayName = "AC13. Sektör listesi kısa sorguda tamamen döner")]
    public void Sektor_listesi_kisa_sorguda_tamamen_doner()
    {
        // Sektör listesi kısadır; kullanıcı hiç yazmadan da gezinebilmelidir.
        Assert.Equal(ActivityCatalog.Sectors.Count, ActivityCatalog.SearchSectors("").Count);
        Assert.Equal(ActivityCatalog.Sectors.Count, ActivityCatalog.SearchSectors(null).Count);
    }

    [Fact(DisplayName = "AC14. Sektör araması Türkçe katlamayla çalışır")]
    public void Sektor_aramasi_katlamayla_calisir()
    {
        Assert.Contains(ActivityCatalog.SearchSectors("insaat"), s => s.Name == "İnşaat ve taahhüt");
        Assert.Contains(ActivityCatalog.SearchSectors("İNŞAAT"), s => s.Name == "İnşaat ve taahhüt");
    }

    [Theory(DisplayName = "AC15. NACE kodu kanonik yazıma çevrilir")]
    [InlineData("2562", "25.62")]
    [InlineData("25.62", "25.62")]
    [InlineData("25 62", "25.62")]
    [InlineData("6201", "62.01")]
    [InlineData("41", "41")]
    public void Kod_kanonige_cevrilir(string girdi, string beklenen)
    {
        // Serbest metin döneminden kalan noktasız kayıtlar ilk kaydetmede düzelsin.
        Assert.Equal(beklenen, ActivityCatalog.NormalizeNace(girdi));
    }

    [Theory(DisplayName = "AC16. Katalogda olmayan kod reddedilir")]
    [InlineData("9999")]
    [InlineData("abcd")]
    [InlineData("")]
    [InlineData(null)]
    public void Bilinmeyen_kod_reddedilir(string? girdi)
    {
        Assert.Null(ActivityCatalog.NormalizeNace(girdi));
    }

    [Theory(DisplayName = "AC17. Sektör adı kanonik yazıma çevrilir")]
    [InlineData("İnşaat ve taahhüt", "İnşaat ve taahhüt")]
    [InlineData("insaat ve taahhut", "İnşaat ve taahhüt")]
    [InlineData("İNŞAAT VE TAAHHÜT", "İnşaat ve taahhüt")]
    [InlineData("  Bilişim ve yazılım  ", "Bilişim ve yazılım")]
    public void Sektor_kanonige_cevrilir(string girdi, string beklenen)
    {
        Assert.Equal(beklenen, ActivityCatalog.NormalizeSector(girdi));
    }

    [Theory(DisplayName = "AC18. Katalogda olmayan sektör reddedilir")]
    [InlineData("inşaat")]
    [InlineData("İmalat")]
    [InlineData("Makine imalatı")]
    [InlineData("")]
    [InlineData(null)]
    public void Bilinmeyen_sektor_reddedilir(string? girdi)
    {
        // Serbest metin döneminden kalan kısaltmalar "yaklaşık doğru" sayılmaz:
        // kullanıcı listeden yeniden seçmelidir. Tahmin etmek yanlış sektörü kalıcı yapar.
        Assert.Null(ActivityCatalog.NormalizeSector(girdi));
    }

    [Fact(DisplayName = "AC19. Her sektör adı katalogdan geri okunabilir")]
    public void Her_sektor_geri_okunabilir()
    {
        Assert.All(ActivityCatalog.Sectors, s => Assert.Equal(s.Name, ActivityCatalog.NormalizeSector(s.Name)));
    }

    [Fact(DisplayName = "AC20. Her NACE kodu katalogdan geri okunabilir")]
    public void Her_kod_geri_okunabilir()
    {
        Assert.All(ActivityCatalog.NaceCodes, n => Assert.Equal(n.Code, ActivityCatalog.NormalizeNace(n.Code)));
    }

    [Fact(DisplayName = "AC21. Sektör adları benzersizdir (katlanmış hâlde de)")]
    public void Sektor_adlari_benzersizdir()
    {
        var katlanmis = ActivityCatalog.Sectors.Select(s => TurkceMetin.BasligiKatla(s.Name)).ToList();

        Assert.Equal(katlanmis.Count, katlanmis.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "AC22. Eşleştirme motorunun kullandığı kodlar katalogda vardır")]
    public void Motorun_kullandigi_kodlar_katalogda()
    {
        // Worker'ın ilan metninden ürettiği sektör kuralları bu kodları kullanır
        // (parser/sektor.py). Katalogda karşılığı olmayan bir kod, firmanın hiçbir
        // zaman seçemeyeceği bir sektör demektir.
        string[] motorKodlari =
        [
            "32.50", "46.46", "21.20", "71.20", "62.01", "62.02", "26.20", "46.51",
            "02.10", "02.20", "16.10", "01.11", "56.29", "46.30", "31.01", "18.10",
            "28.00", "25.62", "33.12", "35.10", "43.21", "46.71", "47.30", "42.12",
            "49.10", "33.17", "41.20", "42.00", "43.00", "49.41", "49.39", "52.29",
            "81.21", "81.29", "80.10", "55.10", "79.11", "85.59", "70.22", "13.00",
            "14.00", "10.00", "46.65", "01.40", "01.50",
        ];

        // Katalog dört haneli sınıfları listeler; iki haneli bölüm karşılığı da kabul
        // edilir çünkü eşleştirme önek mantığıyla çalışır.
        Assert.All(motorKodlari, kod =>
        {
            var bolum = kod[..2];
            Assert.True(
                ActivityCatalog.NormalizeNace(kod) is not null || ActivityCatalog.NormalizeNace(bolum) is not null,
                $"Katalogda ne {kod} ne de {bolum} bölümü var.");
        });
    }
}
