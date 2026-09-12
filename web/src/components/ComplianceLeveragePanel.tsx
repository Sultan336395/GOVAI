import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { ComplianceGap } from '@/api/types'
import { EmptyState, ErrorBox, Loading } from '@/components/Common'
import { formatCurrency } from '@/lib/format'

/**
 * Uyumu fırsata çeviren panel.
 *
 * <p>
 * Ekranın söylediği şey "şu koşulu sağlamıyorsun" değil, <b>"şunu kapatırsan şu
 * çağrılara girersin"</b>. Fark iş planıdır: birincisi bir sorun listesi, ikincisi
 * bir yatırım gerekçesidir.
 * </p>
 *
 * <p>
 * Abartma bilinçli olarak engellenir. "Açılır" yalnızca o çağrıda <b>başka engel
 * kalmadığında</b> yazılır; kalan engel varsa satır "katkı sağlar" olarak görünür.
 * Bir kez yanlış söylenen "6 milyon açıldı" cümlesi ürünün güvenini bitirir.
 * </p>
 */
export default function ComplianceLeveragePanel({ companyId }: { companyId: string }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['compliance-leverage', companyId],
    queryFn: () => api.getComplianceLeverage(companyId),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return null

  return (
    <div className="card">
      <h2>Eksikler ve Getirileri</h2>
      <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
        Her eksik, kapatıldığında hangi çağrıların açılacağıyla birlikte gösterilir.
        {' '}
        {data.evaluatedOpportunityCount} açık çağrı değerlendirildi.
      </p>

      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <div className="card">
          <div className="kpi-label">Kapatılabilir eksik</div>
          <div className="kpi-value">{data.gapCount}</div>
          <div className="kpi-hint">Beyan, belge veya yetkinlik</div>
        </div>
        <div className="card">
          <div className="kpi-label">Açılabilecek çağrı</div>
          <div className="kpi-value">{data.unlockableOpportunityCount}</div>
          <div className="kpi-hint">Tek bir eksiğin kapatılmasıyla</div>
        </div>
        <div className="card">
          <div className="kpi-label">Bilinen tutar</div>
          <div className="kpi-value">
            {data.unlockableAmount === undefined ? '—' : formatCurrency(data.unlockableAmount)}
          </div>
          {/* Tutarı bilinmeyen çağrılar toplama katılmaz; sayı bir ALT SINIRDIR. */}
          <div className="kpi-hint">
            {data.amountIsPartial ? 'En az — bazı çağrıların tutarı bilinmiyor' : 'Azami tutar toplamı'}
          </div>
        </div>
      </div>

      {data.gaps.length === 0 ? (
        <EmptyState>Açık çağrılarda kapatılabilir eksik bulunmadı.</EmptyState>
      ) : (
        <div style={{ display: 'grid', gap: 12 }}>
          {data.gaps.map((eksik) => (
            <GapCard key={eksik.key} gap={eksik} />
          ))}
        </div>
      )}
    </div>
  )
}

function GapCard({ gap }: { gap: ComplianceGap }) {
  const acilanlar = gap.opportunities.filter((o) => o.unlockedByThisAlone)
  const katkilar = gap.opportunities.filter((o) => !o.unlockedByThisAlone)

  return (
    <div className="card" style={{ borderLeft: '3px solid var(--primary)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
        <div>
          <strong>{gap.requirement}</strong>
          <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
            {gap.kindLabel}
            {gap.suggestedAction ? ` · ${gap.suggestedAction}` : ''}
          </div>
        </div>
        <div style={{ textAlign: 'right' }}>
          {gap.unlockCount > 0 ? (
            <>
              <div style={{ fontWeight: 700 }}>
                {gap.unlockedAmount === undefined ? '—' : formatCurrency(gap.unlockedAmount)}
              </div>
              <div className="muted" style={{ fontSize: 12 }}>
                {gap.unlockCount} çağrı açılır
                {gap.amountIsPartial ? ' · bazılarının tutarı bilinmiyor' : ''}
              </div>
            </>
          ) : (
            <div className="muted" style={{ fontSize: 12 }}>
              {gap.affectedCount} çağrıya katkı sağlar
            </div>
          )}
        </div>
      </div>

      {/*
        Beyan eksiğinin getirisi KOŞULLUDUR: alan doldurulduğunda cevap "sağlamıyor"
        da çıkabilir. "Doldur, hibe açılacak" demek kullanıcıyı yanıltırdı.
      */}
      {gap.isConditional ? (
        <p className="uyari" style={{ fontSize: 12, marginTop: 8, marginBottom: 0 }}>
          Bu bir beyan eksiği: alan doldurulduğunda koşul sağlanmayabilir de. Getiri kesin değil,
          kapatması ise en ucuz adımdır.
        </p>
      ) : null}

      {acilanlar.length > 0 ? (
        <ul style={{ margin: '10px 0 0', paddingLeft: 18, fontSize: 13 }}>
          {acilanlar.map((o) => (
            <li key={o.opportunityId}>
              {/* Çağrı için kullanıcı tarafında ayrı bir sayfa yok; bağlantı vermek
                  404'e götürürdü. Başlık düz metin durur. */}
              {o.title}
              {o.maxAmount === undefined ? null : (
                <span className="muted"> · en çok {formatCurrency(o.maxAmount)}</span>
              )}
            </li>
          ))}
        </ul>
      ) : null}

      {katkilar.length > 0 ? (
        <details style={{ marginTop: 8 }}>
          <summary className="muted" style={{ fontSize: 12, cursor: 'pointer' }}>
            {katkilar.length} çağrıda başka engeller de var
          </summary>
          <ul style={{ margin: '8px 0 0', paddingLeft: 18, fontSize: 13 }}>
            {katkilar.map((o) => (
              <li key={o.opportunityId}>
                {o.title}
                <span className="muted"> · {o.remainingGapCount} engel daha kalır</span>
              </li>
            ))}
          </ul>
        </details>
      ) : null}
    </div>
  )
}
