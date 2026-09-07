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
    youngEmployeeCount: 0,
    youngEmployeeMaxAge: null,
    disabledEmployeeCount: 0,
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

  // Genç ve engelli sayıları isteğe bağlıdır ama girildiyse tutarlı olmalıdır.
  // Sunucu da aynı kuralı uygular; buradaki denetim kullanıcıyı beklemeden uyarır.
  const toplam = values.employeeCount ?? 0

  for (const [alan, etiket] of [
    ['youngEmployeeCount', 'Genç çalışan sayısı'],
    ['disabledEmployeeCount', 'Engelli çalışan sayısı'],
  ] as const) {
    const deger = values[alan] ?? 0

    if (deger < 0) {
      errors[alan] = `${etiket} negatif olamaz.`
    } else if (!Number.isInteger(deger)) {
      errors[alan] = `${etiket} tam sayı olmalıdır.`
    } else if (toplam > 0 && deger > toplam) {
      errors[alan] = `${etiket} toplam çalışan sayısını (${toplam}) aşamaz.`
    }
  }

  const yas = values.youngEmployeeMaxAge

  if (yas !== null && yas !== undefined && (yas < 15 || yas > 65)) {
    errors.youngEmployeeMaxAge = 'Yaş sınırı 15 ile 65 arasında olmalıdır.'
  }

  if ((values.youngEmployeeCount ?? 0) > 0 && (yas === null || yas === undefined)) {
    errors.youngEmployeeMaxAge =
      'Genç çalışan sayısı girdiyseniz hangi yaş sınırına göre saydığınızı da belirtin.'
  }
  if ((values.annualRevenue ?? 0) < 0) errors.annualRevenue = 'Ciro negatif olamaz.'

  return errors
}
