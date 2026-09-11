import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useCompanies } from '@/app/contexts'
import type {
  TenderOutcome, TenderPursuit, TenderPursuitStatus,
} from '@/api/types'
import {
  EmptyState, ErrorBox, InfoBox, Kpi, Loading, ScoreCell, SectorFitBadge, VerdictBadge,
} from '@/components/Common'
import { formatDate, formatDeadline } from '@/lib/format'

/**
 * GOVAI Tender — ihale başvuru takip ekranı.
 *
 * Firma ilgilendiği ihaleleri işaretler ve başvuru sürecinin hangi aşamada olduğunu
 * yazar. Bu kayıt firmanın <b>kendi beyanıdır</b>: skoru, kararı ve sıralamayı
 * etkilemez. Ekranda sistemin kendi değerlendirmesi ayrıca gösterilir — amaç tersidir,
 * hazırlığa alınan bir ihalede "sektör uyumu doğrulanamadı" uyarısını zamanında görmek.
 */

const ASAMALAR: { value: TenderPursuitStatus; label: string }[] = [
  { value: 'Inceleniyor', label: 'İnceleniyor' },
  { value: 'Hazirlaniyor', label: 'Hazırlanıyor' },
  { value: 'TeklifVerildi', label: 'Teklif verildi' },
  { value: 'Sonuclandi', label: 'Sonuçlandı' },
  { value: 'Vazgecildi', label: 'Vazgeçildi' },
]

const SONUCLAR: { value: TenderOutcome; label: string }[] = [
  { value: 'Kazanildi', label: 'Kazanıldı' },
  { value: 'Kaybedildi', label: 'Kaybedildi' },
  { value: 'Iptal', label: 'İptal edildi' },
]

type Gorunum = 'acik' | 'kapali' | 'tumu'

export default function TenderBoardPage() {
  const { selectedCompanyId } = useCompanies()
  const queryClient = useQueryClient()

  const [gorunum, setGorunum] = useState<Gorunum>('acik')
  const [acikSatir, setAcikSatir] = useState<string | null>(null)
  const [hata, setHata] = useState<unknown>(null)

  const tahta = useQuery({
    queryKey: ['tender-board', selectedCompanyId],
    queryFn: () => api.getTenderBoard(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  // Takibe alınabilecek ihaleler: açık, ihale kategorisinde ve henüz takipte olmayanlar.
  const adaylar = useQuery({
    queryKey: ['tender-candidates'],
    queryFn: () => api.searchOpportunities({
      categories: ['Tender'],
      onlyOpen: true,
      pageSize: 50,
    }),
    enabled: Boolean(selectedCompanyId),
  })

  const takibeAl = useMutation({
    mutationFn: (opportunityId: string) =>
      api.startTenderPursuit(selectedCompanyId!, opportunityId),
    onSuccess: async () => {
      setHata(null)
      await queryClient.invalidateQueries({ queryKey: ['tender-board', selectedCompanyId] })
    },
    onError: (e) => setHata(e),
  })

  const takipteki = useMemo(
    () => new Set((tahta.data?.pursuits ?? []).map((p) => p.opportunityId)),
    [tahta.data],
  )

  if (!selectedCompanyId) {
    return <EmptyState>İhale takibini görmek için önce bir firma seçin.</EmptyState>
  }

  if (tahta.isLoading) return <Loading />
  if (tahta.error) return <ErrorBox error={tahta.error} />
  if (!tahta.data) return <EmptyState>Takip tahtası alınamadı.</EmptyState>

  const { pursuits, counts } = tahta.data

  const listelenen = pursuits.filter((p) =>
    gorunum === 'tumu' ? true : gorunum === 'acik' ? !p.isClosed : p.isClosed)

  const acilabilir = (adaylar.data?.items ?? []).filter((o) => !takipteki.has(o.id))

  return (
    <>
      <div className="page-header">
        <div>
          <h1>İhale Takibi</h1>
          <p>
            İlgilendiğiniz ihaleleri işaretleyin ve başvuru sürecini aşama aşama izleyin.
          </p>
        </div>
      </div>

      {hata ? <ErrorBox error={hata} /> : null}

      <div className="kpi-row">
        <Kpi label="İnceleniyor" value={String(counts.inceleniyor)} />
        <Kpi label="Hazırlanıyor" value={String(counts.hazirlaniyor)} />
        <Kpi label="Teklif Verildi" value={String(counts.teklifVerildi)} />
        <Kpi label="Kazanıldı" value={String(counts.kazanildi)} />
        <Kpi label="Kaybedildi" value={String(counts.kaybedildi)} />
      </div>

      {/*
        Süresi geçmiş ama kapatılmamış takipler ayrıca uyarılır: liste içinde bir
        satır olarak kalsalardı, üzerine hâlâ iş planlanabilirdi.
      */}
      {counts.suresiGecen > 0 ? (
        <InfoBox>
          {counts.suresiGecen} takipte son başvuru tarihi geçtiği hâlde süreç kapatılmamış.
          Sonucu yazmak, kazanılan ile kaybedilen ihaleyi ayırt edilebilir kılar.
        </InfoBox>
      ) : null}

      <div className="chip-row" style={{ marginTop: 16 }}>
        {([
          ['acik', 'Açık takipler'],
          ['kapali', 'Kapananlar'],
          ['tumu', 'Tümü'],
        ] as [Gorunum, string][]).map(([deger, etiket]) => (
          <button
            key={deger}
            type="button"
            className={gorunum === deger ? 'primary' : undefined}
            onClick={() => setGorunum(deger)}
          >
            {etiket}
          </button>
        ))}
      </div>

      <section style={{ marginTop: 16 }}>
        {listelenen.length === 0 ? (
          <EmptyState>
            {gorunum === 'acik'
              ? 'Şu an açık bir ihale takibiniz yok.'
              : gorunum === 'kapali'
                ? 'Kapanmış takip yok.'
                : 'Henüz ihale takibe alınmadı.'}
          </EmptyState>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>İhale</th>
                  <th>Son Başvuru</th>
                  <th>Aşama</th>
                  <th>Sistem Kararı</th>
                  <th>Sorumlu</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {listelenen.map((p) => (
                  <Satir
                    key={p.id}
                    takip={p}
                    acik={acikSatir === p.id}
                    onAc={() => setAcikSatir(acikSatir === p.id ? null : p.id)}
                    onHata={setHata}
                    companyId={selectedCompanyId}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section style={{ marginTop: 28 }}>
        <h2>Takibe Alınabilecek İhaleler</h2>

        {adaylar.isLoading ? (
          <Loading />
        ) : acilabilir.length === 0 ? (
          <EmptyState>
            Şu an takibe alınabilecek açık ihale görünmüyor. Katalogdaki her ihale
            takibe alınabilir.
          </EmptyState>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>İhale</th>
                  <th>Kurum</th>
                  <th>Son Başvuru</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {acilabilir.map((o) => (
                  <tr key={o.id}>
                    <td><Link to={`/opportunities/${o.id}`}>{o.title}</Link></td>
                    <td>{o.publisher}</td>
                    <td>{formatDeadline(o.daysUntilDeadline)}</td>
                    <td>
                      <button
                        type="button"
                        disabled={takibeAl.isPending}
                        onClick={() => takibeAl.mutate(o.id)}
                      >
                        Takibe al
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  )
}

function Satir({
  takip, acik, onAc, onHata, companyId,
}: {
  takip: TenderPursuit
  acik: boolean
  onAc: () => void
  onHata: (e: unknown) => void
  companyId: string
}) {
  return (
    <>
      <tr>
        <td>
          <Link to={`/opportunities/${takip.opportunityId}`}>{takip.title}</Link>
          <br />
          <small>{takip.publisher}</small>
        </td>
        <td>
          {takip.deadline ? formatDate(takip.deadline) : '—'}
          <br />
          <small className={takip.isOverdue ? 'field-warning' : 'muted'}>
            {takip.isOverdue ? 'Süre doldu' : formatDeadline(takip.daysToDeadline ?? null)}
          </small>
        </td>
        <td>
          {takip.statusLabel}
          {takip.outcomeLabel ? <><br /><small>{takip.outcomeLabel}</small></> : null}
        </td>
        <td>
          {/*
            Sistem kararı takipten etkilenmez; burada yalnızca gösterilir. Hiç
            değerlendirilmemiş bir ihalede uydurma bir puan yazmak yerine sebebi yazılır.
          */}
          {takip.assessment ? (
            <>
              <ScoreCell score={takip.assessment.score} />
              {' '}
              <VerdictBadge verdict={takip.assessment.verdict} />
              <br />
              <SectorFitBadge fit={takip.assessment.sectorFit} />
            </>
          ) : (
            <small className="muted">Henüz değerlendirilmedi</small>
          )}
        </td>
        <td>{takip.owner ?? '—'}</td>
        <td>
          <button type="button" className="inline-link" onClick={onAc}>
            {acik ? 'Kapat' : 'Aşamayı güncelle'}
          </button>
        </td>
      </tr>

      {acik ? (
        <tr>
          <td colSpan={6}>
            <AsamaPaneli takip={takip} onHata={onHata} companyId={companyId} />
          </td>
        </tr>
      ) : null}
    </>
  )
}

function AsamaPaneli({
  takip, onHata, companyId,
}: {
  takip: TenderPursuit
  onHata: (e: unknown) => void
  companyId: string
}) {
  const queryClient = useQueryClient()

  const [asama, setAsama] = useState<TenderPursuitStatus>(takip.status)
  const [sonuc, setSonuc] = useState<TenderOutcome | ''>(takip.outcome ?? '')
  const [not, setNot] = useState(takip.note ?? '')

  const kaydet = useMutation({
    mutationFn: () => api.changeTenderStatus(
      takip.id,
      asama,
      asama === 'Sonuclandi' ? (sonuc === '' ? null : sonuc) : null,
      not,
    ),
    onSuccess: async () => {
      onHata(null)
      await queryClient.invalidateQueries({ queryKey: ['tender-board', companyId] })
    },
    onError: (e) => onHata(e),
  })

  // Sonuç seçilmeden kaydet düğmesi kapalıdır; sunucu da aynı kuralı uygular.
  const eksikSonuc = asama === 'Sonuclandi' && sonuc === ''

  return (
    <div className="asama-paneli">
      <div className="asama-alanlar">
        <label>
          Aşama
          <select
            value={asama}
            onChange={(e) => setAsama(e.target.value as TenderPursuitStatus)}
          >
            {ASAMALAR.map((a) => (
              <option key={a.value} value={a.value}>{a.label}</option>
            ))}
          </select>
        </label>

        {asama === 'Sonuclandi' ? (
          <label>
            Sonuç
            <select
              value={sonuc}
              onChange={(e) => setSonuc(e.target.value as TenderOutcome | '')}
            >
              <option value="">Seçiniz</option>
              {SONUCLAR.map((s) => (
                <option key={s.value} value={s.value}>{s.label}</option>
              ))}
            </select>
          </label>
        ) : null}

        <label style={{ flex: 1, minWidth: 240 }}>
          Not
          <input
            type="text"
            value={not}
            maxLength={2000}
            placeholder="Örn. teminat mektubu hazırlanıyor"
            onChange={(e) => setNot(e.target.value)}
          />
        </label>

        <button
          type="button"
          className="primary"
          disabled={kaydet.isPending || eksikSonuc}
          onClick={() => kaydet.mutate()}
        >
          Kaydet
        </button>
      </div>

      {eksikSonuc ? (
        <p className="field-warning">
          Sonuçlanan ihalede sonucun yazılması zorunludur.
        </p>
      ) : null}

      <h4 style={{ fontSize: 13, marginBottom: 4 }}>Süreç geçmişi</h4>

      <ul className="asama-gecmis">
        {takip.history.map((olay, sira) => (
          <li key={sira}>
            <strong>{olay.toStatusLabel}</strong>
            {olay.outcomeLabel ? ` · ${olay.outcomeLabel}` : ''}
            {olay.fromStatusLabel ? ` (önce: ${olay.fromStatusLabel})` : ''}
            {' — '}
            {formatDate(olay.at)} · {olay.by}
            {olay.note ? <><br /><small>{olay.note}</small></> : null}
          </li>
        ))}
      </ul>
    </div>
  )
}
