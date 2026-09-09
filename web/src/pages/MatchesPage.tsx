import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '@/api/client'
import type { EligibilityVerdict } from '@/api/types'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, Loading, ScoreCell, SectorFitBadge, VerdictBadge } from '@/components/Common'
import { categoryLabels, formatCurrency, formatDeadline, formatPercent } from '@/lib/format'

const VERDICT_OPTIONS: { value: EligibilityVerdict | ''; label: string }[] = [
  { value: '', label: 'Tüm kararlar' },
  { value: 'Eligible', label: 'Uygun' },
  { value: 'ConditionallyEligible', label: 'Şartlı uygun' },
  { value: 'NotEligible', label: 'Uygun değil' },
  { value: 'Indeterminate', label: 'Belirsiz' },
]

export default function MatchesPage() {
  const { selectedCompanyId } = useCompanies()

  const [verdict, setVerdict] = useState<EligibilityVerdict | ''>('')
  const [minScore, setMinScore] = useState('')
  const [deadlineWithinDays, setDeadlineWithinDays] = useState('')
  const [page, setPage] = useState(1)

  const { data, isLoading, error } = useQuery({
    queryKey: ['matches', selectedCompanyId, verdict, minScore, deadlineWithinDays, page],
    queryFn: () =>
      api.listMatches(selectedCompanyId!, {
        verdicts: verdict ? [verdict] : undefined,
        minScore: minScore ? Number(minScore) : undefined,
        deadlineWithinDays: deadlineWithinDays ? Number(deadlineWithinDays) : undefined,
        page,
        pageSize: 25,
      }),
    enabled: Boolean(selectedCompanyId),
  })

  if (!selectedCompanyId) return <EmptyState>Önce bir firma seçin.</EmptyState>

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Fırsat Eşleşmeleri</h1>
          <p>
            Firma profiliniz ile eşleşen çağrılar <strong>önce sektör uyumuna</strong>, sonra
            uygunluk skoruna göre sıralanır. Sektörü doğrulanamayan veya tutmayan çağrılar
            listeden çıkarılmaz; en altta etiketli olarak kalır.
          </p>
        </div>
      </div>

      <div className="toolbar">
        <select
          value={verdict}
          onChange={(e) => {
            setVerdict(e.target.value as EligibilityVerdict | '')
            setPage(1)
          }}
        >
          {VERDICT_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>

        <input
          type="number"
          min={0}
          max={100}
          placeholder="Asgari skor"
          value={minScore}
          onChange={(e) => {
            setMinScore(e.target.value)
            setPage(1)
          }}
          style={{ width: 140 }}
        />

        <input
          type="number"
          min={1}
          placeholder="Son X gün içinde"
          value={deadlineWithinDays}
          onChange={(e) => {
            setDeadlineWithinDays(e.target.value)
            setPage(1)
          }}
          style={{ width: 170 }}
        />
      </div>

      {error ? <ErrorBox error={error} /> : null}
      {isLoading ? <Loading /> : null}

      {data && data.items.length === 0 ? (
        <EmptyState>Bu filtrelerle eşleşen fırsat bulunamadı.</EmptyState>
      ) : null}

      {data && data.items.length > 0 ? (
        <div className="card">
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Fırsat</th>
                  <th>Destek Türü</th>
                  <th>Sektör Uyumu</th>
                  <th>Skor</th>
                  <th>Güven</th>
                  <th>Karar</th>
                  <th>Son Başvuru</th>
                  <th>Azami Tutar</th>
                  <th>Eksikler</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((match) => (
                  <tr key={match.assessmentId}>
                    <td>
                      <Link to={`/matches/${match.assessmentId}`}>{match.opportunityTitle}</Link>
                      <div className="muted" style={{ fontSize: 12 }}>
                        {match.publisher}
                      </div>
                    </td>
                    <td>{categoryLabels[match.supportCategory]}</td>
                    <td>
                      <SectorFitBadge fit={match.sectorFit} />
                    </td>
                    <td>
                      <ScoreCell score={match.finalScore} />
                    </td>
                    <td>{formatPercent(match.confidence)}</td>
                    <td>
                      <VerdictBadge verdict={match.verdict} />
                    </td>
                    <td>{formatDeadline(match.daysUntilDeadline)}</td>
                    <td>{formatCurrency(match.maxAmount)}</td>
                    <td>
                      {match.missingConditionCount} koşul / {match.missingMandatoryDocumentCount} belge
                      {match.dataGapCount > 0 ? (
                        <div className="muted" style={{ fontSize: 12 }}>
                          {match.dataGapCount} veri boşluğu
                        </div>
                      ) : null}
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
              Sayfa {data.page} / {Math.max(1, data.totalPages)} · toplam {data.totalCount} kayıt
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
