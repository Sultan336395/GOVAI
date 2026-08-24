import type { CreateCompanyRequest, MyCompany } from '@/api/types'

/**
 * Şirket formunun değerleri ve doğrulaması. Bileşenden ayrı tutulur: bileşen dosyasının
 * yalnızca bileşen dışa aktarması, Vite'ın hızlı yenilemesi için gereklidir (bkz. CLAUDE.md §5).
 */

export type CompanyFormValues = CreateCompanyRequest

export function emptyCompanyForm(): CompanyFormValues {
  return {
    legalName: '',
    taxNumber: '',
    country: 'TR',
    mainSector: '',
    primaryNaceCode: '',
    legalType: 'LimitedCompany',
    secondaryNaceCodes: [],
    subSectors: [],
    targetCountries: [],
    employeeCount: 0,
    rAndDEmployeeCount: 0,
    womenEmployeeCount: 0,
    annualRevenue: 0,
    balanceSize: 0,
    isInTechnopark: false,
    exportFlag: false,
    relationshipType: 'Independent',
    isHeadCompany: false,
  }
}

export function companyToForm(company: MyCompany): CompanyFormValues {
  return {
    ...emptyCompanyForm(),
    legalName: company.legalName,
    shortName: company.shortName,
    taxNumber: company.taxNumber,
    mainSector: company.mainSector ?? '',
    primaryNaceCode: company.primaryNaceCode ?? '',
    legalType: company.legalType,
    city: company.city,
    employeeCount: company.employeeCount,
    annualRevenue: company.annualRevenue,
    groupId: company.groupId,
    parentCompanyId: company.parentCompanyId,
    relationshipType: company.relationshipType,
    isHeadCompany: company.isHeadCompany,
  }
}

/** Sunucudaki kuralların aynısı: TR için 10 hane, yalnızca rakam. */
export function validateCompanyForm(values: CompanyFormValues): Record<string, string> {
  const errors: Record<string, string> = {}

  if (!values.legalName.trim()) errors.legalName = 'Ticari unvan zorunludur.'
  if (!values.country.trim()) errors.country = 'Ülke zorunludur.'
  if (!values.mainSector.trim()) errors.mainSector = 'Ana sektör zorunludur.'
  if (!values.primaryNaceCode.trim()) errors.primaryNaceCode = 'Ana NACE kodu zorunludur.'

  const tax = values.taxNumber.trim()
  if (!tax) {
    errors.taxNumber = 'Vergi numarası zorunludur.'
  } else if (!/^[0-9]+$/.test(tax)) {
    errors.taxNumber = 'Vergi numarası yalnızca rakam içerebilir.'
  } else if (values.country.trim().toUpperCase() === 'TR' && tax.length !== 10) {
    errors.taxNumber = 'Türkiye için vergi numarası 10 hane olmalıdır.'
  }

  if ((values.employeeCount ?? 0) < 0) errors.employeeCount = 'Çalışan sayısı negatif olamaz.'
  if ((values.annualRevenue ?? 0) < 0) errors.annualRevenue = 'Ciro negatif olamaz.'

  return errors
}
