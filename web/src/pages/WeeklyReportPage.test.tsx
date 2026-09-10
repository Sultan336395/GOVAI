import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WeeklyReportContent, WeeklyReportDetail } from '@/api/types'
import WeeklyReportPage from './WeeklyReportPage'

/**
 * Haftalık rapor ekranı.
 *
 * Ekranın iki sözü var ve testler ikisini de sabitliyor:
 *
 *  1. **Boş bölüm gizlenmez.** Görünmeyen bir bölüm, kullanıcıya o konunun hiç
 *     incelenmediğini düşündürür; başlığı ve sebebi kalmalıdır.
 *  2. **Uydurulmuş aksiyon yazılmaz.** Sistem ne yapılacağını bilmiyorsa bunu söyler,
 *     boş bir hücre ya da tahmin bırakmaz.
 */

const getWeeklyReport = vi.fn()

vi.mock('@/api/client', () => ({
  api: {
    getWeeklyReport: (reportId: string) => getWeeklyReport(reportId),
    weeklyReportExportUrl: (reportId: string, format: string) =>
      `/api/reports/weekly/${reportId}/${format}`,
  },
}))

const bosIcerik: WeeklyReportContent = {
  header: {
    companyId: 'c1',
    companyName: 'Örnek Teknoloji A.Ş.',
    periodStart: '2026-09-07',
    periodEnd: '2026-09-13',
    generatedAt: '2026-09-14T04:30:00Z',
    evaluatedOpportunityCount: 0,
    eligibleCount: 0,
    undeterminedCount: 0,
  },
  supports: [],
  technologyTenders: [],
  otherOpportunities: [],
  regulatoryChanges: [],
  risks: [],
  deadlines: [],
  todos: [],
  notes: ['Firma için henüz değerlendirme yapılmamış.'],
}

function rapor(icerik: Partial<WeeklyReportContent> = {}): WeeklyReportDetail {
  return {
    id: 'r1',
    trigger: 'Scheduled',
    content: { ...bosIcerik, ...icerik },
  }
}

function ekranaBas() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/weekly-reports/r1']}>
        <Routes>
          <Route path="/weekly-reports/:reportId" element={<WeeklyReportPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('WeeklyReportPage', () => {
  beforeEach(() => {
    getWeeklyReport.mockReset()
  })

  afterEach(cleanup)

  it('HRW1. Bütün bölüm başlıkları görünür', async () => {
    getWeeklyReport.mockResolvedValue(rapor())

    ekranaBas()

    await waitFor(() => expect(screen.getByText('Haftalık Rapor')).toBeTruthy())

    for (const baslik of [
      'Öncelikli Yapılacaklar',
      'Riskler ve Zorunlu Aksiyonlar',
      'En Uygun Fon, Hibe ve Teşvikler',
      'Teknoloji ve Yazılım İhaleleri',
      'Diğer Açık Çağrı ve İhaleler',
      'Son Başvuru Takvimi',
      'Mevzuat Değişiklikleri',
    ]) {
      expect(screen.getByText(baslik)).toBeTruthy()
    }
  })

  it('HRW2. Boş bölüm GİZLENMEZ, sebebi yazılır', async () => {
    getWeeklyReport.mockResolvedValue(rapor())

    ekranaBas()

    await waitFor(() => expect(screen.getByText('Mevzuat Değişiklikleri')).toBeTruthy())

    expect(screen.getByText('Bu hafta mevzuat değişikliği yayımlanmadı.')).toBeTruthy()
    expect(screen.getByText('Firma için henüz değerlendirme yapılmamış.')).toBeTruthy()
  })

  it('HRW3. Aksiyonu olmayan risk için TAHMİN yazılmaz', async () => {
    getWeeklyReport.mockResolvedValue(rapor({
      risks: [
        {
          kind: 'BlockingCondition',
          kindLabel: 'Engelleyici koşul',
          subject: 'Asgari 10 çalışan',
          description: 'Asgari 10 çalışan — firmanın değeri: 7',
          action: null,
          affectedOpportunityCount: 3,
        },
      ],
    }))

    ekranaBas()

    await waitFor(() => expect(screen.getByText('Asgari 10 çalışan')).toBeTruthy())

    expect(screen.getByText('Danışman değerlendirmesi gerekiyor')).toBeTruthy()
    expect(screen.getByText('3')).toBeTruthy()
  })

  it('HRW4. PDF ve Excel bağlantıları raporun kendisine gider', async () => {
    getWeeklyReport.mockResolvedValue(rapor())

    ekranaBas()

    await waitFor(() => expect(screen.getByText('PDF İndir')).toBeTruthy())

    expect(screen.getByText('PDF İndir').getAttribute('href'))
      .toBe('/api/reports/weekly/r1/pdf')
    expect(screen.getByText('Excel İndir').getAttribute('href'))
      .toBe('/api/reports/weekly/r1/excel')
  })

  it('HRW5. Raporun otomatik mi elle mi oluştuğu yazılır', async () => {
    getWeeklyReport.mockResolvedValue(rapor())

    ekranaBas()

    await waitFor(() => expect(screen.getByText(/Otomatik oluşturuldu/)).toBeTruthy())
  })
})
