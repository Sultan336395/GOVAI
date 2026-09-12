import { describe, expect, it } from 'vitest'
import type { MyCompany } from '@/api/types'
import { companyToForm, emptyCompanyForm, validateCompanyForm } from './companyForm'

/**
 * Personel kırılımında "beyan edilmedi" ile "sıfır" ayrımı — form tarafı.
 *
 * Sunucu tarafı düzeltilse bile form bu ayrımı bozabilir: alanı 0 ile başlatmak ya da
 * boş girdiyi 0'a çevirmek, kullanıcı hiç dokunmadan "kadın çalışanımız yok" beyan
 * etmesine yol açar ve firma o şartı arayan çağrılardan elenir.
 */

const firma = (ek: Partial<MyCompany> = {}): MyCompany => ({
  id: '01a0',
  legalName: 'Pilot A.Ş.',
  shortName: null,
  taxNumber: '1234567890',
  legalType: 'JointStockCompany',
  size: 'Medium',
  primaryNaceCode: '4120',
  mainSector: 'İnşaat ve taahhüt',
  city: 'Mersin',
  employeeCount: 50,
  annualRevenue: 30_000_000,
  profileCompletionPercentage: 67,
  isActive: true,
  groupId: null,
  groupName: null,
  parentCompanyId: null,
  parentCompanyName: null,
  relationshipType: 'Independent',
  isHeadCompany: false,
  companyRole: 'CompanyOwner',
  isDefault: true,
  ...ek,
})

describe('boş form', () => {
  it('CF1. kırılım alanları BEYANSIZ başlar, sıfırla değil', () => {
    // Sıfırla başlasaydı, dokunulmadan kaydedilen her firma "yok" beyan etmiş olurdu.
    const form = emptyCompanyForm()

    expect(form.womenEmployeeCount).toBeUndefined()
    expect(form.youngEmployeeCount).toBeUndefined()
    expect(form.rAndDEmployeeCount).toBeUndefined()
    expect(form.disabledEmployeeCount).toBeUndefined()
  })

  it('CF2. toplam çalışan sayısı sıfırla başlar', () => {
    // Toplam alanında sıfır zaten "girilmedi" anlamına geliyor (CLAUDE.md §2.2).
    expect(emptyCompanyForm().employeeCount).toBe(0)
  })
})

describe('düzenleme için yükleme', () => {
  it('CF3. kırılım GERİ YÜKLENİR', () => {
    // Yüklenmezse form onları bilmeden gönderir ve her düzenleme beyanı siler.
    const form = companyToForm(firma({
      womenEmployeeCount: 18,
      youngEmployeeCount: 9,
      youngEmployeeMaxAge: 29,
      rAndDEmployeeCount: 6,
      disabledEmployeeCount: 2,
    }))

    expect(form.womenEmployeeCount).toBe(18)
    expect(form.youngEmployeeCount).toBe(9)
    expect(form.youngEmployeeMaxAge).toBe(29)
    expect(form.rAndDEmployeeCount).toBe(6)
    expect(form.disabledEmployeeCount).toBe(2)
  })

  it('CF4. BEYAN EDİLMEMİŞ alan beyansız kalır, sıfıra dönmez', () => {
    const form = companyToForm(firma())

    expect(form.womenEmployeeCount).toBeUndefined()
    expect(form.rAndDEmployeeCount).toBeUndefined()
  })

  it('CF5. BEYAN EDİLMİŞ SIFIR korunur', () => {
    // Firma "gerçekten yok" dediyse bu bir cevaptır; beyansıza çevrilmemeli.
    const form = companyToForm(firma({ womenEmployeeCount: 0 }))

    expect(form.womenEmployeeCount).toBe(0)
  })
})

describe('doğrulama', () => {
  const temel = { ...emptyCompanyForm(), legalName: 'X', taxNumber: '1234567890', employeeCount: 50 }

  it('CF6. beyan edilmemiş alan denetlenmez', () => {
    const hatalar = validateCompanyForm(temel)

    expect(hatalar.womenEmployeeCount).toBeUndefined()
    expect(hatalar.rAndDEmployeeCount).toBeUndefined()
    expect(hatalar.disabledEmployeeCount).toBeUndefined()
  })

  it('CF7. beyan edilmiş sıfır GEÇERLİDİR', () => {
    const hatalar = validateCompanyForm({ ...temel, womenEmployeeCount: 0 })

    expect(hatalar.womenEmployeeCount).toBeUndefined()
  })

  it('CF8. negatif beyan reddedilir', () => {
    const hatalar = validateCompanyForm({ ...temel, womenEmployeeCount: -1 })

    expect(hatalar.womenEmployeeCount).toContain('negatif')
  })

  it('CF9. toplamı aşan beyan reddedilir', () => {
    const hatalar = validateCompanyForm({ ...temel, rAndDEmployeeCount: 51 })

    expect(hatalar.rAndDEmployeeCount).toContain('aşamaz')
  })

  it('CF10. tam sayı olmayan beyan reddedilir', () => {
    const hatalar = validateCompanyForm({ ...temel, disabledEmployeeCount: 1.5 })

    expect(hatalar.disabledEmployeeCount).toContain('tam sayı')
  })

  it('CF11. genç sayısı beyan edildiyse YAŞ SINIRI da istenir', () => {
    // Tanım bilinmeden sayı anlamsızdır; sunucu da aynı kuralı uygular.
    const hatalar = validateCompanyForm({ ...temel, youngEmployeeCount: 9 })

    expect(Object.keys(hatalar).length).toBeGreaterThan(0)
  })

  it('CF12. toplam çalışan sıfırsa kırılım denetimi tetiklenmez', () => {
    // Toplam bilinmiyorken "toplamı aşamaz" demek anlamsız olurdu.
    const hatalar = validateCompanyForm({
      ...temel,
      employeeCount: 0,
      womenEmployeeCount: 5,
    })

    expect(hatalar.womenEmployeeCount).toBeUndefined()
  })
})
