import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CatalogRepairPlanReport, CatalogRepairReport } from '@/api/types'

/**
 * Katalog onarımı ekranı (Platform İnceleme).
 *
 * Ekranın tek sözü var ve testler onu sabitliyor: **onay verilmeden hiçbir şey
 * uygulanmaz.** Uygula düğmesi onay kutusu işaretlenene kadar kapalıdır ve uygulama
 * isteği, kullanıcıya gösterilen planın parmak izini taşır.
 *
 * Bu bir güvenlik sınırı DEĞİLDİR — sunucu aynı kuralı ayrıca uygular
 * (`PlatformReviewAccessTests`). Buradaki işin amacı, kullanıcının yanlışlıkla
 * uygulamasını önlemek ve ne uygulayacağını görmesini sağlamaktır.
 */

const catalogRepairPlan = vi.fn()
const applyCatalogRepair = vi.fn()
const undoCatalogRepair = vi.fn()

vi.mock('@/api/client', () => ({
  api: {
    catalogRepairPlan: () => catalogRepairPlan(),
    applyCatalogRepair: (planHash: string) => applyCatalogRepair(planHash),
    undoCatalogRepair: (runId: string) => undoCatalogRepair(runId),
  },
}))

const { default: CatalogRepairPage } = await import('./CatalogRepairPage')

const PLAN: CatalogRepairPlanReport = {
  stepCount: 6,
  matchedRecordCount: 2,
  willChangeCount: 1,
  alreadyDoneCount: 1,
  planHash: 'a'.repeat(64),
  matches: [
    {
      stepCode: 'KOSGEB-LISTE',
      target: 'Opportunity',
      action: 'Quarantine',
      recordId: '01a05d9e-0000-7000-a000-000000000001',
      currentTitle: 'KOSGEB Destekler',
      officialUrl: 'https://www.kosgeb.gov.tr/site/tr/genel/destekler',
      proposedTitle: null,
      willChange: true,
      skipReason: null,
      affectedAssessmentCount: 7,
    },
    {
      stepCode: 'SGK-SUT',
      target: 'RegulatoryChange',
      action: 'Quarantine',
      recordId: '01a05d9e-0000-7000-a000-000000000002',
      currentTitle: 'SUT Değişiklik Tebliği',
      officialUrl: null,
      proposedTitle: null,
      willChange: false,
      skipReason: 'Kayıt zaten karantinada.',
      affectedAssessmentCount: 0,
    },
  ],
}

const SONUC: CatalogRepairReport = {
  attempted: 2,
  changed: 1,
  alreadyDone: 1,
  runId: '01a05d9e-0000-7000-a000-0000000000ff',
  outcomes: [
    {
      stepCode: 'KOSGEB-LISTE',
      recordId: '01a05d9e-0000-7000-a000-000000000001',
      result: 'Uygulandı.',
      affectedAssessmentCount: 7,
      previousTitle: null,
      target: 'Opportunity',
      action: 'Quarantine',
    },
  ],
}

function ekranaBas() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <CatalogRepairPage />
    </QueryClientProvider>,
  )
}

const uygulaDugmesi = () =>
  document.querySelector('[data-alan="uygula"]') as HTMLButtonElement | null

const onayKutusu = () =>
  document.querySelector('[data-alan="onay-kutusu"]') as HTMLInputElement | null

beforeEach(() => {
  catalogRepairPlan.mockReset().mockResolvedValue(PLAN)
  applyCatalogRepair.mockReset().mockResolvedValue(SONUC)
  undoCatalogRepair.mockReset().mockResolvedValue({ ...SONUC, changed: 1 })
})

afterEach(cleanup)

describe('Katalog onarımı ekranı', () => {
  it('KO-E1. Plan, uygulanmadan önce kayıt kayıt gösterilir', async () => {
    ekranaBas()

    expect(await screen.findByText('KOSGEB Destekler')).toBeTruthy()

    const tablo = document.querySelector('[data-tablo="degisecekler"]')!
    expect(tablo.textContent).toContain('Karantinaya al')
    expect(tablo.textContent).toContain('KOSGEB-LISTE')

    // Etkilenecek değerlendirme sayısı görünür: kullanıcı bedelini bilerek onaylar.
    expect(tablo.textContent).toContain('7')
  })

  it('KO-E2. Atlanacak kayıtların gerekçesi gösterilir', async () => {
    ekranaBas()

    await screen.findByText('KOSGEB Destekler')

    const atlananlar = document.querySelector('[data-alan="atlananlar"]')!
    expect(atlananlar.textContent).toContain('Kayıt zaten karantinada.')
  })

  it('KO-E3. Onay verilmeden Uygula düğmesi kapalıdır', async () => {
    ekranaBas()

    await screen.findByText('KOSGEB Destekler')

    expect(uygulaDugmesi()!.disabled).toBe(true)
    expect(applyCatalogRepair).not.toHaveBeenCalled()
  })

  it('KO-E4. Onaydan sonra uygulama, GÖRÜLEN planın özetiyle gider', async () => {
    ekranaBas()

    await screen.findByText('KOSGEB Destekler')
    await act(async () => { fireEvent.click(onayKutusu()!) })

    expect(uygulaDugmesi()!.disabled).toBe(false)

    await act(async () => { fireEvent.click(uygulaDugmesi()!) })

    // Uydurma bir değer değil, plan ucundan gelen özet gönderilir.
    await waitFor(() => expect(applyCatalogRepair).toHaveBeenCalledWith(PLAN.planHash))
  })

  it('KO-E5. Uygulamadan sonra onay sıfırlanır', async () => {
    ekranaBas()

    await screen.findByText('KOSGEB Destekler')
    await act(async () => { fireEvent.click(onayKutusu()!) })
    await act(async () => { fireEvent.click(uygulaDugmesi()!) })

    // İkinci bir uygulama, yeniden onay ister: tek tıkla tekrar çalıştırılamaz.
    await waitFor(() => expect(onayKutusu()!.checked).toBe(false))
  })

  it('KO-E6. Sonuç ekranından geri alınabilir', async () => {
    ekranaBas()

    await screen.findByText('KOSGEB Destekler')
    await act(async () => { fireEvent.click(onayKutusu()!) })
    await act(async () => { fireEvent.click(uygulaDugmesi()!) })

    const geriAl = (await waitFor(() => {
      const dugme = document.querySelector('[data-alan="geri-al"]')
      expect(dugme).toBeTruthy()
      return dugme
    })) as HTMLButtonElement

    await act(async () => { fireEvent.click(geriAl) })

    await waitFor(() => expect(undoCatalogRepair).toHaveBeenCalledWith(SONUC.runId))
  })

  it('KO-E7. Değişecek kayıt yoksa onay bölümü hiç görünmez', async () => {
    catalogRepairPlan.mockResolvedValue({
      ...PLAN,
      willChangeCount: 0,
      matches: PLAN.matches.filter((m) => !m.willChange),
    })

    ekranaBas()

    await screen.findByText(/zaten karantinada/)

    expect(document.querySelector('[data-alan="onay"]')).toBeNull()
    expect(uygulaDugmesi()).toBeNull()
  })
})
