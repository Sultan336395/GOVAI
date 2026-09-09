import { describe, expect, it } from 'vitest'
import { adet, belgeOkunabilirlik, incelemeNedeni, taramaTakvimi } from './sozluk'

/**
 * Terim sözlüğü.
 *
 * Sözlüğün işi arayüzü tek bir dilde tutmak: aynı kavram her ekranda aynı adla anılsın
 * ve sistemin iç terimleri kullanıcıya sızmasın.
 */

describe('taramaTakvimi', () => {
  it('S1. Günlük takvimi okunur yazar', () => {
    expect(taramaTakvimi('0 6 * * *')).toBe('Her gün 06:00')
    expect(taramaTakvimi('30 23 * * *')).toBe('Her gün 23:30')
  })

  it('S2. Haftalık takvimi gün adıyla yazar', () => {
    expect(taramaTakvimi('0 7 * * 1')).toBe('Her Pazartesi 07:00')
    expect(taramaTakvimi('15 9 * * 0')).toBe('Her Pazar 09:15')
  })

  it('S3. Aylık takvimi yazar', () => {
    expect(taramaTakvimi('0 5 15 * *')).toBe('Her ayın 15. günü 05:00')
  })

  it('S4. Tanımadığı ifadeyi UYDURMAZ, olduğu gibi bırakır', () => {
    // Yanlış bir "her gün" yazmak, ham ifadeden daha kötüdür: kullanıcı kaynağın
    // taranmadığı bir saatte tarandığını sanır.
    expect(taramaTakvimi('*/15 * * * *')).toBe('*/15 * * * *')
    expect(taramaTakvimi('0 6 1,15 * *')).toBe('0 6 1,15 * *')
    expect(taramaTakvimi('bozuk ifade')).toBe('bozuk ifade')
  })

  it('S5. Takvim yoksa bunu söyler', () => {
    expect(taramaTakvimi(null)).toBe('Takvim tanımlı değil')
    expect(taramaTakvimi('')).toBe('Takvim tanımlı değil')
  })
})

describe('durum adları', () => {
  it('S6. Belge durumları yazılım terimi içermez', () => {
    const hepsi = Object.values(belgeOkunabilirlik).join(' ')

    // "Ayrıştırıldı", "OCR", "Parse" gibi terimler kullanıcıya bir şey anlatmaz.
    for (const jargon of ['Ayrıştır', 'OCR', 'Parse', 'parse']) {
      expect(hepsi).not.toContain(jargon)
    }
  })

  it('S7. İnceleme nedenleri ne yapılması gerektiğini anlatır', () => {
    expect(incelemeNedeni.InvalidSourcePage).toBe('Çağrı sayfası değil')
    expect(incelemeNedeni.ParserFailed).toBe('İçeriği okunamadı')

    const hepsi = Object.values(incelemeNedeni).join(' ')
    expect(hepsi).not.toContain('Ayrıştır')
  })
})

describe('adet', () => {
  it('S8. Sayıyı Türkçe biçimde ve tekil adla yazar', () => {
    // Türkçede sayıdan sonra çoğul eki kullanılmaz: "3 çağrılar" yanlıştır.
    expect(adet(3, 'Çağrı')).toBe('3 çağrı')
    expect(adet(1250, 'Kayıt')).toBe('1.250 kayıt')
  })
})
