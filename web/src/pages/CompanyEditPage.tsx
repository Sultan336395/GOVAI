import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useCompanies } from '@/app/contexts'
import { companyPermissions } from '@/app/contexts'
import { EmptyState, Loading, NotAuthorized, SuccessBox } from '@/components/Common'
import CompanyForm from '@/components/CompanyForm'
import { companyToForm } from '@/lib/companyForm'
import type { CompanyFormValues } from '@/lib/companyForm'
import { companyRoleHints, companyRoleLabels } from '@/lib/companyLabels'

/**
 * Şirket profili / düzenleme.
 *
 * Düzenleme yetkisi rolden okunur ve düğme yetkisi olmayana hiç gösterilmez. Yine de
 * asıl karar sunucudadır; buradaki kontrol yalnızca kullanıcıyı boş yere uğraştırmamak
 * içindir.
 */
export default function CompanyEditPage() {
  const { companyId } = useParams<{ companyId: string }>()
  const navigate = useNavigate()
  const { companies, isLoading, refresh } = useCompanies()

  const company = companies.find((candidate) => candidate.id === companyId)

  const [values, setValues] = useState<CompanyFormValues | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (company) setValues(companyToForm(company))
  }, [company])

  const { data: groups = [] } = useQuery({
    queryKey: ['company-groups'],
    queryFn: api.listCompanyGroups,
  })

  if (isLoading || (company && !values)) return <Loading />

  // Erişimi olmayan bir kimlik listede görünmez. Kaydın var olup olmadığı ele verilmez.
  if (!company) {
    return <EmptyState>Bu şirket listenizde yok veya erişiminiz kaldırılmış.</EmptyState>
  }

  const canEdit = companyPermissions.manageProfile(company.companyRole)

  async function handleSubmit() {
    if (!values || !companyId) return

    await api.updateCompany(companyId, values)
    await refresh()
    setSaved(true)
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>{company.legalName}</h1>
          <p>
            VKN {company.taxNumber} · Şirketteki rolünüz:{' '}
            <strong>{companyRoleLabels[company.companyRole]}</strong> ·{' '}
            {companyRoleHints[company.companyRole]}
          </p>
        </div>
        <Link to={`/companies/${company.id}/members`}>
          <button type="button">Kullanıcılar ve yetkiler</button>
        </Link>
      </div>

      {saved ? <SuccessBox>Şirket profili güncellendi.</SuccessBox> : null}

      {!canEdit ? (
        <NotAuthorized message="Firma profilini düzenlemek için şirket sahibi veya şirket yöneticisi olmanız gerekir. Bilgileri görüntüleyebilirsiniz." />
      ) : null}

      {values ? (
        <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0 }}>
          <CompanyForm
            values={values}
            onChange={(next) => {
              setValues(next)
              setSaved(false)
            }}
            onSubmit={handleSubmit}
            submitLabel="Değişiklikleri kaydet"
            lockTaxNumber
            groups={groups}
            parentCandidates={companies.filter((candidate) => candidate.id !== company.id)}
            onCancel={() => navigate('/companies')}
          />
        </fieldset>
      ) : null}
    </>
  )
}
