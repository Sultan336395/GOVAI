import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, tokenStore } from '@/api/client'
import { CompanyContext, useAuth } from '@/app/contexts'
import type { CompanyContextValue } from '@/app/contexts'
import { isPlatformRole } from '@/app/navigation'

const SELECTED_COMPANY_KEY = 'govai.selectedCompany'

export default function CompanyProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [selectedCompanyId, setSelectedCompanyId] = useState<string | null>(() =>
    localStorage.getItem(SELECTED_COMPANY_KEY),
  )
  const [isSwitching, setIsSwitching] = useState(false)

  const {
    data: companies = [],
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: ['my-companies'],
    queryFn: api.listMyCompanies,
    // Platform hesaplarının şirket üyeliği yoktur ve uç onlara kapalıdır
    // (Policies.CompanyData). İstek yine de gönderilirse her sayfa yüklemesinde
    // sessiz bir 403 üretir; sorun ararken gerçek hataların arasına karışır.
    enabled: Boolean(tokenStore.get()) && !isPlatformRole(user?.role ?? null),
  })

  // Aktif şirketi sunucu belirler (girişte jetona ve yanıta yazılır); buradaki
  // localStorage yalnızca sayfa yenilemeleri arasında o değeri taşır.
  // Seçili firma listede yoksa sunucunun varsayılanına, o da yoksa ilk firmaya düşülür:
  // üyelik kaldırıldığında ekran boş bir kimlikte takılı kalmamalıdır.
  useEffect(() => {
    if (companies.length === 0) return

    const stillExists = companies.some((company) => company.id === selectedCompanyId)
    if (stillExists) return

    const fallback = companies.find((company) => company.isDefault) ?? companies[0]
    setSelectedCompanyId(fallback.id)
    localStorage.setItem(SELECTED_COMPANY_KEY, fallback.id)
  }, [companies, selectedCompanyId])

  /**
   * Aktif şirket istemcide değil sunucuda seçilir: uç, üyeliği doğrular ve aktif şirketi
   * yazdığı yeni bir jeton döner. Böylece istemcinin gönderdiği kimliğe güvenilmez ve
   * erişimi kaldırılmış bir şirkete geçiş sessizce başarılı görünmez.
   */
  const selectCompany = useCallback(
    async (id: string) => {
      if (isSwitching || id === selectedCompanyId) return

      setIsSwitching(true)
      try {
        const result = await api.setActiveCompany(id)
        tokenStore.set(result.accessToken)

        setSelectedCompanyId(result.companyId)
        localStorage.setItem(SELECTED_COMPANY_KEY, result.companyId)

        // Önceki şirkete ait tüm veriler geçersizdir; ekranlar yeniden yüklenir.
        await queryClient.invalidateQueries()
      } finally {
        setIsSwitching(false)
      }
    },
    [isSwitching, selectedCompanyId, queryClient],
  )

  const activeRole = useMemo(
    () => companies.find((company) => company.id === selectedCompanyId)?.companyRole ?? null,
    [companies, selectedCompanyId],
  )

  const value = useMemo<CompanyContextValue>(
    () => ({
      companies,
      selectedCompanyId,
      selectCompany,
      isSwitching,
      activeRole,
      refresh: refetch,
      isLoading,
      error,
    }),
    [companies, selectedCompanyId, selectCompany, isSwitching, activeRole, refetch, isLoading, error],
  )

  return <CompanyContext.Provider value={value}>{children}</CompanyContext.Provider>
}
