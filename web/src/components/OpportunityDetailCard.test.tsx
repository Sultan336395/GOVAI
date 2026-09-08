import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { OpportunityDetail, OpportunityProvenance } from '@/api/types'
import { OpportunityDetailCard } from './OpportunityDetailCard'

/**
 * Fırsat detay ekranı (Faz 2).
 *
 * Ekran kullanıcının karar verdiği yerdir. Bu testler üç şeyi sabitler:
 * eksik alan asla boş görünmez, doğrulanmamış bağlantı asla düğme olmaz ve
 * Türkçe karakterler bozulmadan çıkar.
 */

afterEach(cleanup)

const KANIT: OpportunityProvenance = {
  sourceId: '01a05d9e-0000-7000-a000-000000000001',
  sourceName: 'Resmî Gazete İhale İlanları',
  sourceCategory: 'Tender',
  sourceVerified: true,
  sourceVerifiedAt: '2026-08-27T06:00:00Z',
  sourceHealth: 'Healthy',
  officialDomain: 'resmigazete.gov.tr',
  officialUrl: 'https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-1.htm',
  officialUrlRejectionReason: null,
  documentId: '01a05d9e-0000-7000-a000-000000000002',
  documentVersion: 1,
  canonicalUrl: 'https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/2026/08/20260827-3-1.htm',
  contentHash: 'a'.repeat(64),
  normalizedTextHash: 'b'.repeat(64),
  charset: 'utf-8',
  mediaType: 'text/html',
  retrievedAt: '2026-08-27T06:05:00Z',
  parseStatus: 'Parsed',
  requiresOcr: false,
  pageCount: 1,
  parseError: null,
  evidence: [
    {
      sequenceNumber: 1,
      pageNumber: 1,
      sectionTitle: 'İhale konusu',
      paragraphNumber: 1,
      text: 'Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR.',
      startOffset: 0,
      endOffset: 78,
      textHash: 'c'.repeat(64),
    },
  ],
}

const FIRSAT: OpportunityDetail = {
  id: '01a05d9e-0000-7000-a000-000000000003',
  title: 'Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğünden: TAŞINMAZ SATILACAKTIR',
  publisher: 'Mersin Su ve Kanalizasyon İdaresi Genel Müdürlüğü',
  summary: 'Taşınmaz satış ihalesi; şartname bedeli ve geçici teminat ilânda belirtilmiştir.',
  sourceUrl: KANIT.officialUrl,
  sourceType: 'TenderPortal',
  supportCategory: 'Tender',
  publishedAt: '2026-08-27T00:00:00Z',
  deadline: '2026-09-15T07:00:00Z',
  daysUntilDeadline: 19,
  budget: { minAmount: null, maxAmount: 4250000, currency: 'TRY', supportRate: null },
  budgetItems: [],
  budgetRates: [],
  legalBasis: null,
  ruleExtractionConfidence: 0.8,
  isReviewedByConsultant: false,
  rules: [
    {
      id: '01a05d9e-0000-7000-a000-000000000004',
      field: 'Company.Cities',
      operator: 'In',
      value: 'Mersin',
      dimension: 'Region',
      severity: 'Blocking',
      humanReadable: 'İhale yeri Mersin ilidir.',
      sourceExcerpt: 'İhale, İdare binasında yapılacaktır.',
      confidence: 0.9,
      isManuallyOverridden: false,
    },
  ],
  documentChecklist: [
    {
      code: 'GECICI_TEMINAT',
      name: 'Geçici teminat mektubu',
      isMandatory: true,
      issuingAuthority: 'Banka',
      notes: null,
    },
  ],
  fieldAvailability: {
    deadline: 'Provided',
    budget: 'Provided',
    currency: 'Provided',
    eligibleApplicant: 'NotProvided',
    geography: 'Provided',
    sector: 'NotProvided',
    programmeType: 'Provided',
    officialDocumentUrl: 'Provided',
  },
  isOpen: true,
  provenance: KANIT,
}

/**
 * Türlü bütçe kalemleri (Faz 3 / A1).
 *
 * Gerçek KOSGEB "KOBİ Dijital Dönüşüm" sayfasından alınmış tutarlar: hibe üst limiti
 * 1.500.000 TL ile kredi üst limiti 20.000.000 TL aynı belgede geçiyor. Eski sürüm
 * en büyüğünü alıp 20 milyonu hibe gibi gösteriyordu.
 */
const KALEMLI: OpportunityDetail = {
  ...FIRSAT,
  budgetItems: [
    {
      type: 'GrantCeiling',
      label: 'Hibe üst limiti',
      amount: 1500000,
      currency: 'TRY',
      excerpt: "geri ödemesiz destek üst limiti 1.500.000 TL'dir",
      startOffset: 120,
      endOffset: 168,
      needsReview: false,
    },
    {
      type: 'CreditCeiling',
      label: 'Kredi üst limiti',
      amount: 20000000,
      currency: 'TRY',
      excerpt: 'İşletme Başına Kredi Üst Limiti: 20.000.000 TL',
      startOffset: 169,
      endOffset: 214,
      needsReview: false,
    },
    {
      type: 'Undetermined',
      label: 'Türü belirlenemedi',
      amount: 1000000,
      currency: 'TRY',
      excerpt: "Makine-Teçhizat Desteği üst limiti 1.000.000 TL'dir",
      startOffset: 300,
      endOffset: 350,
      needsReview: true,
    },
  ],
  budgetRates: [
    {
      type: 'SupportRate',
      label: 'Destek oranı',
      rate: 0.6,
      excerpt: "destek oranı %60'tır",
      startOffset: 400,
      endOffset: 419,
      needsReview: false,
    },
    {
      type: 'OwnContributionRate',
      label: 'Öz kaynak / ortaklık payı',
      rate: 0.5,
      excerpt: 'girişimcinin ortaklık payı en az %50 olmalıdır',
      startOffset: 500,
      endOffset: 545,
      needsReview: false,
    },
    {
      type: 'Undetermined',
      label: 'Türü belirlenemedi',
      rate: 0.2,
      excerpt: 'Fiyatlara %20 KDV dahildir',
      startOffset: 600,
      endOffset: 626,
      needsReview: true,
    },
  ],
}

/** Bir kalemin kendi kutusunu başlığından bulur; kanıt o kutunun içinde olmalıdır. */
function kalemKutusu(ad: string): HTMLElement {
  const kutu = document.querySelector(`[data-butce-kalemi="${ad}"]`)
  if (!kutu) throw new Error(`Bütçe kalemi bulunamadı: ${ad}`)
  return kutu as HTMLElement
}

describe('Fırsat detay ekranı', () => {
  it('W1. Kısa açıklama, sektör, koşullar ve tarihler gösterilir', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    expect(screen.getByText(/şartname bedeli ve geçici teminat/)).toBeTruthy()

    // Koşul hem "Coğrafi kapsam" alanında hem koşul tablosunda görünür; ikisi de doğrudur.
    expect(screen.getAllByText('İhale yeri Mersin ilidir.').length).toBeGreaterThan(0)
    expect(screen.getByText('Geçici teminat mektubu')).toBeTruthy()
    expect(screen.getByText('Yayın tarihi')).toBeTruthy()
    expect(screen.getByText('Son başvuru tarihi')).toBeTruthy()
  })

  it('W1b. Kaynak türü Türkçe gösterilir, ham enum sızmaz', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    const govde = document.body.textContent ?? ''

    // Bir danışman "TenderPortal" ifadesinden ne anlaması gerektiğini bilemez.
    expect(govde).toContain('İhale portalı')
    expect(govde).not.toContain('TenderPortal')
    expect(govde).not.toContain('OfficialGazette')
  })

  it('W2. Türkçe karakterler bozulmadan görüntülenir', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('TAŞINMAZ SATILACAKTIR')
    expect(govde).toContain('İhale konusu')

    // Bozulma imzalarının hiçbiri ekranda olmamalı.
    for (const bozuk of ['Ä°', 'ÅŸ', 'ÄŸ', 'Ã¼', 'Ã§', 'Ã¶', '�']) {
      expect(govde).not.toContain(bozuk)
    }
  })

  it('W3. Eksik alanlar "Resmî kaynakta belirtilmemiş" gösterilir', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    // Sektör ve başvurabilecek şirket türü resmî kaynakta yok.
    expect(screen.getAllByText('Resmî kaynakta belirtilmemiş').length).toBeGreaterThan(0)
  })

  it('W4. Uygulanamaz alan ile kaynakta olmayan alan ayrı gösterilir', () => {
    const surekli: OpportunityDetail = {
      ...FIRSAT,
      deadline: null,
      daysUntilDeadline: null,
      fieldAvailability: { ...FIRSAT.fieldAvailability, deadline: 'NotApplicable' },
    }

    render(<OpportunityDetailCard data={surekli} />)

    // Sürekli açık çağrıda son başvuru "eksik veri" değildir; doğru ve nihai cevaptır.
    expect(screen.getByText('Bu çağrı için geçerli değil')).toBeTruthy()
  })

  it('W5. Doğrulanmış resmî bağlantı düğmesi yeni sekmede ve güvenli açılır', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    const dugme = screen.getByRole('link', { name: /Resmî kaynağa git/ })

    expect(dugme.getAttribute('href')).toBe(KANIT.officialUrl)
    expect(dugme.getAttribute('target')).toBe('_blank')
    expect(dugme.getAttribute('rel')).toContain('noopener')
    expect(dugme.getAttribute('rel')).toContain('noreferrer')
  })

  it('W6. Doğrulanmamış bağlantı için düğme HİÇ gösterilmez', () => {
    const sahte: OpportunityDetail = {
      ...FIRSAT,
      provenance: {
        ...KANIT,
        officialUrl: null,
        officialUrlRejectionReason:
          "Adresin sunucusu ('resmigazete.gov.tr.kotu-site.com') kaynağın resmî alan adında değil.",
      },
    }

    render(<OpportunityDetailCard data={sahte} />)

    expect(screen.queryByRole('link', { name: /Resmî kaynağa git/ })).toBeNull()
    expect(screen.getByText(/doğrulanamadı/)).toBeTruthy()
    // Sebep kullanıcıdan gizlenmez.
    expect(document.body.textContent).toContain('kotu-site.com')
  })

  it('W7. Süresi geçmiş fırsat açık gibi gösterilmez', () => {
    const gecmis: OpportunityDetail = { ...FIRSAT, isOpen: false, daysUntilDeadline: -3 }

    render(<OpportunityDetailCard data={gecmis} />)

    expect(screen.getByText(/son başvuru tarihi geçti/i)).toBeTruthy()
  })

  it('W8. Kanıt parçası sayfa, bölüm, aralık ve hash ile birlikte gösterilir', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    expect(screen.getByText('İhale konusu')).toBeTruthy()
    expect(screen.getByText('0–78')).toBeTruthy()
    expect(screen.getByText(/Belge sürümü/)).toBeTruthy()

    // Hash metni <code> içinde ayrı bir düğümde durur.
    const hashler = screen.getAllByText('c'.repeat(64))
    expect(hashler.length).toBeGreaterThan(0)
  })

  it('W9. Kanıtı olmayan kayıt bunu açıkça söyler', () => {
    const kanitsiz: OpportunityDetail = {
      ...FIRSAT,
      provenance: { ...KANIT, evidence: [] },
    }

    render(<OpportunityDetailCard data={kanitsiz} />)

    expect(screen.getByText(/Kanıtsız bilgi resmî sayılmaz/)).toBeTruthy()
  })

  it('W10. OCR gereken belge tam içerikli gibi gösterilmez', () => {
    const ocr: OpportunityDetail = {
      ...FIRSAT,
      provenance: {
        ...KANIT,
        requiresOcr: true,
        parseStatus: 'NeedsOcr',
        parseError:
          "PDF'in metin katmanı eksik: 1 sayfadan yalnızca 168 karakter çıktı.",
      },
    }

    render(<OpportunityDetailCard data={ocr} />)

    expect(screen.getByText(/metin katmanı yetersiz/)).toBeTruthy()
    expect(document.body.textContent).toContain('168 karakter')
  })

  it('W11. Kaynak doğrulama durumu ve belge sürümü ekranda yer alır', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('Resmî Gazete İhale İlanları')
    expect(govde).toContain('Doğrulandı')
    expect(govde).toContain('v1')
    expect(govde).toContain('a'.repeat(64))
  })

  it('W12. Kaynak belgesi olmayan kayıt kanıt bölümünü uydurmaz', () => {
    const elle: OpportunityDetail = { ...FIRSAT, provenance: null }

    render(<OpportunityDetailCard data={elle} />)

    expect(screen.getByText(/kanıt zinciri gösterilemiyor/)).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Resmî kaynağa git/ })).toBeNull()
  })

  it('W13. Koşul tablosu kaynak metni ile birlikte gelir', () => {
    render(<OpportunityDetailCard data={FIRSAT} />)

    const tablolar = screen.getAllByRole('table')
    const kosulTablosu = tablolar[0]

    expect(within(kosulTablosu).getByText('İhale yeri Mersin ilidir.')).toBeTruthy()
    expect(within(kosulTablosu).getByText('İhale, İdare binasında yapılacaktır.')).toBeTruthy()
  })

  it('W14. Hibe ve kredi tutarları ayrı başlıklarla görünür', () => {
    render(<OpportunityDetailCard data={KALEMLI} />)

    // Asıl hata: 20 milyonluk kredi hibe diye gösteriliyordu. İki tutar iki ayrı
    // başlık altında olmalı ve değerleri karışmamalı.
    // Tutar hem değer satırında hem kanıt alıntısında geçer; ikisi de aynı kutudadır.
    expect(within(kalemKutusu('Hibe üst limiti')).getAllByText(/1\.500\.000/).length).toBeGreaterThan(0)
    expect(
      within(kalemKutusu('Kredi üst limiti')).getAllByText(/20\.000\.000/).length,
    ).toBeGreaterThan(0)

    expect(within(kalemKutusu('Hibe üst limiti')).queryAllByText(/20\.000\.000/)).toHaveLength(0)
    expect(within(kalemKutusu('Kredi üst limiti')).queryAllByText(/1\.500\.000/)).toHaveLength(0)
  })

  it('W15. Türü belirlenemeyen tutar kesin hibe gibi gösterilmez', () => {
    render(<OpportunityDetailCard data={KALEMLI} />)

    const belirsiz = kalemKutusu('Türü belirlenemedi')

    expect(within(belirsiz).getAllByText(/1\.000\.000/).length).toBeGreaterThan(0)
    // 1.000.000 TL yalnızca "Türü belirlenemedi" kutusunda geçer; hibe kutusunda değil.
    expect(within(kalemKutusu('Hibe üst limiti')).queryAllByText(/1\.000\.000/)).toHaveLength(0)
    expect(belirsiz.textContent).not.toContain('Hibe')
  })

  it('W16. NeedsReview kullanıcıya anlaşılır Türkçe ifadeyle gösterilir', () => {
    render(<OpportunityDetailCard data={KALEMLI} />)

    const belirsiz = kalemKutusu('Türü belirlenemedi')

    // Kısaltma veya İngilizce alan adı değil, ne yapması gerektiğini söyleyen cümle.
    expect(belirsiz.textContent).toContain('hibe olarak kabul etmeyin')
    expect(belirsiz.textContent).toContain('belgeden doğrulayın')

    const govde = document.body.textContent ?? ''
    expect(govde).not.toContain('NeedsReview')
    expect(govde).not.toContain('Undetermined')
  })

  it('W17. Para birimi ve destek oranı doğru görüntülenir', () => {
    render(<OpportunityDetailCard data={KALEMLI} />)

    // Para birimi bir kez yazılır; "₺1.500.000 TRY" iki kez para birimi taşıyordu.
    const hibe = kalemKutusu('Hibe üst limiti').textContent ?? ''
    expect(hibe).toContain('₺')
    expect(hibe).not.toContain('TRY')

    // Destek oranı ile öz kaynak payı ayrı başlıklardadır; %50 destek oranı değildir.
    expect(within(kalemKutusu('Destek oranı')).getByText('%60')).toBeTruthy()
    expect(within(kalemKutusu('Öz kaynak / ortaklık payı')).getByText('%50')).toBeTruthy()
    expect(kalemKutusu('Destek oranı').textContent).not.toContain('%50')

    // Türü belirlenemeyen oran (KDV) hiç gösterilmez.
    expect(document.body.textContent).not.toContain('KDV')
  })

  it('W17b. Avro cinsinden kalem TL sembolüyle gösterilmez', () => {
    const avro: OpportunityDetail = {
      ...KALEMLI,
      budgetItems: [{ ...KALEMLI.budgetItems[0], currency: 'EUR', amount: 250000 }],
      budgetRates: [],
    }

    render(<OpportunityDetailCard data={avro} />)

    const kutu = kalemKutusu('Hibe üst limiti').textContent ?? ''

    expect(kutu).toContain('€')
    expect(kutu).not.toContain('₺')
  })

  it('W18. Kanıt bağlantısı ilgili bütçe kalemine bağlıdır', () => {
    render(<OpportunityDetailCard data={KALEMLI} />)

    const hibe = kalemKutusu('Hibe üst limiti').textContent ?? ''
    const kredi = kalemKutusu('Kredi üst limiti').textContent ?? ''

    // Her alıntı kendi kaleminin kutusunda; kredi cümlesi hibenin altına düşmemeli.
    expect(hibe).toContain('geri ödemesiz destek üst limiti')
    expect(hibe).not.toContain('Kredi Üst Limiti')

    expect(kredi).toContain('İşletme Başına Kredi Üst Limiti')
    expect(kredi).not.toContain('geri ödemesiz')

    // Kanıt belgedeki yerine bağlıdır; danışman tutarı belgede bulabilir.
    expect(hibe).toContain('120–168')
    expect(kredi).toContain('169–214')
  })

  it('W18b. Alıntısı olmayan kalem kanıt uydurmaz', () => {
    const alintisiz: OpportunityDetail = {
      ...KALEMLI,
      budgetItems: [{ ...KALEMLI.budgetItems[0], excerpt: null }],
      budgetRates: [],
    }

    render(<OpportunityDetailCard data={alintisiz} />)

    expect(kalemKutusu('Hibe üst limiti').textContent).toContain('Resmî kaynakta belirtilmemiş')
  })

  it('W19. Eski tek bütçe alanlı kayıtlar ekranı bozmaz', () => {
    // FIRSAT, türlü kalem çıkarımından önce ayrıştırılmış bir kaydı temsil eder:
    // budgetItems ve budgetRates boştur, yalnızca eski tek bütçe alanı doludur.
    render(<OpportunityDetailCard data={FIRSAT} />)

    expect(screen.getByText('Destek / bütçe tutarı')).toBeTruthy()
    expect(document.body.textContent).toContain('4.250.000')

    // Boş kalem listesi için başlıksız boş bir kutu açılmaz.
    expect(screen.queryByText('Belgeden çıkarılan bütçe kalemleri')).toBeNull()
    expect(document.querySelector('[data-butce-kalemi]')).toBeNull()
  })
})
