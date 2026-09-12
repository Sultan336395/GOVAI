import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { OpportunityMatch } from '@/api/types'
import KpiKirilimPaneli from './KpiKirilimPaneli'

/**
 * Özet kartının altında açılan kırılım paneli.
 *
 * <p>
 * Panelin tek işi "karttaki sayı hangi fırsatlardan geliyor" sorusunu cevaplamaktır.
 * Bu yüzden testler iki şeye bakar: doğru satırlar mı geliyor, ve karttaki sayıyla
 * listedeki sayı ayrıştığında bu SÖYLENİYOR mu. İkincisi olmazsa panel yanlış veriyi
 * güvenle gösterir; sessiz yanlış, görünür hatadan kötüdür.
 * </p>
 */

const listMatches = vi.fn()

vi.mock('@/api/client', () => ({
  api: {
    listMatches: (...args: unknown[]) => listMatches(...args),
  },
}))

afterEach(cleanup)

beforeEach(() => {
  listMatches.mockReset()
})

let sayac = 0

const esles = (ek: Partial<OpportunityMatch> = {}): OpportunityMatch => {
  sayac += 1

  return {
    assessmentId: `01a05d9e-0000-7000-a000-00000000000${sayac}`,
    opportunityId: `01a05d9e-0000-7000-a000-00000000010${sayac}`,
    opportunityTitle: `Çağrı ${sayac}`,
    publisher: 'Örnek Kurum',
    supportCategory: 'Grant',
    deadline: null,
    daysUntilDeadline: null,
    finalScore: 50,
    confidence: 0.8,
    verdict: 'ConditionallyEligible',
    sectorFit: 'Matched',
    missingConditionCount: 0,
    missingMandatoryDocumentCount: 0,
    dataGapCount: 0,
    maxAmount: null,
    executiveSummary: null,
    evaluatedAt: '2026-09-12T00:00:00Z',
    ...ek,
  }
}

function sayfa(items: OpportunityMatch[], totalCount = items.length, totalPages = 1) {
  return { items, totalCount, page: 1, pageSize: 200, totalPages }
}

function goster(props: Partial<Parameters<typeof KpiKirilimPaneli>[0]> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <KpiKirilimPaneli
          anahtar="uygun"
          companyId="01a05d9e-0000-7000-a000-0000000000ff"
          karttakiSayi={1}
          onKapat={() => {}}
          {...props}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/** Tablodaki fırsat satırları (başlık satırı hariç). */
async function satirlar() {
  const tablo = await screen.findByRole('table')

  return within(tablo).getAllByRole('row').slice(1)
}

describe('doğru satırlar', () => {
  it('KP1. Yalnızca ölçüte giren fırsatlar listelenir', async () => {
    listMatches.mockResolvedValue(
      sayfa([
        esles({ verdict: 'Eligible', opportunityTitle: 'Uygun Çağrı' }),
        esles({ verdict: 'NotEligible', opportunityTitle: 'Elenen Çağrı' }),
      ]),
    )

    goster({ anahtar: 'uygun', karttakiSayi: 1 })

    expect(await screen.findByText('Uygun Çağrı')).toBeTruthy()
    expect(screen.queryByText('Elenen Çağrı')).toBeNull()
  })

  it('KP2. Boş kırılımda tablo yerine açıklama gösterilir', async () => {
    listMatches.mockResolvedValue(sayfa([esles({ verdict: 'NotEligible' })]))

    goster({ anahtar: 'uygun', karttakiSayi: 0 })

    expect(await screen.findByText(/Bu ölçüte giren fırsat yok/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('KP3. Aciliyet listesinde en yakın tarih başta gelir', async () => {
    listMatches.mockResolvedValue(
      sayfa([
        esles({ daysUntilDeadline: 12, opportunityTitle: 'Uzak Çağrı' }),
        esles({ daysUntilDeadline: 2, opportunityTitle: 'Yakın Çağrı' }),
      ]),
    )

    goster({ anahtar: 'kapanan', karttakiSayi: 2 })

    const ilk = (await satirlar())[0]
    expect(within(ilk).getByText('Yakın Çağrı')).toBeTruthy()
  })
})

describe('toplam gösteren kartlar', () => {
  it('KP4. Belge kartında hem adet hem fırsat sayısı yazılır', async () => {
    // "6" belge üç fırsattan gelebilir; yalnızca biri yazılsaydı liste toplamı
    // açıklamazdı.
    listMatches.mockResolvedValue(
      sayfa([
        esles({ missingMandatoryDocumentCount: 4 }),
        esles({ missingMandatoryDocumentCount: 2 }),
      ]),
    )

    goster({ anahtar: 'belge', karttakiSayi: 6 })

    expect(await screen.findByText(/6 adet · 2 fırsat/)).toBeTruthy()
  })

  it('KP5. Toplam kartında satırın katkısı ayrı sütunda görünür', async () => {
    listMatches.mockResolvedValue(sayfa([esles({ dataGapCount: 3 })]))

    goster({ anahtar: 'bosluk', karttakiSayi: 3 })

    const tablo = await screen.findByRole('table')
    expect(within(tablo).getByRole('columnheader', { name: 'Veri boşluğu' })).toBeTruthy()
  })

  it('KP6. Sayım kartında ek sütun YOKTUR', async () => {
    // Uygun fırsat sayımında her satır bire bir katkıdır; "1" yazan bir sütun
    // eklemek gürültü olurdu.
    listMatches.mockResolvedValue(sayfa([esles({ verdict: 'Eligible' })]))

    goster({ anahtar: 'uygun', karttakiSayi: 1 })

    const tablo = await screen.findByRole('table')
    expect(within(tablo).queryByRole('columnheader', { name: 'Veri boşluğu' })).toBeNull()
  })
})

describe('kart ile liste tutarlılığı', () => {
  it('KP7. Sayılar tutuyorsa uyarı ÇIKMAZ', async () => {
    listMatches.mockResolvedValue(sayfa([esles({ verdict: 'Eligible' })]))

    goster({ anahtar: 'uygun', karttakiSayi: 1 })

    await screen.findByRole('table')
    expect(screen.queryByText(/tutmuyor/)).toBeNull()
  })

  it('KP8. Sayılar tutmuyorsa UYARI ÇIKAR', async () => {
    // Pano ile liste ayrı anlarda yüklenir; arada yeniden skorlama olabilir.
    listMatches.mockResolvedValue(sayfa([esles({ verdict: 'Eligible' })]))

    goster({ anahtar: 'uygun', karttakiSayi: 3 })

    expect(await screen.findByText(/tutmuyor/)).toBeTruthy()
  })

  it('KP9. Ortalama kartı EKRANDAKİ basamakla karşılaştırılır', async () => {
    // Sunucu 54.45 döner, kart 54.5 gösterir. Ham karşılaştırma yapılsaydı her
    // ortalama kartı yanlışlıkla "tutmuyor" derdi.
    listMatches.mockResolvedValue(
      sayfa([esles({ finalScore: 54.4 }), esles({ finalScore: 54.5 })]),
    )

    goster({ anahtar: 'ortalama', karttakiSayi: 54.45 })

    await screen.findByRole('table')
    expect(screen.queryByText(/tutmuyor/)).toBeNull()
  })
})

describe('veri yükleme', () => {
  it('KP10. Tek sayfaya sığmayan liste TAMAMEN alınır', async () => {
    // Yalnızca ilk sayfa alınsaydı liste sessizce eksik kalır ve kartla tutmazdı.
    listMatches
      .mockResolvedValueOnce({
        items: [esles({ verdict: 'Eligible', opportunityTitle: 'İlk Sayfa' })],
        totalCount: 2,
        page: 1,
        pageSize: 200,
        totalPages: 2,
      })
      .mockResolvedValueOnce({
        items: [esles({ verdict: 'Eligible', opportunityTitle: 'İkinci Sayfa' })],
        totalCount: 2,
        page: 2,
        pageSize: 200,
        totalPages: 2,
      })

    goster({ anahtar: 'uygun', karttakiSayi: 2 })

    expect(await screen.findByText('İlk Sayfa')).toBeTruthy()
    expect(screen.getByText('İkinci Sayfa')).toBeTruthy()
    expect(listMatches).toHaveBeenCalledTimes(2)
  })

  it('KP11. Boş dönen sayfa DÖNGÜYÜ KIRAR', async () => {
    // Sunucu beklenenden az kayıt dönerse döngü sonsuza girmemelidir.
    listMatches
      .mockResolvedValueOnce({
        items: [esles({ verdict: 'Eligible' })],
        totalCount: 99,
        page: 1,
        pageSize: 200,
        totalPages: 3,
      })
      .mockResolvedValue({ items: [], totalCount: 99, page: 2, pageSize: 200, totalPages: 3 })

    goster({ anahtar: 'uygun', karttakiSayi: 99 })

    await screen.findByRole('table')
    await waitFor(() => expect(listMatches).toHaveBeenCalledTimes(2))
  })

  it('KP12. Hata durumunda tablo değil hata gösterilir', async () => {
    listMatches.mockRejectedValue(new Error('Sunucuya ulaşılamadı'))

    goster({ anahtar: 'uygun', karttakiSayi: 1 })

    expect(await screen.findByText(/Sunucuya ulaşılamadı/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
})
