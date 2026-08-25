import { useCallback, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { api, tokenStore } from '@/api/client'
import { AuthContext } from '@/app/contexts'
import type { AuthContextValue, SessionUser } from '@/app/contexts'

const USER_STORAGE_KEY = 'govai.user'

/** CompanyProvider ile aynı anahtar: aktif şirket girişte sunucudan yazılır. */
const SELECTED_COMPANY_KEY = 'govai.selectedCompany'

function readStoredUser(): SessionUser | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY)
  if (!raw) return null

  try {
    return JSON.parse(raw) as SessionUser
  } catch {
    // Bozuk oturum verisi sessizce temizlenir; kullanıcı yeniden giriş yapar.
    localStorage.removeItem(USER_STORAGE_KEY)
    localStorage.removeItem(SELECTED_COMPANY_KEY)
    return null
  }
}

export default function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(() =>
    tokenStore.get() ? readStoredUser() : null,
  )

  const login = useCallback(async (email: string, password: string) => {
    const response = await api.login(email, password)
    tokenStore.set(response.accessToken)
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(response.user))

    // Aktif şirketi sunucu belirler; istemci kendi başına seçmez. Önceki oturumdan
    // kalan seçim başka bir hesaba ait olabilir, bu yüzden her girişte üzerine yazılır.
    if (response.activeCompanyId) {
      localStorage.setItem(SELECTED_COMPANY_KEY, response.activeCompanyId)
    } else {
      localStorage.removeItem(SELECTED_COMPANY_KEY)
    }
    setUser(response.user)
  }, [])

  const logout = useCallback(() => {
    tokenStore.clear()
    localStorage.removeItem(USER_STORAGE_KEY)
    localStorage.removeItem(SELECTED_COMPANY_KEY)
    setUser(null)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({ user, isAuthenticated: Boolean(user && tokenStore.get()), login, logout }),
    [user, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
