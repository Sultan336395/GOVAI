import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { api } from '@/api/client'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, Kpi, Loading } from '@/components/Common'
import DashboardInsights from '@/components/DashboardInsights'
import KpiKirilimPaneli, { KPI_PANEL_ID } from '@/components/KpiKirilimPaneli'
import MatchTable from '@/components/MatchTable'
import { formatPercent } from '@/lib/format'
import { type KpiAnahtari } from '@/lib/kpiKirilimi'

const VERDICT_COLORS = ['#15803d', '#b45309', '#b91c1c', '#64748b']

/** Karttaki sayının panodan okunacağı yer; panel bu sayıyla listeyi karşılaştırır. */
const KART_SAYILARI: Record<KpiAnahtari, (d: import('@/api/types').Dashboard) => number> = {
  uygun: (d) => d.eligibleCount,
  sartli: (d) => d.conditionallyEligibleCount,
  ortalama: (d) => d.averageScore,
  kapanan: (d) => d.closingWithin15Days,
  belge: (d) => d.missingMandatoryDocumentTotal,
  bosluk: (d) => d.dataGapTotal,
}

export default function DashboardPage() {
  const { selectedCompanyId } = useCompanies()
  const queryClient = useQueryClient()

  const [acikKpi, setAcikKpi] = useState<KpiAnahtari | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['dashboard', selectedCompanyId],
    queryFn: () => api.getDashboard(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  const rescore = useMutation({
    mutationFn: () => api.rescore(selectedCompanyId!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dashboard', selectedCompanyId] })
      queryClient.invalidateQueries({ queryKey: ['matches', selectedCompanyId] })
    },
  })

  if (!selectedCompanyId) return <EmptyState>Önce bir firma seçin.</EmptyState>
  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Gösterilecek veri yok.</EmptyState>

  const verdictData = [
    { name: 'Uygun', value: data.eligibleCount },
    { name: 'Şartlı uygun', value: data.conditionallyEligibleCount },
    { name: 'Uygun değil', value: data.notEligibleCount },
    { name: 'Belirsiz', value: data.indeterminateCount },
  ].filter((entry) => entry.value > 0)

  const dimensionData = data.dimensionAverages.map((d) => ({
    label: d.label,
    deger: Math.round(d.averageValue * 100),
  }))

  return (
    <>
      <div className="page-header">
        <div>
          <h1>{data.companyName}</h1>
          <p>
            Profil doluluğu {formatPercent(data.profileCompleteness)} · {data.totalEvaluatedOpportunities}{' '}
            çağrı değerlendirildi
          </p>
        </div>
        <div className="toolbar" style={{ margin: 0 }}>
          <a href={api.exportUrl(selectedCompanyId, 'excel')}>
            <button type="button">Excel indir</button>
          </a>
          <a href={api.exportUrl(selectedCompanyId, 'pdf')}>
            <button type="button">PDF rapor</button>
          </a>
          <button
            type="button"
            className="primary"
            onClick={() => rescore.mutate()}
            disabled={rescore.isPending}
          >
            {rescore.isPending ? 'Hesaplanıyor…' : 'Yeniden skorla'}
          </button>
        </div>
      </div>

      {rescore.error ? <ErrorBox error={rescore.error} /> : null}

      {/*
        Kartlar tıklanabilir: sayıyı görmek tek başına "hangileri?" sorusunu
        cevaplamıyordu ve kullanıcı her seferinde eşleşmeler ekranına gidip aynı
        süzgeci elle kuruyordu. Kırılım kartın hemen altında, aynı ekranda açılır.
      */}
      <div className="grid kpis" style={{ marginBottom: acikKpi ? 0 : 16 }}>
        <Kpi
          label="Uygun fırsat"
          value={data.eligibleCount}
          hint="Tüm koşullar sağlanıyor"
          onClick={() => setAcikKpi((o) => (o === 'uygun' ? null : 'uygun'))}
          acik={acikKpi === 'uygun'}
          panelId={KPI_PANEL_ID}
        />
        <Kpi
          label="Şartlı uygun"
          value={data.conditionallyEligibleCount}
          hint="Eksikler kapatılırsa uygun"
          onClick={() => setAcikKpi((o) => (o === 'sartli' ? null : 'sartli'))}
          acik={acikKpi === 'sartli'}
          panelId={KPI_PANEL_ID}
        />
        <Kpi
          label="Ortalama skor"
          value={data.averageScore.toFixed(1)}
          hint="100 üzerinden"
          onClick={() => setAcikKpi((o) => (o === 'ortalama' ? null : 'ortalama'))}
          acik={acikKpi === 'ortalama'}
          panelId={KPI_PANEL_ID}
        />
        <Kpi
          label="15 günde kapanan"
          value={data.closingWithin15Days}
          hint="Aksiyon gerektiren fırsatlar"
          onClick={() => setAcikKpi((o) => (o === 'kapanan' ? null : 'kapanan'))}
          acik={acikKpi === 'kapanan'}
          panelId={KPI_PANEL_ID}
        />
        <Kpi
          label="Eksik zorunlu belge"
          value={data.missingMandatoryDocumentTotal}
          hint="Tüm fırsatlar toplamı"
          onClick={() => setAcikKpi((o) => (o === 'belge' ? null : 'belge'))}
          acik={acikKpi === 'belge'}
          panelId={KPI_PANEL_ID}
        />
        <Kpi
          label="Veri boşluğu"
          value={data.dataGapTotal}
          hint="Profil eksikliği nedeniyle karar verilemeyen koşul"
          onClick={() => setAcikKpi((o) => (o === 'bosluk' ? null : 'bosluk'))}
          acik={acikKpi === 'bosluk'}
          panelId={KPI_PANEL_ID}
        />
      </div>

      {acikKpi ? (
        <KpiKirilimPaneli
          anahtar={acikKpi}
          companyId={selectedCompanyId}
          karttakiSayi={KART_SAYILARI[acikKpi](data)}
          onKapat={() => setAcikKpi(null)}
        />
      ) : null}

      {/*
        Aksiyonlar ve profil göstergesi grafiklerin ÜSTÜNDE durur: ekranın işi önce
        "bugün ne yapmalıyım" sorusuna cevap vermek, sonra genel resmi göstermektir.
      */}
      <DashboardInsights companyId={selectedCompanyId} />

      <div className="grid two" style={{ marginBottom: 16 }}>
        <div className="card">
          <h2>Karar Dağılımı</h2>
          {verdictData.length === 0 ? (
            <EmptyState>Henüz değerlendirme yok.</EmptyState>
          ) : (
            <ResponsiveContainer width="100%" height={240}>
              <PieChart>
                <Pie data={verdictData} dataKey="value" nameKey="name" outerRadius={85} label>
                  {verdictData.map((entry, index) => (
                    <Cell key={entry.name} fill={VERDICT_COLORS[index % VERDICT_COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>

        <div className="card">
          <h2>Skor Boyutları (Ortalama %)</h2>
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={dimensionData} margin={{ left: -18, bottom: 40 }}>
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="label" angle={-30} textAnchor="end" interval={0} fontSize={11} />
              <YAxis domain={[0, 100]} fontSize={11} />
              <Tooltip />
              <Bar dataKey="deger" name="Ortalama" fill="#1d4ed8" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Öncelikli Fırsatlar</h2>
        <MatchTable matches={data.topOpportunities} />
      </div>

      <div className="card">
        <h2>Son Başvurusu Yaklaşanlar</h2>
        {data.closingSoon.length === 0 ? (
          <EmptyState>15 gün içinde kapanan uygun fırsat yok.</EmptyState>
        ) : (
          <MatchTable matches={data.closingSoon} />
        )}
      </div>
    </>
  )
}
