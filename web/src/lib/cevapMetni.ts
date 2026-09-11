/**
 * Rapor cevabının metin biçimi.
 *
 * Sunucu cevabı düz metin üretir ve vurguyu `**...**` ile işaretler. Bu metni HTML
 * olarak basmak (`dangerouslySetInnerHTML`) sunucudan gelen bir çağrı başlığının
 * etiket taşıması hâlinde onu çalıştırırdı; bu yüzden metin burada parçalara
 * ayrılır ve React elemanı olarak basılır.
 */

export interface MetinParcasi {
  text: string
  vurgulu: boolean
}

/**
 * `**` çiftlerini vurgulu parçalara ayırır.
 *
 * Eşi kapanmamış bir `**` vurgu başlatmaz: yarım kalan işaret metnin geri kalanını
 * sessizce kalınlaştırırdı ve okuyucu neyin vurgulandığını ayırt edemezdi.
 */
export function vurguyuAyir(satir: string): MetinParcasi[] {
  const parcalar: MetinParcasi[] = []

  let kalan = satir

  while (kalan.length > 0) {
    const basla = kalan.indexOf('**')

    if (basla < 0) {
      parcalar.push({ text: kalan, vurgulu: false })
      break
    }

    const bitir = kalan.indexOf('**', basla + 2)

    if (bitir < 0) {
      parcalar.push({ text: kalan, vurgulu: false })
      break
    }

    if (basla > 0) {
      parcalar.push({ text: kalan.slice(0, basla), vurgulu: false })
    }

    parcalar.push({ text: kalan.slice(basla + 2, bitir), vurgulu: true })
    kalan = kalan.slice(bitir + 2)
  }

  return parcalar.filter((p) => p.text.length > 0)
}

/** Cevabı satırlara böler; boş satır paragraf ayracı olarak korunur. */
export const satirlaraBol = (cevap: string): string[] =>
  cevap.replace(/\r\n/g, '\n').replace(/\n+$/, '').split('\n')
