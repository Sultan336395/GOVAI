import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { QuarantinedDocumentDetail } from '@/api/types'

/**
 * Belge incelemesi ekranı.
 *
 * Ekranın sözü: karantinadan çıkarma kararı için gereken her şey burada — belgenin
 * metni, sürümleri, ayrıştırma durumu ve neden karantinada olduğu.
 *
 * İlk denemede kayıtlar fırsat detayına bağlanmıştı ve bağlantı hiç görünmüyordu:
 * karantina belge sisteme GİRERKEN uygulanıyor, fırsat kaydı henüz oluşmamış oluyor.
 * Bu testler ekranın fırsatı olmayan belgede de çalıştığını sabitler.
 */

const getQuarantinedDocument = vi.fn()

vi.mock('@/api/client', () => ({
  api: { getQuarantinedDocument: (id: string) => getQuarantinedDocument(id) },
}))

const { default: PlatformDocumentPage } = await import('./PlatformDocumentPage')

const BELGE: QuarantinedDocumentDetail = {
  documentId: '01a05d9e-0000-7000-a000-0000000000d1',
  title: 'TAŞINMAZLAR SATILACAKTIR',
  url: 'https://www.resmigazete.gov.tr/eskiler/tasinmaz',
  canonicalUrl: 'https://www.resmigazete.gov.tr/eskiler/tasinmaz',
  sourceId: '01a05d9e-0000-7000-a000-0000000000s1',
  sourceName: 'Resmî Gazete',
  officialDomain: 'resmigazete.gov.tr',
  reason: 'InvalidSourcePage',
  note: 'İlan sayfası; çağrı değildir.',
  status: 'Discarded',
  processingError: null,
  origin: 'Crawl',
  collectedAt: '2026-09-06T10:00:00Z',
  mediaType: 'text/html',
  textPreview: 'Mülkiyeti idareye ait taşınmazlar satılacaktır. İhale 15.10.2026 tarihindedir.',
  textLength: 77,
  textTruncated: false,
  versions: [
    {
      versionId: '01a05d9e-0000-7000-a000-0000000000v1',
      versionNumber: 1,
      retrievedAt: '2026-09-06T10:00:00Z',
      httpStatusCode: 200,
      mediaType: 'text/html',
      charset: 'windows-1254',
      canonicalUrl: 'https://www.resmigazete.gov.tr/eskiler/tasinmaz',
      rawContentHash: 'a'.repeat(64),
      parseStatus: 'Parsed',
      requiresOcr: false,
      parseError: null,
      pageCount: 1,
      chunkCount: 3,
      title: 'TAŞINMAZLAR SATILACAKTIR',
    },
  ],
  opportunityId: null,
  regulatoryChangeId: null,
}

function ekranaBas(belge: QuarantinedDocumentDetail = BELGE) {
  getQuarantinedDocument.mockResolvedValue(belge)

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/platform/documents/${belge.documentId}`]}>
        <Routes>
          <Route path="/platform/documents/:documentId" element={<PlatformDocumentPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => getQuarantinedDocument.mockReset())

afterEach(cleanup)

describe('Belge incelemesi', () => {
  it('BD1. Belgenin metni ekranda görünür', async () => {
    ekranaBas()

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    const metin = document.querySelector('[data-alan="metin-govdesi"]')!
    expect(metin.textContent).toContain('taşınmazlar satılacaktır')
  })

  it('BD2. Fırsatı olmayan belge de açılır ve durumu söylenir', async () => {
    // Üretimdeki karantina kayıtlarının tamamı böyle.
    ekranaBas()

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    expect(document.body.textContent).toContain('çağrı kaydı oluşturulmamış')
    expect(document.querySelector('[data-alan="firsat"]')).toBeNull()
  })

  it('BD3. Karantina nedeni ve notu gösterilir', async () => {
    ekranaBas()

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('İncelemeye alındı')
    expect(govde).toContain('İlan sayfası; çağrı değildir.')
    // İncelemeye almak silmek değildir; ekran bunu söylemeli.
    expect(govde).toContain('Silinmedi')
  })

  it('BD4. Belge geçmişi okunabilirliği iş dilinde gösterir', async () => {
    ekranaBas()

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    const surumler = document.querySelector('[data-alan="surumler"]')!

    expect(surumler.textContent).toContain('Metni okundu')
    expect(surumler.textContent).toContain('Erişildi')
    expect(surumler.textContent).toContain('güncel')

    // Sürüm numarası, HTTP kodu ve hash sistemin iç muhasebesidir; ekranda işi yok.
    expect(surumler.textContent).not.toContain('v1')
    expect(surumler.textContent).not.toContain('200')
    expect(surumler.textContent).not.toContain('aaaaaaaa')
  })

  it('BD5. Metin kırpıldıysa bu açıkça söylenir', async () => {
    ekranaBas({
      ...BELGE,
      textPreview: 'x'.repeat(20_000),
      textLength: 145_320,
      textTruncated: true,
    })

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    // Sessizce eksik metin göstermek, içerik hakkında yanlış karar verdirir.
    expect(document.body.textContent).toContain('ilk bölümü gösteriliyor')

    // Karakter sayısı kullanıcıya bir şey anlatmaz; kırpıldığını bilmesi yeter.
    expect(document.body.textContent).not.toContain('145.320')
  })

  it('BD6. Metin yoksa sebebi anlatılır', async () => {
    ekranaBas({ ...BELGE, textPreview: null, textLength: 0, textTruncated: false })

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    expect(document.body.textContent).toContain('Taranmış bir görüntü olabilir')
    expect(document.querySelector('[data-alan="metin-govdesi"]')).toBeNull()
  })

  it('BD7. Fırsat türemişse ona bağlantı verilir', async () => {
    ekranaBas({ ...BELGE, opportunityId: '01a05d9e-0000-7000-a000-0000000000f1' })

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    const baglanti = document.querySelector('[data-alan="firsat"]')!

    expect(baglanti.getAttribute('href')).toBe(
      '/platform/opportunities/01a05d9e-0000-7000-a000-0000000000f1',
    )
  })

  it('BD8. Karantina listesine dönüş bağlantısı vardır', async () => {
    ekranaBas()

    await screen.findByText('TAŞINMAZLAR SATILACAKTIR')

    expect(document.querySelector('[data-alan="geri"]')!.getAttribute('href')).toBe('/quarantine')
  })
})
