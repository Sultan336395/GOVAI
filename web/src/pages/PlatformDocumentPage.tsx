import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { ErrorBox, InfoBox, Loading } from '@/components/Common'
import { formatDate } from '@/lib/format'
import { belgeOkunabilirlik, incelemeNedeni, kayitKaynagi } from '@/lib/sozluk'

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
          ← İnceleme Bekleyenler
        </Link>

        <h1>{data.title}</h1>

        <p className="muted">
          {data.sourceName} · {formatDate(data.collectedAt)}
        </p>
      </header>

      {karantinada && (
        <InfoBox>
          <strong>İncelemeye alındı: {incelemeNedeni[data.reason]}</strong>
          <div>
            Bu kayıt katalogda görünmüyor ve firmalara önerilmiyor. <b>Silinmedi</b>;
            belge ve geçmişi olduğu gibi duruyor, dilediğinizde geri alabilirsiniz.
          </div>
          {data.note ? <div className="small">Not: {data.note}</div> : null}
        </InfoBox>
      )}

      <div className="card" data-alan="kunye">
        <h2>Belge bilgileri</h2>

        <dl className="kv">
          <dt>Yayımlayan kurum</dt>
          <dd>{data.sourceName}</dd>

          <dt>Belgenin adresi</dt>
          <dd style={{ overflowWrap: 'anywhere' }}>
            <a href={data.url} target="_blank" rel="noreferrer">
              {data.url}
            </a>
          </dd>

          <dt>Sisteme girişi</dt>
          <dd>{kayitKaynagi[data.origin]}</dd>

          <dt>Alındığı tarih</dt>
          <dd>{formatDate(data.collectedAt)}</dd>

          <dt>İlgili kayıt</dt>
          <dd>
            {data.opportunityId ? (
              <Link to={`/platform/opportunities/${data.opportunityId}`} data-alan="firsat">
                Fırsat kaydını aç
              </Link>
            ) : data.regulatoryChangeId ? (
              <Link to={`/regulatory-changes/${data.regulatoryChangeId}`}>Mevzuat kaydını aç</Link>
            ) : (
              <span className="muted">
                Bu belgeden bir çağrı kaydı oluşturulmamış. Belge, kataloğa girmeden önce
                incelemeye alındığı için beklenen durum budur.
              </span>
            )}
          </dd>
        </dl>
      </div>

      <div className="card" data-alan="surumler">
        <h2>Belge geçmişi</h2>

        <p className="muted">
          Aynı adres birden çok kez alınmış olabilir; içerik her değiştiğinde önceki hâli
          korunur. En üstteki, sistemin şu an kullandığı hâldir.
        </p>

        {data.versions.length === 0 ? (
          <p className="muted">Bu belgenin kayıtlı bir alınışı yok.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Alındığı tarih</th>
                  <th>Durum</th>
                  <th>Okunabilirlik</th>
                </tr>
              </thead>
              <tbody>
                {data.versions.map((v, sira) => (
                  <tr key={v.versionId}>
                    <td>
                      {formatDate(v.retrievedAt)}
                      {sira === 0 ? <span className="badge eligible"> güncel</span> : null}
                    </td>
                    <td>{v.httpStatusCode === 200 ? 'Erişildi' : 'Erişilemedi'}</td>
                    <td>
                      {belgeOkunabilirlik[v.parseStatus]}
                      {v.parseError ? <div className="small">{v.parseError}</div> : null}
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
                Belge uzun olduğu için ilk bölümü gösteriliyor. Tamamını okumak için
                yukarıdaki resmî adrese gidin.
              </InfoBox>
            )}

            <pre className="belge-metni" data-alan="metin-govdesi">
              {data.textPreview}
            </pre>
          </>
        ) : (
          <p className="muted">
            Bu belgenin metni okunamadı. Taranmış bir görüntü olabilir ya da kaynağa
            erişilememiş olabilir; yukarıdaki belge geçmişinde sebebi yazıyor.
          </p>
        )}
      </div>
    </section>
  )
}
