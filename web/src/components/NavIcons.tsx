import type { ReactElement, SVGProps } from 'react'

/**
 * Sol menünün ikon seti.
 *
 * Dışarıdan bir ikon kütüphanesi eklenmedi: on ikon için yeni bir bağımlılık ve
 * paket boyutu taşımaya değmez. Hepsi aynı ızgarada (24×24), aynı çizgi kalınlığında
 * ve `currentColor` ile çizilir; böylece aktif/pasif renk geçişleri menü öğesinin
 * kendi rengini izler ve set görsel olarak tutarlı kalır.
 */

export type NavIconName =
  | 'overview'
  | 'notifications'
  | 'matches'
  | 'catalog'
  | 'scenario'
  | 'companies'
  | 'companyProfile'
  | 'companyGroups'
  | 'members'
  | 'sources'
  | 'regulation'
  | 'quarantine'
  | 'menu'
  | 'close'

type IconProps = SVGProps<SVGSVGElement>

function Svg({ children, ...props }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {children}
    </svg>
  )
}

const icons: Record<NavIconName, (props: IconProps) => ReactElement> = {
  // Panel: dört kutucuk
  overview: (p) => (
    <Svg {...p}>
      <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
      <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
      <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
      <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
    </Svg>
  ),

  // Bildirim: çan
  notifications: (p) => (
    <Svg {...p}>
      <path d="M18 8.5a6 6 0 1 0-12 0c0 5-2 6.5-2 6.5h16s-2-1.5-2-6.5Z" />
      <path d="M10.3 19a2 2 0 0 0 3.4 0" />
    </Svg>
  ),

  // Eşleşme: hedef
  matches: (p) => (
    <Svg {...p}>
      <circle cx="12" cy="12" r="8.5" />
      <circle cx="12" cy="12" r="4" />
      <circle cx="12" cy="12" r="0.6" fill="currentColor" stroke="none" />
    </Svg>
  ),

  // Katalog: üst üste belgeler
  catalog: (p) => (
    <Svg {...p}>
      <path d="M7.5 3.5h9a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-13a2 2 0 0 1 2-2Z" />
      <path d="M9 8h6M9 12h6M9 16h3.5" />
    </Svg>
  ),

  // Senaryo: yükselen eğri
  scenario: (p) => (
    <Svg {...p}>
      <path d="M4 19.5V4.5" />
      <path d="M4 19.5h16" />
      <path d="M7.5 15.5l3.5-4 3 2.5 4.5-6" />
      <path d="M18.5 8v3.5M18.5 8H15" />
    </Svg>
  ),

  // Şirketlerim: iki bina
  companies: (p) => (
    <Svg {...p}>
      <path d="M3.5 20.5V7a1.5 1.5 0 0 1 1.5-1.5h5A1.5 1.5 0 0 1 11.5 7v13.5" />
      <path d="M11.5 20.5V11h6.5a1.5 1.5 0 0 1 1.5 1.5v8" />
      <path d="M2.5 20.5h19" />
      <path d="M6 9h2.5M6 12.5h2.5M6 16h2.5M14.5 14.5H17M14.5 17.5H17" />
    </Svg>
  ),

  // Şirket profili: künye kartı
  companyProfile: (p) => (
    <Svg {...p}>
      <rect x="3" y="4.5" width="18" height="15" rx="2" />
      <circle cx="8.75" cy="10.5" r="2" />
      <path d="M5.5 16c.6-1.6 1.9-2.4 3.25-2.4S11.4 14.4 12 16" />
      <path d="M14.5 10h4M14.5 13.5h4" />
    </Svg>
  ),

  // Şirket grupları: ana ve bağlı düğümler
  companyGroups: (p) => (
    <Svg {...p}>
      <rect x="9" y="2.5" width="6" height="5" rx="1.5" />
      <rect x="2.5" y="16.5" width="6" height="5" rx="1.5" />
      <rect x="15.5" y="16.5" width="6" height="5" rx="1.5" />
      <path d="M12 7.5v4.5" />
      <path d="M5.5 16.5V12h13v4.5" />
    </Svg>
  ),

  // Kullanıcılar ve yetkiler: iki kişi
  members: (p) => (
    <Svg {...p}>
      <circle cx="9.5" cy="8" r="3.25" />
      <path d="M3.5 19.5c0-3.1 2.7-5 6-5s6 1.9 6 5" />
      <path d="M16.5 5.2a3.25 3.25 0 0 1 0 6.1" />
      <path d="M18 14.9c1.7.6 2.9 1.9 2.9 4.6" />
    </Svg>
  ),

  // Veri kaynakları: veritabanı
  sources: (p) => (
    <Svg {...p}>
      <ellipse cx="12" cy="6" rx="7.5" ry="3" />
      <path d="M4.5 6v12c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V6" />
      <path d="M4.5 12c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3" />
    </Svg>
  ),

  // Mevzuat: terazi
  regulation: (p) => (
    <Svg {...p}>
      <path d="M12 3.5v17" />
      <path d="M6 20.5h12" />
      <path d="M5 7.5h14" />
      <path d="M5 7.5 2.5 14h5L5 7.5Z" />
      <path d="M19 7.5 16.5 14h5L19 7.5Z" />
    </Svg>
  ),

  // Karantina: uyarı üçgeni
  quarantine: (p) => (
    <Svg {...p}>
      <path d="M12 4.5 21 19.5H3L12 4.5Z" />
      <path d="M12 10v4" />
      <circle cx="12" cy="16.75" r="0.6" fill="currentColor" stroke="none" />
    </Svg>
  ),

  menu: (p) => (
    <Svg {...p}>
      <path d="M4 7h16M4 12h16M4 17h16" />
    </Svg>
  ),

  close: (p) => (
    <Svg {...p}>
      <path d="M6 6l12 12M18 6L6 18" />
    </Svg>
  ),
}

export default function NavIcon({ name, ...props }: { name: NavIconName } & IconProps) {
  return icons[name](props)
}
