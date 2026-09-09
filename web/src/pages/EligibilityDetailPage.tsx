import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api } from '@/api/client'
import type { DocumentCheck, RuleEvaluation } from '@/api/types'
import { EmptyState, ErrorBox, Loading, SectorFitBadge, VerdictBadge } from '@/components/Common'
import { OpportunityAnalysisPanel } from '@/components/AnalysisPanel'
import { OpportunityDetailCard } from '@/components/OpportunityDetailCard'
import { formatDate, formatPercent } from '@/lib/format'

export default function EligibilityDetailPage() {
  const { assessmentId } = useParams<{ assessmentId: string }>()
  const queryClient = useQueryClient()

  const { data, isLoading, error } = useQuery({
    queryKey: ['eligibility', assessmentId],
    queryFn: () => api.getEligibilityDetail(assessmentId!),
    enabled: Boolean(assessmentId),
  })

  // Fırsatın kendi künyesi, kanıt zinciri ve resmî bağlantısı ayrı uçtan gelir.
  // Karantinaya alınmış bir fırsat burada 404 döner ve detay gösterilmez.
  const firsat = useQuery({
    queryKey: ['opportunity', data?.opportunityId],
    queryFn: () => api.getOpportunity(data!.opportunityId),
    enabled: Boolean(data?.opportunityId),
  })

  // DeepTech analizi ayrı uçtan gelir: kriter kriter sonuç, kırılımlı puan ve
  // güven seviyesi. Uç POST'tur çünkü sürümlü bir analiz kaydı yazar; aynı
  // sürümlerle çağrıldığında yeni kayıt oluşmaz.
  const analiz = useQuery({
    queryKey: ['analysis', data?.companyId, data?.opportunityId],
    queryFn: () => api.analyzeOpportunity(data!.companyId, data!.opportunityId),
    enabled: Boolean(data?.companyId && data?.opportunityId),
  })

  const generateSummary = useMutation({
    mutationFn: () => api.generateSummary(assessmentId!),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['eligibility', assessmentId] }),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Değerlendirme bulunamadı.</EmptyState>

  return (
    <>
      <div className="page-header">
        <div>
          <Link to="/matches" className="muted">
            ← Eşleşmelere dön
          </Link>
          <h1 style={{ marginTop: 6 }}>{data.opportunityTitle}</h1>
          <p>
            {data.publisher} · Son başvuru {formatDate(data.deadline)}
          </p>
        </div>
        <div style={{ textAlign: 'right' }}>
          <div style={{ fontSize: 34, fontWeight: 700 }}>{data.finalScore.toFixed(1)}</div>
          <VerdictBadge verdict={data.verdict} />
          <div style={{ marginTop: 6 }}>
            <SectorFitBadge fit={data.sectorFit} />
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
            Skor güveni {formatPercent(data.confidence)}
          </div>
        </div>
      </div>

      {analiz.data ? <OpportunityAnalysisPanel data={analiz.data} /> : null}

      {firsat.data ? <OpportunityDetailCard data={firsat.data} /> : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Yönetici özeti</h2>
        {data.executiveSummary ? (
          <p style={{ margin: 0 }}>{data.executiveSummary}</p>
        ) : (
          <>
            <p className="muted" style={{ marginTop: 0 }}>
              Bu değerlendirme için henüz özet üretilmedi.
            </p>
            <button
              type="button"
              onClick={() => generateSummary.mutate()}
              disabled={generateSummary.isPending}
            >
              {generateSummary.isPending ? 'Üretiliyor…' : 'Yönetici özeti üret'}
            </button>
            {generateSummary.error ? <ErrorBox error={generateSummary.error} /> : null}
          </>
        )}
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Skor nasıl hesaplandı?</h2>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Boyut</th>
                <th>Puan</th>
                <th>Ağırlık</th>
                <th>Katkı</th>
                <th>Gerekçe</th>
              </tr>
            </thead>
            <tbody>
              {data.dimensions.map((dimension) => (
                <tr key={dimension.dimension}>
                  <td>{dimension.dimensionLabel}</td>
                  <td>
                    {formatPercent(dimension.value)}
                    <div className="meter">
                      <span style={{ width: `${dimension.value * 100}%` }} />
                    </div>
                  </td>
                  <td>{formatPercent(dimension.weight)}</td>
                  <td>{formatPercent(dimension.contribution, 1)}</td>
                  <td className="muted">{dimension.rationale}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid two" style={{ marginBottom: 16 }}>
        <RuleSection
          title="Elenme sebepleri"
          emptyText="Firmayı eleyen bir koşul yok."
          variant="blocking"
          rules={data.blockingFailures}
        />
        <RuleSection
          title="Kapatılabilir eksikler"
          emptyText="Kapatılması gereken koşul yok."
          variant="missing"
          rules={data.missingConditions}
        />
      </div>

      <div className="grid two" style={{ marginBottom: 16 }}>
        <RuleSection
          title="Veri boşlukları"
          emptyText="Profil bu çağrı için eksiksiz."
          variant="gap"
          rules={data.dataGaps}
        />
        <RuleSection
          title="Sağlanan koşullar"
          emptyText="Sağlanan koşul yok."
          variant="satisfied"
          rules={data.satisfiedConditions}
        />
      </div>

      <div className="card">
        <h2>Belge kontrol listesi</h2>
        <DocumentTable documents={data.documentChecklist} />
      </div>
    </>
  )
}

function RuleSection({
  title,
  rules,
  variant,
  emptyText,
}: {
  title: string
  rules: RuleEvaluation[]
  variant: 'blocking' | 'missing' | 'satisfied' | 'gap'
  emptyText: string
}) {
  return (
    <div className="card">
      <h2>
        {title} <span className="muted">({rules.length})</span>
      </h2>
      {rules.length === 0 ? (
        <p className="muted" style={{ margin: 0 }}>
          {emptyText}
        </p>
      ) : (
        <ul className="rule-list">
          {rules.map((rule, index) => (
            <li key={`${rule.field}-${index}`} className={`rule-item ${variant}`}>
              <div className="requirement">{rule.requirement}</div>
              <div className="detail">
                Beklenen: <strong>{rule.expectedValue}</strong> · Mevcut:{' '}
                <strong>{rule.actualValue}</strong>
              </div>
              {rule.suggestedAction ? <div className="detail">→ {rule.suggestedAction}</div> : null}
              {rule.sourceExcerpt ? <div className="excerpt">“{rule.sourceExcerpt}”</div> : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

const documentStatusLabels: Record<DocumentCheck['status'], string> = {
  Provided: 'Hazır',
  Missing: 'Eksik',
  Expired: 'Süresi dolmuş',
  NotRequired: 'Gerekmiyor',
}

const documentStatusClass: Record<DocumentCheck['status'], string> = {
  Provided: 'eligible',
  Missing: 'not-eligible',
  Expired: 'conditional',
  NotRequired: 'indeterminate',
}

function DocumentTable({ documents }: { documents: DocumentCheck[] }) {
  if (documents.length === 0) {
    return (
      <p className="muted" style={{ margin: 0 }}>
        Çağrı metninden belge listesi çıkarılamadı. Bu durumda belge hazır olma puanı nötr uygulanır.
      </p>
    )
  }

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Belge</th>
            <th>Zorunlu</th>
            <th>Durum</th>
            <th>Geçerlilik</th>
            <th>Yapılacak</th>
          </tr>
        </thead>
        <tbody>
          {documents.map((doc) => (
            <tr key={doc.code}>
              <td>
                {doc.name}
                {doc.issuingAuthority ? (
                  <div className="muted" style={{ fontSize: 12 }}>
                    {doc.issuingAuthority}
                  </div>
                ) : null}
              </td>
              <td>{doc.isMandatory ? 'Evet' : 'Hayır'}</td>
              <td>
                <span className={`badge ${documentStatusClass[doc.status]}`}>
                  {documentStatusLabels[doc.status]}
                </span>
              </td>
              <td>{formatDate(doc.validUntil)}</td>
              <td className="muted">{doc.action ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
