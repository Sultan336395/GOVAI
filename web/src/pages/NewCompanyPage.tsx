import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { CreateCompanyResult } from '@/api/types'
import { canCreateCompany, useAuth, useCompanies } from '@/app/contexts'
import { InfoBox, NotAuthorized, SuccessBox } from '@/components/Common'
import CompanyForm from '@/components/CompanyForm'
import { emptyCompanyForm } from '@/lib/companyForm'
import type { CompanyFormValues } from '@/lib/companyForm'

/**
 * Yeni şirket ekleme.
 *
 * Mükerrer vergi numarası bir hata değil, anlamlı bir sonuçtur ve üç şekilde biter:
 * oluşturuldu, zaten bu çalışma alanında var, ya da başka bir çalışma alanında kayıtlı.
 * Üçüncü durumda karşı taraf hakkında hiçbir bilgi gösterilmez — yalnızca doğrulama
 * talebi açıldığı söylenir.
 */
export default function NewCompanyPage() {
  const navigate = useNavigate()
  const { companies, refresh, selectCompany } = useCompanies()
  const { user } = useAuth()

  const [values, setValues] = useState<CompanyFormValues>(emptyCompanyForm)
  const [result, setResult] = useState<CreateCompanyResult | null>(null)

  const { data: groups = [] } = useQuery({
    queryKey: ['company-groups'],
    queryFn: api.listCompanyGroups,
  })

  // Ekran doğrudan adresle açılsa da yetkisiz kullanıcıya form gösterilmez.
  const canAddCompany = canCreateCompany(user?.role ?? null, companies)

  async function handleSubmit() {
    const created = await api.createCompany(values)
    setResult(created)

    if (created.outcome === 'Created') {
      await refresh()
    }
  }

  if (!canAddCompany) {
    return (
      <>
        <div className="page-header">
          <div>
            <h1>Yeni Şirket Ekle</h1>
          </div>
        </div>
        <NotAuthorized message="Yeni şirket eklemek için çalışma alanı yöneticisi olmanız ya da en az bir şirkette şirket sahibi olmanız gerekir." />
      </>
    )
  }

  if (result && result.outcome === 'Created' && result.companyId) {
    return (
      <>
        <div className="page-header">
          <div>
            <h1>Şirket Eklendi</h1>
          </div>
        </div>

        <SuccessBox>
          <strong>{values.legalName} çalışma alanınıza eklendi.</strong>
          <p className="muted" style={{ margin: '6px 0 0' }}>
            Şirket sahibi olarak siz atandınız. Profili tamamladıkça skorlar netleşir.
          </p>
          <div className="toolbar" style={{ marginTop: 12 }}>
            <button
              type="button"
              onClick={() => void selectCompany(result.companyId!).then(() => navigate('/'))}
            >
              Bu şirkete geç
            </button>
            <Link to={`/companies/${result.companyId}/edit`}>
              <button type="button">Profili tamamla</button>
            </Link>
            <Link to="/companies">
              <button type="button">Şirketlerim</button>
            </Link>
          </div>
        </SuccessBox>
      </>
    )
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Yeni Şirket Ekle</h1>
          <p>
            Yıldızlı alanlar zorunludur. Kalan bilgileri sonradan tamamlayabilirsiniz; eksik alan
            firmayı elemez, yalnızca ilgili koşulları belirsiz bırakır.
          </p>
        </div>
      </div>

      {result && result.outcome === 'AlreadyInWorkspace' ? (
        <InfoBox>
          <strong>{result.message}</strong>
          {result.canNavigateToExisting && result.companyId ? (
            <div className="toolbar" style={{ marginTop: 12 }}>
              <Link to={`/companies/${result.companyId}/edit`}>
                <button type="button">Mevcut şirkete git</button>
              </Link>
            </div>
          ) : (
            <p className="muted" style={{ margin: '6px 0 0' }}>
              Bu şirkete erişiminiz yok. Şirket sahibinden sizi eklemesini isteyin.
            </p>
          )}
        </InfoBox>
      ) : null}

      {result && result.outcome === 'VerificationRequired' ? (
        <InfoBox>
          <strong>{result.message}</strong>
          <p className="muted" style={{ margin: '6px 0 0' }}>
            Şirket oluşturulmadı. Talebinizin durumunu Şirketlerim ekranından izleyebilirsiniz.
          </p>
        </InfoBox>
      ) : null}

      <CompanyForm
        values={values}
        onChange={setValues}
        onSubmit={handleSubmit}
        submitLabel="Şirketi ekle"
        groups={groups}
        parentCandidates={companies}
        onCancel={() => navigate('/companies')}
      />
    </>
  )
}
