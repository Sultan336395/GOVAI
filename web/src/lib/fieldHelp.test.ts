import { describe, expect, it } from 'vitest'
import { fieldHelp } from './fieldHelp'

/**
 * Alan açıklamaları.
 *
 * Bu metinler kullanıcının alanı nasıl dolduracağına karar verdiği yerdir; yanlış
 * doldurulan alan yanlış skora, yanlış skor yanlış fırsat listesine yol açar. Testler
 * iki şeyi korur: metinlerin eksiksizliği ve **sistemde karşılığı olmayan bir kuralın
 * yazılmamış olması**.
 */
describe('Alan açıklamaları', () => {
  const girdiler = Object.entries(fieldHelp)

  it('WH1. Her alanın hem kısa hem ayrıntılı açıklaması vardır', () => {
    for (const [ad, yardim] of girdiler) {
      expect(yardim.short.trim(), ad).not.toBe('')
      expect(yardim.detail.trim(), ad).not.toBe('')
    }
  })

  it('WH2. Kısa açıklama etiketin altına sığacak kadar kısadır', () => {
    for (const [ad, yardim] of girdiler) {
      expect(yardim.short.length, `${ad}: ${yardim.short}`).toBeLessThanOrEqual(80)
    }
  })

  it('WH3. Ayrıntılı açıklama kısadan daha fazlasını söyler', () => {
    for (const [ad, yardim] of girdiler) {
      expect(yardim.detail.length, ad).toBeGreaterThan(yardim.short.length)
    }
  })

  it('WH4. Metinler cümle olarak biter', () => {
    for (const [ad, yardim] of girdiler) {
      expect(yardim.short.endsWith('.') || yardim.short.endsWith('?'), `${ad}: ${yardim.short}`).toBe(true)
      expect(yardim.detail.endsWith('.'), `${ad}: ${yardim.detail}`).toBe(true)
    }
  })

  it('WH5. Ar-Ge personeline yaş koşulu ATFEDİLMEZ', () => {
    // Kullanıcının sorusu buydu: "Ar-Ge personeli yaş aralığı olarak mı geçiyor?"
    // Cevap hayır — motorda Workforce.RAndDEmployeeCount için yaş ya da unvan koşulu
    // yoktur, beyan edilen sayıdır. Açıklama bunu söylemeli ve yaş uydurmamalı.
    for (const alan of [fieldHelp.rAndDEmployeeCount, fieldHelp.scenarioRndCount]) {
      expect(alan.detail).toMatch(/yaş/i)
      expect(alan.detail).toMatch(/YOKTUR/)
      expect(alan.detail).not.toMatch(/\d{2}\s*yaş/)
    }
  })

  it('WH6. Ciro alanı hangi yılın sorulduğunu söyler', () => {
    for (const alan of [fieldHelp.annualRevenue, fieldHelp.scenarioRevenue]) {
      expect(alan.short).toMatch(/son .*yıl/i)
    }
  })

  it('WH7. Aynı alan iki ekranda çelişmez', () => {
    // Ar-Ge personeli hem firma kartında hem senaryo ekranında var ve ikisi de motorda
    // AYNI alanı besliyor. Açıklamaları farklı şey anlatırsa kullanıcı hangisine
    // güveneceğini bilemez.
    for (const [profil, senaryo] of [
      [fieldHelp.rAndDEmployeeCount, fieldHelp.scenarioRndCount],
      [fieldHelp.employeeCount, fieldHelp.scenarioEmployeeCount],
      [fieldHelp.womenEmployeeCount, fieldHelp.scenarioWomenCount],
      [fieldHelp.annualRevenue, fieldHelp.scenarioRevenue],
    ]) {
      expect(profil.detail).not.toBe(senaryo.detail)
    }

    // Senaryo alanları kaydın değişmediğini ya da boş bırakılabileceğini söylemeli.
    for (const alan of [
      fieldHelp.scenarioEmployeeCount,
      fieldHelp.scenarioRndCount,
      fieldHelp.scenarioCertificates,
    ]) {
      expect(alan.detail).toMatch(/kayıt|Boş bırak|silmez/i)
    }
  })

  it('WH8. Skorlamada kullanılmayan alanlar bunu açıkça söyler', () => {
    // Kullanıcı hangi alanın skoru etkilediğini bilmeli; aksi hâlde evrak alanlarını
    // doldurmak için gereksiz emek harcar.
    for (const alan of [
      fieldHelp.taxOffice,
      fieldHelp.mersisNumber,
      fieldHelp.tradeRegistryNumber,
      fieldHelp.website,
      fieldHelp.phone,
    ]) {
      expect(alan.detail).toMatch(/skorlamada kullanılmaz/)
    }
  })
})
