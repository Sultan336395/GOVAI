import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { OpportunityDetail } from '@/api/types'

/**
 * İnceleyicinin fırsat detay ekranı.
 *
 * Ekranın iki sözü var:
 *
 * 1. Karar vermek için gereken her şey burada: kurallar, kanıt zinciri, belge sürümü
 *    ve resmî adres. Daha önce inceleyici yalnızca başlığı görüyor, kaydın ne olduğunu
 *    anlamak için resmî siteye gitmek zorunda kalıyordu.
 * 2. **Firma verisi yoktur.** Platform rolünün müşteri verisine erişmemesi sınırı
 *    ekranın kendisinde de korunur.
 */

const getOpportunity = vi.fn()

vi.mock('@/api/client', () => ({
  api: { getOpportunity: (id: string) => getOpportunity(id) },
}))

const { default: PlatformOpportunityPage } = await import('./PlatformOpportunityPage')

const FIRSAT: OpportunityDetail = {
  id: '01a05d9e-0000-7000-a000-000000000001',
  title: 'KOBİ Dijital Dönüşüm Destek Programı',
  publisher: 'KOSGEB',
  summary: 'Dijital dönüşüm için geri ödemesiz destek.',
  sourceUrl: 'https://www.kosgeb.gov.tr/site/tr/genel/destekdetay/kobi-dijital',
  sourceType: 'KosgebOrSimilar',
  supportCategory: 'DigitalTransformation',
  publishedAt: '2026-08-01T00:00:00Z',
  deadline: '2026-12-31T00:00:00Z',
  daysUntilDeadline: 90,
  budget: null,
  budgetItems: [],
  budgetRates: [],
  legalBasis: null,
  ruleExtractionConfidence: 0.9,
  isReviewedByConsultant: false,
  rules: [],
  documentChecklist: [],
  fieldAvailability: {
    deadline: 'Provided',
    budget: 'NotProvided',
    currency: 'NotProvided',
    eligibleApplicant: 'NotProvided',
    geography: 'NotProvided',
    sector: 'NotProvided',
    programmeType: 'NotProvided',
    officialDocumentUrl: 'Provided',
  },
  isOpen: true,
  provenance: null,
  quarantineReason: 'None',
  quarantineNote: null,
}

function ekranaBas(kayit: OpportunityDetail = FIRSAT) {
  getOpportunity.mockResolvedValue(kayit)

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/platform/opportunities/${kayit.id}`]}>
        <Routes>
          <Route
            path="/platform/opportunities/:opportunityId"
            element={<PlatformOpportunityPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => getOpportunity.mockReset())

afterEach(cleanup)

describe('İnceleyici fırsat detayı', () => {
  it('PD1. Kaydın künyesi ekranda açılır', async () => {
    ekranaBas()

    expect(await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')).toBeTruthy()
    expect(document.body.textContent).toContain('KOSGEB')
  })

  it('PD2. Doğru kaydın detayı istenir', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    expect(getOpportunity).toHaveBeenCalledWith(FIRSAT.id)
  })

  it('PD3. Ekranın salt okunur olduğu söylenir', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    expect(document.body.textContent).toContain('salt okunur')
  })

  it('PD4. Karantinadaki kayıt bunu açıkça söyler', async () => {
    ekranaBas({
      ...FIRSAT,
      quarantineReason: 'InvalidSourcePage',
      quarantineNote: 'Liste sayfası veya yürürlükten kaldırılmış destek sayfası.',
    })

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('Bu kayıt karantinada')
    // "Silindi" izlenimi verilmemeli: karantina silme değildir.
    expect(govde).toContain('silinmedi')
    expect(govde).toContain('Liste sayfası')
  })

  it('PD5. Karantinada olmayan kayıtta karantina uyarısı çıkmaz', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    expect(document.body.textContent).not.toContain('Bu kayıt karantinada')
  })

  it('PD6. Ekranda firma verisi yoktur', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const govde = document.body.textContent ?? ''

    // Platform rolü müşteri verisine erişemez; ekran o sınırı da korumalıdır.
    for (const yasak of ['Uygunluk puanı', 'Eşleşme', 'Firma', 'Şirket']) {
      expect(govde).not.toContain(yasak)
    }
  })

  it('PD7. Platform İnceleme alanına dönüş bağlantısı vardır', async () => {
    ekranaBas()

    await screen.findByText('KOBİ Dijital Dönüşüm Destek Programı')

    const geri = document.querySelector('[data-alan="geri"]')!
    expect(geri.getAttribute('href')).toBe('/quarantine')
  })
})
