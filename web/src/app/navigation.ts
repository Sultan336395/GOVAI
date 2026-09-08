import type { CompanyRole, UserRole } from '@/api/types'
import type { NavIconName } from '@/components/NavIcons'

/**
 * Sol menünün tanımı ve rol bazlı görünürlüğü.
 *
 * Bileşenden ayrı tutulur: menü kuralları kod okunurken tek yerde görülsün ve bileşen
 * dosyası yalnızca bileşen dışa aktarsın (hızlı yenileme kuralı, bkz. CLAUDE.md §5).
 *
 * <b>Bu dosya bir güvenlik sınırı değildir.</b> Menüden gizlemek yetkiyi kaldırmaz;
 * yetki her istekte sunucuda, `Policies` ve `CompanyAccessGuard` ile denetlenir.
 * Buradaki tek amaç kullanıcıya çalışmayacak bir bağlantı göstermemektir.
 */

export interface NavItem {
  to: string
  label: string
  icon: NavIconName
  /** Yalnızca kök yol için: alt yollarda da "etkin" görünmesin diye. */
  end?: boolean
}

export interface NavGroup {
  title: string
  items: NavItem[]
}

/** Menüyü kuran bağlam. Hepsi zaten ekranda mevcut olan bilgilerdir. */
export interface NavContext {
  /** Kiracı veya platform rolü (jetondaki `govai_role`). */
  userRole: UserRole | null
  /** Aktif şirketteki rol. Şirket seçilmemişse `null`. */
  activeRole: CompanyRole | null
  activeCompanyId: string | null
  /** Kullanıcı, şirketlerinden herhangi birinde sahip veya yönetici mi? */
  canManageAnyCompany: boolean
  /**
   * Kullanıcının sahip ya da yönetici olduğu varsayılan şirket. Yalnızca aktif şirket
   * henüz çözülmediğinde (liste yükleniyor ya da kayıtlı seçim eskimiş) kullanılır.
   */
  fallbackOwnedCompanyId: string | null
}

/** Ortak katalogu ve kaynakları işleten roller. Kiracı yöneticisi bunlara dâhil değildir. */
const platformRoles: UserRole[] = ['PlatformCatalogManager', 'PlatformReviewer']

export function isPlatformRole(role: UserRole | null): boolean {
  return role !== null && platformRoles.includes(role)
}

export function buildNavigation({
  userRole,
  activeRole,
  activeCompanyId,
  canManageAnyCompany,
  fallbackOwnedCompanyId,
}: NavContext): NavGroup[] {
  // Veri toplama servisi bir insan hesabı değildir; panelde işi yoktur.
  if (userRole === 'SystemIngest') {
    return []
  }

  // Platform işletimi kiracı verisine erişemez (Policies.CompanyData onları dışarıda
  // bırakır). Şirket ekranlarını göstermek çalışmayan bağlantı üretirdi.
  // İNCELEYİCİ TEK BİR ALAN GÖRÜR.
  //
  // Rolün tek işi kataloğu denetlemek ve düzeltmek; menüsünde başka bir şey olmaması
  // hem en az yetki ilkesinin görünen yüzü hem de pratik bir kolaylıktır — hesabı
  // devralan kişi ne yapması gerektiğini aramak zorunda kalmaz.
  if (userRole === 'PlatformReviewer') {
    return [
      {
        title: 'Platform İnceleme',
        items: [
          { to: '/quarantine', label: 'Karantina İnceleme', icon: 'quarantine' },
          { to: '/platform/catalog-repair', label: 'Katalog Onarımı', icon: 'quarantine' },
          { to: '/platform/rule-evidence', label: 'Kanıt Bağlama', icon: 'regulation' },
        ],
      },
    ]
  }

  if (isPlatformRole(userRole)) {
    const platform: NavGroup[] = [
      {
        title: 'Fırsat ve Analiz',
        items: [
          { to: '/opportunities', label: 'Fon, Hibe ve İhale Kataloğu', icon: 'catalog' },
        ],
      },
      {
        title: 'Mevzuat ve Uyum',
        items: [
          { to: '/regulatory-changes', label: 'Mevzuat Değişiklikleri', icon: 'regulation' },
        ],
      },
    ]

    // Kaynak yönetimi katalog yöneticisinin işidir; karantina incelemesi ikisinin de.
    const system: NavItem[] = [
      { to: '/sources', label: 'Veri Kaynakları', icon: 'sources' },
      { to: '/quarantine', label: 'Karantina İnceleme', icon: 'quarantine' },
      { to: '/platform/catalog-repair', label: 'Katalog Onarımı', icon: 'quarantine' },
      { to: '/platform/rule-evidence', label: 'Kanıt Bağlama', icon: 'regulation' },
    ]

    platform.push({ title: 'Platform Yönetimi', items: system })

    return platform
  }

  const groups: NavGroup[] = [
    {
      title: 'Genel',
      items: [
        { to: '/', label: 'Genel Bakış', icon: 'overview', end: true },
        { to: '/notifications', label: 'Bildirimler', icon: 'notifications' },
      ],
    },
    {
      title: 'Fırsat ve Analiz',
      items: [
        { to: '/matches', label: 'Fırsat Eşleşmelerim', icon: 'matches' },
        { to: '/opportunities', label: 'Fon, Hibe ve İhale Kataloğu', icon: 'catalog' },
        { to: '/simulation', label: 'Senaryo Analizi', icon: 'scenario' },
      ],
    },
  ]

  // Mevzuat kiracı kullanıcılarına da açıktır: resmî ve doğrulanmış kayıtları okurlar.
  groups.push({
    title: 'Mevzuat ve Uyum',
    items: [{ to: '/regulatory-changes', label: 'Mevzuat Değişiklikleri', icon: 'regulation' }],
  })

  const companyItems: NavItem[] = [
    { to: '/companies', label: 'Şirketlerim', icon: 'companies', end: true },
    { to: '/company', label: 'Şirket Profili', icon: 'companyProfile' },
  ]

  // Grup ve hiyerarşi kurmak sahip/yönetici işidir; uzman ve görüntüleyici için
  // ekran yalnızca "yetkiniz yok" derdi.
  if (canManageAnyCompany) {
    companyItems.push({
      to: '/companies/groups',
      label: 'Şirket Grupları',
      icon: 'companyGroups',
    })
  }

  // Ekran iki kademelidir: CompanyOwner yönetir, CompanyManager yalnızca görür.
  // Sunucudaki karşılığı da böyledir — üyelik listesi Read, her değiştirme işlemi
  // ManageMembers ister ve onu yalnızca sahibi karşılar.
  // CompanyExpert ve CompanyViewer menüyü hiç görmez.
  //
  // Aktif şirket henüz çözülmediyse (liste yükleniyor ya da kayıtlı seçim başka bir
  // hesaba ait) menü kaybolmasın diye yedek şirkete düşülür.
  const membersCompanyId =
    activeRole === 'CompanyOwner' || activeRole === 'CompanyManager'
      ? activeCompanyId
      : activeRole === null
        ? fallbackOwnedCompanyId
        : null

  if (membersCompanyId) {
    companyItems.push({
      to: `/companies/${membersCompanyId}/members`,
      label: 'Kullanıcılar ve Yetkiler',
      icon: 'members',
    })
  }

  groups.push({ title: 'Şirket Yönetimi', items: companyItems })

  return groups
}
