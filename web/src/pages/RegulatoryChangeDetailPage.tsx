import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { ErrorBox, InfoBox, Loading } from '@/components/Common'
import {
  changeTypeLabels,
  parseStatusLabels,
  regulationDomainLabels,
} from '@/lib/regulatoryLabels'
import { formatDate, NOT_PROVIDED_LABEL } from '@/lib/format'

/**
 * Mevzuat detayı.
 *
 * Resmî özet YALNIZCA belgeden alınan metindir; yapay zekâ yorumu içermez.
 * Kanıt bölümleri, iddianın hangi paragrafa dayandığını göstermek için saklanır.
 */
export default function RegulatoryChangeDetailPage() {
  const { changeId } = useParams<{ changeId: string }>()

  const { data, isLoading, error } = useQuery({
    queryKey: ['regulatory-change', changeId],
    queryFn: () => api.getRegulatoryChange(changeId!),
    enabled: Boolean(changeId),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <ErrorBox error={new Error('Kayıt bulunamadı.')} />

  const alan = (etiket: string, deger: string | null | undefined) => (
    <div>
      <div className="kpi-label">{etiket}</div>
      <div>{deger ? deger : <span className="muted">{NOT_PROVIDED_LABEL}</span>}</div>
    </div>
  )

  return (
    <>
      <div className="page-header">
        <div>
          <h1>{data.title}</h1>
          <p>
            {data.authority} · {regulationDomainLabels[data.regulationDomain]} ·{' '}
            {changeTypeLabels[data.changeType]} ·{' '}
            {data.jurisdiction === 'EU' ? 'Avrupa Birliği' : data.jurisdiction}
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <a href={data.officialUrl} target="_blank" rel="noreferrer noopener">
            <button type="button">Resmî kaynağa git</button>
          </a>
          <Link to="/regulatory-changes">
            <button type="button">Listeye dön</button>
          </Link>
        </div>
      </div>

      <InfoBox>
        Şirket etkisi DeepTech analiz motoru tarafından değerlendirilecektir.
      </InfoBox>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Künye</h2>
        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          {alan('Resmî numara', data.officialNumber)}
          {alan('Yayın tarihi', data.publicationDate ? formatDate(data.publicationDate) : null)}
          {alan('Yürürlük tarihi', data.effectiveDate ? formatDate(data.effectiveDate) : null)}
          {alan('Doğrulama durumu', 'Doğrulandı')}
          {alan('Son doğrulama', formatDate(data.lastVerifiedAt))}
          {alan('Kaynak', data.sourceName)}
        </div>
      </div>

      {data.summary ? (
        <div className="card" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Resmî özet</h2>
          <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
            Bu metin doğrudan resmî belgeden alınmıştır; yorum içermez.
          </p>
          <p style={{ whiteSpace: 'pre-wrap' }}>{data.summary}</p>
        </div>
      ) : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Belge bilgileri</h2>
        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          {alan('Belge sürümü', `v${data.documentVersion}`)}
          {alan('Alınma zamanı', formatDate(data.retrievedAt))}
          {alan('HTTP durumu', String(data.httpStatusCode))}
          {alan('Medya türü', data.mediaType)}
          {alan('Karakter kümesi', data.charset)}
          {alan('Sayfa sayısı', data.pageCount ? String(data.pageCount) : null)}
          {alan('Ayrıştırma', parseStatusLabels[data.parseStatus])}
          {alan('OCR gerekli mi', data.requiresOcr ? 'Evet' : 'Hayır')}
        </div>

        <div style={{ marginTop: 12 }}>
          <div className="kpi-label">Kaynak adresi</div>
          <a href={data.canonicalUrl} target="_blank" rel="noreferrer noopener">
            {data.canonicalUrl}
          </a>
        </div>

        {data.previousVersionId ? (
          <div style={{ marginTop: 12 }}>
            <Link to={`/regulatory-changes/${data.previousVersionId}`}>
              Önceki sürümü görüntüle
            </Link>
          </div>
        ) : null}
      </div>

      <div className="card">
        <h2 style={{ marginTop: 0 }}>Kanıt bölümleri</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          Belgeden kesilmiş, konumu korunmuş parçalar. Her parça belge sürümüne ve
          oradaki karakter aralığına geri bağlanır.
        </p>

        {data.evidence.length === 0 ? (
          <div className="state">Bu sürüm için kanıt parçası üretilmemiş.</div>
        ) : (
          <div className="rule-list">
            {data.evidence.map((chunk) => (
              <div className="rule-item" key={chunk.sequenceNumber}>
                <div className="muted" style={{ fontSize: 12, marginBottom: 4 }}>
                  {chunk.sectionTitle ? `${chunk.sectionTitle} · ` : ''}
                  {chunk.pageNumber ? `sayfa ${chunk.pageNumber} · ` : ''}
                  {chunk.paragraphNumber ? `paragraf ${chunk.paragraphNumber} · ` : ''}
                  karakter {chunk.startOffset}–{chunk.endOffset}
                </div>
                <div>{chunk.text}</div>
              </div>
            ))}
          </div>
        )}
      </div>
    </>
  )
}
