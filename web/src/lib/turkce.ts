/**
 * Türkçe metin karşılaştırma — sunucudaki `TurkceMetin` ile aynı katlama.
 *
 * Gerekli, çünkü `toLowerCase()` noktasız `ı` ile noktalı `I`'yı eşleştirmez:
 * "İNŞAAT" ile "inşaat" tutmaz ve kullanıcı kendi yazdığı sektörü listede bulamaz.
 * `localeCompare` ise tarayıcının diline göre değişir; deterministik olmaz.
 */

const KATLAMA: Record<string, string> = {
  ı: 'i', İ: 'i', I: 'i', i: 'i',
  ş: 's', Ş: 's',
  ğ: 'g', Ğ: 'g',
  ü: 'u', Ü: 'u',
  ö: 'o', Ö: 'o',
  ç: 'c', Ç: 'c',
  â: 'a', Â: 'a',
  î: 'i', Î: 'i',
  û: 'u', Û: 'u',
}

/** Katlar ve gürültüyü atar: harf/rakam dışındaki her şey tek boşluğa iner. */
export function katla(value: string): string {
  let out = ''
  let bosluk = true

  for (const ch of value) {
    const katlanmis = (KATLAMA[ch] ?? ch.toLowerCase())

    if (/[\p{L}\p{N}]/u.test(katlanmis)) {
      out += katlanmis
      bosluk = false
    } else if (!bosluk) {
      out += ' '
      bosluk = true
    }
  }

  return out.trim()
}

/** İki metin Türkçe katlamayla aynı mı? */
export const ayniMi = (a: string, b: string): boolean => katla(a) === katla(b)

/** Koddaki nokta, boşluk ve tireyi atar: "25.62", "2562" ve "25 62" aynı koddur. */
export const sadeceRakam = (value: string): string => value.replace(/\D/g, '')
