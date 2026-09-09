import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { ErrorBox, InfoBox, Loading } from '@/components/Common'
import { documentOriginLabels, quarantineReasonLabels } from '@/lib/regulatoryLabels'
import { formatDate } from '@/lib/format'

const ayristirmaEtiketleri: Record<string, string> = {
  Pending: 'Ayrıştırılmadı',
  Parsed: 'Ayrıştırıldı',
  Failed: 'Ayrıştırma başarısız',
  NeedsOcr: 'Taranmış belge — metin katmanı yok',
}

/**
 * Belge incelemesi (Platform İnceleme).
 *
 * <p>
 * <b>Neden bu ekran var:</b> karantinadan çıkarma kararı başlık ve adrese bakarak
 * verilemez. İlk denemede kayıtları fırsat detayına bağlamıştım; sahada işe yaramadı,
 * çünkü karantina belge sisteme <i>girerken</i> uygulanıyor — fırsat kaydı henüz
 * oluşmamış oluyor. Üretimdeki altı karantina kaydının hiçbirinde fırsat yoktu ve
 * bağlantı hiç görünmedi. İnceleyicinin görmesi gereken şey belgenin kendisi.
 * </p>
 *
 * <p>
 * Ekran salt okurdur ve firma verisi içermez. Metin uzun belgelerde kırpılır; kırpma
 * <b>açıkça söylenir</b> — sessizce eksik metin göstermek, belgenin içeriği hakkında
 * yanlış karar verdirir.
 * </p>
 */
export default function PlatformDocumentPage() {
  const { documentId = '' } = useParams()

  const { data, isLoading, error } = useQuery({
    queryKey: ['platform-document', documentId],
    queryFn: () => api.getQuarantinedDocument(documentId),
    enabled: documentId.length > 0,
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return null

  const karantinada = data.reason !== 'None'

  return (
    <section className="page" data-sayfa="platform-belge">
      <header className="page-header">
        <Link to="/quarantine" className="muted small" data-alan="geri">
          ← Karantina İnceleme
        </Link>

        <h1>{data.title}</h1>

        <p className="muted">
          {data.sourceName} · {formatDate(data.collectedAt)} · {data.mediaType}
        </p>
      </header>

      {karantinada && (
        <InfoBox>
          <strong>Karantinada: {quarantineReasonLabels[data.reason]}</strong>
          <div>
            Katalogda görünmüyor ve skorlanmıyor. Kayıt <b>silinmedi</b>; belge, ham
            içeriği ve sürümleri yerinde duruyor.
          </div>
          {data.note ? <div className="small">Not: {data.note}</div> : null}
        </InfoBox>
      )}

      <div className="card" data-alan="kunye">
        <h2>Künye</h2>

        <dl className="kv">
          <dt>Kaynak</dt>
          <dd>
            {data.sourceName}
            {data.officialDomain ? <span className="muted"> ({data.officialDomain})</span> : null}
          </dd>

          <dt>Adres</dt>
          <dd style={{ overflowWrap: 'anywhere' }}>
            <a href={data.url} target="_blank" rel="noreferrer">
              {data.url}
            </a>
          </dd>

          {data.canonicalUrl && data.canonicalUrl !== data.url ? (
            <>
              <dt>Yönlendirilen adres</dt>
              <dd style={{ overflowWrap: 'anywhere' }}>{data.canonicalUrl}</dd>
            </>
          ) : null}

          <dt>Kayda giriş</dt>
          <dd>{documentOriginLabels[data.origin]}</dd>

          <dt>İşlem durumu</dt>
          <dd>
            {data.status}
            {data.processingError ? (
              <div className="small">Hata: {data.processingError}</div>
            ) : null}
          </dd>

          <dt>Türeyen kayıt</dt>
          <dd>
            {data.opportunityId ? (
              <Link to={`/platform/opportunities/${data.opportunityId}`} data-alan="firsat">
                Fırsat kaydını aç
              </Link>
            ) : data.regulatoryChangeId ? (
              <Link to={`/regulatory-changes/${data.regulatoryChangeId}`}>Mevzuat kaydını aç</Link>
            ) : (
              <span className="muted">
                Yok — belge karantinaya girerken kayıt henüz oluşmamıştı.
              </span>
            )}
          </dd>
        </dl>
      </div>

      <div className="card" data-alan="surumler">
        <h2>Sürümler ({data.versions.length})</h2>

        {data.versions.length === 0 ? (
          <p className="muted">Bu belgenin kayıtlı sürümü yok.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Sürüm</th>
                  <th>Alındığı zaman</th>
                  <th>HTTP</th>
                  <th>Ayrıştırma</th>
                  <th>Kanıt parçası</th>
                  <th>Hash</th>
                </tr>
              </thead>
              <tbody>
                {data.versions.map((v) => (
                  <tr key={v.versionId}>
                    <td>v{v.versionNumber}</td>
                    <td>{formatDate(v.retrievedAt)}</td>
                    <td>{v.httpStatusCode}</td>
                    <td>
                      {ayristirmaEtiketleri[v.parseStatus] ?? v.parseStatus}
                      {v.parseError ? <div className="small">{v.parseError}</div> : null}
                    </td>
                    <td>{v.chunkCount}</td>
                    <td className="small" style={{ overflowWrap: 'anywhere' }}>
                      {v.rawContentHash.slice(0, 16)}…
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card" data-alan="metin">
        <h2>Belge metni</h2>

        {data.textPreview ? (
          <>
            {data.textTruncated && (
              <InfoBox>
                Metin uzun olduğu için kırpıldı: {data.textPreview.length.toLocaleString('tr-TR')} /{' '}
                {data.textLength.toLocaleString('tr-TR')} karakter gösteriliyor. Tamamı için resmî
                kaynağa gidin.
              </InfoBox>
            )}

            <pre className="belge-metni" data-alan="metin-govdesi">
              {data.textPreview}
            </pre>
          </>
        ) : (
          <p className="muted">
            Bu belgenin çıkarılmış metni yok. Taranmış bir PDF olabilir ya da ayrıştırma
            başarısız olmuştur; sürüm tablosundaki ayrıştırma durumuna bakın.
          </p>
        )}
      </div>
    </section>
  )
}
