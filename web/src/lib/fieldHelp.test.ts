import { describe, expect, it } from 'vitest'
import { fieldHelp } from './fieldHelp'

/**
 * Alan açıklamaları.
 *
 * Kural: burada terimin **sözlük anlamı** durur, sistemin o alanla ne yaptığı değil.
 * İlk sürümde davranış anlatılıyordu ("bu alan skoru şöyle etkiler"); ipuçları
 * okunmayacak kadar uzadı ve kullanıcının asıl sorduğu "bu kelime ne demek?" sorusunu
 * cevaplamadı. Bu testler hem kısalığı hem de uydurma ölçüt girmemesini sabitler.
 */
describe('Alan açıklamaları', () => {
  const girdiler = Object.entries(fieldHelp)

  it('WH1. Her alanın bir tanımı vardır', () => {
    expect(girdiler.length).toBeGreaterThan(0)

    for (const [ad, tanim] of girdiler) {
      expect(tanim.trim(), ad).not.toBe('')
    }
  })

  it('WH2. Tanımlar kısadır', () => {
    // İpucu balonu iki satırı geçmemeli; uzun metin okunmadan kapatılıyor.
    for (const [ad, tanim] of girdiler) {
      expect(tanim.length, `${ad}: ${tanim}`).toBeLessThanOrEqual(120)
    }
  })

  it('WH3. Tanımlar tek cümledir ve noktayla biter', () => {
    for (const [ad, tanim] of girdiler) {
      expect(tanim.endsWith('.'), `${ad}: ${tanim}`).toBe(true)

      // Cümle ortasında nokta = ikinci cümle başlamış demektir.
      expect(tanim.slice(0, -1).includes('. '), `${ad}: ${tanim}`).toBe(false)
    }
  })

  it('WH4. Tanımlar sistemin davranışını değil terimi anlatır', () => {
    // "Skoru şöyle etkiler", "boş bırakırsanız", "listeden seçilir" gibi ifadeler
    // davranıştır; kullanıcının sorduğu soru bu değildi.
    const davranis = /skor|puan|boş bırak|listeden seç|kayıt değişmez|değerlendirilemedi/i

    for (const [ad, tanim] of girdiler) {
      expect(davranis.test(tanim), `${ad}: ${tanim}`).toBe(false)
    }
  })

  it('WH5. Ar-Ge personeline yaş koşulu ATFEDİLMEZ', () => {
    // Kullanıcının sorusu buydu: "Ar-Ge personeli yaş aralığı olarak mı geçiyor?"
    // Motorda Workforce.RAndDEmployeeCount için yaş ya da unvan koşulu yoktur.
    for (const tanim of [fieldHelp.rAndDEmployeeCount, fieldHelp.scenarioRndCount]) {
      expect(tanim).not.toMatch(/yaş/i)
    }
  })

  it('WH6. Aynı terim iki ekranda aynı tanımı gösterir', () => {
    // Ar-Ge personeli, çalışan sayısı ve ciro hem firma kartında hem senaryo ekranında
    // var ve ikisi de motorda AYNI alanı besliyor. Tanımlar ayrışırsa kullanıcı
    // hangisine güveneceğini bilemez.
    expect(fieldHelp.rAndDEmployeeCount).toBe(fieldHelp.scenarioRndCount)
    expect(fieldHelp.employeeCount).toBe(fieldHelp.scenarioEmployeeCount)
    expect(fieldHelp.womenEmployeeCount).toBe(fieldHelp.scenarioWomenCount)
    expect(fieldHelp.annualRevenue).toBe(fieldHelp.scenarioRevenue)
  })

  it('WH7. Kısaltmalar ilk geçtiği yerde açılır', () => {
    expect(fieldHelp.mersisNumber).toMatch(/Merkezi Sicil Kayıt Sistemi/)
    expect(fieldHelp.primaryNaceCode).toMatch(/ekonomik faaliyet sınıflaması/)
  })

  it('WH8. Vergi numarası tanımı hane sayısını ve veren kurumu söyler', () => {
    expect(fieldHelp.taxNumber).toMatch(/Gelir İdaresi Başkanlığı/)
    expect(fieldHelp.taxNumber).toMatch(/10 haneli/)
  })
})
