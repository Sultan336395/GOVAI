import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { api } from '@/api/client'
import type { ActivityItem, DashboardAction, OpportunityFunnel } from '@/api/types'
import { EmptyState, ErrorBox, InfoBox, Loading } from '@/components/Common'
import { formatDate } from '@/lib/format'

/**
 * Dashboard'un dört ek bölümü: aksiyonlar, profil doluluğu, huni ve eğilim, hareketler.
 *
 * Hepsi tek istekte gelir; bölümler parça parça beklemez. Ana dashboard sayılarından
 * ayrı bir uçtur: bu bölümler hesaplanamasa da sayılar görünmelidir.
 */
export default function DashboardInsights({ companyId }: { companyId: string }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['dashboard-insights', companyId],
    queryFn: () => api.getDashboardInsights(companyId),
  })

  if (isLoading) return <Loading />

  // Bu bölümler bir ek katmandır; hata alırsa ekranın geri kalanı çalışmaya devam eder.
  if (error) return <ErrorBox error={error} />
  if (!data) return null

  const { actions, profile, funnel, trend, activity, notes } = data

  return (
    <>
      {/*
        Notlar en üstte durur. İçlerinde "henüz değerlendirme yok" gibi bölümlerin
        kapsamını sınırlayan uyarılar olabilir; altta kalsalardı okuyucu tabloları
        eksiksiz sanarak karar verirdi.
      */}
      {notes.length > 0 ? (
        <InfoBox>
          {notes.map((not) => <div key={not}>{not}</div>)}
        </InfoBox>
      ) : null}

      <section className="card" style={{ marginBottom: 16 }}>
        <h2>Şimdi Yapılacaklar</h2>

        {actions.length === 0 ? (
          <EmptyState>Süre kısıtı olan veya engelleyici eksik taşıyan bir iş görünmüyor.</EmptyState>
        ) : (
          <ul className="aksiyon-listesi">
            {actions.map((a, sira) => <Aksiyon key={`${a.title}-${sira}`} aksiyon={a} />)}
          </ul>
        )}
      </section>

      <div className="grid two" style={{ marginBottom: 16 }}>
        <section className="card">
          <h2>Profil Tamamlanma</h2>
          <Profil
            yuzde={profile.percentage}
            bilinen={profile.knownCount}
            toplam={profile.totalCount}
            eksikler={profile.missingLabels}
            veriBoslugu={profile.dataGapCount}
          />
        </section>

        <section className="card">
          <h2>Fırsat Hunisi</h2>
          <Huni huni={funnel} />
        </section>
      </div>

      <section className="card" style={{ marginBottom: 16 }}>
        <h2>Haftalık Eğilim</h2>

        {trend.length < 2 ? (
          <EmptyState>
            Eğilim çizgisi için en az iki haftalık rapor gerekir. Rapor her Pazartesi
            sabahı otomatik üretilir.
          </EmptyState>
        ) : (
          <ResponsiveContainer width="100%" height={240}>
            <LineChart
              data={trend.map((t) => ({
                hafta: formatDate(t.periodStart),
                Çağrı: t.opportunityCount,
                İhale: t.tenderCount,
                Risk: t.riskCount,
              }))}
              margin={{ left: -18 }}
            >
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="hafta" fontSize={11} />
              <YAxis allowDecimals={false} fontSize={11} />
              <Tooltip />
              <Line type="monotone" dataKey="Çağrı" stroke="#1d4ed8" strokeWidth={2} />
              <Line type="monotone" dataKey="İhale" stroke="#15803d" strokeWidth={2} />
              <Line type="monotone" dataKey="Risk" stroke="#b45309" strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </section>

      <section className="card" style={{ marginBottom: 16 }}>
        <h2>Son Hareketler</h2>

        {activity.length === 0 ? (
          <EmptyState>Bu firmada kayıtlı bir hareket yok.</EmptyState>
        ) : (
          <ul className="hareket-listesi">
            {activity.map((h, sira) => <Hareket key={`${h.kind}-${sira}`} hareket={h} />)}
          </ul>
        )}
      </section>
    </>
  )
}

function Aksiyon({ aksiyon }: { aksiyon: DashboardAction }) {
  return (
    <li className={`aksiyon aksiyon-${aksiyon.priority.toLowerCase()}`}>
      <div className="aksiyon-etiket">{aksiyon.priorityLabel}</div>
      <div className="aksiyon-govde">
        <Link to={aksiyon.target}>{aksiyon.title}</Link>
        <div className="muted">{aksiyon.reason}</div>
      </div>
      {aksiyon.dueAt ? (
        <div className="aksiyon-tarih">{formatDate(aksiyon.dueAt)}</div>
      ) : null}
    </li>
  )
}

function Profil({
  yuzde, bilinen, toplam, eksikler, veriBoslugu,
}: {
  yuzde: number
  bilinen: number
  toplam: number
  eksikler: string[]
  veriBoslugu: number
}) {
  return (
    <>
      <div className="profil-oran">
        <strong>%{yuzde}</strong>
        <span className="muted"> · {bilinen}/{toplam} alan biliniyor</span>
      </div>

      <div
        className="profil-cubuk"
        role="progressbar"
        aria-valuenow={yuzde}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Profil tamamlanma oranı"
      >
        <span style={{ width: `${yuzde}%` }} />
      </div>

      {/*
        Eksik alan firmayı ELEMEZ; kararı belirsiz bırakır (CLAUDE.md §2.2). Metin
        bunu açıkça söyler, yoksa kullanıcı eksik alanı bir ret sebebi sanır.
      */}
      <p className="muted" style={{ marginTop: 10 }}>
        Eksik alan firmayı elemez; o alana bakan koşullarda karar "belirsiz" kalır.
        {veriBoslugu > 0 ? ` Şu an ${veriBoslugu} koşulda karar verilemedi.` : ''}
      </p>

      {eksikler.length === 0 ? (
        <p>Kural motorunun okuduğu alanların tamamı dolu.</p>
      ) : (
        <>
          <h3 style={{ fontSize: 13, marginBottom: 6 }}>Girilmemiş alanlar</h3>
          <div className="chip-row">
            {eksikler.map((e) => <span key={e} className="chip">{e}</span>)}
          </div>
          <Link to="/company">Firma profilini tamamla →</Link>
        </>
      )}
    </>
  )
}

function Huni({ huni }: { huni: OpportunityFunnel }) {
  const basamaklar: [string, number][] = [
    ['Değerlendirilen', huni.evaluated],
    ['Uygun', huni.eligible],
    ['Şartlı uygun', huni.conditionallyEligible],
    ['Takibe alınan', huni.tracked],
    ['Teklif verilen', huni.submitted],
    ['Kazanılan', huni.won],
  ]

  const enBuyuk = Math.max(...basamaklar.map(([, sayi]) => sayi), 1)

  return (
    <>
      <ul className="huni">
        {basamaklar.map(([ad, sayi]) => (
          <li key={ad}>
            <span className="huni-ad">{ad}</span>
            <span className="huni-cubuk">
              <span style={{ width: `${Math.round((sayi / enBuyuk) * 100)}%` }} />
            </span>
            <span className="huni-sayi">{sayi}</span>
          </li>
        ))}
      </ul>

      {/*
        "Değerlendirilen" sistemin, "takibe alınan" firmanın sayısıdır. Firma sistemin
        uygun görmediği bir ihaleyi de takibe alabilir; alt basamak üstten büyük
        çıkabilir ve bu bir hata değildir.
      */}
      <p className="muted">
        Üst iki basamak sistemin kararı, alt üçü firmanın kendi beyanıdır; bu yüzden
        alt basamak üsttekinden büyük olabilir.
      </p>
    </>
  )
}

function Hareket({ hareket }: { hareket: ActivityItem }) {
  return (
    <li>
      <span className="hareket-zaman">{formatDate(hareket.at)}</span>
      <span className="hareket-govde">
        {hareket.target
          ? <Link to={hareket.target}>{hareket.title}</Link>
          : hareket.title}
        {hareket.detail ? <><br /><small className="muted">{hareket.detail}</small></> : null}
      </span>
      {hareket.actor ? <span className="hareket-kisi">{hareket.actor}</span> : null}
    </li>
  )
}
