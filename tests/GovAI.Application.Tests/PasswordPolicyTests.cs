using GovAI.Application.Common;

namespace GovAI.Application.Tests;

/// <summary>
/// Platform hesabı parola kuralı (Faz 2).
///
/// Platform hesapları ortak kataloğu ve karantina kararlarını yönetir; birinin ele
/// geçirilmesi tek bir müşteriyi değil <b>tüm kiracıları</b> etkiler. Bu yüzden
/// kiracı kullanıcılarından daha katı bir eşik uygulanır.
/// </summary>
public sealed class PasswordPolicyTests
{
    [Theory(DisplayName = "P1. Güçlü parolalar kabul edilir")]
    [InlineData("Kaynak-Denetim-2026")]
    [InlineData("uzun ve karisik Parola 42")]
    [InlineData("T3knopark!Mersin")]
    [InlineData("aA1!bB2@cC3#dD4$")]
    public void Guclu_parola_kabul_edilir(string parola)
    {
        Assert.True(PasswordPolicy.IsStrong(parola, out var gerekce), gerekce);
        Assert.Equal(string.Empty, gerekce);
    }

    [Theory(DisplayName = "P2. Kısa parola reddedilir")]
    [InlineData("Kisa1!")]
    [InlineData("aA1!bB2@cC")]
    public void Kisa_parola_reddedilir(string parola)
    {
        Assert.False(PasswordPolicy.IsStrong(parola, out var gerekce));
        Assert.Contains($"{PasswordPolicy.MinimumLength} karakter", gerekce, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "P3. Tek tür karakterden oluşan uzun dizi reddedilir")]
    [InlineData("abcdefghijklmnop")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    [InlineData("1234567890123456")]
    public void Tek_tur_reddedilir(string parola)
    {
        Assert.False(PasswordPolicy.IsStrong(parola, out var gerekce));
        Assert.Contains("karakter türü", gerekce, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "P4. Uzun ama tekrar eden dizi reddedilir")]
    public void Tekrar_eden_dizi_reddedilir()
    {
        // Uzunluk şartını sağlar, üç sınıf da vardır ama gerçekte üç karakterdir.
        Assert.False(PasswordPolicy.IsStrong("aA1aA1aA1aA1aA1", out var gerekce));
        Assert.Contains("az sayıda farklı karakter", gerekce, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "P5. Boş parola reddedilir")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("            ")]
    public void Bos_parola_reddedilir(string? parola)
    {
        Assert.False(PasswordPolicy.IsStrong(parola, out var gerekce));
        Assert.NotEmpty(gerekce);
    }

    [Fact(DisplayName = "P6. Eşik kiracı kullanıcılarından katıdır")]
    public void Esik_kiraci_kullanicisindan_kati()
    {
        // Kiracı kullanıcıları için kural 10 karakterdir; platform hesabı daha katı olmalı.
        Assert.True(PasswordPolicy.MinimumLength > 10);

        Assert.False(PasswordPolicy.IsStrong("Parola123!", out _));
    }

    [Fact(DisplayName = "P7. Gerekçe kullanıcıya gösterilebilir bir metindir")]
    public void Gerekce_kullaniciya_gosterilebilir()
    {
        PasswordPolicy.IsStrong("kisa", out var gerekce);

        Assert.EndsWith(".", gerekce, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", gerekce, StringComparison.OrdinalIgnoreCase);
    }
}
