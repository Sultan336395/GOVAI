import { useState } from 'react'
import type {
  AnalysisConfidence,
  AnalysisContribution,
  AnalysisVersion,
  CriterionResult,
  ExplainableScore,
  OpportunityAnalysis,
  RegulationImpactAnalysis,
} from '@/api/types'
import { EmptyState } from '@/components/Common'
import { formatDate, formatPercent } from '@/lib/format'

/**
 * DeepTech analiz sonucu paneli (Faz 3).
 *
 * Ekranın tek bir işi var: kullanıcının **"bu sonuç neden çıktı?"** sorusunu tıklama
 * gerektirmeden cevaplayabilmesi. Bu yüzden sayı asla tek başına durmaz — yanında
 * kırılımı, kriter kriter gerekçesi ve resmî belgeden alıntısı bulunur.
 *
 * Üç kural bilinçlidir:
 *
 * 1. Puan hiçbir yerde "kazanma ihtimali" olarak adlandırılmaz. Sistemde o tahmini
 *    yapacak geçmiş başvuru sonucu verisi yoktur; öyle yazmak kullanıcıyı yanıltır.
 * 2. Düşük güvenli analiz kesin sonuç gibi görünmez; sayının hemen yanında uyarı çıkar.
 * 3. Kural katkısı ile yapay zekâ katkısı ayrı gösterilir. Tek bir açıklama metni
 *    verilseydi kullanıcı hangi cümlenin deterministik kuraldan geldiğini ayırt edemezdi.
 */
export function OpportunityAnalysisPanel({ data }: { data: OpportunityAnalysis }) {
  return (
    <section className="card" style={{ marginBottom: 16 }} data-analiz="firsat">
      <Baslik
        baslik="Uygunluk analizi"
        durum={data.verdictLabel}
        confidence={data.confidence}
        contribution={data.contribution}
      />

      <PuanOzeti score={data.score} confidence={data.confidence} />
      <PuanKirilimi score={data.score} />

      <KriterListesi baslik="Sağlanan kriterler" kriterler={data.met} bos="Sağlanan kriter yok." />
      <KriterListesi
        baslik="Sağlanmayan kriterler"
        kriterler={data.notMet}
        bos="Sağlanmayan kriter yok."
      />
      <KriterListesi
        baslik="Eksik bilgiler"
        kriterler={data.missing}
        bos="Karara bağlanamayan bilgi yok."
      />
      <KriterListesi
        baslik="Çelişkili kanıtlar"
        kriterler={data.conflicting}
        bos="Resmî belgede çelişki bulunamadı."
      />

      <YapayZekaKatkisi contribution={data.contribution} />
      <SurumKunyesi version={data.version} evaluatedAt={data.evaluatedAt} />
    </section>
  )
}

/**
 * Mevzuat etki paneli. Fırsat panelinden iki farkı var: puan yoktur (mevzuata
 * başvurulmaz, uyulur) ve her sonucun yanında hukuki uyarı durur.
 */
export function RegulationImpactPanel({ data }: { data: RegulationImpactAnalysis }) {
  return (
    <section className="card" style={{ marginBottom: 16 }} data-analiz="mevzuat">
      <Baslik
        baslik="Olası etki analizi"
        durum={data.impactLabel}
        confidence={data.confidence}
        contribution={data.contribution}
      />

      <p className="muted" style={{ fontSize: 12, marginTop: 0 }}>{data.legalDisclaimer}</p>

      <KriterListesi
        baslik="Değerlendirilen kriterler"
        kriterler={data.criteria.filter((k) => k.outcome !== 'NotApplicable')}
        bos="Belgeden değerlendirilebilir bir kriter çıkarılamadı."
      />

      {data.openQuestions.length > 0 ? (
        <>
          <h3 style={{ marginBottom: 6 }}>Doğrulanması gereken noktalar</h3>
          <ul style={{ marginTop: 0 }}>
            {data.openQuestions.map((soru) => (
              <li key={soru}>{soru}</li>
            ))}
          </ul>
        </>
      ) : null}

      <YapayZekaKatkisi contribution={data.contribution} />
      <SurumKunyesi version={data.version} evaluatedAt={data.evaluatedAt} />
    </section>
  )
}

function Baslik({
  baslik,
  durum,
  confidence,
  contribution,
}: {
  baslik: string
  durum: string
  confidence: AnalysisConfidence
  contribution: AnalysisContribution
}) {
  return (
    <>
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          alignItems: 'baseline',
          justifyContent: 'space-between',
        }}
      >
        <h2 style={{ margin: 0 }}>{baslik}</h2>
        <div style={{ textAlign: 'right' }}>
          <strong>{durum}</strong>
          <div className="muted" style={{ fontSize: 12 }} data-alan="guven-ozeti">
            {/* Başlık sunucudan gelir: model çalışmadıysa "Kural tabanlı güven" yazar.
                "Yapay zekâ destekli güven" yazmak, model hiç çalışmamışken onun da doğruladığı
                izlenimini verir — kullanıcının alabileceği en yanıltıcı mesaj. */}
            {confidence.title}: {confidence.levelLabel} ({formatPercent(confidence.value)})
          </div>
          <div className="muted" style={{ fontSize: 12 }} data-alan="analiz-turu">
            {contribution.modeLabel} · Yapay zekâ güveni: {contribution.aiConfidenceLabel}
          </div>
        </div>
      </div>

      {contribution.warning ? (
        <p
          className="muted"
          data-uyari="analiz"
          style={{ marginTop: 8, marginBottom: 0, borderLeft: '3px solid #b45309', paddingLeft: 8 }}
        >
          {contribution.warning}
        </p>
      ) : null}
    </>
  )
}

function PuanOzeti({
  score,
  confidence,
}: {
  score: ExplainableScore
  confidence: AnalysisConfidence
}) {
  return (
    <div style={{ marginTop: 12, marginBottom: 12 }} data-alan="puan-ozeti">
      <div className="muted" style={{ fontSize: 12 }}>
        {score.label}
      </div>
      <div style={{ fontSize: 34, fontWeight: 700 }}>{score.value.toFixed(1)}</div>

      {score.hasMandatoryFailure ? (
        <div className="muted">
          Zorunlu bir koşul sağlanmadığı için puan sıfırlandı; başvuru bu hâliyle yapılamaz.
        </div>
      ) : null}

      {score.missingDataEffect > 0 ? (
        <div className="muted" style={{ fontSize: 12 }}>
          Eksik bilgi tamamlanırsa puan en fazla {score.missingDataEffect.toFixed(1)} artabilir.
        </div>
      ) : null}

      <div className="muted" style={{ fontSize: 12 }}>
        Güven seviyesi ayrı hesaplanır: {confidence.levelLabel}. Puan firmanın koşulları ne
        ölçüde karşıladığını gösterir, başvurunun kabul edileceğini değil.
      </div>
    </div>
  )
}

function PuanKirilimi({ score }: { score: ExplainableScore }) {
  return (
    <>
      <h3 style={{ marginBottom: 6 }}>Puan kırılımı</h3>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Başlık</th>
              <th>Puan</th>
              <th>Ağırlık</th>
              <th>Katkı</th>
              <th>Gerekçe</th>
            </tr>
          </thead>
          <tbody>
            {score.components.map((bilesen) => (
              <tr key={bilesen.group} data-kirilim={bilesen.group}>
                <td>{bilesen.name}</td>
                <td>{formatPercent(bilesen.value)}</td>
                <td>{formatPercent(bilesen.weight)}</td>
                <td>{(bilesen.contribution * 100).toFixed(1)}</td>
                <td className="muted" style={{ fontSize: 12 }}>
                  {bilesen.rationale}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}

function KriterListesi({
  baslik,
  kriterler,
  bos,
}: {
  baslik: string
  kriterler: CriterionResult[]
  bos: string
}) {
  if (kriterler.length === 0) {
    return (
      <>
        <h3 style={{ marginBottom: 6 }}>{baslik}</h3>
        <EmptyState>{bos}</EmptyState>
      </>
    )
  }

  return (
    <>
      <h3 style={{ marginBottom: 6 }}>{baslik}</h3>
      <div>
        {kriterler.map((kriter) => (
          <KriterSatiri key={kriter.code} kriter={kriter} />
        ))}
      </div>
    </>
  )
}

/**
 * Tek bir kriter. "Bu sonuç neden çıktı?" düğmesi gerekçeyi, kullanılan firma
 * alanlarını ve resmî belgeden alıntıyı açar; kapalıyken ekranı boğmaz.
 */
function KriterSatiri({ kriter }: { kriter: CriterionResult }) {
  const [acik, setAcik] = useState(false)

  return (
    <div style={{ marginBottom: 10 }} data-kriter={kriter.code}>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'baseline' }}>
        <strong>{kriter.name}</strong>
        <span className="muted" style={{ fontSize: 12 }}>
          {kriter.outcomeLabel}
          {kriter.isMandatory ? ' · zorunlu' : ''}
        </span>
        <button
          type="button"
          className="inline-link"
          onClick={() => setAcik((v) => !v)}
          aria-expanded={acik}
        >
          Bu sonuç neden çıktı?
        </button>
      </div>

      {acik ? (
        <div className="muted" style={{ fontSize: 12, marginTop: 4 }} data-gerekce={kriter.code}>
          <div>{kriter.rationale}</div>

          {kriter.missingOrConflictExplanation ? (
            <div style={{ marginTop: 4 }}>{kriter.missingOrConflictExplanation}</div>
          ) : null}

          {kriter.companyFields.length > 0 ? (
            <div style={{ marginTop: 4 }}>
              Kullanılan firma alanları: {kriter.companyFields.join(', ')}
            </div>
          ) : null}

          {kriter.evidence.length > 0 ? (
            <ul style={{ marginTop: 4, marginBottom: 0 }}>
              {kriter.evidence.map((kanit, index) => (
                <li key={`${kriter.code}-${index}`}>
                  “{kanit.excerpt}”
                  {kanit.locator ? <span> ({kanit.locator})</span> : null}
                </li>
              ))}
            </ul>
          ) : (
            <div style={{ marginTop: 4 }}>Bu kriter için belgeden alıntı çıkarılamadı.</div>
          )}
        </div>
      ) : null}
    </div>
  )
}

/**
 * Yapay zekâ katkısı. Model bağlı değilken bu blok "kural tabanlı" olduğunu söyler;
 * sistem hiçbir koşulda "hibrit çalışıyor" izlenimi vermez.
 */
function YapayZekaKatkisi({ contribution }: { contribution: AnalysisContribution }) {
  return (
    <div style={{ marginTop: 12 }} data-alan="yapay-zeka-katkisi">
      <h3 style={{ marginBottom: 6 }}>Yapay zekâ katkısı</h3>

      {contribution.hasAiContribution ? (
        <ul style={{ marginTop: 0 }}>
          {contribution.aiExplanations.map((aciklama) => (
            <li key={aciklama}>{aciklama}</li>
          ))}
        </ul>
      ) : (
        <EmptyState>
          {contribution.aiStatusLabel}. Yukarıdaki sonucun tamamı resmî belgedeki kurallardan
          hesaplandı.
        </EmptyState>
      )}

      {contribution.rejectedClaimCount > 0 ? (
        <div className="muted" style={{ fontSize: 12 }}>
          Kanıtla doğrulanamayan {contribution.rejectedClaimCount} yapay zekâ ifadesi elendi ve
          size gösterilmedi.
        </div>
      ) : null}

      {contribution.conflicts.map((celiski) => (
        <div key={celiski.criterionCode} className="muted" style={{ fontSize: 12 }}>
          {celiski.criterionCode}: {celiski.note}
        </div>
      ))}
    </div>
  )
}

/**
 * Analizin künyesi.
 *
 * Eskiden burada kural seti, firma profili ve mali veri sürüm numaraları ile modelin
 * teknik adı yazıyordu. Bunlar sistemin iç muhasebesidir; danışman için tek anlamlı
 * bilgi analizin NE ZAMAN ve NEYE GÖRE yapıldığıdır. Sürüm bilgisi kaydedilmeye devam
 * eder — yalnızca ekranda gösterilmez.
 */
function SurumKunyesi({
  version,
  evaluatedAt,
}: {
  version: AnalysisVersion
  evaluatedAt: string
}) {
  return (
    <div className="muted" style={{ fontSize: 12, marginTop: 12 }} data-alan="surum-kunyesi">
      {formatDate(evaluatedAt)} tarihinde, firmanın o günkü bilgileri ve çağrının resmî
      belgesi kullanılarak hazırlandı.
      {version.modelName
        ? ' Yapay zekâ açıklaması eklendi.'
        : ' Yalnızca kural tabanlı değerlendirme yapıldı.'}
    </div>
  )
}
