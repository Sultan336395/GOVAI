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
   * Kullanıcının CompanyOwner olduğu varsayılan şirket. Yalnızca aktif şirket henüz
   * çözülmediğinde (liste yükleniyor ya da kayıtlı seçim eskimiş) kullanılır.
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
  if (isPlatformRole(userRole)) {
    return [
      {
        title: 'Fırsat ve Analiz',
        items: [
          { to: '/opportunities', label: 'Fon, Hibe ve İhale Kataloğu', icon: 'catalog' },
        ],
      },
      {
        title: 'Sistem Yönetimi',
        items: [{ to: '/sources', label: 'Veri Kaynakları', icon: 'sources' }],
      },
    ]
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

  // Üyelik ve rol yönetimi yalnızca CompanyOwner'a açıktır: sunucudaki karşılığı
  // CompanyPermission.ManageMembers'tır ve orada da yalnızca sahibi karşılar.
  // CompanyManager'a göstermek, açıldığında "yetkiniz yok" diyen bir menü üretirdi.
  //
  // Aktif şirket henüz çözülmediyse (liste yükleniyor ya da kayıtlı seçim başka bir
  // hesaba ait) menü kaybolmasın diye sahibi olunan varsayılan şirkete düşülür.
  const membersCompanyId =
    activeRole === 'CompanyOwner'
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
