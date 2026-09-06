import { describe, expect, it } from 'vitest'
import type { SectorFit } from '@/api/types'
import { sectorFitClass, sectorFitLabels } from './format'

/**
 * Sektör uyumu etiketleri — listenin sırasını kullanıcıya açıklayan tek metin.
 *
 * Liste sektöre göre sıralanır; etiket olmadan kullanıcı bir kaydın neden en altta
 * durduğunu göremez ve sıralama "rastgele" görünür. Bu testler etiketi ve üç durumun
 * eksiksiz karşılanmasını sabitler.
 */
describe('Sektör uyumu etiketleri', () => {
  const HEPSI: SectorFit[] = ['Matched', 'Unverified', 'NotMatched']

  it('WS1. Üç durumun da Türkçe karşılığı vardır', () => {
    for (const fit of HEPSI) {
      expect(sectorFitLabels[fit], fit).toBeTruthy()
    }
  })

  it('WS2. Doğrulanamayan kayıt kullanıcının gördüğü metinle işaretlenir', () => {
    expect(sectorFitLabels.Unverified).toBe('Sektör uyumu doğrulanamadı')
  })

  it('WS3. "Doğrulanamadı" ile "uyumsuz" ayrı metinlerdir', () => {
    // Biri bilgi eksikliği, diğeri verilmiş bir karar; aynı yazılırsa kullanıcı
    // veri tamamlaması gereken kaydı fark edemez.
    expect(sectorFitLabels.Unverified).not.toBe(sectorFitLabels.NotMatched)
  })

  it('WS4. Ham enum değeri kullanıcıya gösterilmez', () => {
    for (const fit of HEPSI) {
      expect(sectorFitLabels[fit]).not.toContain(fit)
    }
  })

  it('WS5. Her durumun bir rozet sınıfı vardır', () => {
    for (const fit of HEPSI) {
      expect(sectorFitClass[fit], fit).toBeTruthy()
    }
  })

  it('WS6. Uyumlu ile uyumsuz farklı renk sınıfı kullanır', () => {
    expect(sectorFitClass.Matched).not.toBe(sectorFitClass.NotMatched)
  })
})
