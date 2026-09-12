"""Zamanlanmış işlerin takvimi Türkiye saatine göre mi?

Bu testler sahadan gelen bir hatayı sabitler. Zamanlayıcı ``Europe/Istanbul`` ile
kurulmuştu ama işler ``CronTrigger(hour=3, minute=30)`` gibi saat dilimi VERİLMEMİŞ
tetikleyici nesneleriyle ekleniyordu. APScheduler hazır bir tetikleyici nesnesine
kendi saat dilimini uygulamaz; tetikleyici de saat dilimi verilmeyince makinenin
yerel saatine düşer ve konteynerde ``tzdata`` olmadığı için bu UTC olur.

Sonuç: gece skorlama turu 03:30 yerine 06:30'da, ERP çekme 02:45 yerine 05:45'te
çalışıyordu. Kimse hata görmüyordu çünkü iş hiç başarısız olmuyordu — sadece üç
saat geç başlıyordu ve sabah rapora bakan kullanıcı dünkü skorları görüyordu.

Hiçbir mevcut test zamanlayıcıya bakmıyordu; bu dosya o boşluğu kapatır.
"""

from __future__ import annotations

from datetime import datetime
from zoneinfo import ZoneInfo

import pytest
from apscheduler.schedulers.background import BackgroundScheduler
from apscheduler.triggers import cron as cron_modulu
from apscheduler.triggers.cron import CronTrigger
from apscheduler.triggers.interval import IntervalTrigger

from govai_workers import scheduler as zamanlayici

ISTANBUL = ZoneInfo("Europe/Istanbul")

#: İş kimliği -> Türkiye saatiyle beklenen (saat, dakika).
GECE_ISLERI = {
    "erp-pull": (2, 45),
    "nightly-rescore": (3, 30),
    "ai-second-opinions": (4, 15),
    "weekly-reports": (7, 30),
}


@pytest.fixture
def isler(monkeypatch):
    """İşleri kaydeder ama zamanlayıcıyı BAŞLATMAZ; takvim okumak için yeterli.

    Makinenin yerel saati bilerek UTC'ye sabitlenir. Aksi halde test, Türkiye
    saatiyle çalışan bir geliştirici makinesinde yeşil kalır ve hatayı yalnızca
    sunucuda gösterirdi — zaten bu hatanın aylarca fark edilmeme sebebi buydu.
    Sabitlenince tek geçme yolu saat dilimini AÇIKÇA vermektir.
    """
    monkeypatch.setattr(cron_modulu, "get_localzone", lambda: ZoneInfo("UTC"))

    s = BackgroundScheduler(timezone=ISTANBUL)
    zamanlayici.isleri_kur(s, client=object())

    # Başlatılmayan zamanlayıcıda işler "bekleyen" listesinde durur; get_jobs onları
    # zaten oradan verir. Kapatma da gerekmez, hiçbir iş parçacığı yoktur.
    return {is_.id: is_ for is_ in s.get_jobs()}


def test_zt1_her_cron_isi_turkiye_saatine_bagli(isler):
    """Saat dilimi olmayan tek bir iş bile üç saat kayar."""
    takvimli = {k: i for k, i in isler.items() if isinstance(i.trigger, CronTrigger)}
    assert takvimli, "Takvime bağlı hiç iş bulunamadı; test yanlış yeri ölçüyor."

    for kimlik, is_ in takvimli.items():
        saat_dilimi = str(is_.trigger.timezone)

        assert saat_dilimi == "Europe/Istanbul", (
            f"{kimlik} işi {saat_dilimi} saat diliminde çalışıyor; "
            "Türkiye saatine bağlı olmalıydı."
        )


@pytest.mark.parametrize("kimlik", sorted(GECE_ISLERI))
def test_zt2_gece_isleri_gercekten_gece_calisir(isler, kimlik):
    """Asıl soru saat dilimi etiketi değil, işin hangi saatte tetiklendiğidir."""
    saat, dakika = GECE_ISLERI[kimlik]
    # Yaz saati uygulanmayan bir kış günü seçilir ki test UTC+3 sabitine değil,
    # tetikleyicinin kendi hesabına baksın.
    simdi = datetime(2026, 1, 5, 0, 5, tzinfo=ISTANBUL)

    sonraki = isler[kimlik].trigger.get_next_fire_time(None, simdi)

    yerel = sonraki.astimezone(ISTANBUL)
    assert (yerel.hour, yerel.minute) == (saat, dakika), (
        f"{kimlik} Türkiye saatiyle {yerel:%H:%M}'de çalışıyor; "
        f"{saat:02d}:{dakika:02d} bekleniyordu."
    )


def test_zt3_gece_sirasi_korunur(isler):
    """Önce ERP çekme, sonra skorlama, sonra ikinci görüş.

    Sıra bozulursa skorlar bir gün eski profille üretilir ve ikinci görüş bir gün
    eski karara bakar. Saat dilimi düzeltmesi bu sırayı bozmamalıdır.
    """
    simdi = datetime(2026, 1, 5, 0, 5, tzinfo=ISTANBUL)

    zaman = {
        kimlik: isler[kimlik].trigger.get_next_fire_time(None, simdi)
        for kimlik in ("erp-pull", "nightly-rescore", "ai-second-opinions")
    }

    assert zaman["erp-pull"] < zaman["nightly-rescore"] < zaman["ai-second-opinions"]


def test_zt4_haftalik_rapor_pazartesi_ve_skorlamadan_sonra(isler):
    """Rapor o anki skorların anlık görüntüsüdür; gece turundan sonra üretilmeli."""
    pazartesi_gecesi = datetime(2026, 1, 5, 0, 5, tzinfo=ISTANBUL)

    rapor = isler["weekly-reports"].trigger.get_next_fire_time(None, pazartesi_gecesi)
    skorlama = isler["nightly-rescore"].trigger.get_next_fire_time(None, pazartesi_gecesi)

    assert rapor.astimezone(ISTANBUL).weekday() == 0, "Rapor pazartesi günü üretilmeli."
    assert skorlama < rapor, "Rapor, gece skorlama turundan sonra çalışmalı."


def test_zt5_yaz_saatinde_de_ayni_saatte_calisir(isler):
    """Türkiye kalıcı UTC+3 kullanır; tetikleyici yine de yerel saate bağlı olmalı.

    Sabit bir UTC farkı yazılsaydı test bugün geçer, ülke saat uygulamasını
    değiştirdiği gün sessizce kayardı.
    """
    temmuz = datetime(2026, 7, 6, 0, 5, tzinfo=ISTANBUL)

    yerel = isler["nightly-rescore"].trigger.get_next_fire_time(None, temmuz).astimezone(ISTANBUL)

    assert (yerel.hour, yerel.minute) == (3, 30)


def test_zt6_sik_isler_aralik_tabanli_kalir(isler):
    """Bildirim gönderimi ve nabız takvime değil aralığa bağlıdır.

    Bunları cron'a çevirmek, konteyner her yeniden başladığında ilk turu
    geciktirirdi.
    """
    for kimlik in ("dispatch-notifications", "heartbeat"):
        assert isinstance(isler[kimlik].trigger, IntervalTrigger), (
            f"{kimlik} {type(isler[kimlik].trigger).__name__} kullanıyor; "
            "aralık tabanlı kalmalıydı."
        )
