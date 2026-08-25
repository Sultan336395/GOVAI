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

    expect(gruplar(ctx)).toEqual(['Fırsat ve Analiz', 'Mevzuat ve Uyum', 'Platform Yönetimi'])
    expect(etiketler(ctx)).toContain('Veri Kaynakları')
    expect(etiketler(ctx)).toContain('Karantina İnceleme')
    expect(etiketler(ctx)).not.toContain('Şirketlerim')
    expect(etiketler(ctx)).not.toContain('Fırsat Eşleşmelerim')
  })

  it('inceleyici karantinayı görür ama kaynak yapılandırmasını görmez', () => {
    const ctx = baglam({ userRole: 'PlatformReviewer' })

    expect(etiketler(ctx)).toContain('Karantina İnceleme')
    expect(etiketler(ctx)).not.toContain('Veri Kaynakları')
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
      'Mevzuat ve Uyum',
      'Şirket Yönetimi',
    ])
    expect(etiketler(ctx)).toContain('Mevzuat Değişiklikleri')
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
