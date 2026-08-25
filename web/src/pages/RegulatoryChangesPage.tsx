import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { RegulationDomain } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading } from '@/components/Common'
import { changeTypeLabels, regulationDomainLabels, regulationDomainOrder } from '@/lib/regulatoryLabels'
import { formatDate, NOT_PROVIDED_LABEL } from '@/lib/format'

/**
 * Mevzuat Değişiklikleri.
 *
 * Yalnızca resmî kaynaktan alınmış ve doğrulanmış kayıtlar listelenir; karantinadaki
 * ya da henüz doğrulanmamış kayıt burada görünmez.
 *
 * Bu fazda şirkete etki HESAPLANMAZ; sayfa bunu açıkça söyler.
 */
export default function RegulatoryChangesPage() {
  const [domain, setDomain] = useState<RegulationDomain | ''>('')
  const [jurisdiction, setJurisdiction] = useState('')

  const { data = [], isLoading, error } = useQuery({
    queryKey: ['regulatory-changes', domain, jurisdiction],
    queryFn: () =>
      api.listRegulatoryChanges({
        domain: domain || undefined,
        jurisdiction: jurisdiction || undefined,
      }),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Mevzuat Değişiklikleri</h1>
          <p>
            Resmî kaynaklardan toplanan ve kaynağına karşı doğrulanmış mevzuat kayıtları.
            Her kayıt, alındığı belgenin belirli bir sürümüne ve o sürümdeki paragrafa kadar
            geri gösterilebilir.
          </p>
        </div>
      </div>

      <InfoBox>
        Şirket etkisi DeepTech analiz motoru tarafından değerlendirilecektir.
      </InfoBox>

      <div className="toolbar" style={{ marginBottom: 16 }}>
        <div className="field" style={{ marginBottom: 0, minWidth: 200 }}>
          <label htmlFor="domain">Düzenleme alanı</label>
          <select
            id="domain"
            value={domain}
            onChange={(e) => setDomain(e.target.value as RegulationDomain | '')}
          >
            <option value="">Tümü</option>
            {regulationDomainOrder.map((d) => (
              <option key={d} value={d}>
                {regulationDomainLabels[d]}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ marginBottom: 0, minWidth: 160 }}>
          <label htmlFor="jurisdiction">Yargı alanı</label>
          <select
            id="jurisdiction"
            value={jurisdiction}
            onChange={(e) => setJurisdiction(e.target.value)}
          >
            <option value="">Tümü</option>
            <option value="TR">Türkiye</option>
            <option value="EU">Avrupa Birliği</option>
          </select>
        </div>
      </div>

      {data.length === 0 ? (
        <EmptyState>
          Bu filtrede doğrulanmış mevzuat kaydı yok. Kaynaklar doğrulandıkça kayıtlar burada
          görünür.
        </EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Başlık</th>
                <th>Düzenleme alanı</th>
                <th>Kurum</th>
                <th>Yargı alanı</th>
                <th>Değişiklik türü</th>
                <th>Yayın</th>
                <th>Yürürlük</th>
                <th>Doğrulama</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((change) => (
                <tr key={change.id}>
                  <td>
                    <strong>{change.title}</strong>
                  </td>
                  <td>{regulationDomainLabels[change.regulationDomain]}</td>
                  <td>{change.authority}</td>
                  <td>{change.jurisdiction === 'EU' ? 'Avrupa Birliği' : change.jurisdiction}</td>
                  <td>{changeTypeLabels[change.changeType]}</td>
                  <td>
                    {change.publicationDate ? (
                      formatDate(change.publicationDate)
                    ) : (
                      <span className="muted">{NOT_PROVIDED_LABEL}</span>
                    )}
                  </td>
                  <td>
                    {change.effectiveDate ? (
                      formatDate(change.effectiveDate)
                    ) : (
                      <span className="muted">{NOT_PROVIDED_LABEL}</span>
                    )}
                  </td>
                  <td>
                    <span className="badge eligible">Doğrulandı</span>
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 8 }}>
                      <Link to={`/regulatory-changes/${change.id}`}>
                        <button type="button">Detay</button>
                      </Link>
                      <a href={change.officialUrl} target="_blank" rel="noreferrer noopener">
                        <button type="button">Resmî kaynak</button>
                      </a>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
