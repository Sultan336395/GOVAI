import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { EmptyState, ErrorBox, Loading } from '@/components/Common'
import { NOT_PROVIDED_LABEL, categoryLabels, formatCurrency, formatDate, formatDeadline } from '@/lib/format'

export default function OpportunitiesPage() {
  const [search, setSearch] = useState('')
  const [onlyOpen, setOnlyOpen] = useState(true)
  const [page, setPage] = useState(1)

  const { data, isLoading, error } = useQuery({
    queryKey: ['opportunities', search, onlyOpen, page],
    queryFn: () => api.searchOpportunities({ search, onlyOpen, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Çağrı Kataloğu</h1>
          <p>Toplanan tüm resmî teşvik, hibe ve ihale çağrıları.</p>
        </div>
      </div>

      <div className="toolbar">
        <input
          placeholder="Başlık, kurum veya özet içinde ara"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setPage(1)
          }}
          style={{ width: 320 }}
        />
        <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <input
            type="checkbox"
            checked={onlyOpen}
            onChange={(e) => {
              setOnlyOpen(e.target.checked)
              setPage(1)
            }}
            style={{ width: 'auto' }}
          />
          Yalnızca açık çağrılar
        </label>
      </div>

      {error ? <ErrorBox error={error} /> : null}
      {isLoading ? <Loading /> : null}

      {data && data.items.length === 0 ? <EmptyState>Çağrı bulunamadı.</EmptyState> : null}

      {data && data.items.length > 0 ? (
        <div className="card">
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Çağrı</th>
                  <th>Destek Türü</th>
                  <th>Yayın</th>
                  <th>Son Başvuru</th>
                  <th>Azami Tutar</th>
                  <th>Kural / Belge</th>
                  <th>Onay</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((opportunity) => (
                  <tr key={opportunity.id}>
                    <td>
                      {opportunity.title}
                      <div className="muted" style={{ fontSize: 12 }}>
                        {opportunity.publisher}
                      </div>
                    </td>
                    <td>{categoryLabels[opportunity.supportCategory]}</td>
                    <td>{formatDate(opportunity.publishedAt)}</td>
                    <td>
                      {opportunity.deadline ? (
                        <>
                          {formatDate(opportunity.deadline)}
                          <div className="muted" style={{ fontSize: 12 }}>
                            {formatDeadline(opportunity.daysUntilDeadline)}
                          </div>
                        </>
                      ) : (
                        // Boş bırakmak "son başvuru yok" demektir; gerçek çoğu zaman
                        // "resmî kaynakta yazmıyor"dur (Faz 2 veri kalitesi).
                        <span className="muted">{NOT_PROVIDED_LABEL}</span>
                      )}
                    </td>
                    <td>
                      {opportunity.maxAmount !== null ? (
                        formatCurrency(opportunity.maxAmount)
                      ) : (
                        <span className="muted">{NOT_PROVIDED_LABEL}</span>
                      )}
                    </td>
                    <td>
                      {opportunity.ruleCount} / {opportunity.documentCount}
                    </td>
                    <td>
                      <span
                        className={`badge ${opportunity.isReviewedByConsultant ? 'eligible' : 'indeterminate'}`}
                      >
                        {opportunity.isReviewedByConsultant ? 'Onaylı' : 'Otomatik'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="toolbar" style={{ marginTop: 14, marginBottom: 0 }}>
            <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Önceki
            </button>
            <span className="muted">
              Sayfa {data.page} / {Math.max(1, data.totalPages)} · toplam {data.totalCount} çağrı
            </span>
            <button
              type="button"
              disabled={page >= data.totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Sonraki
            </button>
          </div>
        </div>
      ) : null}
    </>
  )
}
