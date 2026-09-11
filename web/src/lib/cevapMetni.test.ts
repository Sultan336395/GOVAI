import { describe, expect, it } from 'vitest'
import { satirlaraBol, vurguyuAyir } from './cevapMetni'

describe('vurguyuAyir', () => {
  it('CM1. vurgusuz metni tek parça bırakır', () => {
    expect(vurguyuAyir('Bu hafta açık destek yok.')).toEqual([
      { text: 'Bu hafta açık destek yok.', vurgulu: false },
    ])
  })

  it('CM2. çift yıldız arasını vurgular', () => {
    expect(vurguyuAyir('Önce **KOSGEB Ar-Ge** çağrısına bakın.')).toEqual([
      { text: 'Önce ', vurgulu: false },
      { text: 'KOSGEB Ar-Ge', vurgulu: true },
      { text: ' çağrısına bakın.', vurgulu: false },
    ])
  })

  it('CM3. kapanmayan yıldız vurgu BAŞLATMAZ', () => {
    // Yarım kalan işaret metnin geri kalanını kalınlaştırırdı.
    expect(vurguyuAyir('Kalan **süre az')).toEqual([
      { text: 'Kalan **süre az', vurgulu: false },
    ])
  })

  it('CM4. birden çok vurguyu ayrı ayrı işaretler', () => {
    const parcalar = vurguyuAyir('**A** ile **B** karşılaştırıldı.')

    expect(parcalar.filter((p) => p.vurgulu).map((p) => p.text)).toEqual(['A', 'B'])
  })

  it('CM5. boş metin boş dizi verir', () => {
    expect(vurguyuAyir('')).toEqual([])
  })
})

describe('satirlaraBol', () => {
  it('CM6. boş satırı paragraf ayracı olarak korur', () => {
    expect(satirlaraBol('bir\n\niki')).toEqual(['bir', '', 'iki'])
  })

  it('CM7. sondaki fazla satır sonlarını atar', () => {
    expect(satirlaraBol('bir\niki\n\n')).toEqual(['bir', 'iki'])
  })

  it('CM8. Windows satır sonunu normalleştirir', () => {
    expect(satirlaraBol('bir\r\niki')).toEqual(['bir', 'iki'])
  })
})
