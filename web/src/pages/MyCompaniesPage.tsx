import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { companyPermissions, useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, InfoBox, Loading } from '@/components/Common'
import {
  companyRoleLabels,
  enterpriseSizeLabels,
  legalTypeLabels,
  relationshipLabels,
} from '@/lib/companyLabels'
import { formatCurrency } from '@/lib/format'

/**
 * Şirketlerim. Kullanıcının üyeliği olan şirketleri listeler — kiracının tamamını değil.
 * Erişim kaynağı sunucudaki üyelik kaydıdır; bu ekran onun görünen yüzüdür.
 */
export default function MyCompaniesPage() {
  const { companies, selectedCompanyId, selectCompany, isSwitching, activeRole, isLoading, error } =
    useCompanies()

  // Şirket ekleme bir profil yönetimi işidir: sahip ve yönetici yapar.
  // Düğmeyi gizlemek bir güvenlik önlemi değildir; /companies/new ekranı da aynı
  // kuralı kendi içinde uygular.
  const canAddCompany = companyPermissions.manageProfile(activeRole)

  const { data: verificationRequests = [] } = useQuery({
    queryKey: ['verification-requests'],
    queryFn: api.listVerificationRequests,
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  const pending = verificationRequests.filter((request) => request.status === 'Pending')

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Şirketlerim</h1>
          <p>
            Erişiminiz olan {companies.length} şirket. Aktif şirketi değiştirdiğinizde panel,
            eşleşmeler ve raporlar o şirkete göre yeniden yüklenir.
          </p>
        </div>
        {canAddCompany ? (
          <Link to="/companies/new">
            <button type="button">Yeni şirket ekle</button>
          </Link>
        ) : null}
      </div>

      {pending.length > 0 ? (
        <InfoBox>
          <strong>{pending.length} doğrulama talebiniz bekliyor.</strong>
          <p className="muted" style={{ margin: '6px 0 0' }}>
            Eklemek istediğiniz vergi numarası başka bir çalışma alanında kayıtlı. Talebiniz
            kaydedildi; sonuçlanana kadar şirket oluşturulmaz.
          </p>
          <ul style={{ margin: '8px 0 0' }}>
            {pending.map((request) => (
              <li key={request.id}>
                {request.requestedLegalName} — VKN {request.taxNumber}
              </li>
            ))}
          </ul>
        </InfoBox>
      ) : null}

      {companies.length === 0 ? (
        <EmptyState>
          {canAddCompany
            ? 'Henüz erişebildiğiniz bir şirket yok. "Yeni şirket ekle" ile başlayabilirsiniz.'
            : 'Henüz erişebildiğiniz bir şirket yok. Şirket sahibinden sizi eklemesini isteyin.'}
        </EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Şirket</th>
                <th>VKN</th>
                <th>Rolünüz</th>
                <th>Ölçek</th>
                <th>Çalışan</th>
                <th>Ciro</th>
                <th>Grup / bağ</th>
                <th>Profil</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {companies.map((company) => {
                const isActive = company.id === selectedCompanyId

                return (
                  <tr key={company.id}>
                    <td>
                      <strong>{company.legalName}</strong>
                      {isActive ? <span className="badge"> aktif</span> : null}
                      {company.isDefault ? (
                        <span className="muted" style={{ fontSize: 12 }}> · varsayılan</span>
                      ) : null}
                      <div className="muted" style={{ fontSize: 12 }}>
                        {legalTypeLabels[company.legalType]}
                        {company.city ? ` · ${company.city}` : ''}
                        {company.mainSector ? ` · ${company.mainSector}` : ''}
                      </div>
                    </td>
                    <td>{company.taxNumber}</td>
                    <td>{companyRoleLabels[company.companyRole]}</td>
                    <td>{enterpriseSizeLabels[company.size] ?? company.size}</td>
                    <td>{company.employeeCount}</td>
                    <td>{formatCurrency(company.annualRevenue)}</td>
                    <td>
                      {company.groupName ?? '—'}
                      <div className="muted" style={{ fontSize: 12 }}>
                        {relationshipLabels[company.relationshipType]}
                        {company.parentCompanyName ? ` · ${company.parentCompanyName}` : ''}
                      </div>
                    </td>
                    <td>
                      <div className="score">%{company.profileCompletionPercentage}</div>
                      <div className="meter">
                        <span style={{ width: `${company.profileCompletionPercentage}%` }} />
                      </div>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 8 }}>
                        <button
                          type="button"
                          disabled={isActive || isSwitching}
                          onClick={() => void selectCompany(company.id)}
                        >
                          {isActive ? 'Aktif' : isSwitching ? 'Geçiliyor…' : 'Bu şirkete geç'}
                        </button>
                        <Link to={`/companies/${company.id}/edit`}>
                          <button type="button">Profil</button>
                        </Link>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
