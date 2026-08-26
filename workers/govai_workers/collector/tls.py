"""Resmî kurum sunucularıyla TLS uyumluluğu.

**Bu modül sertifika doğrulamasını hiçbir koşulda kapatmaz.** Yaptığı tek şey,
resmî sunucuların gerçekten sunduğu — ama Python'un varsayılan ayarlarının
konuşmadığı — geçerli TLS yapılandırmalarını konuşabilmektir. Her iki durumda da
sertifika zinciri doğrulanır ve ana bilgisayar adı denetlenir.

İki ayrı sorun ve iki ayrı, dar çözüm:

1. **Eksik ara sertifika** (``resmigazete.gov.tr``)
   Sunucu yalnızca yaprak sertifikayı gönderiyor, ara CA'yı göndermiyor. Bu bir
   *sunucu yapılandırma eksiğidir*; sertifikanın kendisi geçerlidir
   (DigiCert / GeoTrust TLS RSA CA G1, sahibi Cumhurbaşkanlığı).
   Tarayıcılar bu durumda sertifikanın içindeki resmî AIA adresinden eksik halkayı
   çeker. Biz de aynısını yapıyoruz — **ama yalnızca**:

   * halka, adı ve parmak izi burada açıkça yazılmış bir sertifikaysa,
   * ve bu halka, sistemin zaten güvendiği bir köke bağlanıyorsa.

   Kök hâlâ işletim sisteminin güven deposundan gelir. Yani doğrulama zayıflamaz;
   sunucunun göndermeyi unuttuğu ara sertifika tamamlanır.

2. **Şifre takımı uyuşmazlığı** (``ekap.kik.gov.tr``)
   Sunucu yalnızca ``AES128-GCM-SHA256`` (ileri gizlilik sunmayan, eski RSA anahtar
   değişimi) kabul ediyor. Python'un varsayılan listesinde bu takım yok, bu yüzden
   sunucu ``handshake_failure`` gönderiyor. OpenSSL'in kendi varsayılanı bu takımı
   kabul eder. Bu bir *güvenlik atlatma* değil, sunucunun konuştuğu lehçeyi
   konuşmaktır: sertifika ve ana bilgisayar adı denetimi aynen çalışır.

Politika **alan adı bazlıdır**: burada adı geçmeyen hiçbir sunucu için gevşetme
uygulanmaz. Genel bir TLS ayarı değiştirilmez.
"""

from __future__ import annotations

import hashlib
import ssl
import threading
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from tempfile import gettempdir

from govai_workers.logging_setup import get_logger

log = get_logger(__name__)


@dataclass(frozen=True)
class TlsUyumu:
    """Tek bir resmî alan adı için TLS uyumluluk kaydı."""

    #: Neden gerekli — logda ve raporda görünür.
    gerekce: str

    #: Sunucunun göndermediği ara sertifikanın resmî CA adresi (AIA).
    ara_sertifika_url: str | None = None

    #: Ara sertifikanın beklenen SHA-256 parmak izi. Sabitlenmiştir: indirilen
    #: dosya bu değere uymazsa kullanılmaz.
    ara_sertifika_sha256: str | None = None

    #: Sunucunun kabul ettiği eski ama geçerli şifre takımları.
    sifre_listesi: str | None = None


#: Yalnızca burada adı geçen alan adları etkilenir.
UYUMLULUK: dict[str, TlsUyumu] = {
    "resmigazete.gov.tr": TlsUyumu(
        gerekce=(
            "Sunucu ara sertifikayı göndermiyor (eksik zincir). Sertifika geçerli: "
            "DigiCert/GeoTrust TLS RSA CA G1, sahibi Cumhurbaşkanlığı."
        ),
        ara_sertifika_url="http://cacerts.geotrust.com/GeoTrustTLSRSACAG1.crt",
        ara_sertifika_sha256=(
            "c06e307f7cfc1d32fa72a4c033c87b90019af216f0775d64978a2eca6c8a230e"
        ),
    ),
    "kik.gov.tr": TlsUyumu(
        gerekce=(
            "Sunucu yalnızca eski RSA anahtar değişimli AES128-GCM-SHA256 kabul ediyor; "
            "Python'un varsayılan listesinde bu takım yok."
        ),
        sifre_listesi="DEFAULT:@SECLEVEL=1",
    ),
}


@dataclass
class _Onbellek:
    kilit: threading.Lock = field(default_factory=threading.Lock)
    baglamlar: dict[str, ssl.SSLContext] = field(default_factory=dict)


_onbellek = _Onbellek()


def uyum_bul(host: str) -> tuple[str, TlsUyumu] | None:
    """Bu ana bilgisayar için tanımlı bir uyumluluk kaydı var mı?"""
    host = host.lower().rstrip(".")

    for alan, uyum in UYUMLULUK.items():
        if host == alan or host.endswith("." + alan):
            return alan, uyum

    return None


def _ara_sertifikayi_indir(uyum: TlsUyumu) -> Path | None:
    """Eksik ara sertifikayı resmî CA adresinden indirir ve parmak izini doğrular."""
    if not uyum.ara_sertifika_url or not uyum.ara_sertifika_sha256:
        return None

    hedef = Path(gettempdir()) / f"govai-ara-{uyum.ara_sertifika_sha256[:16]}.pem"

    if hedef.exists():
        return hedef

    with urllib.request.urlopen(uyum.ara_sertifika_url, timeout=30) as yanit:  # noqa: S310
        der = yanit.read()

    parmak_izi = hashlib.sha256(der).hexdigest()

    if parmak_izi != uyum.ara_sertifika_sha256:
        # Beklenmeyen içerik: sessizce kullanmak yerine reddediyoruz.
        log.error(
            "ara_sertifika_parmak_izi_uyusmadi",
            beklenen=uyum.ara_sertifika_sha256[:16],
            gelen=parmak_izi[:16],
        )
        return None

    hedef.write_text(ssl.DER_cert_to_PEM_cert(der), encoding="ascii")
    log.info("ara_sertifika_indirildi", parmak_izi=parmak_izi[:16])
    return hedef


def baglam_olustur(host: str) -> ssl.SSLContext:
    """
    Bu ana bilgisayar için TLS bağlamı üretir.

    Uyumluluk kaydı yoksa Python'un varsayılan, en katı bağlamı döner.
    Kayıt varsa da doğrulama ve ana bilgisayar adı denetimi **açık kalır**.
    """
    eslesme = uyum_bul(host)

    if eslesme is None:
        return ssl.create_default_context()

    alan, uyum = eslesme

    with _onbellek.kilit:
        mevcut = _onbellek.baglamlar.get(alan)
        if mevcut is not None:
            return mevcut

        baglam = ssl.create_default_context()

        if uyum.sifre_listesi:
            baglam.set_ciphers(uyum.sifre_listesi)

        ara = _ara_sertifikayi_indir(uyum)
        if ara is not None:
            # Yalnızca eksik HALKA eklenir. Zincirin tepesindeki kök hâlâ
            # sistemin güven deposundan gelmek zorundadır.
            baglam.load_verify_locations(cafile=str(ara))

        # Bu iki satır sözleşmedir: doğrulama asla kapanmaz.
        assert baglam.verify_mode == ssl.CERT_REQUIRED  # noqa: S101
        assert baglam.check_hostname is True  # noqa: S101

        log.info("tls_uyumu_uygulandi", alan=alan, gerekce=uyum.gerekce)
        _onbellek.baglamlar[alan] = baglam
        return baglam
