import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ErpConnection, ErpFieldMap } from '@/api/types'
import ErpConnectionPage from './ErpConnectionPage'

/**
 * ERP bağlantı ekranı.
 *
 * Ekranın üç sözü var ve testler üçünü de sabitliyor:
 *
 *  1. **Kayıtlı kimlik geri gösterilmez.** Gösterilen bir sır ekran görüntüsüne ve
 *     tarayıcı geçmişine düşer; alan boş kalır ve boş gönderim mevcut kimliği korur.
 *  2. **Eşleme düzenlenebilir ve kaydedilir.** Kullanıcı ERP'deki gerçek alan adını
 *     yazabilmeli; yazdığı değer kaydetme isteğine girmeli.
 *  3. **Bulunamayan alanlar işaretlenir.** Kullanıcı hangi satırı düzelteceğini
 *     aramak zorunda kalmamalı.
 */

const getErpConnection = vi.fn()
const upsertErpConnection = vi.fn()
const pullErpProfile = vi.fn()
const getErpFieldMapDefaults = vi.fn()

vi.mock('@/api/client', () => ({
  api: {
    getErpConnection: (id: string) => getErpConnection(id),
    upsertErpConnection: (id: string, body: unknown) => upsertErpConnection(id, body),
    pullErpProfile: (id: string) => pullErpProfile(id),
    getErpFieldMapDefaults: (vendor: string) => getErpFieldMapDefaults(vendor),
  },
}))

vi.mock('@/app/contexts', () => ({
  useCompanies: () => ({ selectedCompanyId: 'c1' }),
}))

const esleme: ErpFieldMap = {
  annualRevenue: 'mali.yillikCiro',
  balanceSize: null,
  equity: null,
  exportRevenue: null,
  employeeCount: 'personel.toplam',
  womenEmployeeCount: 'personel.kadin',
  youngEmployeeCount: null,
  rAndDEmployeeCount: null,
  disabledEmployeeCount: null,
  youngEmployeeMaxAge: null,
  certificates: 'belgeler',
  certificateCodeField: 'kod',
  certificateValidUntilField: null,
}

const baglanti: ErpConnection = {
  id: 'e1',
  companyId: 'c1',
  vendor: 'Logo',
  baseUrl: 'https://erp.ornek.com/api',
  authMode: 'ApiKeyHeader',
  hasSecret: true,
  isOnPremise: false,
  isEnabled: true,
  fieldMap: esleme,
  lastRunAt: '2026-09-11T02:45:00Z',
  lastRunStatus: 'Succeeded',
  lastRunMessage: 'Güncellenen bölümler: Workforce.',
  consecutiveFailureCount: 0,
}

function ekranaBas() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ErpConnectionPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ErpConnectionPage', () => {
  beforeEach(() => {
    getErpConnection.mockReset().mockResolvedValue(baglanti)
    upsertErpConnection.mockReset().mockResolvedValue(baglanti)
    pullErpProfile.mockReset()
    getErpFieldMapDefaults.mockReset()
  })

  afterEach(cleanup)

  it('EW1. Kayıtlı kimlik bilgisi ekranda GÖSTERİLMEZ', async () => {
    // Alan yardım ipucu taşıyan bir etiketle geliyor; sorgu doğrudan alanın kendisine
    // yapılır ki test etiket biçimine değil, DAVRANIŞA baksın.
    const { container } = ekranaBas()

    await waitFor(() => expect(container.querySelector('#secret')).toBeTruthy())

    const alan = container.querySelector('#secret') as HTMLInputElement

    expect(alan.value).toBe('')
    expect(alan.type).toBe('password')
    expect(screen.getByText(/Boş bırakırsanız korunur/)).toBeTruthy()
  })

  it('EW2. Eşleme alanları kayıtlı değerlerle DOLU gelir', async () => {
    ekranaBas()

    await waitFor(() => expect(screen.getByLabelText('Kadın çalışan')).toBeTruthy())

    expect((screen.getByLabelText('Kadın çalışan') as HTMLInputElement).value)
      .toBe('personel.kadin')
    expect((screen.getByLabelText('Yıllık ciro') as HTMLInputElement).value)
      .toBe('mali.yillikCiro')

    // Tanımsız alan boş gelir, uydurulmuş bir değer gösterilmez.
    expect((screen.getByLabelText('Özkaynak') as HTMLInputElement).value).toBe('')
  })

  it('EW3. Değiştirilen eşleme KAYDETME isteğine girer', async () => {
    ekranaBas()

    await waitFor(() => expect(screen.getByLabelText('Kadın çalışan')).toBeTruthy())

    await act(async () => {
      fireEvent.change(screen.getByLabelText('Kadın çalışan'), {
        target: { value: 'ik.kadinSayisi' },
      })
    })

    await act(async () => {
      fireEvent.click(screen.getByText('Kaydet'))
    })

    await waitFor(() => expect(upsertErpConnection).toHaveBeenCalled())

    const [, govde] = upsertErpConnection.mock.calls[0]

    expect((govde as { fieldMap: ErpFieldMap }).fieldMap.womenEmployeeCount)
      .toBe('ik.kadinSayisi')

    // Kimlik alanı boş bırakıldı; mevcut kimliğin korunması için null gitmeli.
    expect((govde as { secret: string | null }).secret).toBeNull()
  })

  it('EW4. Boşaltılan satır null olarak gider — ERP okumasın diye', async () => {
    ekranaBas()

    await waitFor(() => expect(screen.getByLabelText('Belgeler')).toBeTruthy())

    await act(async () => {
      fireEvent.change(screen.getByLabelText('Belgeler'), { target: { value: '   ' } })
    })

    await act(async () => {
      fireEvent.click(screen.getByText('Kaydet'))
    })

    await waitFor(() => expect(upsertErpConnection).toHaveBeenCalled())

    const [, govde] = upsertErpConnection.mock.calls[0]

    expect((govde as { fieldMap: ErpFieldMap }).fieldMap.certificates).toBeNull()
  })

  it('EW5. Ürün varsayılanı SUNUCUDAN alınır', async () => {
    // Varsayılanları istemciye kopyalamak, iki tarafın zamanla ayrışması demek olurdu.
    getErpFieldMapDefaults.mockResolvedValue({ ...esleme, womenEmployeeCount: 'personel.kadin' })

    ekranaBas()

    await waitFor(() => expect(screen.getByText('Ürün varsayılanını yükle')).toBeTruthy())

    await act(async () => {
      fireEvent.click(screen.getByText('Ürün varsayılanını yükle'))
    })

    await waitFor(() => expect(getErpFieldMapDefaults).toHaveBeenCalledWith('Logo'))
  })

  it('EW6. Bulunamayan alanlar deneme sonrası İŞARETLENİR', async () => {
    pullErpProfile.mockResolvedValue({
      companyId: 'c1',
      status: 'Failed',
      updatedSections: [],
      missingFields: ['Kadın çalışan', 'Genç çalışan'],
      message: 'ERP yanıtında beklenen alanlar bulunamadı.',
    })

    ekranaBas()

    await waitFor(() => expect(screen.getByText('Şimdi Dene ve Çek')).toBeTruthy())

    await act(async () => {
      fireEvent.click(screen.getByText('Şimdi Dene ve Çek'))
    })

    await waitFor(() => expect(screen.getAllByText('ERP yanıtında bulunamadı').length).toBe(2))

    // Kullanıcı hangi satırı düzelteceğini aramak zorunda kalmamalı.
    expect((screen.getByLabelText('Kadın çalışan') as HTMLInputElement)
      .getAttribute('aria-invalid')).toBe('true')
    expect((screen.getByLabelText('Yıllık ciro') as HTMLInputElement)
      .getAttribute('aria-invalid')).toBe('false')
  })

  it('EW8. Bağlantı YOKKEN hata gösterilmez, boş form açılır', async () => {
    // Sunucu bağlantı yoksa 204 döner ve istemci null görür. "Bağlantı yok" bir hata
    // değil, kurulumun başlangıç durumudur; ekranda "data is undefined" yazamaz.
    getErpConnection.mockResolvedValue(null)

    const { container } = ekranaBas()

    await waitFor(() => expect(screen.getByText('Bağlantı Ayarları')).toBeTruthy())

    expect(screen.queryByText(/undefined/)).toBeNull()

    // Alanlar boş ve düzenlenebilir gelir; kullanıcı kuruluma başlayabilir.
    expect((container.querySelector('#baseUrl') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Kadın çalışan') as HTMLInputElement).value).toBe('')

    // Bağlantı kurulmadan deneme çekimi yapılamaz.
    expect((screen.getByText('Şimdi Dene ve Çek') as HTMLButtonElement).disabled).toBe(true)
  })

  it('EW7. Durdurulmuş bağlantının sebebi yazılır', async () => {
    getErpConnection.mockResolvedValue({
      ...baglanti,
      isEnabled: false,
      lastRunStatus: 'Failed',
      lastRunMessage: 'ERP kimlik bilgisi kabul edilmedi.',
    })

    ekranaBas()

    await waitFor(() => expect(screen.getByText(/bağlantı durduruldu/)).toBeTruthy())

    expect(screen.getByText(/hesabı kilitletir/)).toBeTruthy()
  })
})
