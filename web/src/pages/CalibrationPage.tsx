import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { EligibilityVerdict, VerdictDisagreementReason } from '@/api/types'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, InfoBox, Kpi, Loading } from '@/components/Common'
import { HelpTip } from '@/components/HelpTip'
import { formatDate, formatPercent, verdictLabels } from '@/lib/format'

/**
 * Karar Doğruluğu — sistemin kararlarının danışman görüşüyle karşılaştırılması.
 *
 * Bu ekran karar üretmez, karar mekanizmasını ÖLÇER. Danışman görüşü hiçbir skoru
 * değiştirmez; motor deterministik kalır ve buradaki sayılar ağırlıkların doğruluğunu
 * sınamak için kullanılır.
 *
 * Ekran öneri de üretmez: hangi ağırlığın nasıl değişeceği insan kararıdır. Buradaki
 * iş, hatayı türüne göre sayıp nerede durduğunu göstermektir.
 */
export default function CalibrationPage() {
  const { selectedCompanyId } = useCompanies()

  const { data, isLoading, error } = useQuery({
    queryKey: ['calibration-report', selectedCompanyId],
    queryFn: () => api.getCalibrationReport(selectedCompanyId ?? undefined),
    enabled: Boolean(selectedCompanyId),
  })

  const kayitlar = useQuery({
    queryKey: ['expert-verdicts', selectedCompanyId],
    queryFn: () => api.listExpertVerdicts(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  if (!selectedCompanyId) {
    return <EmptyState>Ölçümü görmek için önce bir firma seçin.</EmptyState>
  }

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Ölçüm bulunamadı.</EmptyState>

  const { summary, ruleExtraction } = data

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Karar Doğruluğu</h1>
          <p>
            Sistemin verdiği kararlar ile danışmanın kendi kararının karşılaştırması.
            Danışman görüşü hiçbir skoru değiştirmez; yalnızca sistemin ne kadar isabetli
            olduğunu ölçmek için saklanır.
          </p>
        </div>
      </div>

      {summary.totalVerdicts === 0 ? (
        <EmptyState>
          Henüz danışman değerlendirmesi kaydedilmemiş. Bir fırsatın detay ekranında
          kendi kararınızı işaretlediğinizde ölçüm burada oluşmaya başlar.
        </EmptyState>
      ) : (
        <>
          {/*
            Örneklem uyarısı EN ÜSTTE durur. Az sayıda vakayla hesaplanan bir oran
            istatistik değil gürültüdür; bu sayıya bakarak ağırlık değiştirmek modeli bozar.
          */}
          {!summary.isSampleSufficient ? (
            <InfoBox>
              Ölçüm {summary.totalVerdicts} vakaya dayanıyor. Oranları yorumlamak için bu
              sayı henüz yeterli değil; aşağıdaki değerler eğilim olarak okunmalı, ağırlık
              değişikliğine gerekçe yapılmamalıdır.
            </InfoBox>
          ) : null}

          <div className="kpi-row">
            <Kpi
              label="Uyum Oranı"
              value={formatPercent(summary.agreementRate)}
              hint={`${summary.agreementCount} / ${summary.totalVerdicts} vaka`}
            />
            <Kpi
              label="Yanlış Pozitif"
              value={String(summary.falsePositiveCount)}
              hint={formatPercent(summary.falsePositiveRate)}
            />
            <Kpi
              label="Yanlış Negatif"
              value={String(summary.falseNegativeCount)}
              hint={formatPercent(summary.falseNegativeRate)}
            />
            <Kpi
              label="Eksik Veriden Ayrışma"
              value={String(summary.dataGapDisagreementCount)}
              hint="Model değil, veri sorunu"
            />
          </div>

          <InfoBox>
            <div>
              <strong>Yanlış pozitif</strong> <HelpTip field="yanlisPozitif" /> — sistem uygun
              dedi, danışman uygun değil dedi. Firma uygun olmadığı bir programa zaman ayırır.
            </div>
            <div>
              <strong>Yanlış negatif</strong> <HelpTip field="yanlisNegatif" /> — sistem uygun
              değil dedi, danışman uygun dedi. Bu hata sessizdir: firma fırsatı hiç görmez.
            </div>
            <div>
              <strong>Eksik veriden ayrışma</strong> — ayrışmanın sebebi modelin yanlışlığı
              değil, firma profilindeki eksik bilgidir. İkisi ayrı düzeltme gerektirir.
            </div>
          </InfoBox>

          <section style={{ marginTop: 24 }}>
            <h2>Hatanın Yönü</h2>
            <p className="muted">
              Tek bir uyum oranı, sistemin fazla iyimser mi yoksa fazla temkinli mi olduğunu
              söylemez. İki durum ters yönde düzeltme gerektirir.
            </p>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Sistemin Kararı</th>
                    <th>Danışmanın Kararı</th>
                    <th>Vaka</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.matrix.map((h) => (
                    <tr key={`${h.systemVerdict}-${h.expertOpinion}`}>
                      <td>{verdictLabels[h.systemVerdict]}</td>
                      <td>{verdictLabels[h.expertOpinion]}</td>
                      <td>{h.count}</td>
                      <td>{hucreDurumu(h.systemVerdict, h.expertOpinion)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          {summary.reasons.length > 0 ? (
            <section style={{ marginTop: 24 }}>
              <h2>Ayrışma Sebepleri</h2>
              <p className="muted">
                Hangi düzeltmenin en çok işe yarayacağını gösterir. "Kural yanlış
                çıkarılmış" ile "ağırlık yanlış" ayrı işlerdir.
              </p>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Sebep</th>
                      <th>Vaka</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.reasons.map((s) => (
                      <tr key={s.reason}>
                        <td>{sebepAdlari[s.reason]}</td>
                        <td>{s.count}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          ) : null}

          <section style={{ marginTop: 24 }}>
            <h2>Skor Ayrım Gücü</h2>
            <p className="muted">
              Danışmanın "uygun" dediği vakaların ortalama skoru, "uygun değil"
              dediklerinden belirgin biçimde yüksek olmalıdır. İki ortalama birbirine
              yakınsa skor ayrım üretmiyor demektir.
            </p>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Danışmanın Kararı</th>
                    <th>Vaka</th>
                    <th>Ortalama Skor</th>
                    <th>En Düşük</th>
                    <th>En Yüksek</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.scoreBands.map((b) => (
                    <tr key={b.expertOpinion}>
                      <td>{verdictLabels[b.expertOpinion]}</td>
                      <td>{b.count}</td>
                      <td>{b.averageSystemScore.toFixed(1)}</td>
                      <td>{b.minSystemScore.toFixed(1)}</td>
                      <td>{b.maxSystemScore.toFixed(1)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}

      <section style={{ marginTop: 24 }}>
        <h2>
          Kural Çıkarım Kalitesi <HelpTip field="kuralDuzeltmeOrani" />
        </h2>
        <p className="muted">
          Çağrı metinlerinden çıkarılan kuralların ne kadarının elle düzeltildiği. Yüksek
          oran, metin çözümlemesinin iyileştirilmesi gerektiğini gösterir.
        </p>
        <div className="kpi-row">
          <Kpi label="Toplam Kural" value={String(ruleExtraction.totalRules)} />
          <Kpi label="Elle Düzeltilen" value={String(ruleExtraction.manuallyOverriddenRules)} />
          <Kpi label="Düzeltme Oranı" value={formatPercent(ruleExtraction.overrideRate)} />
        </div>
      </section>

      {kayitlar.data && kayitlar.data.length > 0 ? (
        <section style={{ marginTop: 24 }}>
          <h2>Kaydedilen Değerlendirmeler</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Çağrı</th>
                  <th>Sistem</th>
                  <th>Skor</th>
                  <th>Danışman</th>
                  <th>Sebep</th>
                  <th>Kaydeden</th>
                  <th>Tarih</th>
                </tr>
              </thead>
              <tbody>
                {kayitlar.data.map((k) => (
                  <tr key={k.id}>
                    <td>{k.opportunityTitle}</td>
                    <td>{verdictLabels[k.systemVerdict]}</td>
                    <td>{k.systemScore.toFixed(1)}</td>
                    <td>{verdictLabels[k.expertOpinion]}</td>
                    <td>{k.agrees ? '—' : sebepAdlari[k.disagreementReason]}</td>
                    <td>{k.recordedBy}</td>
                    <td>{formatDate(k.recordedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}
    </>
  )
}

/** Hücrenin ne anlama geldiği; ham enum adı kullanıcıya gösterilmez. */
function hucreDurumu(sistem: EligibilityVerdict, uzman: EligibilityVerdict): string {
  if (sistem === uzman) return 'Uyumlu'

  const pozitif = (v: EligibilityVerdict) =>
    v === 'Eligible' || v === 'ConditionallyEligible'

  if (pozitif(sistem) && uzman === 'NotEligible') return 'Yanlış pozitif'
  if (sistem === 'NotEligible' && pozitif(uzman)) return 'Yanlış negatif'

  return 'Farklı derece'
}

const sebepAdlari: Record<VerdictDisagreementReason, string> = {
  None: '—',
  RuleExtraction: 'Kural yanlış çıkarılmış',
  CompanyData: 'Firma verisi yanlış veya eski',
  ScoreWeighting: 'Ağırlıklar sonucu yanlış tarafa çekiyor',
  UnwrittenPractice: 'Metinde yazmayan kurum uygulaması',
  Other: 'Diğer',
}
