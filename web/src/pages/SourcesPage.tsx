import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { SourceDto } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading } from '@/components/Common'
import {
  quarantineReasonLabels,
  sourceCategoryLabels,
  sourceHealthClass,
  sourceHealthLabels,
} from '@/lib/regulatoryLabels'
import { formatDate } from '@/lib/format'
import { taramaTakvimi } from '@/lib/sozluk'

const statusLabels: Record<SourceDto['lastRunStatus'], string> = {
  Pending: 'Beklemede',
  Running: 'Çalışıyor',
  Succeeded: 'Başarılı',
  Failed: 'Başarısız',
  Skipped: 'Atlandı',
}

/**
 * Veri Kaynakları (PlatformCatalogManager).
 *
 * Doğrulanmamış kaynak taranmaz ve burada "aktif" gösterilmez: tarama düğmesi yalnızca
 * gerçekten taranabilir kaynaklarda etkindir.
 */
export default function SourcesPage() {
  const queryClient = useQueryClient()

  const { data = [], isLoading, error } = useQuery({
    queryKey: ['sources'],
    queryFn: api.listSources,
  })

  // Karantina sayısı kaynak başına gösterilir: bozuk seçicinin ilk belirtisi budur.
  const { data: quarantined = [] } = useQuery({
    queryKey: ['quarantine'],
    queryFn: api.listQuarantined,
    retry: false,
  })

  const crawl = useMutation({
    mutationFn: (sourceId: string) => api.triggerCrawl(sourceId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sources'] }),
  })

  const toggle = useMutation({
    mutationFn: (input: { id: string; enabled: boolean }) =>
      api.setSourceEnabled(input.id, input.enabled),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sources'] }),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  const quarantineCounts = quarantined.reduce<Record<string, number>>((acc, item) => {
    acc[item.sourceName] = (acc[item.sourceName] ?? 0) + 1
    return acc
  }, {})

  const dogrulanan = data.filter((s) => s.configurationVerified).length

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Veri Kaynakları</h1>
          <p>
            Resmî kurum siteleri ve tarama planları. {data.length} kaynak tanımlı,{' '}
            {dogrulanan} tanesi canlı doğrulanmış.
          </p>
        </div>
      </div>

      <InfoBox>
        Doğrulanmamış kaynak taranmaz. Bir kaynağın taranabilir olması için seçicisinin
        canlı olarak bağlantı çıkardığının kanıtlanması gerekir.
      </InfoBox>

      {data.length === 0 ? (
        <EmptyState>Tanımlı kaynak yok.</EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Kaynak</th>
                <th>Kategori</th>
                <th>Sağlık</th>
                <th>Doğrulama</th>
                <th>Son Tarama</th>
                <th>Son Hata</th>
                <th>Karantina</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((source) => (
                <tr key={source.id}>
                  <td>
                    <strong>{source.name}</strong>
                    <div className="muted" style={{ fontSize: 12 }}>
                      {source.authority ?? '—'}
                      {source.jurisdiction ? ` · ${source.jurisdiction}` : ''}
                      {source.officialDomain ? ` · ${source.officialDomain}` : ''}
                    </div>
                    <div className="muted" style={{ fontSize: 12 }}>
                      {taramaTakvimi(source.cronExpression)} · en fazla {source.maxPages} sayfa
                    </div>
                  </td>
                  <td>{sourceCategoryLabels[source.category] ?? '—'}</td>
                  <td>
                    <span className={`badge ${sourceHealthClass[source.health]}`}>
                      {sourceHealthLabels[source.health]}
                    </span>
                  </td>
                  <td>
                    {source.configurationVerified ? (
                      <>
                        <span className="badge eligible">Doğrulandı</span>
                        <div className="muted" style={{ fontSize: 12 }}>
                          {source.configurationVerifiedAt
                            ? formatDate(source.configurationVerifiedAt)
                            : ''}
                        </div>
                      </>
                    ) : (
                      <span className="badge indeterminate">Beklemede</span>
                    )}
                  </td>
                  <td>
                    {source.lastRunAt ? formatDate(source.lastRunAt) : '—'}
                    <div className="muted" style={{ fontSize: 12 }}>
                      {statusLabels[source.lastRunStatus]}
                    </div>
                  </td>
                  <td className="muted" style={{ maxWidth: 260, overflowWrap: 'anywhere' }}>
                    {source.lastRunMessage ?? '—'}
                  </td>
                  <td>{quarantineCounts[source.name] ?? 0}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                      <button
                        type="button"
                        title={
                          source.isCrawlable
                            ? 'Taramayı şimdi başlat'
                            : 'Doğrulanmamış kaynak taranamaz'
                        }
                        disabled={!source.isCrawlable || crawl.isPending}
                        onClick={() => crawl.mutate(source.id)}
                      >
                        Şimdi tara
                      </button>
                      <button
                        type="button"
                        disabled={toggle.isPending}
                        onClick={() => toggle.mutate({ id: source.id, enabled: !source.isEnabled })}
                      >
                        {source.isEnabled ? 'Pasifleştir' : 'Aktifleştir'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {quarantined.length > 0 ? (
        <div className="card" style={{ marginTop: 16 }}>
          <h2 style={{ marginTop: 0 }}>Son gelen karantina kayıtları</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Başlık</th>
                  <th>Kaynak</th>
                  <th>Neden</th>
                  <th>Toplanma</th>
                </tr>
              </thead>
              <tbody>
                {quarantined.slice(0, 8).map((item) => (
                  <tr key={item.documentId}>
                    <td>{item.title}</td>
                    <td>{item.sourceName}</td>
                    <td>{quarantineReasonLabels[item.reason]}</td>
                    <td>{formatDate(item.collectedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </>
  )
}
