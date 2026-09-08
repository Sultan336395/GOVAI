import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type {
  AnalysisContribution,
  AnalysisVersion,
  CriterionResult,
  OpportunityAnalysis,
  RegulationImpactAnalysis,
} from '@/api/types'
import { OpportunityAnalysisPanel, RegulationImpactPanel } from './AnalysisPanel'

/**
 * DeepTech analiz paneli (Faz 3 — Aşama 3).
 *
 * Bu testler ekranın üç sözünü sabitler:
 *
 * 1. Kullanıcı "bu sonuç neden çıktı?" sorusunun cevabını ekranda bulabilir.
 * 2. Düşük güvenli ya da modelsiz sonuç kesin gibi görünmez.
 * 3. Puan hiçbir yerde "kazanma ihtimali" olarak adlandırılmaz.
 *
 * Masaüstü ve mobil ayrı ayrı sınanır: mobilde geniş içerik (puan kırılımı tablosu)
 * kendi kutusunda kaymalı, sayfayı yana kaydırmamalıdır.
 */

afterEach(cleanup)

const SURUM: AnalysisVersion = {
  analysisRunId: '01a05d9e-0000-7000-a000-000000000001',
  companyProfileVersion: 7,
  financialDataVersion: 3,
  ruleSetVersion: '2026.09.1',
  promptVersion: null,
  modelProvider: null,
  modelName: null,
  outputSchemaVersion: null,
  correlationId: 'korelasyon-1',
  startedAt: '2026-09-08T09:00:00Z',
  completedAt: '2026-09-08T09:00:02Z',
  status: 'CompletedWithoutAi',
}

const MODELSIZ_KATKI: AnalysisContribution = {
  hasAiContribution: false,
  aiStatus: 'AIUnavailable',
  aiStatusLabel: 'Yapay zekâ kullanılamadı',
  aiExplanations: [],
  rejectedClaimCount: 0,
  conflicts: [],
  warning: 'Bu sonuç yalnızca resmî belgedeki kurallara göre hesaplandı; yapay zekâ açıklaması yok.',
}

function kriter(over: Partial<CriterionResult> = {}): CriterionResult {
  return {
    code: 'EMPLOYEE_COUNT',
    name: 'Çalışan sayısı',
    isMandatory: false,
    outcome: 'Met',
    outcomeLabel: 'Sağlanıyor',
    rationale: '1 koşulun tamamı firmanın verisiyle karşılanıyor.',
    companyFields: ['Workforce.EmployeeCount'],
    evidence: [
      {
        evidenceChunkId: null,
        documentVersionId: null,
        excerpt: 'Başvuru sahibinin en az 10 çalışanı olmalıdır.',
        locator: 'Başvuru Şartları',
      },
    ],
    scoreImpact: 1,
    missingOrConflictExplanation: null,
    ruleSetVersion: '2026.09.1',
    group: 'Workforce',
    groupName: 'İş gücü',
    ...over,
  }
}

const SAGLANAN = kriter()

const SAGLANMAYAN = kriter({
  code: 'GEOGRAPHY',
  name: 'Coğrafi kapsam',
  outcome: 'NotMet',
  outcomeLabel: 'Sağlanmıyor',
  rationale: '1 koşuldan 1 tanesi karşılanmıyor: TR62 bölgesi (firma: TR33, beklenen: TR62)',
  group: 'Geography',
  groupName: 'Coğrafya',
})

const EKSIK = kriter({
  code: 'REVENUE_FINANCIALS',
  name: 'Ciro ve mali kriterler',
  outcome: 'Unknown',
  outcomeLabel: 'Bilgi eksik',
  rationale: '1 koşul firma verisi eksik olduğu için değerlendirilemedi.',
  missingOrConflictExplanation:
    'Şu alanlar doldurulmadan bu kriter karara bağlanamaz: Financials.AnnualRevenue.',
  scoreImpact: 0.5,
  group: 'ScaleAndFinancials',
  groupName: 'Ölçek ve mali yapı',
})

const CELISKILI = kriter({
  code: 'SME_SCALE',
  name: 'KOBİ ve ölçek durumu',
  outcome: 'ConflictingEvidence',
  outcomeLabel: 'Belgede çelişki var',
  rationale: 'Resmî belge bu başlıkta birbiriyle çelişen koşullar içeriyor.',
  missingOrConflictExplanation: 'Aynı alan için farklı koşullar yazılmış.',
  scoreImpact: 0.25,
  group: 'ScaleAndFinancials',
  groupName: 'Ölçek ve mali yapı',
})

const ANALIZ: OpportunityAnalysis = {
  companyId: '01a05d9e-0000-7000-a000-000000000010',
  opportunityId: '01a05d9e-0000-7000-a000-000000000011',
  opportunityTitle: 'KOBİ Dijital Dönüşüm Destek Programı',
  evaluatedAt: '2026-09-08T09:00:00Z',
  verdict: 'ConditionallyEligible',
  verdictLabel: 'Şartlı uygun',
  sectorFit: 'Matched',
  score: {
    value: 63.4,
    label: 'Uygunluk puanı',
    components: [
      {
        group: 'Mandatory',
        name: 'Zorunlu kriterler',
        value: 1,
        weight: 0.2,
        contribution: 0.2,
        criterionCount: 1,
        metCount: 1,
        notMetCount: 0,
        unknownCount: 0,
        conflictCount: 0,
        rationale: '1 zorunlu koşulun tamamı sağlanıyor.',
      },
      {
        group: 'SectorNace',
        name: 'Sektör ve NACE',
        value: 1,
        weight: 0.22,
        contribution: 0.22,
        criterionCount: 1,
        metCount: 1,
        notMetCount: 0,
        unknownCount: 0,
        conflictCount: 0,
        rationale: '1 kriterden 1 tanesi sağlanıyor.',
      },
    ],
    hasMandatoryFailure: false,
    missingDataEffect: 8.5,
    ruleSetVersion: '2026.09.1',
  },
  confidence: {
    value: 0.62,
    level: 'Medium',
    levelLabel: 'Orta',
    factors: [
      {
        code: 'PROFILE_COMPLETENESS',
        name: 'Şirket profili doluluğu',
        value: 0.5,
        weight: 0.2,
        explanation: 'Firma verisine dayanan 2 kriterden 1 tanesi eksik.',
        notMeasured: false,
      },
      {
        code: 'AI_EVIDENCE_AGREEMENT',
        name: 'Yapay zekâ–kanıt tutarlılığı',
        value: 0,
        weight: 0.05,
        explanation: 'Bu analizde yapay zekâ katkısı yok; bileşen ölçülmedi.',
        notMeasured: true,
      },
    ],
    ruleSetVersion: '2026.09.1',
  },
  criteria: [SAGLANAN, SAGLANMAYAN, EKSIK, CELISKILI],
  met: [SAGLANAN],
  notMet: [SAGLANMAYAN],
  missing: [EKSIK],
  conflicting: [CELISKILI],
  ruleSetVersion: '2026.09.1',
  contribution: MODELSIZ_KATKI,
  version: SURUM,
}

const ETKI: RegulationImpactAnalysis = {
  companyId: ANALIZ.companyId,
  regulatoryChangeId: '01a05d9e-0000-7000-a000-000000000020',
  regulationTitle: 'Muhtasar ve Prim Hizmet Beyannamesi Süresinin Uzatılması',
  evaluatedAt: '2026-09-08T09:00:00Z',
  impact: 'PotentiallyApplicable',
  impactLabel: 'Kapsaması muhtemel',
  confidence: ANALIZ.confidence,
  criteria: [
    kriter({
      code: 'REG_EMPLOYER_STATUS',
      name: 'Çalışan ve işverenlik durumu',
      outcome: 'Met',
      outcomeLabel: 'Sağlanıyor',
      rationale: 'Belge işverenlere yönelik; firma 20 çalışanla işveren sıfatı taşıyor.',
      group: 'Workforce',
      groupName: 'İş gücü',
    }),
    kriter({
      code: 'REG_TRANSITION',
      name: 'Geçiş süresi',
      outcome: 'NotApplicable',
      outcomeLabel: 'Bu çağrı için geçerli değil',
      rationale: 'Belgede geçiş süresine ilişkin bir ifade bulunamadı.',
      group: 'Timing',
      groupName: 'Tarih',
    }),
  ],
  openQuestions: ['Yürürlük tarihi resmî metinden doğrulanmalıdır.'],
  legalDisclaimer:
    'Bu değerlendirme resmî belgedeki ifadelere dayanan bir ön incelemedir, hukuki görüş değildir.',
  ruleSetVersion: '2026.09.1',
  contribution: MODELSIZ_KATKI,
  version: SURUM,
}

/** Bir kriterin kendi kutusunu koduna göre bulur. */
function kriterKutusu(kod: string): HTMLElement {
  const kutu = document.querySelector(`[data-kriter="${kod}"]`)
  if (!kutu) throw new Error(`Kriter kutusu bulunamadı: ${kod}`)
  return kutu as HTMLElement
}

describe('Fırsat analiz paneli', () => {
  it('AP1. Analiz sonucu, uygunluk durumu ve güven seviyesi birlikte görünür', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    expect(screen.getByText('Uygunluk analizi')).toBeTruthy()
    expect(screen.getByText('Şartlı uygun')).toBeTruthy()
    expect(document.body.textContent).toContain('Güven: Orta')
    expect(document.body.textContent).toContain('63.4')
  })

  it('AP2. Puan "uygunluk puanı" olarak adlandırılır, kazanma ihtimali olarak değil', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('Uygunluk puanı')
    expect(govde).not.toContain('kazanma ihtimali')
    expect(govde).not.toContain('Kazanma')
    expect(govde).not.toContain('olasılık')
  })

  it('AP3. Puan kırılımı her başlığı ağırlığı ve katkısıyla gösterir', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const zorunlu = document.querySelector('[data-kirilim="Mandatory"]')
    const sektor = document.querySelector('[data-kirilim="SectorNace"]')

    expect(zorunlu).toBeTruthy()
    expect(sektor).toBeTruthy()
    expect(zorunlu!.textContent).toContain('Zorunlu kriterler')
    expect(zorunlu!.textContent).toContain('%20')
    expect(sektor!.textContent).toContain('%22')
  })

  it('AP4. Sağlanan, sağlanmayan, eksik ve çelişkili kriterler ayrı başlıklarda listelenir', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    expect(screen.getByText('Sağlanan kriterler')).toBeTruthy()
    expect(screen.getByText('Sağlanmayan kriterler')).toBeTruthy()
    expect(screen.getByText('Eksik bilgiler')).toBeTruthy()
    expect(screen.getByText('Çelişkili kanıtlar')).toBeTruthy()

    expect(kriterKutusu('EMPLOYEE_COUNT').textContent).toContain('Sağlanıyor')
    expect(kriterKutusu('GEOGRAPHY').textContent).toContain('Sağlanmıyor')
    expect(kriterKutusu('REVENUE_FINANCIALS').textContent).toContain('Bilgi eksik')
    expect(kriterKutusu('SME_SCALE').textContent).toContain('Belgede çelişki var')
  })

  it('AP5. "Bu sonuç neden çıktı?" gerekçeyi, kullanılan alanı ve kanıtı açar', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const kutu = kriterKutusu('EMPLOYEE_COUNT')
    const dugme = within(kutu).getByRole('button', { name: 'Bu sonuç neden çıktı?' })

    // Kapalıyken gerekçe ekranı boğmaz.
    expect(document.querySelector('[data-gerekce="EMPLOYEE_COUNT"]')).toBeNull()

    fireEvent.click(dugme)

    const gerekce = document.querySelector('[data-gerekce="EMPLOYEE_COUNT"]')!
    expect(gerekce.textContent).toContain('karşılanıyor')
    expect(gerekce.textContent).toContain('Workforce.EmployeeCount')
    expect(gerekce.textContent).toContain('en az 10 çalışanı olmalıdır')
  })

  it('AP6. Eksik kriterde neyin doldurulacağı yazar', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const kutu = kriterKutusu('REVENUE_FINANCIALS')
    fireEvent.click(within(kutu).getByRole('button', { name: 'Bu sonuç neden çıktı?' }))

    expect(document.querySelector('[data-gerekce="REVENUE_FINANCIALS"]')!.textContent).toContain(
      'Financials.AnnualRevenue',
    )
  })

  it('AP7. Model bağlı değilken sonuç kural tabanlı olduğunu söyler', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const uyari = document.querySelector('[data-uyari="analiz"]')
    expect(uyari).toBeTruthy()
    expect(uyari!.textContent).toContain('yalnızca resmî belgedeki kurallara göre')

    const katki = document.querySelector('[data-alan="yapay-zeka-katkisi"]')!
    expect(katki.textContent).toContain('Yapay zekâ kullanılamadı')
    // "Hibrit çalışıyor" izlenimi verilmez.
    expect(document.body.textContent).not.toContain('hibrit')
  })

  it('AP8. Düşük güvenli analiz kesin sonuç gibi görünmez', () => {
    const dusuk: OpportunityAnalysis = {
      ...ANALIZ,
      confidence: { ...ANALIZ.confidence, level: 'Low', levelLabel: 'Düşük', value: 0.31 },
      contribution: {
        ...MODELSIZ_KATKI,
        warning:
          'Bu sonuç yalnızca resmî belgedeki kurallara göre hesaplandı; yapay zekâ açıklaması yok. '
          + 'Güven seviyesi düşük: eksik bilgi veya doğrulanmamış kaynak nedeniyle bu sonuç kesin '
          + 'kabul edilmemelidir.',
      },
    }

    render(<OpportunityAnalysisPanel data={dusuk} />)

    expect(document.body.textContent).toContain('Güven: Düşük')
    expect(document.querySelector('[data-uyari="analiz"]')!.textContent).toContain(
      'kesin kabul edilmemelidir',
    )
  })

  it('AP9. Zorunlu kriter başarısızlığında puanın sıfırlandığı açıkça yazar', () => {
    const engelli: OpportunityAnalysis = {
      ...ANALIZ,
      verdict: 'NotEligible',
      verdictLabel: 'Uygun değil',
      score: { ...ANALIZ.score, value: 0, hasMandatoryFailure: true, missingDataEffect: 0 },
    }

    render(<OpportunityAnalysisPanel data={engelli} />)

    expect(document.querySelector('[data-alan="puan-ozeti"]')!.textContent).toContain(
      'Zorunlu bir koşul sağlanmadığı için puan sıfırlandı',
    )
  })

  it('AP10. Eksik bilginin puan tavanına etkisi gösterilir', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    expect(document.querySelector('[data-alan="puan-ozeti"]')!.textContent).toContain(
      'en fazla 8.5 artabilir',
    )
  })

  it('AP11. Analiz sürümü ve tarihi künyede görünür', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const kunye = document.querySelector('[data-alan="surum-kunyesi"]')!

    expect(kunye.textContent).toContain('08.09.2026')
    expect(kunye.textContent).toContain('2026.09.1')
    expect(kunye.textContent).toContain('v7')
    expect(kunye.textContent).toContain('Model kullanılmadı')
  })

  it('AP12. Elenen yapay zekâ ifadelerinin sayısı bildirilir, içerikleri gösterilmez', () => {
    const elenmis: OpportunityAnalysis = {
      ...ANALIZ,
      contribution: { ...MODELSIZ_KATKI, rejectedClaimCount: 2 },
    }

    render(<OpportunityAnalysisPanel data={elenmis} />)

    expect(document.querySelector('[data-alan="yapay-zeka-katkisi"]')!.textContent).toContain(
      '2 yapay zekâ ifadesi elendi',
    )
  })

  it('AP13. Kanıtlı yapay zekâ açıklaması gösterilir ve kural katkısından ayrı durur', () => {
    const modelli: OpportunityAnalysis = {
      ...ANALIZ,
      contribution: {
        hasAiContribution: true,
        aiStatus: 'Succeeded',
        aiStatusLabel: 'Yapay zekâ açıklaması eklendi',
        aiExplanations: ['Belge, imalat sektöründeki işletmeleri hedefliyor.'],
        rejectedClaimCount: 1,
        conflicts: [],
        warning: null,
      },
    }

    render(<OpportunityAnalysisPanel data={modelli} />)

    const katki = document.querySelector('[data-alan="yapay-zeka-katkisi"]')!

    expect(katki.textContent).toContain('imalat sektöründeki işletmeleri hedefliyor')
    // Kural gerekçeleri kendi bölümlerinde; yapay zekâ bloğuna karışmaz.
    expect(katki.textContent).not.toContain('Workforce.EmployeeCount')
    expect(document.querySelector('[data-uyari="analiz"]')).toBeNull()
  })

  it('AP14. Kural ile modelin çeliştiği nokta kullanıcıya bildirilir', () => {
    const celiskili: OpportunityAnalysis = {
      ...ANALIZ,
      contribution: {
        ...MODELSIZ_KATKI,
        hasAiContribution: true,
        aiStatus: 'Succeeded',
        aiStatusLabel: 'Yapay zekâ açıklaması eklendi',
        aiExplanations: ['Açıklama.'],
        warning: null,
        conflicts: [
          {
            criterionCode: 'GEOGRAPHY',
            ruleOutcome: 'NotMet',
            claimType: 'SupportsCriterion',
            note: 'Kural sonucu "NotMet" iken model bunun aksini savundu. Kural sonucu korundu.',
          },
        ],
      },
    }

    render(<OpportunityAnalysisPanel data={celiskili} />)

    expect(document.querySelector('[data-alan="yapay-zeka-katkisi"]')!.textContent).toContain(
      'Kural sonucu korundu',
    )
  })

  it('AP15. Türkçe karakterler bozulmadan görüntülenir', () => {
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const govde = document.body.textContent ?? ''

    expect(govde).toContain('Çalışan sayısı')
    expect(govde).toContain('Coğrafi kapsam')
    expect(govde).toContain('KOBİ ve ölçek durumu')
    expect(govde).toContain('Çelişkili kanıtlar')

    for (const bozuk of ['Ä°', 'ÅŸ', 'ÄŸ', 'Ã¼', 'Ã§', 'Ã¶', '�']) {
      expect(govde).not.toContain(bozuk)
    }
  })
})

describe('Mevzuat etki paneli', () => {
  it('AP16. Etki durumu, güven ve hukuki uyarı birlikte görünür', () => {
    render(<RegulationImpactPanel data={ETKI} />)

    expect(screen.getByText('Olası etki analizi')).toBeTruthy()
    expect(screen.getByText('Kapsaması muhtemel')).toBeTruthy()
    expect(document.body.textContent).toContain('hukuki görüş değildir')
  })

  it('AP17. Doğrulanması gereken noktalar listelenir', () => {
    render(<RegulationImpactPanel data={ETKI} />)

    expect(screen.getByText('Doğrulanması gereken noktalar')).toBeTruthy()
    expect(document.body.textContent).toContain('Yürürlük tarihi resmî metinden doğrulanmalıdır')
  })

  it('AP18. Geçerli olmayan kriterler ekranı doldurmaz', () => {
    render(<RegulationImpactPanel data={ETKI} />)

    expect(document.querySelector('[data-kriter="REG_EMPLOYER_STATUS"]')).toBeTruthy()
    expect(document.querySelector('[data-kriter="REG_TRANSITION"]')).toBeNull()
  })

  it('AP19. Mevzuat panelinde uygunluk puanı gösterilmez', () => {
    render(<RegulationImpactPanel data={ETKI} />)

    // Mevzuata başvurulmaz, uyulur: puan burada anlamsızdır ve gösterilmez.
    expect(document.querySelector('[data-alan="puan-ozeti"]')).toBeNull()
    expect(document.body.textContent).not.toContain('Uygunluk puanı')
  })

  it('AP20. Etki panelinde de yapay zekâ katkısı ayrı bildirilir', () => {
    render(<RegulationImpactPanel data={ETKI} />)

    expect(document.querySelector('[data-alan="yapay-zeka-katkisi"]')!.textContent).toContain(
      'Yapay zekâ kullanılamadı',
    )
  })
})

describe('Mobil görünüm', () => {
  /** 375 px genişlik: iPhone SE/12 mini sınıfı, sahadaki en dar ekran. */
  function mobilYap() {
    Object.defineProperty(window, 'innerWidth', { writable: true, configurable: true, value: 375 })
    window.dispatchEvent(new Event('resize'))
  }

  it('AP21. Mobilde tüm ana bölümler görünür kalır', () => {
    mobilYap()
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    expect(screen.getByText('Uygunluk analizi')).toBeTruthy()
    expect(screen.getByText('Puan kırılımı')).toBeTruthy()
    expect(screen.getByText('Sağlanmayan kriterler')).toBeTruthy()
    expect(document.querySelector('[data-alan="surum-kunyesi"]')).toBeTruthy()
  })

  it('AP22. Mobilde puan kırılımı tablosu kendi kutusunda kayar', () => {
    mobilYap()
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    // Geniş tablo sayfayı yana kaydırmamalı; kendi sarmalayıcısında kaymalı.
    const tablo = screen.getAllByRole('table')[0]
    expect(tablo.closest('.table-wrap')).toBeTruthy()
  })

  it('AP23. Mobilde "Bu sonuç neden çıktı?" çalışmaya devam eder', () => {
    mobilYap()
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const kutu = kriterKutusu('GEOGRAPHY')
    fireEvent.click(within(kutu).getByRole('button', { name: 'Bu sonuç neden çıktı?' }))

    expect(document.querySelector('[data-gerekce="GEOGRAPHY"]')!.textContent).toContain('TR62')
  })

  it('AP24. Mobilde uyarı metni kırpılmaz', () => {
    mobilYap()
    render(<OpportunityAnalysisPanel data={ANALIZ} />)

    const uyari = document.querySelector('[data-uyari="analiz"]') as HTMLElement

    expect(uyari.textContent).toBe(MODELSIZ_KATKI.warning)
    expect(uyari.style.overflow).not.toBe('hidden')
  })
})
