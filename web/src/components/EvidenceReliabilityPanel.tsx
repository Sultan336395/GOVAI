import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { EvidenceStatus } from '@/api/types'
import { EmptyState, ErrorBox, Loading } from '@/components/Common'

/**
 * Kanıt eskime paneli.
 *
 * <p>
 * Ekranın söylediği şey "belgeniz var mı" değil, <b>"belgeniz hâlâ geçerli bir karar
 * dayanağı mı"</b>. Bu iki soru farklıdır ve ikincisi denetimde ortaya çıkana kadar
 * görünmez; panelin varlık sebebi o farkı önceden göstermektir.
 * </p>
 */

const DURUM_SINIFI: Record<EvidenceStatus, string> = {
  Guvenilir: 'badge eligible',
  Zayifliyor: 'badge conditional',
  Guvenilmez: 'badge not-eligible',
  Gecersiz: 'badge not-eligible',
  Bilinmiyor: 'badge',
}

export default function EvidenceReliabilityPanel({ companyId }: { companyId: string }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['evidence-portfolio', companyId],
    queryFn: () => api.getEvidencePortfolio(companyId),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return null

  return (
    <div className="card">
      <h2>Kanıt Güvenilirliği</h2>
      <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
        Bir belgenin elinizde olması, bugün hâlâ karar dayanağı sayıldığı anlamına gelmez.
        Çalışan değişir, sistem güncellenir, sertifika biter, mevzuat değişir.
      </p>

      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <div className="card">
          <div className="kpi-label">Karar dayanağı</div>
          <div className="kpi-value">
            {data.dependable}/{data.total}
          </div>
          <div className="kpi-hint">Bugün güvenilebilecek kanıt</div>
        </div>
        <div className="card">
          <div className="kpi-label">İlgi bekleyen</div>
          <div className="kpi-value">{data.needsAttention}</div>
          <div className="kpi-hint">Süresi dolmuş veya artık dayanak değil</div>
        </div>
        <div className="card">
          <div className="kpi-label">Ortalama güvenilirlik</div>
          {/*
            Ölçülemeyen kanıtlar ortalamaya KARIŞMAZ. Sıfır sayılsalardı tarihi
            girilmemiş her belge firmanın skorunu haksız yere aşağı çekerdi.
          */}
          <div className="kpi-value">
            {data.averageScore === undefined ? '—' : `%${Math.round(data.averageScore * 100)}`}
          </div>
          <div className="kpi-hint">
            {data.unknown > 0 ? `${data.unknown} kanıt hesaplanamıyor` : 'Ölçülebilen kanıtlar'}
          </div>
        </div>
      </div>

      {data.items.length === 0 ? (
        <EmptyState>Henüz kanıt kaydı yok.</EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Kanıt</th>
                <th>Tür</th>
                <th>Durum</th>
                <th>Güvenilirlik</th>
                <th>Yaş</th>
                <th>Gerekçe</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((k) => (
                <tr key={`${k.kind}-${k.label}`}>
                  <td>{k.label}</td>
                  <td className="muted">{k.kindLabel}</td>
                  <td>
                    <span className={DURUM_SINIFI[k.status]}>{k.statusLabel}</span>
                  </td>
                  <td>
                    {/* Hesaplanamayan kanıt "%0" gösterilmez; sıfır bir ölçümdür,
                        bilinmemek ise ölçümün yokluğudur. */}
                    {k.score === undefined ? '—' : `%${Math.round(k.score * 100)}`}
                  </td>
                  <td>{k.ageDays === undefined ? '—' : `${k.ageDays} gün`}</td>
                  <td style={{ fontSize: 13 }}>{k.reason}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
