import type { EligibilityVerdict, SectorFit, SourceType, SupportCategory } from '@/api/types'

const currencyFormatter = new Intl.NumberFormat('tr-TR', {
  style: 'currency',
  currency: 'TRY',
  maximumFractionDigits: 0,
})

const dateFormatter = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
})

export const formatCurrency = (value: number | null | undefined): string =>
  value === null || value === undefined ? '—' : currencyFormatter.format(value)

/**
 * Belgeden çıkarılmış bir tutarı kendi para birimiyle yazar.
 *
 * `formatCurrency` her tutarı TRY sembolüyle basar; kalemin para birimini ayrıca
 * eklemek "₺1.500.000 TRY" gibi iki kez para birimi taşıyan bir metin üretiyordu.
 * Avro cinsinden bir AB çağrısında ise tutar yanlış para biriminde görünürdü.
 */
export function formatAmount(
  value: number | null | undefined,
  currency: string | null | undefined,
): string {
  if (value === null || value === undefined) return '—'

  const kod = (currency ?? 'TRY').trim().toUpperCase()

  try {
    return new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency: kod,
      maximumFractionDigits: 0,
    }).format(value)
  } catch {
    // Tanınmayan kod uydurulmaz; sayı yazılır, kod olduğu gibi eklenir.
    return `${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 }).format(value)} ${kod}`
  }
}

export const formatDate = (value: string | null | undefined): string =>
  value ? dateFormatter.format(new Date(value)) : '—'

export const formatPercent = (ratio: number, digits = 0): string =>
  `%${(ratio * 100).toFixed(digits)}`

export const formatScore = (score: number): string => score.toFixed(1)

export const verdictLabels: Record<EligibilityVerdict, string> = {
  Eligible: 'Uygun',
  ConditionallyEligible: 'Şartlı uygun',
  NotEligible: 'Uygun değil',
  Indeterminate: 'Belirsiz',
}

export const verdictClass: Record<EligibilityVerdict, string> = {
  Eligible: 'eligible',
  ConditionallyEligible: 'conditional',
  NotEligible: 'not-eligible',
  Indeterminate: 'indeterminate',
}

/**
 * Sektör uyumunun Türkçe karşılığı.
 *
 * "Doğrulanamadı" ile "uyumsuz" bilerek ayrı yazılır: birincisi bilgi eksikliğidir
 * (çağrı metninden sektör çıkarılamadı ya da firmanın NACE kodu girilmemiş), ikincisi
 * verilmiş bir karardır. Kullanıcı ikisine farklı tepki verir — biri veri tamamlamayı,
 * diğeri listeyi kapatmayı gerektirir.
 */
export const sectorFitLabels: Record<SectorFit, string> = {
  Matched: 'Sektör uyumlu',
  Unverified: 'Sektör uyumu doğrulanamadı',
  NotMatched: 'Sektör uyumsuz',
}

export const sectorFitClass: Record<SectorFit, string> = {
  Matched: 'eligible',
  Unverified: 'indeterminate',
  NotMatched: 'not-eligible',
}

/**
 * Kaynak türünün Türkçe karşılığı.
 *
 * Ham enum değeri ("OfficialGazette") kullanıcıya gösterilmez: arayüz metinleri
 * Türkçedir ve bir danışman "OfficialGazette" ifadesinden ne anlaması gerektiğini
 * bilemez.
 */
export const sourceTypeLabels: Record<SourceType, string> = {
  OfficialGazette: 'Resmî Gazete',
  Ministry: 'Bakanlık',
  DevelopmentAgency: 'Kalkınma ajansı',
  KosgebOrSimilar: 'KOSGEB ve benzeri kurum',
  TenderPortal: 'İhale portalı',
  EuOrInternational: 'AB / uluslararası kurum',
  Other: 'Diğer',
}

export const categoryLabels: Record<SupportCategory, string> = {
  EmploymentIncentive: 'İstihdam teşviki',
  InvestmentIncentive: 'Yatırım teşviki',
  Grant: 'Hibe',
  RndSupport: 'Ar-Ge desteği',
  DigitalTransformation: 'Dijital dönüşüm',
  ExportSupport: 'İhracat desteği',
  GreenTransformation: 'Yeşil dönüşüm',
  Tender: 'Kamu ihalesi',
  Loan: 'Kredi / faiz desteği',
  Other: 'Diğer',
}

/** Son başvuruya kalan süreyi insan diline çevirir. */
export function formatDeadline(days: number | null): string {
  if (days === null) return 'Süresiz'
  if (days < 0) return 'Süresi doldu'
  if (days === 0) return 'Bugün son gün'
  if (days === 1) return 'Yarın son gün'
  return `${days} gün kaldı`
}

/**
 * Faz 2 – veri kalitesi gösterimi.
 *
 * Boş bir alanı boş bırakmak, kullanıcıya "bu çağrının son başvuru tarihi yok" demekle
 * aynı şeydir. Oysa gerçek çoğu zaman "resmî kaynakta yazmıyor"dur. İkisi ayrı gösterilir.
 */
export const NOT_PROVIDED_LABEL = 'Resmî kaynakta belirtilmemiş'
export const NOT_APPLICABLE_LABEL = 'Bu çağrı için geçerli değil'

export type FieldAvailabilityState = 'Provided' | 'NotProvided' | 'NotApplicable'

/**
 * Bir alanı gösterime hazırlar: değer varsa biçimlendirir, yoksa nedenini yazar.
 * `null`, boş dize veya `undefined` hiçbir zaman doğrudan ekrana çıkmaz.
 */
export function displayField(
  value: string | number | null | undefined,
  availability?: FieldAvailabilityState,
): string {
  if (value !== null && value !== undefined && String(value).trim() !== '') {
    return String(value)
  }

  if (availability === 'NotApplicable') return NOT_APPLICABLE_LABEL

  return NOT_PROVIDED_LABEL
}
