import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, InfoBox, Loading } from '@/components/Common'
import { HelpTip } from '@/components/HelpTip'
import { formatDate } from '@/lib/format'
import { adet } from '@/lib/sozluk'

/**
 * Haftalık Raporlar — geçmiş listesi.
 *
 * Rapor her Pazartesi sabahı otomatik üretilir ve GEÇEN haftayı anlatır. Liste
 * ekranı rapor gövdesini açmaz; satırdaki sayılar kayıtla birlikte saklanan
 * özetlerden gelir.
 *
 * Geçmiş rapor açıldığında bugünün verisiyle yeniden hesaplanmaz: o hafta ne
 * görüldüyse onu gösterir.
 */
export default function WeeklyReportsPage() {
  const { selectedCompanyId } = useCompanies()
  const queryClient = useQueryClient()
  const [hata, setHata] = useState<unknown>(null)

  const { data = [], isLoading, error } = useQuery({
    queryKey: ['weekly-reports', selectedCompanyId],
    queryFn: () => api.listWeeklyReports(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  const uret = useMutation({
    mutationFn: () => api.generateWeeklyReport(selectedCompanyId!),
    onSuccess: async () => {
      setHata(null)
      await queryClient.invalidateQueries({ queryKey: ['weekly-reports', selectedCompanyId] })
    },
    onError: (e) => setHata(e),
  })

  if (!selectedCompanyId) {
    return <EmptyState>Rapor görmek için önce bir firma seçin.</EmptyState>
  }

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Haftalık Raporlar</h1>
          <p>
            Firmaya uygun fon, hibe, teşvik ve teknoloji ihaleleri; bu haftanın mevzuat
            değişiklikleri; riskler, son başvuru takvimi ve yapılacaklar. Rapor her
            Pazartesi sabahı kendiliğinden üretilir ve geçen haftayı anlatır.
          </p>
        </div>

        <button
          type="button"
          className="primary"
          onClick={() => uret.mutate()}
          disabled={uret.isPending}
        >
          {uret.isPending ? 'Hazırlanıyor…' : 'Şimdi Rapor Oluştur'}
        </button>
      </div>

      {hata ? <ErrorBox error={hata} /> : null}

      <InfoBox>
        Geçmiş raporlar olduğu gibi saklanır; açtığınızda o hafta ne görüldüyse onu
        gösterir. Aynı haftayı yeniden oluşturmak yeni bir satır açmaz, mevcut raporu
        günceller.
      </InfoBox>

      {data.length === 0 ? (
        <EmptyState>
          Henüz rapor yok. İlk haftalık rapor Pazartesi sabahı kendiliğinden üretilecek;
          beklemek istemezseniz yukarıdaki düğmeyle hemen oluşturabilirsiniz.
        </EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Dönem <HelpTip field="raporDonemi" /></th>
                <th>Nasıl Oluştu <HelpTip field="raporKaynagi" /></th>
                <th>Çağrılar</th>
                <th>Teknoloji İhalesi</th>
                <th>Mevzuat</th>
                <th>Riskler</th>
                <th>Takvimde</th>
                <th>Acil Son Başvuru <HelpTip field="acilSonBasvuru" /></th>
                <th>Çıktılar</th>
              </tr>
            </thead>
            <tbody>
              {data.map((rapor) => (
                <tr key={rapor.id}>
                  <td>
                    <Link to={`/weekly-reports/${rapor.id}`}>
                      {formatDate(rapor.periodStart)} – {formatDate(rapor.periodEnd)}
                    </Link>
                    <br />
                    <small>Oluşturuldu: {formatDate(rapor.generatedAt)}</small>
                  </td>
                  <td>{rapor.trigger === 'Scheduled' ? 'Otomatik' : 'Elle istendi'}</td>
                  <td>{adet(rapor.opportunityCount, 'Çağrı')}</td>
                  <td>{adet(rapor.tenderCount, 'İhale')}</td>
                  <td>{adet(rapor.regulatoryChangeCount, 'Değişiklik')}</td>
                  <td>{adet(rapor.riskCount, 'Risk')}</td>
                  <td>{adet(rapor.deadlineCount, 'Çağrı')}</td>
                  <td>{adet(rapor.urgentDeadlineCount, 'Çağrı')}</td>
                  <td>
                    <a href={api.weeklyReportExportUrl(rapor.id, 'pdf')}>PDF</a>
                    {' · '}
                    <a href={api.weeklyReportExportUrl(rapor.id, 'excel')}>Excel</a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
