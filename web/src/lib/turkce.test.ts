import { describe, expect, it } from 'vitest'
import { ayniMi, katla, sadeceRakam } from './turkce'

/**
 * Türkçe katlama — sunucudaki `TurkceMetin` ile aynı davranmak zorunda.
 *
 * Sektör ve NACE alanları listeden seçilir; seçili değerin listedeki karşılığını bulmak
 * bu karşılaştırmaya dayanır. Katlama ayrışırsa kullanıcı kayıtlı değerini listede
 * bulamaz ve alan "yeniden seçin" uyarısıyla boş görünür.
 */
describe('Türkçe katlama', () => {
  it('WT1. Noktalı ve noktasız i eşitlenir', () => {
    // Asıl tuzak burada: toLowerCase("I") = "i" ama toLowerCase("İ") = "i̇" (birleşik).
    expect(katla('İNŞAAT')).toBe('insaat')
    expect(katla('inşaat')).toBe('insaat')
    expect(katla('IŞIK')).toBe('isik')
    expect(katla('ışık')).toBe('isik')
  })

  it('WT2. Türkçe harfler karşılıklarına iner', () => {
    expect(katla('ĞÜŞÖÇ ğüşöç')).toBe('gusoc gusoc')
  })

  it('WT3. Şapkalı harfler de katlanır', () => {
    // Resmî metinlerde "İLÂN" ile "İLAN" yıllara göre değişiyor.
    expect(katla('İLÂN')).toBe('ilan')
  })

  it('WT4. Noktalama tek boşluğa iner ve kenarlar kırpılır', () => {
    expect(katla('  Bilişim,   ve   Yazılım!  ')).toBe('bilisim ve yazilim')
  })

  it('WT5. Boş metin boş kalır', () => {
    expect(katla('')).toBe('')
    expect(katla('   ')).toBe('')
  })

  it('WT6. Aynılık karşılaştırması yazım farkına takılmaz', () => {
    expect(ayniMi('İnşaat ve taahhüt', 'insaat ve taahhut')).toBe(true)
    expect(ayniMi('İNŞAAT VE TAAHHÜT', 'İnşaat ve taahhüt')).toBe(true)
  })

  it('WT7. Farklı sektörler aynı sayılmaz', () => {
    expect(ayniMi('İnşaat ve taahhüt', 'Bilişim ve yazılım')).toBe(false)

    // Kısaltma "yaklaşık doğru" değildir: kullanıcı listeden yeniden seçmelidir.
    expect(ayniMi('inşaat', 'İnşaat ve taahhüt')).toBe(false)
  })

  it('WT8. NACE kodunda yalnızca rakamlar karşılaştırılır', () => {
    // Katalog "25.62" gösterir, alan modeli "2562" saklar; ikisi aynı koddur.
    expect(sadeceRakam('25.62')).toBe('2562')
    expect(sadeceRakam('2562')).toBe('2562')
    expect(sadeceRakam('25 62')).toBe('2562')
    expect(sadeceRakam('')).toBe('')
  })
})
