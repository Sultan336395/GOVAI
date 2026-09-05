import { describe, expect, it } from 'vitest'
import {
  PASSWORD_MIN_CLASSES,
  PASSWORD_MIN_LENGTH,
  passwordProblem,
} from './password'

/**
 * Parola kuralı — C# tarafındaki `PasswordPolicy` ile aynı eşikler.
 *
 * İki taraf ayrışırsa kullanıcı ya formu dolduramadan reddedilir ya da "geçerli"
 * görünen bir parola sunucuda geri çevrilir. Bu testler eşikleri sabitler.
 */
describe('Parola kuralı', () => {
  it('WP1. Güçlü parolalar kabul edilir', () => {
    for (const p of [
      'Kaynak-Denetim-2026',
      'uzun ve karisik Parola 42',
      'T3knopark!Mersin',
      'aA1!bB2@cC3#dD4$',
    ]) {
      expect(passwordProblem(p), p).toBeNull()
    }
  })

  it('WP2. Kısa parola reddedilir', () => {
    expect(passwordProblem('Kisa1!')).toContain(`${PASSWORD_MIN_LENGTH} karakter`)
    expect(passwordProblem('aA1!bB2@cC')).toContain(`${PASSWORD_MIN_LENGTH} karakter`)
  })

  it('WP3. Tek tür karakterden oluşan uzun dizi reddedilir', () => {
    for (const p of ['abcdefghijklmnop', 'ABCDEFGHIJKLMNOP', '1234567890123456']) {
      expect(passwordProblem(p), p).toContain('karakter türü')
    }
  })

  it('WP4. Uzun ama tekrar eden dizi reddedilir', () => {
    expect(passwordProblem('aA1aA1aA1aA1aA1')).toContain('az sayıda farklı karakter')
  })

  it('WP5. Boş parola reddedilir', () => {
    expect(passwordProblem('')).toBe('Parola boş olamaz.')
  })

  it('WP6. Eşikler sunucu tarafıyla aynı', () => {
    // C# PasswordPolicy: MinimumLength = 12, MinimumCharacterClasses = 3.
    expect(PASSWORD_MIN_LENGTH).toBe(12)
    expect(PASSWORD_MIN_CLASSES).toBe(3)
  })

  it('WP7. Türkçe karakterler harf sayılır', () => {
    // 'ş', 'ğ', 'İ' harftir; simge sayılıp yanlışlıkla sınıf çeşitliliği artmamalı.
    expect(passwordProblem('şğüöçışğüöçı')).toContain('karakter türü')
  })

  it('WP8. Sorun mesajı kullanıcıya gösterilebilir', () => {
    const mesaj = passwordProblem('kisa')

    expect(mesaj).not.toBeNull()
    expect(mesaj!.endsWith('.')).toBe(true)
  })
})
