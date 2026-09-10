import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type {
  ReportDeadlineItem,
  ReportOpportunityItem,
  ReportRegulatoryItem,
  ReportRiskItem,
  ReportTodoItem,
} from '@/api/types'
import {
  EmptyState, ErrorBox, InfoBox, Kpi, Loading, ScoreCell, SectorFitBadge, VerdictBadge,
} from '@/components/Common'
import { formatDate, formatDeadline } from '@/lib/format'

/**
 * Tek bir haftalık raporun tamamı.
 *
 * Rapor kaydedildiği gibi gösterilir; burada hiçbir şey yeniden hesaplanmaz. Boş
 * bölüm atlanmaz — raporun kendi notu o bölümün neden boş olduğunu söyler, çünkü
 * görünmeyen bir bölüm "bu konu hiç incelenmedi" izlenimi verir.
 */
export default function WeeklyReportPage() {
  const { reportId } = useParams<{ reportId: string }>()

  const { data, isLoading, error } = useQuery({
    queryKey: ['weekly-report', reportId],
    queryFn: () => api.getWeeklyReport(reportId!),
    enabled: Boolean(reportId),
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Rapor bulunamadı.</EmptyState>

  const {
    header, supports, technologyTenders, otherOpportunities,
    regulatoryChanges, risks, pastPeriodGaps, deadlines, todos, notes,
  } = data.content

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Haftalık Rapor</h1>
          <p>
            {header.companyName} · {formatDate(header.periodStart)} – {formatDate(header.periodEnd)}
            {' · '}
            {data.trigger === 'Scheduled' ? 'Otomatik oluşturuldu' : 'Elle oluşturuldu'}
          </p>
        </div>

        <div>
          <a className="button" href={api.weeklyReportExportUrl(data.id, 'pdf')}>PDF İndir</a>
          {' '}
          <a className="button" href={api.weeklyReportExportUrl(data.id, 'excel')}>Excel İndir</a>
        </div>
      </div>

      <div className="kpi-row">
        <Kpi label="Değerlendirilen Çağrı" value={String(header.evaluatedOpportunityCount)} />
        <Kpi label="Uygun Bulunan" value={String(header.eligibleCount)} />
        <Kpi label="Kararı Belirsiz" value={String(header.undeterminedCount)} />
      </div>

      {/*
        Notlar tabloların ÜSTÜNDE durur. İçlerinde "şu kadar değerlendirmenin ayrıntısı
        okunamadı" gibi raporun kapsamını sınırlayan uyarılar olabilir; bunu en alta
        koymak okuyucunun tabloları eksiksiz sanarak karar vermesine yol açar.
      */}
      {notes.length > 0 ? (
        <InfoBox>
          {notes.map((not) => (
            <div key={not}>{not}</div>
          ))}
        </InfoBox>
      ) : null}

      <Bolum baslik="Öncelikli Yapılacaklar" bos="Bu dönem için açık bir iş çıkmadı.">
        {todos.length > 0 ? <TodoTablosu satirlar={todos} /> : null}
      </Bolum>

      <Bolum baslik="Riskler ve Zorunlu Aksiyonlar" bos="Başvuruyu engelleyen bir eksik görünmüyor.">
        {risks.length > 0 ? <RiskTablosu satirlar={risks} /> : null}
      </Bolum>

      {/*
        Geçmiş dönem eksikleri güncel risklerin ARDINDAN gelir ve başlığı bunun bir iş
        listesi olmadığını söyler. İkisini aynı görünümde vermek, okuyucunun
        başvurulamayacak bir çağrı için iş planlamasına yol açardı.
      */}
      <Bolum
        baslik="Geçmiş Dönem Eksikleri"
        bos="Başvuru süresi geçmiş çağrılardan kalan bir eksik yok."
      >
        {pastPeriodGaps.length > 0 ? (
          <>
            <p className="muted">
              Bu çağrıların başvuru süresi geçti; şimdi yapılacak bir iş yoktur. Eksikler
              yine de geçerli: aynı koşulu isteyen yeni bir çağrı açıldığında güncel
              risk listesine geçerler.
            </p>
            <GecmisEksikTablosu satirlar={pastPeriodGaps} />
          </>
        ) : null}
      </Bolum>

      <Bolum baslik="En Uygun Fon, Hibe ve Teşvikler" bos="Bu dönemde uygun açık destek bulunamadı.">
        {supports.length > 0 ? <CagriTablosu satirlar={supports} /> : null}
      </Bolum>

      <Bolum baslik="Teknoloji ve Yazılım İhaleleri" bos="Bu dönemde teknoloji konulu açık ihale bulunamadı.">
        {technologyTenders.length > 0 ? <CagriTablosu satirlar={technologyTenders} /> : null}
      </Bolum>

      <Bolum
        baslik="Diğer Açık Çağrı ve İhaleler"
        bos="Destek ve teknoloji dışında açık başka bir çağrı bulunamadı."
      >
        {otherOpportunities.length > 0 ? <CagriTablosu satirlar={otherOpportunities} /> : null}
      </Bolum>

      <Bolum baslik="Son Başvuru Takvimi" bos="Yakın dönemde kapanan bir çağrı yok.">
        {deadlines.length > 0 ? <TakvimTablosu satirlar={deadlines} /> : null}
      </Bolum>

      <Bolum baslik="Mevzuat Değişiklikleri" bos="Bu hafta mevzuat değişikliği yayımlanmadı.">
        {regulatoryChanges.length > 0 ? <MevzuatTablosu satirlar={regulatoryChanges} /> : null}
      </Bolum>
    </>
  )
}

/** Boş bölüm gizlenmez; başlığı ve sebebi kalır. */
function Bolum({
  baslik,
  bos,
  children,
}: {
  baslik: string
  bos: string
  children: React.ReactNode
}) {
  return (
    <section style={{ marginTop: 24 }}>
      <h2>{baslik}</h2>
      {children ?? <EmptyState>{bos}</EmptyState>}
    </section>
  )
}

function CagriTablosu({ satirlar }: { satirlar: ReportOpportunityItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Çağrı</th>
            <th>Tür</th>
            <th>Skor</th>
            <th>Karar</th>
            <th>Son Başvuru</th>
            <th>Eksik Koşullar</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((s) => (
            <tr key={s.opportunityId}>
              <td>
                <Link to={`/opportunities/${s.opportunityId}`}>{s.title}</Link>
                <br />
                <small>{s.publisher} · {s.sectorFitLabel}</small>
              </td>
              <td>{s.categoryLabel}</td>
              <td><ScoreCell score={s.score} /></td>
              <td><VerdictBadge verdict={s.verdict} /></td>
              <td>{formatDeadline(s.daysToDeadline)}</td>
              <td>
                {s.missingConditions.length === 0
                  ? 'Eksik yok'
                  : s.missingConditions.join('; ')}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function RiskTablosu({ satirlar }: { satirlar: ReportRiskItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Cinsi</th>
            <th>Konu</th>
            <th>Durum</th>
            <th>Yapılması Gereken</th>
            <th>Etkilenen Çağrı</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((r) => (
            <tr key={`${r.kind}-${r.subject}`}>
              <td>{r.kindLabel}</td>
              <td>{r.subject}</td>
              <td>{r.description}</td>
              {/* Sistem öneri üretemediyse uydurulmaz. */}
              <td>{r.action ?? 'Danışman değerlendirmesi gerekiyor'}</td>
              <td>{r.affectedOpportunityCount}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * Geçmiş dönem eksikleri.
 *
 * Güncel risk tablosundan bilerek ayrı: sütun adları "yapılması gereken" değil
 * "gelecek çağrılar için" diyor, çünkü bu satırlar üzerine şimdi iş verilmez.
 */
function GecmisEksikTablosu({ satirlar }: { satirlar: ReportRiskItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Cinsi</th>
            <th>Konu</th>
            <th>Durum</th>
            <th>Gelecek Çağrılar İçin</th>
            <th>İlgili Kapanmış Çağrı</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((r) => (
            <tr key={`gecmis-${r.kind}-${r.subject}`}>
              <td>{r.kindLabel}</td>
              <td>{r.subject}</td>
              <td>{r.description}</td>
              {/* Sistem öneri üretemediyse uydurulmaz. */}
              <td>{r.action ?? 'Danışman değerlendirmesi gerekiyor'}</td>
              <td>{r.affectedOpportunityCount}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function TakvimTablosu({ satirlar }: { satirlar: ReportDeadlineItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Çağrı</th>
            <th>Son Başvuru</th>
            <th>Kalan</th>
            <th>Skor</th>
            <th>Karar</th>
            <th>Sektör Uyumu</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((d) => (
            <tr key={d.opportunityId}>
              <td><Link to={`/opportunities/${d.opportunityId}`}>{d.title}</Link></td>
              <td>{formatDate(d.deadline)}</td>
              <td>{formatDeadline(d.daysRemaining)}</td>
              <td><ScoreCell score={d.score} /></td>
              <td><VerdictBadge verdict={d.verdict} /></td>
              {/* Yalnızca skor gösteren bir takvim, sektörü doğrulanamamış yüksek
                  puanlı bir çağrıyı güvenli gibi gösterirdi (CLAUDE.md §2.2.1). */}
              <td><SectorFitBadge fit={d.sectorFit} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function TodoTablosu({ satirlar }: { satirlar: ReportTodoItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Öncelik</th>
            <th>İş</th>
            <th>Gerekçe</th>
            <th>Tarih</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((t, sira) => (
            <tr key={`${t.title}-${sira}`}>
              <td>{t.priorityLabel}</td>
              <td>
                {t.opportunityId
                  ? <Link to={`/opportunities/${t.opportunityId}`}>{t.title}</Link>
                  : t.title}
              </td>
              <td>{t.reason}</td>
              <td>{t.dueAt ? formatDate(t.dueAt) : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function MevzuatTablosu({ satirlar }: { satirlar: ReportRegulatoryItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Düzenleme</th>
            <th>Kurum</th>
            <th>Yayım Tarihi</th>
          </tr>
        </thead>
        <tbody>
          {satirlar.map((m) => (
            <tr key={m.regulatoryChangeId}>
              <td>
                <Link to={`/regulatory-changes/${m.regulatoryChangeId}`}>{m.title}</Link>
                {m.summary ? <><br /><small>{m.summary}</small></> : null}
              </td>
              <td>{m.authority}</td>
              <td>{formatDate(m.publishedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
