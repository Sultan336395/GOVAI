"""Worker sağlık kontrolü (Faz 2).

Bir worker'ın "ayakta" olması, süreç tablosunda görünmesi demek DEĞİLDİR. Python
süreci pekâlâ yaşarken RabbitMQ bağlantısı kopmuş, kuyruk tüketicisi ölmüş ve worker
hiçbir mesaj işlemiyor olabilir. Bu durumda Docker container'ı "çalışıyor" gösterir,
kuyruk sessizce birikir ve kimse fark etmez.

Bu yüzden sağlık **nabza** dayanır ve nabız, tüketicinin kendi olay döngüsünden atar:

* ``consume()`` nabız yazımını AMQP bağlantısının ``call_later`` zamanlayıcısına
  bağlar. Bağlantı koparsa olay döngüsü durur, nabız da durur.
* ``scheduler`` aynı nabzı APScheduler işinden atar; o da zamanlayıcı yaşadığı sürece.

Sağlık kontrolü nabzın **tazeliğine** bakar. Süreç yaşayıp bağlantı ölmüşse dosya
eskir ve container sağlıksız işaretlenir.
"""

from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path

#: Nabız dosyası. Container'da ``govai`` kullanıcısı yazabilsin diye ``/tmp`` altında.
NABIZ_DOSYASI = Path(os.environ.get("GOVAI_HEALTH_FILE", "/tmp/govai-health"))

#: Nabız bu aralıkla atar (saniye).
NABIZ_ARALIGI = 15.0

#: Nabız bu süreden eskiyse worker sağlıksızdır. Üç atımlık kayba tolerans tanır.
AZAMI_YAS = 60.0


def isaretle(
    bilesen: str,
    *,
    baglanti_var: bool = True,
    dosya: Path | None = None,
    simdi: float | None = None,
) -> None:
    """Nabzı yazar.

    ``baglanti_var=False`` yalnızca başlangıçta, tüketici henüz bağlanmadan
    kullanılır: süreç ayakta ama kuyruğa bağlı değil. Sağlık kontrolü bunu
    **sağlıksız** sayar — yanlış bir "hazır" sinyali vermemek için.
    """
    hedef = dosya or NABIZ_DOSYASI
    icerik = {
        "bilesen": bilesen,
        "baglanti_var": baglanti_var,
        "zaman": simdi if simdi is not None else time.time(),
    }

    # Atomik yazım: yarım yazılmış dosya okunmasın.
    gecici = hedef.with_suffix(".gecici")
    gecici.write_text(json.dumps(icerik), encoding="utf-8")
    gecici.replace(hedef)


def durum(
    *,
    dosya: Path | None = None,
    azami_yas: float = AZAMI_YAS,
    simdi: float | None = None,
) -> tuple[bool, str]:
    """Worker sağlıklı mı? ``(saglikli, aciklama)`` döner."""
    hedef = dosya or NABIZ_DOSYASI

    if not hedef.exists():
        return False, "Nabız dosyası yok; worker henüz başlamadı ya da çöktü."

    try:
        icerik = json.loads(hedef.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as hata:
        return False, f"Nabız dosyası okunamadı: {hata}"

    an = simdi if simdi is not None else time.time()
    yas = an - float(icerik.get("zaman", 0))

    if yas > azami_yas:
        return False, (
            f"Nabız {yas:.0f} saniyedir atmıyor (sınır {azami_yas:.0f}s). "
            "Süreç yaşıyor olabilir ama kuyruk tüketicisi çalışmıyor."
        )

    if not icerik.get("baglanti_var", False):
        return False, "Worker ayakta ama kuyruğa bağlı değil."

    bilesen = icerik.get("bilesen", "?")
    return True, f"{bilesen}: kuyruğa bağlı, nabız {yas:.0f} saniye önce."


def main() -> int:
    """``govai-health`` giriş noktası. Docker HEALTHCHECK bunu çağırır."""
    saglikli, aciklama = durum()
    print(aciklama, file=sys.stdout if saglikli else sys.stderr)
    return 0 if saglikli else 1
