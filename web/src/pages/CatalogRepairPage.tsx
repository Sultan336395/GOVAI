import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { CatalogRepairPlanReport, CatalogRepairReport } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading, SuccessBox } from '@/components/Common'

const hedefEtiketleri: Record<string, string> = {
  Opportunity: 'Fırsat',
  RegulatoryChange: 'Mevzuat kaydı',
}

const eylemEtiketleri: Record<string, string> = {
  Quarantine: 'Karantinaya al',
  RetitleFromDocument: 'Başlığı belgeden düzelt',
}

/**
 * Katalog onarımı (Platform İnceleme).
 *
 * Ekranın tamamı tek bir kurala göre kurulmuştur: **önce gör, sonra onayla, sonra
 * uygula.** Uygula düğmesi plan getirilmeden ve onay kutusu işaretlenmeden açılmaz;
 * sunucu da aynı kuralı ayrıca uygular (plan parmak izi), böylece arayüz atlanarak
 * doğrudan uç çağrılsa bile onaysız değişiklik yapılamaz.
 *
 * Karantina silme değildir: kayıt durur, kanıtı korunur ve bu ekrandan geri alınabilir.
 */
export default function CatalogRepairPage() {
  const queryClient = useQueryClient()

  const [plan, setPlan] = useState<CatalogRepairPlanReport | null>(null)
  const [onaylandi, setOnaylandi] = useState(false)
  const [sonuc, setSonuc] = useState<CatalogRepairReport | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const {
    data: otomatikPlan,
    isLoading,
    error,
  } = useQuery({
    queryKey: ['catalog-repair-plan'],
    queryFn: api.catalogRepairPlan,
  })

  const gecerliPlan = plan ?? otomatikPlan ?? null

  const uygula = useMutation({
    mutationFn: () => api.applyCatalogRepair(gecerliPlan!.planHash),
    onSuccess: async (rapor) => {
      setSonuc(rapor)
      setOnaylandi(false)
      setNotice(
        `${rapor.changed} kayıt onarıldı. Hiçbir kayıt silinmedi; ` +
          'karantinaya alınanların belgesi, ham içeriği ve kanıtları yerinde duruyor.',
      )
      const yeni = await queryClient.fetchQuery({
        queryKey: ['catalog-repair-plan'],
        queryFn: api.catalogRepairPlan,
      })
      setPlan(yeni)
    },
  })

  const geriAl = useMutation({
    mutationFn: (runId: string) => api.undoCatalogRepair(runId),
    onSuccess: async (rapor) => {
      setSonuc(null)
      setNotice(`${rapor.changed} kayıt geri alındı.`)
      const yeni = await queryClient.fetchQuery({
        queryKey: ['catalog-repair-plan'],
        queryFn: api.catalogRepairPlan,
      })
      setPlan(yeni)
    },
  })

  const planiTazele = useMutation({
    mutationFn: api.catalogRepairPlan,
    onSuccess: (yeni) => {
      setPlan(yeni)
      setOnaylandi(false)
      setSonuc(null)
      setNotice(null)
    },
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  const degisecekler = gecerliPlan?.matches.filter((m) => m.willChange) ?? []
  const atlananlar = gecerliPlan?.matches.filter((m) => !m.willChange) ?? []

  return (
    <section className="page" data-sayfa="katalog-onarimi">
      <header className="page-header">
        <h1>Katalog Onarımı</h1>
        <p className="muted">
          Kataloğa yanlışlıkla girmiş kayıtları düzeltir. <strong>Hiçbir kayıt silinmez</strong>;
          çöp kayıtlar karantinaya alınır, yanlış başlıklar belge sürümündeki gerçek başlıkla
          düzeltilir. İşlem geri alınabilir.
        </p>
      </header>

      {notice && <SuccessBox>{notice}</SuccessBox>}
      {uygula.error && <ErrorBox error={uygula.error} />}
      {geriAl.error && <ErrorBox error={geriAl.error} />}

      <div className="card" data-alan="plan-ozeti">
        <h2>Plan</h2>

        {gecerliPlan === null || gecerliPlan.matches.length === 0 ? (
          <EmptyState>Onarılacak kayıt bulunamadı. Katalog temiz.</EmptyState>
        ) : (
          <>
            <p>
              <strong>{degisecekler.length}</strong> kayıt değişecek,{' '}
              <strong>{atlananlar.length}</strong> kayıt atlanacak.
            </p>

            {degisecekler.length > 0 && (
              <div className="table-scroll">
                <table data-tablo="degisecekler">
                  <thead>
                    <tr>
                      <th>Kayıt</th>
                      <th>Tür</th>
                      <th>Yapılacak</th>
                      <th>Gerekçe</th>
                      <th>Etkilenen değerlendirme</th>
                    </tr>
                  </thead>
                  <tbody>
                    {degisecekler.map((m) => (
                      <tr key={m.recordId}>
                        <td>
                          <div>{m.currentTitle}</div>
                          {m.officialUrl && (
                            <a
                              className="muted small"
                              href={m.officialUrl}
                              target="_blank"
                              rel="noreferrer"
                            >
                              resmî kaynak
                            </a>
                          )}
                          {m.proposedTitle && (
                            <div className="small">
                              Yeni başlık: <strong>{m.proposedTitle}</strong>
                            </div>
                          )}
                        </td>
                        <td>{hedefEtiketleri[m.target] ?? m.target}</td>
                        <td>{eylemEtiketleri[m.action] ?? m.action}</td>
                        <td className="small">{m.stepCode}</td>
                        <td>{m.affectedAssessmentCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {atlananlar.length > 0 && (
              <details data-alan="atlananlar">
                <summary>Atlanacak {atlananlar.length} kayıt ve sebepleri</summary>
                <ul>
                  {atlananlar.map((m) => (
                    <li key={m.recordId}>
                      <strong>{m.currentTitle}</strong> — {m.skipReason}
                    </li>
                  ))}
                </ul>
              </details>
            )}
          </>
        )}

        <div className="row-actions">
          <button
            type="button"
            className="secondary"
            onClick={() => planiTazele.mutate()}
            disabled={planiTazele.isPending}
          >
            Planı yenile
          </button>
        </div>
      </div>

      {degisecekler.length > 0 && (
        <div className="card" data-alan="onay">
          <h2>Onay</h2>

          <InfoBox>
            Bu işlem <strong>{degisecekler.length}</strong> kaydı değiştirecek. Onaylamadan
            hiçbir şey uygulanmaz. Uygulandıktan sonra da geri alabilirsiniz.
          </InfoBox>

          <label className="checkbox">
            <input
              type="checkbox"
              data-alan="onay-kutusu"
              checked={onaylandi}
              onChange={(e) => setOnaylandi(e.target.checked)}
            />
            Yukarıdaki listeyi inceledim ve uygulanmasını onaylıyorum.
          </label>

          <button
            type="button"
            data-alan="uygula"
            onClick={() => uygula.mutate()}
            disabled={!onaylandi || uygula.isPending}
          >
            {uygula.isPending ? 'Uygulanıyor…' : 'Onarımı uygula'}
          </button>
        </div>
      )}

      {sonuc && (
        <div className="card" data-alan="sonuc">
          <h2>Sonuç</h2>

          <p>
            {sonuc.changed} kayıt onarıldı, {sonuc.alreadyDone} kayıt zaten uygulanmıştı.
          </p>

          <ul>
            {sonuc.outcomes.map((o) => (
              <li key={`${o.stepCode}-${o.recordId}`}>
                <strong>{o.stepCode}</strong> — {o.result}
                {o.previousTitle && (
                  <span className="muted small"> (eski başlık: {o.previousTitle})</span>
                )}
              </li>
            ))}
          </ul>

          {sonuc.runId && (
            <button
              type="button"
              className="secondary"
              data-alan="geri-al"
              onClick={() => geriAl.mutate(sonuc.runId!)}
              disabled={geriAl.isPending}
            >
              {geriAl.isPending ? 'Geri alınıyor…' : 'Bu onarımı geri al'}
            </button>
          )}
        </div>
      )}
    </section>
  )
}
