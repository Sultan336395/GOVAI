import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, tokenStore } from '@/api/client'
import { CompanyContext } from '@/app/contexts'
import type { CompanyContextValue } from '@/app/contexts'

const SELECTED_COMPANY_KEY = 'govai.selectedCompany'

export default function CompanyProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
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
    enabled: Boolean(tokenStore.get()),
  })

  // Seçili firma listede yoksa varsayılana, o da yoksa ilk firmaya düşülür.
  // Üyelik kaldırıldığında ekranın boş bir kimlikte takılı kalmaması için gereklidir.
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
