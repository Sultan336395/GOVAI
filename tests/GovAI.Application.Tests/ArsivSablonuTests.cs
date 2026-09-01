using System.Text.Json.Nodes;
using GovAI.Persistence.Seed;

namespace GovAI.Application.Tests;

/// <summary>
/// Arşiv şablonunun kaynağa yerleştirilmesi (Faz 2).
///
/// <para>
/// Seed her uygulama açılışında çalışır. Bu yüzden buradaki kural üç şeyi birden
/// sağlamak zorundadır: aynı işlem tekrarlandığında hiçbir şey değişmemeli
/// (<b>idempotent</b>), katalog düzeltilirse değer yenilenebilmeli (<b>sürümlü</b>) ve
/// operatörün panelden girdiği hiçbir değer kaybolmamalı.
/// </para>
///
/// <para>
/// Kural tamamen yereldir: ağa çıkmaz, saat okumaz. Aynı girdi her zaman aynı çıktıyı
/// verir — bu olmadan seed'i açılışta çalıştırmak güvenli olmazdı.
/// </para>
/// </summary>
public sealed class ArsivSablonuTests
{
    private const string Sablon = "/fihrist?tarih={date}";

    private static string? Deger(string? json, string anahtar) =>
        json is null ? null : JsonNode.Parse(json)?[anahtar]?.ToString();

    // ═══════════════ İlk yerleştirme ═══════════════

    [Fact(DisplayName = "A1. Gövde yoksa şablon ve sürüm eklenir")]
    public void Bos_govdeye_eklenir()
    {
        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            currentJson: null, Sablon, version: 1, out var govde);

        Assert.Equal(SourceConfigurationJson.Outcome.Added, sonuc);
        Assert.Equal(Sablon, Deger(govde, "archiveUrlTemplate"));
        Assert.Equal("1", Deger(govde, "archiveUrlTemplateVersion"));
    }

    [Fact(DisplayName = "A2. Var olan anahtarlar korunur")]
    public void Diger_anahtarlar_korunur()
    {
        var mevcut = """{"userAgent":"GovAI/1.0","timeoutSeconds":45}""";

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            mevcut, Sablon, version: 1, out var govde);

        Assert.Equal(SourceConfigurationJson.Outcome.Added, sonuc);
        Assert.Equal("GovAI/1.0", Deger(govde, "userAgent"));
        Assert.Equal("45", Deger(govde, "timeoutSeconds"));
        Assert.Equal(Sablon, Deger(govde, "archiveUrlTemplate"));
    }

    // ═══════════════ Idempotentlik ═══════════════

    [Fact(DisplayName = "A3. İkinci çalıştırma gövdeyi değiştirmez")]
    public void Ikinci_calistirma_degistirmez()
    {
        SourceConfigurationJson.ApplyArchiveUrlTemplate(null, Sablon, 1, out var birinci);

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            birinci, Sablon, version: 1, out var ikinci);

        Assert.Equal(SourceConfigurationJson.Outcome.AlreadyCurrent, sonuc);
        Assert.Equal(birinci, ikinci);
    }

    [Fact(DisplayName = "A4. Onuncu çalıştırma da aynı sonucu verir")]
    public void Tekrarlanan_calistirma_kararli()
    {
        string? govde = null;

        for (var i = 0; i < 10; i++)
        {
            SourceConfigurationJson.ApplyArchiveUrlTemplate(govde, Sablon, 1, out govde);
        }

        SourceConfigurationJson.ApplyArchiveUrlTemplate(govde, Sablon, 1, out var son);

        Assert.Equal(govde, son);
        Assert.Equal(Sablon, Deger(son, "archiveUrlTemplate"));
    }

    // ═══════════════ Sürümleme ═══════════════

    [Fact(DisplayName = "A5. Katalog sürümü artınca şablon yenilenir")]
    public void Surum_artinca_yenilenir()
    {
        SourceConfigurationJson.ApplyArchiveUrlTemplate(null, "/eski?d={date}", 1, out var v1);

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            v1, Sablon, version: 2, out var v2);

        Assert.Equal(SourceConfigurationJson.Outcome.Upgraded, sonuc);
        Assert.Equal(Sablon, Deger(v2, "archiveUrlTemplate"));
        Assert.Equal("2", Deger(v2, "archiveUrlTemplateVersion"));
    }

    [Fact(DisplayName = "A6. Kayıtlı sürüm daha yeniyse geriye alınmaz")]
    public void Yeni_surum_geriye_alinmaz()
    {
        SourceConfigurationJson.ApplyArchiveUrlTemplate(null, Sablon, 5, out var v5);

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            v5, "/eski?d={date}", version: 2, out var sonrasi);

        Assert.Equal(SourceConfigurationJson.Outcome.AlreadyCurrent, sonuc);
        Assert.Equal(Sablon, Deger(sonrasi, "archiveUrlTemplate"));
    }

    // ═══════════════ Operatör değeri ═══════════════

    [Fact(DisplayName = "A7. Sürüm damgası olmayan şablon operatöründür, ezilmez")]
    public void Operator_degeri_ezilmez()
    {
        // Panelden elle girilmiş: damga yok.
        var elle = """{"archiveUrlTemplate":"/ozel-arsiv?gun={date}"}""";

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            elle, Sablon, version: 9, out var govde);

        Assert.Equal(SourceConfigurationJson.Outcome.OperatorValueKept, sonuc);
        Assert.Equal(elle, govde);
    }

    // ═══════════════ Bozuk gövde ═══════════════

    [Theory(DisplayName = "A8. Bozuk gövde hata vermez, boş sayılır")]
    [InlineData("{bu json değil")]
    [InlineData("[1,2,3]")]
    [InlineData("\"düz metin\"")]
    [InlineData("   ")]
    public void Bozuk_govde_hata_vermez(string bozuk)
    {
        // Tek bir kaynağın bozuk ayarı yüzünden uygulama açılmamazlık edemez.
        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            bozuk, Sablon, version: 1, out var govde);

        Assert.Equal(SourceConfigurationJson.Outcome.Added, sonuc);
        Assert.Equal(Sablon, Deger(govde, "archiveUrlTemplate"));
    }

    [Fact(DisplayName = "A9. Sürüm metin olarak yazılmışsa da okunur")]
    public void Metin_surum_okunur()
    {
        var metinSurum = """{"archiveUrlTemplate":"/eski","archiveUrlTemplateVersion":"1"}""";

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            metinSurum, Sablon, version: 2, out var govde);

        Assert.Equal(SourceConfigurationJson.Outcome.Upgraded, sonuc);
        Assert.Equal(Sablon, Deger(govde, "archiveUrlTemplate"));
    }

    [Fact(DisplayName = "A10. Anlamsız sürüm değeri operatör değeri sayılır")]
    public void Anlamsiz_surum_operator_sayilir()
    {
        var bozukSurum = """{"archiveUrlTemplate":"/ozel","archiveUrlTemplateVersion":{"a":1}}""";

        var sonuc = SourceConfigurationJson.ApplyArchiveUrlTemplate(
            bozukSurum, Sablon, version: 2, out var govde);

        // Damga okunamıyorsa seed'in yazdığı kanıtlanamaz; güvenli taraf dokunmamaktır.
        Assert.Equal(SourceConfigurationJson.Outcome.OperatorValueKept, sonuc);
        Assert.Equal(bozukSurum, govde);
    }
}
