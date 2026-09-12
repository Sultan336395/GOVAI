import type { OpportunityMatch } from '@/api/types'

/**
 * Pano özet kartlarının kırılımı.
 *
 * Karttaki sayı sunucuda hesaplanır (`ReportingService.GetDashboardAsync`), kartın
 * altında açılan liste ise burada. İki taraf AYNI ÖLÇÜTÜ kullanmak zorundadır; aksi
 * hâlde kullanıcı "3" yazan kartı açıp dört satır görür ve ekrana güveni biter.
 *
 * Bu yüzden ölçütler sunucudaki karşılıklarıyla satır satır eşlenmiştir ve saf
 * fonksiyon olarak durur — testle sabitlenebilsin diye.
 */

export type KpiAnahtari = 'uygun' | 'sartli' | 'ortalama' | 'kapanan' | 'belge' | 'bosluk'

/** Sunucudaki <c>ClosingSoonDayThreshold</c> ile aynı olmak zorunda. */
export const KAPANMA_ESIGI_GUN = 15

export interface KpiTanimi {
  /** Açılan panelin başlığı. */
  baslik: string
  /** Sayının ne anlama geldiğini bir cümlede söyler. */
  aciklama: string
  /** Kayıt bu kırılıma giriyor mu? */
  secer: (m: OpportunityMatch) => boolean
  /**
   * Satırın karttaki sayıya katkısı. Kart bir SAYIM ise `null` döner (her satır
   * bire bir katkıdır); kart bir TOPLAM ise satırın payını verir.
   *
   * Ayrım görünürdür: "Eksik zorunlu belge 6" altı fırsat değil, üç fırsatta toplam
   * altı belge olabilir. Bunu göstermezsek liste eksik sayılır.
   */
  pay: ((m: OpportunityMatch) => number) | null
  /** Listede öne çıkarılacak sütunun başlığı; yoksa ek sütun gösterilmez. */
  vurguBasligi: string | null
}

export const KPI_TANIMLARI: Record<KpiAnahtari, KpiTanimi> = {
  uygun: {
    baslik: 'Uygun fırsatlar',
    aciklama: 'Tüm koşulları sağlanan çağrılar.',
    secer: (m) => m.verdict === 'Eligible',
    pay: null,
    vurguBasligi: null,
  },
  sartli: {
    baslik: 'Şartlı uygun fırsatlar',
    aciklama: 'Eksikler kapatılırsa uygun hâle gelecek çağrılar.',
    secer: (m) => m.verdict === 'ConditionallyEligible',
    pay: null,
    vurguBasligi: 'Eksik koşul',
  },
  ortalama: {
    baslik: 'Ortalamayı oluşturan değerlendirmeler',
    aciklama: 'Ortalama, değerlendirilen tüm çağrıların skorundan hesaplanır.',
    secer: () => true,
    pay: null,
    vurguBasligi: null,
  },
  kapanan: {
    baslik: 'Son başvurusu 15 günde dolan fırsatlar',
    // Sunucu ölçütü: 0 < kalanGün <= 15 VE karar "uygun değil" DEĞİL. Uygun olmayan
    // çağrıyı aksiyon listesine koymak, kapanmasının bir önemi olmadığı için yanıltırdı.
    aciklama: 'Süresi dolmak üzere olan, elenmemiş çağrılar.',
    secer: (m) =>
      m.daysUntilDeadline !== null &&
      m.daysUntilDeadline > 0 &&
      m.daysUntilDeadline <= KAPANMA_ESIGI_GUN &&
      m.verdict !== 'NotEligible',
    pay: null,
    vurguBasligi: null,
  },
  belge: {
    baslik: 'Zorunlu belgesi eksik fırsatlar',
    aciklama: 'Karttaki sayı belge adedidir; aynı fırsatta birden çok belge eksik olabilir.',
    secer: (m) => m.missingMandatoryDocumentCount > 0,
    pay: (m) => m.missingMandatoryDocumentCount,
    vurguBasligi: 'Eksik belge',
  },
  bosluk: {
    baslik: 'Veri boşluğu olan fırsatlar',
    aciklama:
      'Profilde beyan edilmemiş alan yüzünden karar verilemeyen koşullar. Bunlar firmayı ELEMEZ; kararı askıya alır.',
    secer: (m) => m.dataGapCount > 0,
    pay: (m) => m.dataGapCount,
    vurguBasligi: 'Veri boşluğu',
  },
}

export interface KpiKirilimi {
  kayitlar: OpportunityMatch[]
  /** Kaç fırsat listeleniyor. */
  firsatSayisi: number
  /**
   * Karttaki sayının bu listeden yeniden hesaplanmış hâli. Kartla tutmazsa ekranda
   * gösterilir; sessizce farklı iki sayı göstermektense tutarsızlığı söylemek yeğdir.
   */
  toplam: number
}

/**
 * Bir kırılımın kayıtlarını ve toplamını hesaplar.
 *
 * Sıralama kırılıma göre değişir: aciliyet listesinde en yakın tarih, ötekilerde en
 * yüksek skor önce gelir — kullanıcı listeyi açtığında ilk satır zaten aradığı satırdır.
 */
export function kpiKirilimiHesapla(anahtar: KpiAnahtari, kayitlar: OpportunityMatch[]): KpiKirilimi {
  const tanim = KPI_TANIMLARI[anahtar]
  const secilenler = kayitlar.filter(tanim.secer)

  const siralanmis = [...secilenler].sort((a, b) =>
    anahtar === 'kapanan'
      ? (a.daysUntilDeadline ?? Number.MAX_SAFE_INTEGER) -
        (b.daysUntilDeadline ?? Number.MAX_SAFE_INTEGER)
      : b.finalScore - a.finalScore,
  )

  const toplam =
    anahtar === 'ortalama'
      ? ortalamaSkor(secilenler)
      : tanim.pay
        ? secilenler.reduce((sum, m) => sum + tanim.pay!(m), 0)
        : secilenler.length

  return { kayitlar: siralanmis, firsatSayisi: secilenler.length, toplam }
}

/**
 * Ortalama skor.
 *
 * Sunucu iki basamağa yuvarlar, ekran tek basamak gösterir. Karşılaştırma ekrandaki
 * biçim üzerinden yapılmalıdır; yoksa 54.45 ile 54.5 "tutmuyor" sanılır.
 */
export function ortalamaSkor(kayitlar: OpportunityMatch[]): number {
  if (kayitlar.length === 0) return 0

  const toplam = kayitlar.reduce((sum, m) => sum + m.finalScore, 0)

  return Math.round((toplam / kayitlar.length) * 100) / 100
}

/** Kart ile listenin aynı sayıyı gösterip göstermediği. */
export function toplamTutuyorMu(anahtar: KpiAnahtari, kirilim: KpiKirilimi, karttaki: number): boolean {
  return anahtar === 'ortalama'
    ? kirilim.toplam.toFixed(1) === karttaki.toFixed(1)
    : kirilim.toplam === karttaki
}
