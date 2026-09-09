import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { RuleEvidenceBackfillOutcome, RuleEvidenceBackfillReport } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading, SuccessBox } from '@/components/Common'

/**
 * Sonuç etiketleri.
 *
 * "Atlandı" demek yetmez: inceleyicinin bir sonraki adımı sonucun TÜRÜNE bağlıdır.
 * Kanıtı bulunamayan kayıt elle incelenir, ham içeriği olmayan kayıt yeniden indirilir,
 * karantinadaki kayıt ise hiç ele alınmaz.
 */
const sonucEtiketleri: Record<RuleEvidenceBackfillOutcome, string> = {
  Bound: 'Kanıt bağlanacak',
  AlreadyBound: 'Zaten bağlı',
  NoEvidenceFound: 'Kanıt bulunamadı — dokunulmayacak',
  NeedsReparse: 'Saklanan içerikten yeniden ayrıştırılacak',
  NeedsRedownload: 'Ham içerik yok — yeniden indirme gerekli',
  SkippedQuarantined: 'Karantinada — atlanacak',
  SkippedUnverifiedSource: 'Kaynak doğrulanmamış — atlanacak',
  NoSourceDocument: 'Elle açılmış kayıt — belgesi yok',
  NoRules: 'Kuralı yok',
  Failed: 'Hata',
}

/** Ekranda öne çıkarılacak sonuçlar; gerisi ayrıntı bölümünde kalır. */
const oncelikliSonuclar: RuleEvidenceBackfillOutcome[] = [
  'Bound',
  'NeedsReparse',
  'NoEvidenceFound',
  'NeedsRedownload',
]

/**
 * Kanıt bağlama (Platform İnceleme).
 *
 * Mevcut fırsat kurallarını, veritabanında **zaten duran** belge sürümlerine bağlar.
 * İnternete çıkmaz. Kanıtı güvenilir biçimde bulunamayan kural tahmin edilerek
 * bağlanmaz ve silinmez — kural motorunda çalışmaya devam eder, yalnızca yapay zekâ
 * o kural hakkında resmî kaynağa dayalı açıklama üretemez.
 */
export default function RuleEvidenceBackfillPage() {
  const queryClient = useQueryClient()

  const [plan, setPlan] = useState<RuleEvidenceBackfillReport | null>(null)
  const [onaylandi, setOnaylandi] = useState(false)
  const [sonuc, setSonuc] = useState<RuleEvidenceBackfillReport | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const {
    data: otomatikPlan,
    isLoading,
    error,
  } = useQuery({
    queryKey: ['rule-evidence-plan'],
    queryFn: () => api.ruleEvidenceBackfillPlan(100),
  })

  const gecerliPlan = plan ?? otomatikPlan ?? null

  const uygula = useMutation({
    mutationFn: () => api.applyRuleEvidenceBackfill(gecerliPlan!.planHash, 100),
    onSuccess: async (rapor) => {
      setSonuc(rapor)
      setOnaylandi(false)
      setNotice(
        `${rapor.evidenceLinksCreated} kanıt bağlantısı kuruldu. ` +
          'Kanıtı bulunamayan kurallara dokunulmadı; hiçbir kural silinmedi.',
      )
      const yeni = await queryClient.fetchQuery({
        queryKey: ['rule-evidence-plan'],
        queryFn: () => api.ruleEvidenceBackfillPlan(100),
      })
      setPlan(yeni)
    },
  })

  const geriAl = useMutation({
    mutationFn: (runId: string) => api.undoRuleEvidenceBackfill(runId),
    onSuccess: async (rapor) => {
      setSonuc(null)
      setNotice(`${rapor.removedLinkCount} bağlantı geri alındı.`)
      const yeni = await queryClient.fetchQuery({
        queryKey: ['rule-evidence-plan'],
        queryFn: () => api.ruleEvidenceBackfillPlan(100),
      })
      setPlan(yeni)
    },
  })

  const planiTazele = useMutation({
    mutationFn: () => api.ruleEvidenceBackfillPlan(100),
    onSuccess: (yeni) => {
      setPlan(yeni)
      setOnaylandi(false)
      setSonuc(null)
      setNotice(null)
    },
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  const onemliler = gecerliPlan?.items.filter((i) => oncelikliSonuclar.includes(i.outcome)) ?? []
  const digerleri = gecerliPlan?.items.filter((i) => !oncelikliSonuclar.includes(i.outcome)) ?? []

  return (
    <section className="page" data-sayfa="kanit-baglama">
      <header className="page-header">
        <h1>Kanıt Bağlama</h1>
        <p className="muted">
          Mevcut fırsat kurallarını, veritabanında zaten duran resmî belge sürümlerine bağlar.
          <strong> İnternete çıkmaz.</strong> Kanıtı güvenilir biçimde bulunamayan kural tahmin
          edilerek bağlanmaz ve silinmez.
        </p>
      </header>

      {notice && <SuccessBox>{notice}</SuccessBox>}
      {uygula.error && <ErrorBox error={uygula.error} />}
      {geriAl.error && <ErrorBox error={geriAl.error} />}

      <div className="card" data-alan="plan-ozeti">
        <h2>Plan</h2>

        {gecerliPlan === null || gecerliPlan.totalExamined === 0 ? (
          <EmptyState>İncelenecek fırsat kaydı bulunamadı.</EmptyState>
        ) : (
          <>
            <ul data-alan="sayilar">
              <li>
                İncelenen kayıt: <strong>{gecerliPlan.totalExamined}</strong>
              </li>
              <li>
                Bağlanacak kayıt: <strong>{gecerliPlan.boundCount}</strong> (
                {gecerliPlan.evidenceLinksCreated} bağlantı)
              </li>
              <li>Zaten bağlı: {gecerliPlan.alreadyBoundCount}</li>
              <li>Kanıt bulunamayan: {gecerliPlan.noEvidenceCount}</li>
              <li>Yeniden ayrıştırılacak: {gecerliPlan.needsReparseCount}</li>
              <li>Yeniden indirme gereken: {gecerliPlan.needsRedownloadCount}</li>
              <li>Karantinada olduğu için atlanan: {gecerliPlan.skippedQuarantinedCount}</li>
              <li>Kaynağı doğrulanmamış: {gecerliPlan.skippedUnverifiedSourceCount}</li>
              <li>Hata alan: {gecerliPlan.failedCount}</li>
            </ul>

            {gecerliPlan.hasMore && (
              <InfoBox>
                Bu tur ilk {gecerliPlan.totalExamined} kaydı kapsıyor. Uygulandıktan sonra aynı
                ekrandan devam edebilirsiniz; işlem kaldığı yerden sürer.
              </InfoBox>
            )}

            {onemliler.length > 0 && (
              <div className="table-scroll">
                <table data-tablo="kayitlar">
                  <thead>
                    <tr>
                      <th>Fırsat</th>
                      <th>Sonuç</th>
                      <th>Kural</th>
                      <th>Belge sürümü</th>
                      <th>Açıklama</th>
                    </tr>
                  </thead>
                  <tbody>
                    {onemliler.map((i) => (
                      <tr key={i.opportunityId}>
                        <td>
                          <Link
                            to={`/platform/opportunities/${i.opportunityId}`}
                            data-alan="detay"
                          >
                            {i.title || i.opportunityId}
                          </Link>
                          {i.officialUrl && (
                            <a
                              className="muted small"
                              href={i.officialUrl}
                              target="_blank"
                              rel="noreferrer"
                            >
                              resmî kaynak
                            </a>
                          )}
                        </td>
                        <td>{sonucEtiketleri[i.outcome]}</td>
                        <td>
                          {i.rulesBound}/{i.ruleCount}
                        </td>
                        <td>
                          {i.documentVersionNumber === null ? '—' : `v${i.documentVersionNumber}`}
                        </td>
                        <td className="small">{i.explanation}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {digerleri.length > 0 && (
              <details data-alan="digerleri">
                <summary>Değişmeyecek {digerleri.length} kayıt</summary>
                <ul>
                  {digerleri.map((i) => (
                    <li key={i.opportunityId}>
                      <Link to={`/platform/opportunities/${i.opportunityId}`}>
                        {i.title || i.opportunityId}
                      </Link>{' '}
                      — {sonucEtiketleri[i.outcome]}
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

      {(gecerliPlan?.boundCount ?? 0) + (gecerliPlan?.needsReparseCount ?? 0) > 0 && (
        <div className="card" data-alan="onay">
          <h2>Onay</h2>

          <InfoBox>
            Bu işlem <strong>{gecerliPlan!.evidenceLinksCreated}</strong> kanıt bağlantısı
            kuracak. Kural, belge, sürüm ve kanıt parçaları değişmez; yalnızca aralarındaki
            ilişki eklenir. Onaylamadan hiçbir şey uygulanmaz.
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
            {uygula.isPending ? 'Uygulanıyor…' : 'Kanıt bağlamayı uygula'}
          </button>
        </div>
      )}

      {sonuc && (
        <div className="card" data-alan="sonuc">
          <h2>Sonuç</h2>

          <p>
            {sonuc.boundCount} kayıtta {sonuc.evidenceLinksCreated} bağlantı kuruldu.{' '}
            {sonuc.noEvidenceCount} kayıtta kanıt bulunamadı ve dokunulmadı.
          </p>

          {sonuc.runId && (
            <button
              type="button"
              className="secondary"
              data-alan="geri-al"
              onClick={() => geriAl.mutate(sonuc.runId!)}
              disabled={geriAl.isPending}
            >
              {geriAl.isPending ? 'Geri alınıyor…' : 'Bu bağlamayı geri al'}
            </button>
          )}
        </div>
      )}
    </section>
  )
}
