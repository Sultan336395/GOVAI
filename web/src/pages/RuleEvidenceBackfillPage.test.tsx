import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RuleEvidenceBackfillItem, RuleEvidenceBackfillReport } from '@/api/types'

/**
 * Kanıt bağlama ekranı (Platform İnceleme).
 *
 * İki söz sabitleniyor:
 *
 * 1. Onay verilmeden hiçbir bağlantı kurulmaz; uygulama isteği görülen planın
 *    parmak izini taşır.
 * 2. "Atlandı" demek yetmez — inceleyicinin bir sonraki adımı sonucun TÜRÜNE bağlıdır:
 *    kanıtı bulunamayan kayıt elle incelenir, ham içeriği olmayan yeniden indirilir,
 *    karantinadaki hiç ele alınmaz.
 */

const ruleEvidenceBackfillPlan = vi.fn()
const applyRuleEvidenceBackfill = vi.fn()
const undoRuleEvidenceBackfill = vi.fn()

vi.mock('@/api/client', () => ({
  api: {
    ruleEvidenceBackfillPlan: (batchSize: number, after?: string) =>
      ruleEvidenceBackfillPlan(batchSize, after),
    applyRuleEvidenceBackfill: (planHash: string, batchSize: number, after?: string) =>
      applyRuleEvidenceBackfill(planHash, batchSize, after),
    undoRuleEvidenceBackfill: (runId: string) => undoRuleEvidenceBackfill(runId),
  },
}))

const { default: RuleEvidenceBackfillPage } = await import('./RuleEvidenceBackfillPage')

function kayit(over: Partial<RuleEvidenceBackfillItem> = {}): RuleEvidenceBackfillItem {
  return {
    opportunityId: '01a05d9e-0000-7000-a000-000000000001',
    title: 'KOBİ Dijital Dönüşüm Destek Programı',
    outcome: 'Bound',
    explanation: '1 kural, belge sürüm 2 içindeki parçalara bağlandı.',
    ruleCount: 3,
    rulesAlreadyBound: 0,
    rulesBound: 1,
    rulesWithoutEvidence: 2,
    documentVersionNumber: 2,
    documentVersionHash: 'd'.repeat(64),
    officialUrl: 'https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital',
    ...over,
  }
}

const PLAN: RuleEvidenceBackfillReport = {
  applied: false,
  totalExamined: 3,
  boundCount: 1,
  alreadyBoundCount: 1,
  noEvidenceCount: 1,
  needsReparseCount: 0,
  needsRedownloadCount: 0,
  skippedQuarantinedCount: 0,
  skippedUnverifiedSourceCount: 0,
  noSourceDocumentCount: 0,
  failedCount: 0,
  evidenceLinksCreated: 1,
  nextCursor: null,
  hasMore: false,
  planHash: 'b'.repeat(64),
  runId: null,
  items: [
    kayit(),
    kayit({
      opportunityId: '01a05d9e-0000-7000-a000-000000000002',
      title: 'Girişimci Destek Programı',
      outcome: 'NoEvidenceFound',
      explanation: '2 kuralın hiçbiri belge metninde güvenilir biçimde bulunamadı.',
      rulesBound: 0,
    }),
    kayit({
      opportunityId: '01a05d9e-0000-7000-a000-000000000003',
      title: 'Yurt Dışı Pazar Desteği',
      outcome: 'AlreadyBound',
      explanation: 'Tüm kuralların kanıt bağlantısı zaten var.',
      rulesBound: 0,
    }),
  ],
}

const SONUC: RuleEvidenceBackfillReport = {
  ...PLAN,
  applied: true,
  runId: '01a05d9e-0000-7000-a000-0000000000ff',
}

function ekranaBas() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <RuleEvidenceBackfillPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const uygulaDugmesi = () =>
  document.querySelector('[data-alan="uygula"]') as HTMLButtonElement | null

const onayKutusu = () =>
  document.querySelector('[data-alan="onay-kutusu"]') as HTMLInputElement | null

beforeEach(() => {
  ruleEvidenceBackfillPlan.mockReset().mockResolvedValue(PLAN)
  applyRuleEvidenceBackfill.mockReset().mockResolvedValue(SONUC)
  undoRuleEvidenceBackfill.mockReset().mockResolvedValue({ removedLinkCount: 1 })
})

afterEach(cleanup)

describe('Kanıt bağlama ekranı', () => {
  it('KB-E1. Sonuç türleri ayrı ayrı adlandırılır', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const tablo = document.querySelector('[data-tablo="kayitlar"]')!

    expect(tablo.textContent).toContain('Kanıt bağlanacak')
    expect(tablo.textContent).toContain('Kanıt bulunamadı — dokunulmayacak')
  })

  it('KB-E2. Değişmeyecek kayıtlar ayrı bölümde durur', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const digerleri = document.querySelector('[data-alan="digerleri"]')!
    expect(digerleri.textContent).toContain('Yurt Dışı Pazar Desteği')
    expect(digerleri.textContent).toContain('Zaten bağlı')
  })

  it('KB-E3. Belge sürümü ve resmî kaynak ekranda görünür', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const tablo = document.querySelector('[data-tablo="kayitlar"]')!

    // Kanıt zincirinin görünen halkası: hangi sürüme bağlanıyor.
    expect(tablo.textContent).toContain('v2')

    const baglanti = tablo.querySelector('a[href*="kosgeb.gov.tr"]')
    expect(baglanti).toBeTruthy()
  })

  it('KB-E4. Onay verilmeden Uygula düğmesi kapalıdır', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    expect(uygulaDugmesi()!.disabled).toBe(true)
    expect(applyRuleEvidenceBackfill).not.toHaveBeenCalled()
  })

  it('KB-E5. Onaydan sonra uygulama, GÖRÜLEN planın özetiyle gider', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')
    await act(async () => {
      fireEvent.click(onayKutusu()!)
    })

    expect(uygulaDugmesi()!.disabled).toBe(false)

    await act(async () => {
      fireEvent.click(uygulaDugmesi()!)
    })

    // Üçüncü argüman imleçtir; ilk turda verilmez.
    await waitFor(() =>
      expect(applyRuleEvidenceBackfill).toHaveBeenCalledWith(PLAN.planHash, 100, undefined),
    )
  })

  it('KB-E6. Sonuç ekranından geri alınabilir', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')
    await act(async () => {
      fireEvent.click(onayKutusu()!)
    })
    await act(async () => {
      fireEvent.click(uygulaDugmesi()!)
    })

    const geriAl = (await waitFor(() => {
      const dugme = document.querySelector('[data-alan="geri-al"]')
      expect(dugme).toBeTruthy()
      return dugme
    })) as HTMLButtonElement

    await act(async () => {
      fireEvent.click(geriAl)
    })

    await waitFor(() => expect(undoRuleEvidenceBackfill).toHaveBeenCalledWith(SONUC.runId))
  })

  it('KB-E9. Fırsat başlığı detay ekranına götürür', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const detay = document.querySelector('[data-alan="detay"]')!

    expect(detay.getAttribute('href')).toBe(
      '/platform/opportunities/01a05d9e-0000-7000-a000-000000000001',
    )
  })

  it('KB-E7. Bağlanacak kayıt yoksa onay bölümü hiç görünmez', async () => {
    ruleEvidenceBackfillPlan.mockResolvedValue({
      ...PLAN,
      boundCount: 0,
      evidenceLinksCreated: 0,
      items: PLAN.items.filter((i) => i.outcome !== 'Bound'),
    })

    ekranaBas()

    await screen.findByText('Girişimci Destek Programı')

    expect(document.querySelector('[data-alan="onay"]')).toBeNull()
    expect(uygulaDugmesi()).toBeNull()
  })

  it('KB-E8. Devam eden tur olduğunda kullanıcı bilgilendirilir', async () => {
    ruleEvidenceBackfillPlan.mockResolvedValue({ ...PLAN, hasMore: true })

    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    expect(document.body.textContent).toContain('kaldığı yerden sürer')
  })
})
