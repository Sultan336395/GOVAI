import type {
  DocumentParseStatus,
  QuarantineReason,
  RegulationDomain,
  RegulatoryChangeType,
  SourceCategory,
  SourceHealth,
} from '@/api/types'

/** Faz 2 – RegTech etiketleri. Kullanıcıya görünen metinler Türkçedir. */

export const regulationDomainLabels: Record<RegulationDomain, string> = {
  Tax: 'Vergi',
  SocialSecurity: 'Sosyal güvenlik',
  LabourLaw: 'İş hukuku',
  CommercialLaw: 'Ticaret hukuku',
  DataProtection: 'Kişisel verilerin korunması',
  CorporateGovernance: 'Kurumsal yönetim',
  Other: 'Diğer',
}

export const regulationDomainOrder: RegulationDomain[] = [
  'Tax', 'SocialSecurity', 'LabourLaw', 'CommercialLaw',
  'DataProtection', 'CorporateGovernance', 'Other',
]

export const changeTypeLabels: Record<RegulatoryChangeType, string> = {
  NewRegulation: 'Yeni düzenleme',
  Amendment: 'Değişiklik',
  Repeal: 'Yürürlükten kaldırma',
  Circular: 'Genelge',
  Communique: 'Tebliğ',
  Decision: 'Karar',
  Guidance: 'Rehber',
  Announcement: 'Duyuru',
}

export const sourceCategoryLabels: Record<SourceCategory, string> = {
  Regulation: 'Mevzuat',
  Tax: 'Vergi',
  SocialSecurity: 'Sosyal güvenlik',
  LabourLaw: 'İş hukuku',
  CommercialLaw: 'Ticaret hukuku',
  Grant: 'Hibe',
  Incentive: 'Teşvik',
  Fund: 'Fon',
  Tender: 'İhale',
  EuProgramme: 'AB programı',
}

export const sourceHealthLabels: Record<SourceHealth, string> = {
  Unverified: 'Doğrulanmadı',
  Healthy: 'Sağlıklı',
  Degraded: 'Kısmi hata',
  Failing: 'Hatalı',
}

/** Sağlık durumunun rozet sınıfı; mevcut skor rozetleriyle aynı görsel dili kullanır. */
export const sourceHealthClass: Record<SourceHealth, string> = {
  Unverified: 'indeterminate',
  Healthy: 'eligible',
  Degraded: 'conditional',
  Failing: 'not',
}

export const quarantineReasonLabels: Record<QuarantineReason, string> = {
  None: 'Temiz',
  InvalidSourcePage: 'İlan olmayan sayfa',
  Duplicate: 'Mükerrer kayıt',
  MissingRequiredFields: 'Zorunlu alan eksik',
  ParserFailed: 'Ayrıştırılamadı',
  NeedsManualReview: 'İnceleme gerekiyor',
}

export const parseStatusLabels: Record<DocumentParseStatus, string> = {
  Pending: 'Bekliyor',
  Parsed: 'Ayrıştırıldı',
  Failed: 'Başarısız',
  NeedsOcr: 'OCR gerekiyor',
}
