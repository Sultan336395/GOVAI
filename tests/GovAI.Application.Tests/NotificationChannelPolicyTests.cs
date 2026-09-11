using GovAI.Application.Notifications;
using GovAI.Domain.Common;

namespace GovAI.Application.Tests;

/// <summary>
/// Hangi bildirim türünün ayrıca e-postayla da gideceği.
///
/// <para>
/// Bu testler bir ürün kararını korur: e-posta listesi <b>kısa</b> kalmalıdır. Her
/// türün e-postaya çevrilmesi, kutunun dolması ve insanların kural yazıp klasöre
/// atması demektir; o noktada zamana bağlı asıl uyarılar da görünmez olur.
/// </para>
/// </summary>
public class NotificationChannelPolicyTests
{
    [Theory(DisplayName = "BK1. Zamana bağlı türler e-postayla DA iletilir")]
    [InlineData(NotificationKind.DeadlineApproaching)]
    [InlineData(NotificationKind.DocumentMissing)]
    public void Zamana_bagli_turler_epostaya_gider(NotificationKind kind)
    {
        // İkisi de kaçırılırsa fırsat kapanır: son başvuru geçer, belge yetişmez.
        Assert.Equal(NotificationChannel.Email, NotificationChannelPolicy.For(kind));
    }

    [Theory(DisplayName = "BK2. Sık üretilen türler YALNIZCA panelde kalır")]
    [InlineData(NotificationKind.NewMatch)]
    [InlineData(NotificationKind.ScoreChanged)]
    [InlineData(NotificationKind.RegulationChanged)]
    [InlineData(NotificationKind.SystemAlert)]
    public void Sik_uretilen_turler_panelde_kalir(NotificationKind kind)
    {
        // Skor her yeniden skorlamada değişebilir; e-postaya çevrilseydi gürültü olurdu.
        Assert.Equal(NotificationChannel.InApp, NotificationChannelPolicy.For(kind));
    }

    [Fact(DisplayName = "BK3. Tanımsız bir tür e-posta göndermeye BAŞLAMAZ")]
    public void Bilinmeyen_tur_epostaya_gitmez()
    {
        // Yeni bir bildirim türü eklendiğinde kimseye sormadan posta atmamalı;
        // listeye girmek bilinçli bir karar olmalı.
        var tanimsiz = (NotificationKind)9999;

        Assert.Equal(NotificationChannel.InApp, NotificationChannelPolicy.For(tanimsiz));
    }

    [Fact(DisplayName = "BK4. E-posta listesi KISA kalır")]
    public void Eposta_listesi_kisa_kalir()
    {
        // Sayı bir hedef değil, bir fren: liste büyüyorsa bunun bilinçli bir ürün
        // kararı olduğu burada da görünsün.
        Assert.True(
            NotificationChannelPolicy.EmailKinds.Count <= 3,
            "E-posta ile iletilen tür sayısı arttı. Bu bilinçli bir karar mı? "
            + "Her tür e-postaya çevrilirse hiçbiri okunmaz.");
    }
}
