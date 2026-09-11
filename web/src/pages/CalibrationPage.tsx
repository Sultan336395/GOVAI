import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type {
  CalibrationSummary,
  EligibilityVerdict,
  VerdictDisagreementReason,
} from '@/api/types'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, InfoBox, Kpi, Loading } from '@/components/Common'
import { HelpTip } from '@/components/HelpTip'
import { formatDate, formatPercent, verdictLabels } from '@/lib/format'

/**
 * Karar Doğruluğu — kural motorunun kararlarının bağımsız ikinci görüşle karşılaştırılması.
 *
 * İkinci görüşü iki taraf verebilir ve ekran ikisini ASLA tek sayıda toplamaz:
 *
 *   * Yapay zekâ görüşü her gece kendiliğinden birikir ve TARAMA yapar: nereye bakılmalı.
 *   * Danışman görüşü seyrektir ama ağırlıkların doğruluğunu sınayabilecek TEK ölçüttür.
 *
 * Ağırlıkları modelin görüşüne göre ayarlamak, sistemi gerçeğe değil modelin eğilimine
 * kalibre etmek olurdu; üstelik iki taraf da aynı metni okuduğu için aynı yanlışı birlikte
 * yapabilirler. Ekran bu ayrımı başlıkta ve açıklamada açıkça söyler.
 */
export default function CalibrationPage() {
  const { selectedCompanyId } = useCompanies()
  const queryClient = useQueryClient()
  const [hata, setHata] = useState<unknown>(null)
  const [tur, setTur] = useState<string | null>(null)

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

  const ikinciGorus = useMutation({
    mutationFn: () => api.collectAiSecondOpinions(selectedCompanyId!),
    onSuccess: async (sonuc) => {
      setHata(null)

      // Anahtar yoksa bu bir arıza DEĞİLDİR: sistem kural tabanlı çalışmaya devam eder,
      // yalnızca ikinci görüş toplanmaz. Kullanıcıya bunu ayırt ettirmek gerekir.
      setTur(sonuc.aiEnabled
        ? `${sonuc.recordedCount} görüş alındı · ${sonuc.disagreedCount} ayrışma`
        : 'Yapay zekâ bağlantısı tanımlı değil; görüş toplanmadı.')

      await queryClient.invalidateQueries({ queryKey: ['calibration-report', selectedCompanyId] })
      await queryClient.invalidateQueries({ queryKey: ['expert-verdicts', selectedCompanyId] })
    },
    onError: (e) => setHata(e),
  })

  if (!selectedCompanyId) {
    return <EmptyState>Ölçümü görmek için önce bir firma seçin.</EmptyState>
  }

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Ölçüm bulunamadı.</EmptyState>

  const { human, ai, ruleExtraction } = data

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Karar Doğruluğu</h1>
          <p>
            Kural motorunun kararları, bağımsız bir ikinci görüşle karşılaştırılır. İkinci
            görüş hiçbir skoru değiştirmez; yalnızca sistemin nerede isabet ettiğini ve
            nereye bakılması gerektiğini gösterir.
          </p>
        </div>

        <button
          type="button"
          className="primary"
          onClick={() => ikinciGorus.mutate()}
          disabled={ikinciGorus.isPending}
        >
          {ikinciGorus.isPending ? 'Görüş alınıyor…' : 'Yapay Zekâdan Görüş Al'}
        </button>
      </div>

      {hata ? <ErrorBox error={hata} /> : null}
      {tur ? <InfoBox>{tur}</InfoBox> : null}

      <InfoBox>
        <div>
          <strong>Yapay zekâ görüşü</strong> her gece kendiliğinden toplanır ve{' '}
          <strong>tarama</strong> yapar: iki taraf ayrı yollardan aynı sonuca varıyorsa kayıt
          büyük olasılıkla doğrudur, ayrılıyorsa insan bakmalıdır.
        </div>
        <div>
          <strong>Danışman görüşü</strong> seyrektir ama ağırlıkların doğruluğunu
          sınayabilecek tek ölçüttür. Ağırlıkları modelin görüşüne göre ayarlamak, sistemi
          gerçeğe değil modelin eğilimine kalibre etmek olurdu.
        </div>
      </InfoBox>

      <Olcum
        baslik="Danışman Görüşüne Karşı Ölçüm"
        aciklama="Ağırlık kalibrasyonunun tek geçerli ölçütü budur."
        bos="Henüz danışman değerlendirmesi kaydedilmemiş."
        ozet={human}
      />

      <Olcum
        baslik="Yapay Zekâ Görüşüne Karşı Ölçüm"
        aciklama={
          'Tarama sinyalidir: nereye bakılacağını gösterir, ağırlık değişikliğine gerekçe '
          + 'olamaz. İki taraf da aynı metni okur ve aynı yanlışı birlikte yapabilirler.'
        }
        bos="Henüz yapay zekâ görüşü toplanmamış. Gece turu bunu kendiliğinden yapar."
        ozet={ai}
      />

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
          <h2>Kaydedilen Görüşler</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Çağrı</th>
                  <th>Görüşü Veren</th>
                  <th>Sistem</th>
                  <th>Skor</th>
                  <th>İkinci Görüş</th>
                  <th>Durum</th>
                  <th>Tarih</th>
                </tr>
              </thead>
              <tbody>
                {kayitlar.data.map((k) => (
                  <tr key={k.id}>
                    <td>{k.opportunityTitle}</td>
                    <td>
                      {k.source === 'Ai' ? 'Yapay zekâ' : 'Danışman'}
                      {k.reviewerModel ? <><br /><small>{k.reviewerModel}</small></> : null}
                    </td>
                    <td>{verdictLabels[k.systemVerdict]}</td>
                    <td>{k.systemScore.toFixed(1)}</td>
                    <td>{verdictLabels[k.expertOpinion]}</td>
                    <td>{k.agrees ? 'Uyumlu' : 'Ayrışma'}</td>
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

/** Tek bir kaynağın ölçümü. İki ölçüm aynı bileşenle ama AYRI bölümlerde gösterilir. */
function Olcum({
  baslik,
  aciklama,
  bos,
  ozet,
}: {
  baslik: string
  aciklama: string
  bos: string
  ozet: CalibrationSummary
}) {
  return (
    <section style={{ marginTop: 24 }}>
      <h2>{baslik}</h2>
      <p className="muted">{aciklama}</p>

      {ozet.totalVerdicts === 0 ? (
        <EmptyState>{bos}</EmptyState>
      ) : (
        <>
          {/*
            Örneklem uyarısı ölçümün ÜSTÜNDE durur. Az sayıda vakayla hesaplanan bir oran
            istatistik değil gürültüdür; bu sayıya bakarak ağırlık değiştirmek modeli bozar.
          */}
          {!ozet.isSampleSufficient ? (
            <InfoBox>
              Ölçüm {ozet.totalVerdicts} vakaya dayanıyor. Oranları yorumlamak için bu sayı
              henüz yeterli değil; değerler eğilim olarak okunmalı, ağırlık değişikliğine
              gerekçe yapılmamalıdır.
            </InfoBox>
          ) : null}

          <div className="kpi-row">
            <Kpi
              label="Uyum Oranı"
              value={formatPercent(ozet.agreementRate)}
              hint={`${ozet.agreementCount} / ${ozet.totalVerdicts} vaka`}
            />
            <Kpi
              label="Yanlış Pozitif"
              value={String(ozet.falsePositiveCount)}
              hint={formatPercent(ozet.falsePositiveRate)}
            />
            <Kpi
              label="Yanlış Negatif"
              value={String(ozet.falseNegativeCount)}
              hint={formatPercent(ozet.falseNegativeRate)}
            />
            <Kpi
              label="Eksik Veriden Ayrışma"
              value={String(ozet.dataGapDisagreementCount)}
              hint="Model değil, veri sorunu"
            />
          </div>

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Sistemin Kararı</th>
                  <th>İkinci Görüş</th>
                  <th>Vaka</th>
                  <th>Durum</th>
                </tr>
              </thead>
              <tbody>
                {ozet.matrix.map((h) => (
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

          {ozet.scoreBands.length > 0 ? (
            <>
              <h3 style={{ marginTop: 16 }}>Skor Ayrım Gücü</h3>
              <p className="muted">
                İkinci görüşün "uygun" dediği vakaların ortalama skoru, "uygun değil"
                dediklerinden belirgin biçimde yüksek olmalıdır. İki ortalama birbirine
                yakınsa skor ayrım üretmiyor demektir.
              </p>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>İkinci Görüş</th>
                      <th>Vaka</th>
                      <th>Ortalama Skor</th>
                      <th>En Düşük</th>
                      <th>En Yüksek</th>
                    </tr>
                  </thead>
                  <tbody>
                    {ozet.scoreBands.map((b) => (
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
            </>
          ) : null}

          {ozet.reasons.length > 0 ? (
            <>
              <h3 style={{ marginTop: 16 }}>Ayrışma Sebepleri</h3>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Sebep</th>
                      <th>Vaka</th>
                    </tr>
                  </thead>
                  <tbody>
                    {ozet.reasons.map((s) => (
                      <tr key={s.reason}>
                        <td>{sebepAdlari[s.reason]}</td>
                        <td>{s.count}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          ) : null}
        </>
      )}
    </section>
  )
}

/** Hücrenin ne anlama geldiği; ham enum adı kullanıcıya gösterilmez. */
function hucreDurumu(sistem: EligibilityVerdict, ikinci: EligibilityVerdict): string {
  if (sistem === ikinci) return 'Uyumlu'

  const pozitif = (v: EligibilityVerdict) => v === 'Eligible' || v === 'ConditionallyEligible'

  if (pozitif(sistem) && ikinci === 'NotEligible') return 'Yanlış pozitif'
  if (sistem === 'NotEligible' && pozitif(ikinci)) return 'Yanlış negatif'

  return 'Farklı derece'
}

const sebepAdlari: Record<VerdictDisagreementReason, string> = {
  None: '—',
  RuleExtraction: 'Kural yanlış çıkarılmış',
  CompanyData: 'Firma verisi yanlış veya eski',
  ScoreWeighting: 'Ağırlıklar sonucu yanlış tarafa çekiyor',
  UnwrittenPractice: 'Metinde yazmayan kurum uygulaması',
  Other: 'Gerekçe kayıtta yazılı',
}
