/**
 * Parola gücü kuralı — C# tarafındaki `PasswordPolicy` ile aynı eşikler.
 *
 * İstemci denetimi bir güvenlik sınırı DEĞİLDİR; sunucu kendi denetimini her koşulda
 * yapar. Buradaki amaç kullanıcının sunucuya gidip gelmeden ne beklendiğini
 * görmesidir.
 *
 * İki taraf ayrışırsa kullanıcı formu dolduramadan reddedilir ya da tam tersi
 * "geçerli" görünen bir parola sunucuda geri çevrilir; bu yüzden eşikler burada da
 * yazılıdır ve testle sabitlenir.
 */

export const PASSWORD_MIN_LENGTH = 12
export const PASSWORD_MIN_CLASSES = 3
export const PASSWORD_MIN_DISTINCT = 6

/**
 * Parolanın sorunu nedir? Sorun yoksa `null` döner.
 */
export function passwordProblem(password: string): string | null {
  if (!password) return 'Parola boş olamaz.'

  if (password.length < PASSWORD_MIN_LENGTH) {
    return `Parola en az ${PASSWORD_MIN_LENGTH} karakter olmalıdır.`
  }

  const kucuk = /\p{Ll}/u.test(password)
  const buyuk = /\p{Lu}/u.test(password)
  const rakam = /\p{Nd}/u.test(password)
  const diger = /[^\p{L}\p{Nd}]/u.test(password)

  const sinif = [kucuk, buyuk, rakam, diger].filter(Boolean).length

  if (sinif < PASSWORD_MIN_CLASSES) {
    return (
      `Parola en az ${PASSWORD_MIN_CLASSES} farklı karakter türü içermelidir: ` +
      'küçük harf, büyük harf, rakam ve simgelerden en az üçü.'
    )
  }

  if (new Set(password).size < PASSWORD_MIN_DISTINCT) {
    return 'Parola çok az sayıda farklı karakterden oluşuyor.'
  }

  return null
}
