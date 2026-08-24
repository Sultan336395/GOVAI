import { createContext, useContext } from 'react'
import type { CompanyRole, LoginResponse, MyCompany } from '@/api/types'

/**
 * Context nesneleri ve hook'ları burada, provider bileşenlerinden ayrı tutulur.
 * Bileşen dosyalarının yalnızca bileşen dışa aktarması, Vite'ın hızlı yenileme (fast refresh)
 * davranışının doğru çalışması için gereklidir.
 */

export type SessionUser = LoginResponse['user']

export interface AuthContextValue {
  user: SessionUser | null
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth, AuthProvider içinde kullanılmalıdır.')
  }
  return context
}

export interface CompanyContextValue {
  /** Kullanıcının üyeliği olan şirketler. Kiracının tamamı değildir. */
  companies: MyCompany[]
  selectedCompanyId: string | null
  /** Aktif şirketi sunucuda değiştirir; yetki sunucuda doğrulanır ve jeton yenilenir. */
  selectCompany: (id: string) => Promise<void>
  /** Şirket değişimi sürerken çift tıklamayı ve yarı yüklü ekranı engellemek için. */
  isSwitching: boolean
  /** Aktif şirketteki rol; ekranların düğme göstermeden önce sorduğu değer. */
  activeRole: CompanyRole | null
  refresh: () => Promise<unknown>
  isLoading: boolean
  error: unknown
}

export const CompanyContext = createContext<CompanyContextValue | null>(null)

export function useCompanies(): CompanyContextValue {
  const context = useContext(CompanyContext)
  if (!context) {
    throw new Error('useCompanies, CompanyProvider içinde kullanılmalıdır.')
  }
  return context
}

/**
 * Rol–yetki matrisi. C# karşılığı: CompanyAccessGuard.Satisfies.
 * Okuma ayrıca sorulmaz: üyeliği olan her rol görüntüleyebilir.
 */
export const companyPermissions = {
  operate: (role: CompanyRole | null) =>
    role === 'CompanyOwner' || role === 'CompanyManager' || role === 'CompanyExpert',
  manageProfile: (role: CompanyRole | null) =>
    role === 'CompanyOwner' || role === 'CompanyManager',
  manageMembers: (role: CompanyRole | null) => role === 'CompanyOwner',
}
