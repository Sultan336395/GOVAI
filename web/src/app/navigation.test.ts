import { describe, expect, it } from 'vitest'
import type { CompanyRole, UserRole } from '@/api/types'
import { buildNavigation, isPlatformRole, type NavContext } from './navigation'

/**
 * Sol menünün rol bazlı görünürlüğü.
 *
 * Menü bir güvenlik sınırı değildir (bkz. `navigation.ts`); sunucu yetkisi ayrıca
 * `SourcePipelineTests` ve `PlatformRoleTests` ile doğrulanır. Buradaki testin işi,
 * kullanıcıya çalışmayacak bir bağlantının gösterilmediğini garanti etmektir.
 */

function baglam(over: Partial<NavContext> = {}): NavContext {
  return {
    userRole: 'CompanyManager',
    activeRole: 'CompanyOwner',
    activeCompanyId: 'sirket-1',
    canManageAnyCompany: true,
    fallbackOwnedCompanyId: 'sirket-1',
    ...over,
  }
}

const gruplar = (ctx: NavContext) => buildNavigation(ctx).map((g) => g.title)
const etiketler = (ctx: NavContext) =>
  buildNavigation(ctx).flatMap((g) => g.items.map((i) => i.label))

describe('platform rolleri', () => {
  it('kiracı rolleri platform sayılmaz', () => {
    const kiraci: UserRole[] = ['SuperAdmin', 'CompanyManager', 'OperationUser', 'ReadOnly']

    expect(kiraci.every((r) => !isPlatformRole(r))).toBe(true)
    expect(isPlatformRole('PlatformCatalogManager')).toBe(true)
    expect(isPlatformRole('PlatformReviewer')).toBe(true)
    expect(isPlatformRole(null)).toBe(false)
  })

  it('katalog yöneticisi kaynakları ve karantinayı görür, şirket ekranlarını görmez', () => {
    const ctx = baglam({ userRole: 'PlatformCatalogManager' })

    expect(gruplar(ctx)).toEqual(['Fırsat ve Analiz', 'Mevzuat ve Uyum', 'Katalog Denetimi'])
    expect(etiketler(ctx)).toContain('Resmî Kaynaklar')
    expect(etiketler(ctx)).toContain('İnceleme Bekleyenler')
    expect(etiketler(ctx)).not.toContain('Şirketlerim')
    expect(etiketler(ctx)).not.toContain('Fırsat Eşleşmelerim')
  })

  it('inceleyici YALNIZCA Katalog Denetimi alanını görür', () => {
    const ctx = baglam({ userRole: 'PlatformReviewer' })

    // Rolün tek işi kataloğu denetlemek. Tek grup görmesi en az yetki ilkesinin
    // görünen yüzü; hesabı devralan kişi ne yapacağını aramak zorunda kalmaz.
    expect(gruplar(ctx)).toEqual(['Katalog Denetimi'])

    expect(etiketler(ctx)).toEqual([
      'İnceleme Bekleyenler',
      'Katalog Düzeltme',
      'Dayanak Eşleştirme',
    ])
  })

  it('inceleyici kaynak yapılandırmasını ve şirket ekranlarını görmez', () => {
    const ctx = baglam({ userRole: 'PlatformReviewer' })

    for (const gorunmemeli of [
      'Resmî Kaynaklar',
      'Şirketlerim',
      'Fırsat Eşleşmelerim',
      'Fon, Hibe ve İhale Kataloğu',
      'Mevzuat Değişiklikleri',
    ]) {
      expect(etiketler(ctx)).not.toContain(gorunmemeli)
    }
  })

  it('katalog yöneticisi bakım ekranlarını da görür', () => {
    const ctx = baglam({ userRole: 'PlatformCatalogManager' })

    expect(etiketler(ctx)).toContain('Katalog Düzeltme')
    expect(etiketler(ctx)).toContain('Dayanak Eşleştirme')
  })

  it('kiracı kullanıcısı bakım ekranlarını hiç görmez', () => {
    // Menü bir güvenlik sınırı değildir; sunucu yetkisi PlatformReviewAccessTests ile
    // ayrıca doğrulanır. Buradaki iş, çalışmayacak bağlantı göstermemektir.
    for (const rol of ['SuperAdmin', 'CompanyManager', 'OperationUser', 'ReadOnly'] as UserRole[]) {
      const ctx = baglam({ userRole: rol })

      expect(etiketler(ctx)).not.toContain('Katalog Düzeltme')
      expect(etiketler(ctx)).not.toContain('Dayanak Eşleştirme')
    }
  })

  it('veri toplama servisi hesabı hiçbir menü görmez', () => {
    expect(buildNavigation(baglam({ userRole: 'SystemIngest' }))).toEqual([])
  })
})

describe('kiracı kullanıcıları', () => {
  it('mevzuat şirket kullanıcısına açıktır, platform ekranları değildir', () => {
    const ctx = baglam()

    expect(gruplar(ctx)).toEqual([
      'Genel',
      'Fırsat ve Analiz',
      'Raporlar',
      'Mevzuat ve Uyum',
      'Şirket Yönetimi',
    ])
    expect(etiketler(ctx)).toContain('Mevzuat Değişiklikleri')
    expect(etiketler(ctx)).toContain('Haftalık Raporlar')
    expect(etiketler(ctx)).toContain('Karar Doğruluğu')
    expect(etiketler(ctx)).not.toContain('Veri Kaynakları')
    expect(etiketler(ctx)).not.toContain('Karantina İnceleme')
  })

  it.each<CompanyRole>(['CompanyOwner', 'CompanyManager'])(
    '%s kullanıcı yetkileri ekranını görür',
    (rol) => {
      expect(etiketler(baglam({ activeRole: rol }))).toContain('Kullanıcılar ve Yetkiler')
    },
  )

  it.each<CompanyRole>(['CompanyExpert', 'CompanyViewer'])(
    '%s kullanıcı yetkileri ekranını görmez',
    (rol) => {
      const ctx = baglam({ activeRole: rol, canManageAnyCompany: false })

      expect(etiketler(ctx)).not.toContain('Kullanıcılar ve Yetkiler')
      expect(etiketler(ctx)).not.toContain('Şirket Grupları')
      // Mevzuat okumak yönetim yetkisi gerektirmez.
      expect(etiketler(ctx)).toContain('Mevzuat Değişiklikleri')
    },
  )

  it('aktif şirket henüz çözülmediyse yedek şirkete düşülür', () => {
    const ctx = baglam({ activeRole: null, activeCompanyId: null })
    const hedef = buildNavigation(ctx)
      .flatMap((g) => g.items)
      .find((i) => i.label === 'Kullanıcılar ve Yetkiler')

    expect(hedef?.to).toBe('/companies/sirket-1/members')
  })

  it('yedek şirket de yoksa menü çalışmayan bağlantı üretmez', () => {
    const ctx = baglam({ activeRole: null, activeCompanyId: null, fallbackOwnedCompanyId: null })

    expect(etiketler(ctx)).not.toContain('Kullanıcılar ve Yetkiler')
  })
})
