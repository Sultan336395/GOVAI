import { describe, expect, it } from 'vitest'
import type { OpportunityMatch } from '@/api/types'
import {
  KAPANMA_ESIGI_GUN,
  KPI_TANIMLARI,
  kpiKirilimiHesapla,
  ortalamaSkor,
  toplamTutuyorMu,
} from './kpiKirilimi'

/**
 * Özet kartı ile altında açılan listenin AYNI ölçütü kullandığını sabitler.
 *
 * Bu testlerin varlık sebebi şu: kart sayısı sunucuda, liste tarayıcıda hesaplanıyor.
 * İki ölçüt ayrıştığında hiçbir şey hata vermez — kullanıcı "3" yazan kartı açıp dört
 * satır görür ve bunu bize ancak fark ederse söyler.
 */

const esles = (ek: Partial<OpportunityMatch> = {}): OpportunityMatch => ({
  assessmentId: crypto.randomUUID(),
  opportunityId: crypto.randomUUID(),
  opportunityTitle: 'Örnek Çağrı',
  publisher: 'Örnek Kurum',
  supportCategory: 'Grant',
  deadline: null,
  daysUntilDeadline: null,
  finalScore: 50,
  confidence: 0.8,
  verdict: 'ConditionallyEligible',
  sectorFit: 'Matched',
  missingConditionCount: 0,
  missingMandatoryDocumentCount: 0,
  dataGapCount: 0,
  maxAmount: null,
  executiveSummary: null,
  evaluatedAt: '2026-09-12T00:00:00Z',
  ...ek,
})

describe('karar kırılımları', () => {
  it('KK1. Uygun kartı yalnızca tüm koşulları sağlayanları getirir', () => {
    const kayitlar = [
      esles({ verdict: 'Eligible' }),
      esles({ verdict: 'ConditionallyEligible' }),
      esles({ verdict: 'NotEligible' }),
      esles({ verdict: 'Indeterminate' }),
    ]

    const kirilim = kpiKirilimiHesapla('uygun', kayitlar)

    expect(kirilim.toplam).toBe(1)
    expect(kirilim.kayitlar.every((m) => m.verdict === 'Eligible')).toBe(true)
  })

  it('KK2. Şartlı uygun kartı "uygun" olanları İÇERMEZ', () => {
    // İkisi karışırsa kartların toplamı değerlendirme sayısını aşar.
    const kayitlar = [esles({ verdict: 'Eligible' }), esles({ verdict: 'ConditionallyEligible' })]

    expect(kpiKirilimiHesapla('sartli', kayitlar).toplam).toBe(1)
  })
})

describe('son başvurusu yaklaşanlar', () => {
  it('KK3. Eşik GÜNÜ dahildir, ertesi gün değildir', () => {
    const kayitlar = [
      esles({ daysUntilDeadline: KAPANMA_ESIGI_GUN }),
      esles({ daysUntilDeadline: KAPANMA_ESIGI_GUN + 1 }),
    ]

    expect(kpiKirilimiHesapla('kapanan', kayitlar).toplam).toBe(1)
  })

  it('KK4. Süresi DOLMUŞ çağrı aksiyon listesine girmez', () => {
    // Sunucu ölçütü "0 < kalanGün"dür; 0 ve negatif kalan gün kapanmış demektir.
    const kayitlar = [
      esles({ daysUntilDeadline: 0 }),
      esles({ daysUntilDeadline: -3 }),
      esles({ daysUntilDeadline: 5 }),
    ]

    expect(kpiKirilimiHesapla('kapanan', kayitlar).toplam).toBe(1)
  })

  it('KK5. ELENMİŞ çağrı aksiyon listesine girmez', () => {
    // Uygun olmayan bir çağrının kapanmasının kullanıcı için bir önemi yoktur.
    const kayitlar = [
      esles({ daysUntilDeadline: 3, verdict: 'NotEligible' }),
      esles({ daysUntilDeadline: 3, verdict: 'Indeterminate' }),
    ]

    expect(kpiKirilimiHesapla('kapanan', kayitlar).toplam).toBe(1)
  })

  it('KK6. Tarihi olmayan çağrı listeye girmez', () => {
    expect(kpiKirilimiHesapla('kapanan', [esles({ daysUntilDeadline: null })]).toplam).toBe(0)
  })

  it('KK7. En yakın tarih başa gelir', () => {
    const kayitlar = [esles({ daysUntilDeadline: 12 }), esles({ daysUntilDeadline: 2 })]

    expect(kpiKirilimiHesapla('kapanan', kayitlar).kayitlar[0].daysUntilDeadline).toBe(2)
  })
})

describe('toplam gösteren kartlar', () => {
  it('KK8. Eksik belge kartı FIRSAT değil BELGE sayar', () => {
    // Ayrım gerçek: üç fırsatta altı belge eksik olabilir. Fırsat sayısı gösterilseydi
    // kart "3" derdi, liste de üç satır olurdu ama kartın anlamı değişirdi.
    const kayitlar = [
      esles({ missingMandatoryDocumentCount: 4 }),
      esles({ missingMandatoryDocumentCount: 2 }),
      esles({ missingMandatoryDocumentCount: 0 }),
    ]

    const kirilim = kpiKirilimiHesapla('belge', kayitlar)

    expect(kirilim.toplam).toBe(6)
    expect(kirilim.firsatSayisi).toBe(2)
  })

  it('KK9. Veri boşluğu kartı da toplam sayar', () => {
    const kayitlar = [esles({ dataGapCount: 3 }), esles({ dataGapCount: 1 }), esles({ dataGapCount: 0 })]

    const kirilim = kpiKirilimiHesapla('bosluk', kayitlar)

    expect(kirilim.toplam).toBe(4)
    expect(kirilim.firsatSayisi).toBe(2)
  })

  it('KK10. Sıfır katkılı kayıt listeye girmez', () => {
    // Girseydi kullanıcı "veri boşluğu yok" yazan satırları boşluk listesinde görürdü.
    const kirilim = kpiKirilimiHesapla('bosluk', [esles({ dataGapCount: 0 })])

    expect(kirilim.kayitlar).toHaveLength(0)
  })
})

describe('ortalama skor', () => {
  it('KK11. Ortalama TÜM değerlendirmelerden hesaplanır', () => {
    // Yalnızca uygun olanlardan hesaplansaydı ortalama gerçekte olduğundan yüksek çıkardı.
    const kayitlar = [
      esles({ finalScore: 80, verdict: 'Eligible' }),
      esles({ finalScore: 20, verdict: 'NotEligible' }),
    ]

    expect(kpiKirilimiHesapla('ortalama', kayitlar).toplam).toBe(50)
  })

  it('KK12. Boş listede ortalama sıfırdır, NaN değil', () => {
    expect(ortalamaSkor([])).toBe(0)
  })

  it('KK13. Ortalama iki basamağa yuvarlanır', () => {
    const kayitlar = [esles({ finalScore: 10 }), esles({ finalScore: 10 }), esles({ finalScore: 11 })]

    expect(ortalamaSkor(kayitlar)).toBe(10.33)
  })

  it('KK14. En yüksek skor başa gelir', () => {
    const kayitlar = [esles({ finalScore: 30 }), esles({ finalScore: 90 })]

    expect(kpiKirilimiHesapla('ortalama', kayitlar).kayitlar[0].finalScore).toBe(90)
  })
})

describe('kart ile liste tutarlılığı', () => {
  it('KK15. Tutan sayı tutarlı bildirilir', () => {
    const kirilim = kpiKirilimiHesapla('uygun', [esles({ verdict: 'Eligible' })])

    expect(toplamTutuyorMu('uygun', kirilim, 1)).toBe(true)
  })

  it('KK16. Tutmayan sayı YAKALANIR', () => {
    // Liste eksik yüklendiğinde (sayfalama sınırı) sessiz kalmamak için.
    const kirilim = kpiKirilimiHesapla('uygun', [esles({ verdict: 'Eligible' })])

    expect(toplamTutuyorMu('uygun', kirilim, 3)).toBe(false)
  })

  it('KK17. Ortalama EKRANDAKİ basamakla karşılaştırılır', () => {
    // Sunucu 54.45 döner, ekran 54.5 gösterir. Ham sayı karşılaştırılsaydı her
    // ortalama kartı "tutmuyor" uyarısı verirdi.
    const kayitlar = [esles({ finalScore: 54.4 }), esles({ finalScore: 54.5 })]
    const kirilim = kpiKirilimiHesapla('ortalama', kayitlar)

    expect(kirilim.toplam).toBe(54.45)
    expect(toplamTutuyorMu('ortalama', kirilim, 54.45)).toBe(true)
  })
})

describe('tanım bütünlüğü', () => {
  it('KK18. Her kartın başlığı ve açıklaması vardır', () => {
    // Panel başlıksız açılırsa kullanıcı neye baktığını bilmez.
    for (const [anahtar, tanim] of Object.entries(KPI_TANIMLARI)) {
      expect(tanim.baslik, anahtar).toBeTruthy()
      expect(tanim.aciklama, anahtar).toBeTruthy()
    }
  })

  it('KK19. Toplam gösteren kartlarda vurgu sütunu vardır', () => {
    // Satırın toplama kaç katkı yaptığı görünmezse liste toplamı açıklamaz.
    for (const [anahtar, tanim] of Object.entries(KPI_TANIMLARI)) {
      if (tanim.pay) {
        expect(tanim.vurguBasligi, anahtar).toBeTruthy()
      }
    }
  })
})
