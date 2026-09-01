import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { TriageReport } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading, SuccessBox } from '@/components/Common'
import { documentOriginLabels, quarantineReasonLabels } from '@/lib/regulatoryLabels'
import { formatDate } from '@/lib/format'

/**
 * Karantina inceleme (PlatformReviewer).
 *
 * Karantina silme değildir: kayıtlar durur ve buradan geri alınabilir. Katalog dışında
 * tutulmalarının tek sonucu, şirketlere fırsat olarak gösterilmemeleri ve skorlanmamalarıdır.
 */
export default function QuarantinePage() {
  const queryClient = useQueryClient()
  const [report, setReport] = useState<TriageReport | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  // Kontrollü manuel içe aktarma
  const [importSourceId, setImportSourceId] = useState('')
  const [importUrl, setImportUrl] = useState('')

  const { data = [], isLoading, error } = useQuery({
    queryKey: ['quarantine'],
    queryFn: api.listQuarantined,
  })

  const triage = useMutation({
    mutationFn: (apply: boolean) => api.runTriage(apply),
    onSuccess: async (result, apply) => {
      setReport(result)
      setNotice(
        apply
          ? `${result.applied} belge karantinaya alındı, ${result.affectedOpportunities} fırsat katalogdan çıkarıldı; ${result.affectedAssessments} değerlendirme yeniden değerlendirilmek üzere işaretlendi.`
          : `${result.reviewed} kayıt incelendi, ${result.flagged} tanesi işaretlendi. Hiçbir şey değiştirilmedi.`,
      )
      if (apply) await queryClient.invalidateQueries({ queryKey: ['quarantine'] })
    },
  })

  // Otomatik taramaya uygun olmayan kaynaklar için: inceleyici resmî ilan adresini
  // verir, sistem içeriği kendisi indirir. Kayıt "elle aktarıldı" diye işaretlenir.
  const { data: sources = [] } = useQuery({
    queryKey: ['sources'],
    queryFn: api.listSources,
    retry: false,
  })

  const manualImport = useMutation({
    mutationFn: () => api.manualImport(importSourceId, importUrl.trim()),
    onSuccess: async (result) => {
      setNotice(
        `${result.sourceName}: kayıt alındı (HTTP ${result.httpStatusCode}, ` +
          `${result.contentLength.toLocaleString('tr-TR')} karakter). ${result.note}`,
      )
      setImportUrl('')
      await queryClient.invalidateQueries({ queryKey: ['quarantine'] })
    },
  })

  const approve = useMutation({
    mutationFn: (documentId: string) => api.approveQuarantined(documentId),
    onSuccess: async () => {
      setNotice('Kayıt karantinadan çıkarıldı ve yeniden ayrıştırmaya alındı.')
      await queryClient.invalidateQueries({ queryKey: ['quarantine'] })
    },
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Karantina İnceleme</h1>
          <p>
            Katalog dışında tutulan kayıtlar. Hiçbiri silinmez; buradan geri alınabilir.
            Karantinadaki kayıt şirketlere gösterilmez, skorlanmaz ve bildirim üretmez.
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="button" disabled={triage.isPending} onClick={() => triage.mutate(false)}>
            {triage.isPending ? 'Taranıyor…' : 'Kuralları çalıştır (rapor)'}
          </button>
          <button type="button" disabled={triage.isPending} onClick={() => triage.mutate(true)}>
            Uygula
          </button>
        </div>
      </div>

      {notice ? <SuccessBox>{notice}</SuccessBox> : null}
      {triage.error ? <ErrorBox error={triage.error} /> : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Resmî adresten elle içe aktarma</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          Otomatik taramaya uygun olmayan kaynaklar içindir. Adres kaynağın{' '}
          <strong>resmî alan adına</strong> ait olmalıdır; içerik yapıştırılmaz, sistem
          kendisi indirir. <strong>Bu bir tarama değildir:</strong> kaynak doğrulanmış
          sayılmaz ve kayıt "elle aktarıldı" olarak işaretlenir.
        </p>

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span className="muted" style={{ fontSize: 12 }}>Kaynak</span>
            <select
              value={importSourceId}
              onChange={(e) => setImportSourceId(e.target.value)}
              style={{ minWidth: 240 }}
            >
              <option value="">Kaynak seçin…</option>
              {sources.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                  {s.officialDomain ? ` (${s.officialDomain})` : ''}
                </option>
              ))}
            </select>
          </label>

          <label style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: 1, minWidth: 320 }}>
            <span className="muted" style={{ fontSize: 12 }}>Resmî ilan/belge adresi</span>
            <input
              type="url"
              value={importUrl}
              placeholder="https://ekap.kik.gov.tr/EKAP/..."
              onChange={(e) => setImportUrl(e.target.value)}
            />
          </label>

          <button
            type="button"
            disabled={!importSourceId || !importUrl.trim() || manualImport.isPending}
            onClick={() => manualImport.mutate()}
          >
            {manualImport.isPending ? 'Alınıyor…' : 'İçe aktar'}
          </button>
        </div>

        {manualImport.error ? <ErrorBox error={manualImport.error} /> : null}
      </div>

      {report && report.rows.length > 0 ? (
        <div className="card" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Triyaj raporu</h2>
          <p className="muted" style={{ marginTop: 0 }}>
            {report.reviewed} kayıt incelendi · {report.clean} temiz · {report.flagged} işaretlendi
          </p>

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Başlık</th>
                  <th>Kaynak</th>
                  <th>Önerilen neden</th>
                  <th>Kanıt</th>
                  <th>Eksik alan</th>
                  <th>Skorlanmış</th>
                  <th>Önerilen işlem</th>
                </tr>
              </thead>
              <tbody>
                {report.rows.map((row) => (
                  <tr key={row.documentId}>
                    <td>{row.title}</td>
                    <td>{row.sourceName}</td>
                    <td>{quarantineReasonLabels[row.proposedReason]}</td>
                    <td className="muted">{row.evidence}</td>
                    <td>{row.missingField ?? '—'}</td>
                    <td>{row.isScored ? `Evet (${row.linkedAssessmentCount})` : 'Hayır'}</td>
                    <td className="muted">{row.recommendedAction}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      <InfoBox>
        Karantinadan çıkarılan kayıt yeniden ayrıştırılır. Kaynağın URL kalıbı
        daraltılmadıkça aynı sayfa tekrar toplanabilir.
      </InfoBox>

      {data.length === 0 ? (
        <EmptyState>Karantinada kayıt yok.</EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Başlık</th>
                <th>Kaynak</th>
                <th>Neden</th>
                <th>Not</th>
                <th>Sürüm</th>
                <th>Köken</th>
                <th>Toplanma</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
                <tr key={item.documentId}>
                  <td>
                    <strong>{item.title}</strong>
                    {item.titleRepaired ? (
                      <div className="muted" style={{ fontSize: 12 }}>
                        Başlık bozuk karakter kümesiyle kaydedilmişti; burada onarılmış
                        hâli gösteriliyor. Ham belge değiştirilmedi.
                      </div>
                    ) : null}
                    <div className="muted" style={{ fontSize: 12, overflowWrap: 'anywhere' }}>
                      {item.url}
                    </div>
                  </td>
                  <td>{item.sourceName}</td>
                  <td>{quarantineReasonLabels[item.reason]}</td>
                  <td className="muted">{item.note ?? '—'}</td>
                  <td>{item.versionCount}</td>
                  <td>
                    <span className={item.origin === 'ManualImport' ? 'badge indeterminate' : 'muted'}>
                      {documentOriginLabels[item.origin]}
                    </span>
                  </td>
                  <td>{formatDate(item.collectedAt)}</td>
                  <td>
                    <button
                      type="button"
                      disabled={approve.isPending}
                      onClick={() => approve.mutate(item.documentId)}
                    >
                      Karantinadan çıkar
                    </button>
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
