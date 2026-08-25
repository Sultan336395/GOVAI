import type { EligibilityVerdict, SupportCategory } from '@/api/types'

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
