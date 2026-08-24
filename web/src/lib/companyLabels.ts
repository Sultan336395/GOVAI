import type { CompanyRelationshipType, CompanyRole, LegalType } from '@/api/types'

export const companyRoleLabels: Record<CompanyRole, string> = {
  CompanyOwner: 'Şirket sahibi',
  CompanyManager: 'Şirket yöneticisi',
  CompanyExpert: 'Uzman',
  CompanyViewer: 'Görüntüleyici',
}

/** Rolün ne yapabildiğini kullanıcıya tek cümleyle anlatır. */
export const companyRoleHints: Record<CompanyRole, string> = {
  CompanyOwner: 'Her şeyi yapabilir; kullanıcı ve yetki yönetimi dâhil.',
  CompanyManager: 'Firma profilini düzenler, analiz çalıştırır. Kullanıcı yönetemez.',
  CompanyExpert: 'Analiz ve simülasyon çalıştırır. Profili düzenleyemez.',
  CompanyViewer: 'Yalnızca görüntüler.',
}

export const relationshipLabels: Record<CompanyRelationshipType, string> = {
  Independent: 'Bağımsız',
  HeadCompany: 'Ana şirket',
  Subsidiary: 'Bağlı şirket',
  Affiliate: 'İştirak',
  Branch: 'Şube',
  GroupCompany: 'Grup şirketi',
}

export const legalTypeLabels: Record<LegalType, string> = {
  Unknown: 'Belirtilmemiş',
  SoleProprietorship: 'Şahıs işletmesi',
  LimitedCompany: 'Limited şirket',
  JointStockCompany: 'Anonim şirket',
  Cooperative: 'Kooperatif',
  Association: 'Dernek',
  Foundation: 'Vakıf',
  PublicEntity: 'Kamu kurumu',
}

export const enterpriseSizeLabels: Record<string, string> = {
  Micro: 'Mikro',
  Small: 'Küçük',
  Medium: 'Orta',
  Large: 'Büyük',
}

export const companyRoleOrder: CompanyRole[] = [
  'CompanyOwner',
  'CompanyManager',
  'CompanyExpert',
  'CompanyViewer',
]

export const relationshipOrder: CompanyRelationshipType[] = [
  'Independent',
  'HeadCompany',
  'Subsidiary',
  'Affiliate',
  'Branch',
  'GroupCompany',
]

export const legalTypeOrder: LegalType[] = [
  'LimitedCompany',
  'JointStockCompany',
  'SoleProprietorship',
  'Cooperative',
  'Association',
  'Foundation',
  'PublicEntity',
  'Unknown',
]
