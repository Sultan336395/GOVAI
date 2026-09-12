import { Link } from 'react-router-dom'
import type { OpportunityMatch } from '@/api/types'
import { EmptyState, ScoreCell, SectorFitBadge, VerdictBadge } from '@/components/Common'
import { formatCurrency, formatDeadline } from '@/lib/format'

/** Kırılıma özel ek sütun: satırın karttaki sayıya katkısını gösterir. */
export interface VurguSutunu {
  baslik: string
  deger: (m: OpportunityMatch) => number
}

/**
 * Fırsat eşleşmelerinin ortak tablosu.
 *
 * Panoda üç yerde kullanılır (öncelikli fırsatlar, son başvurusu yaklaşanlar, özet
 * kartı kırılımı). Tek bir yerde durması, sütun eklendiğinde üç listenin ayrışmasını
 * önler.
 */
export default function MatchTable({
  matches,
  vurgu,
  bosMesaj,
}: {
  matches: OpportunityMatch[]
  vurgu?: VurguSutunu
  bosMesaj?: string
}) {
  if (matches.length === 0) {
    return (
      <EmptyState>
        {bosMesaj ?? 'Henüz eşleşme yok. "Yeniden skorla" ile hesaplama başlatabilirsiniz.'}
      </EmptyState>
    )
  }

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Fırsat</th>
            <th>Sektör Uyumu</th>
            <th>Skor</th>
            <th>Karar</th>
            <th>Son Başvuru</th>
            <th>Azami Tutar</th>
            <th>Eksikler</th>
            {vurgu ? <th>{vurgu.baslik}</th> : null}
          </tr>
        </thead>
        <tbody>
          {matches.map((match) => (
            <tr key={match.assessmentId}>
              <td>
                <Link to={`/matches/${match.assessmentId}`}>{match.opportunityTitle}</Link>
                <div className="muted" style={{ fontSize: 12 }}>
                  {match.publisher}
                </div>
              </td>
              <td>
                <SectorFitBadge fit={match.sectorFit} />
              </td>
              <td>
                <ScoreCell score={match.finalScore} />
              </td>
              <td>
                <VerdictBadge verdict={match.verdict} />
              </td>
              <td>{formatDeadline(match.daysUntilDeadline)}</td>
              <td>{formatCurrency(match.maxAmount)}</td>
              <td>
                {match.missingConditionCount} koşul
                <div className="muted" style={{ fontSize: 12 }}>
                  {match.missingMandatoryDocumentCount} belge
                </div>
              </td>
              {vurgu ? (
                <td>
                  <strong>{vurgu.deger(match)}</strong>
                </td>
              ) : null}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
