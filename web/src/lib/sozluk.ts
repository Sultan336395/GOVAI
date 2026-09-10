/**
 * Uygulamanın terim sözlüğü.
 *
 * <p>
 * Arayüzde geçen her başlık ve durum adı buradan gelir. Ekranlar kendi metinlerini
 * yazdığında aynı kavram üç ayrı adla anılıyordu — "kanıt parçası", "belge bölümü",
 * "alıntı" hepsi aynı şeydi; kullanıcı bunların farklı şeyler olduğunu sanıyordu.
 * </p>
 *
 * <h3>Dil kuralları</h3>
 * <ul>
 *   <li>
 *     <b>Teknik iç yapı kullanıcıya gösterilmez.</b> Sürüm numarası, hash, karakter
 *     aralığı, ayrıştırma durumu, kayıt kimliği — bunlar sistemin iç işleyişidir.
 *     Danışman bunlara bakarak bir karar vermez.
 *   </li>
 *   <li>
 *     <b>İzlenebilirlik korunur, jargon atılır.</b> "Belge sürüm 3, hash d4f8…,
 *     karakter 60–152" yerine "15 Ağustos 2026 tarihli resmî belge · Başvuru Şartları
 *     bölümü" ve belgeye giden bağlantı. Bilgi aynı, dil insanın.
 *   </li>
 *   <li>
 *     <b>Başlıklar iş dilinde.</b> Kullanıcı ekranda kendi mesleğinin sözcüklerini
 *     görür: "çağrı", "başvuru koşulu", "resmî dayanak", "uygunluk".
 *   </li>
 *   <li>
 *     <b>Belirsizlik gizlenmez.</b> "Bilinmiyor" ile "hayır" ayrı şeylerdir ve ayrı
 *     yazılır; ürünün üç iddiasından biri budur.
 *   </li>
 * </ul>
 */

// ── Ana kavramlar ───────────────────────────────────────────────────────────

export const terimler = {
  /** Fon, hibe, teşvik ya da ihale kaydı. */
  cagri: 'Çağrı',
  cagriCogul: 'Çağrılar',

  /** Çağrı metninden çıkarılmış, firmanın sağlaması gereken koşul. */
  kosul: 'Başvuru Koşulu',
  kosulCogul: 'Başvuru Koşulları',

  /** Koşulun dayandığı resmî belge bölümü. */
  dayanak: 'Resmî Dayanak',
  dayanakCogul: 'Resmî Dayanaklar',

  /** Çağrının alındığı resmî belge. */
  belge: 'Resmî Belge',

  /** Kurumun yayın kanalı. */
  kaynak: 'Resmî Kaynak',

  /** Katalog dışına alınmış kayıt. */
  incelemeBekleyen: 'İncelemeye Alınan Kayıt',
} as const

// ── Bölüm başlıkları ────────────────────────────────────────────────────────

export const bolumBasliklari = {
  ozet: 'Özet',
  kunye: 'Çağrı Bilgileri',
  destekTutari: 'Destek Tutarı',
  basvuruKosullari: 'Başvuru Koşulları',
  istenenBelgeler: 'İstenen Belgeler',
  resmiDayanak: 'Resmî Dayanak',
  belgeIcerigi: 'Belge İçeriği',
  kaynakBilgisi: 'Kaynak Bilgisi',
  degerlendirme: 'Değerlendirme',
  gerekce: 'Gerekçe',
} as const

// ── Durum adları ────────────────────────────────────────────────────────────

/**
 * Belgenin okunabilirlik durumu.
 *
 * "Ayrıştırma" bir yazılım terimidir; kullanıcının bilmesi gereken tek şey belgenin
 * okunup okunamadığıdır. "OCR gerekiyor" da öyle: kullanıcı için anlamı "bu belge
 * taranmış görüntü, metni çıkarılamadı"dır.
 */
export const belgeOkunabilirlik = {
  Pending: 'Henüz işlenmedi',
  Parsed: 'Metni okundu',
  Failed: 'Metni okunamadı',
  NeedsOcr: 'Taranmış belge — metni çıkarılamadı',
  Skipped: 'Metni okundu, çağrı kaydı açılmadı',
} as const

/** Kaydın sisteme nasıl girdiği. */
export const kayitKaynagi = {
  Crawl: 'Otomatik tarama',
  ManualImport: 'Elle eklendi',
} as const

/**
 * İncelemeye alınma nedeni.
 *
 * "Karantina" kelimesi ekranda kalır — kullanıcılar bu terimi benimsedi ve
 * "incelemeye alındı" ile karışmıyor. Ama nedenler iş dilinde yazılır.
 */
export const incelemeNedeni = {
  None: 'Sorun yok',
  InvalidSourcePage: 'Çağrı sayfası değil',
  Duplicate: 'Aynı kayıt zaten var',
  MissingRequiredFields: 'Bilgileri eksik',
  ParserFailed: 'İçeriği okunamadı',
  NeedsManualReview: 'Uzman incelemesi gerekiyor',
} as const

/** Kaynağın çalışma durumu. */
export const kaynakDurumu = {
  Unverified: 'Henüz doğrulanmadı',
  Healthy: 'Çalışıyor',
  Degraded: 'Kısmen çalışıyor',
  Failing: 'Erişilemiyor',
} as const

// ── Yardımcılar ─────────────────────────────────────────────────────────────

/**
 * Bir sayıyı "3 çağrı" gibi okunur hâle getirir.
 *
 * Türkçede çoğul eki sayıdan sonra kullanılmaz ("3 çağrılar" yanlıştır); bu yüzden
 * tekil ad yeterlidir ve ayrı bir çoğul kuralı gerekmez.
 */
export function adet(sayi: number, ad: string): string {
  return `${sayi.toLocaleString('tr-TR')} ${ad.toLocaleLowerCase('tr-TR')}`
}

/**
 * Tarama takvimini okunur hâle getirir.
 *
 * Ekranda ham cron ifadesi (`0 6 * * *`) yazıyordu; bunu okuyabilmek için cron
 * söz dizimini bilmek gerekiyor ve danışmanın böyle bir yükümlülüğü yok. Kullanıcının
 * bilmek istediği tek şey kaynağın ne sıklıkta tarandığıdır.
 *
 * Tanınmayan bir ifade **uydurulmaz**: olduğu gibi gösterilir. Yanlış bir "her gün"
 * yazmak, ham ifadeden daha kötüdür.
 */
export function taramaTakvimi(cron: string | null | undefined): string {
  if (!cron) return 'Takvim tanımlı değil'

  const parcalar = cron.trim().split(/\s+/)
  if (parcalar.length !== 5) return cron

  const [dakika, saat, ayinGunu, ay, haftaninGunu] = parcalar

  const saatMetni =
    /^\d+$/.test(saat) && /^\d+$/.test(dakika)
      ? `${saat.padStart(2, '0')}:${dakika.padStart(2, '0')}`
      : null

  if (!saatMetni) return cron

  const gunler = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi']

  if (ayinGunu === '*' && ay === '*' && haftaninGunu === '*') {
    return `Her gün ${saatMetni}`
  }

  if (ayinGunu === '*' && ay === '*' && /^[0-6]$/.test(haftaninGunu)) {
    return `Her ${gunler[Number(haftaninGunu)]} ${saatMetni}`
  }

  if (/^\d+$/.test(ayinGunu) && ay === '*' && haftaninGunu === '*') {
    return `Her ayın ${ayinGunu}. günü ${saatMetni}`
  }

  return cron
}

/**
 * Aynı başlığı taşıyan kayıtları ayırt edecek ek bilgi.
 *
 * Resmî Gazete ilanları standart başlıklarla yayımlanır: aynı gün iki ayrı orman
 * ihalesi de "ORMAN EMVALİ SATILACAKTIR" adını taşır. Bunlar mükerrer kayıt
 * DEĞİLDİR — iki ayrı ihaledir — ama listede yan yana durunca kullanıcı hangisinin
 * hangisi olduğunu anlayamaz ve sistemi kayıt tekrarlıyor sanır.
 *
 * Çözüm ayırt edici bir iz göstermektir: ilanın kendi belge adı. Resmî Gazete
 * adresleri `20260908-3-6.pdf` biçimindedir; son parça o günün kaçıncı ilanı
 * olduğunu söyler ve iki kaydı kesin ayırır.
 *
 * Ayırt edici bir iz çıkarılamıyorsa <b>uydurulmaz</b>: `null` döner ve ekran ek
 * satır göstermez.
 */
export function ayirtEdiciIz(sourceUrl: string | null | undefined): string | null {
  if (!sourceUrl) return null

  try {
    const yol = new URL(sourceUrl).pathname
    const sonParca = yol.split('/').filter(Boolean).pop()

    if (!sonParca) return null

    // Uzantı atılır; kalan ad kaydın kendi işaretidir.
    const ad = sonParca.replace(/\.[a-z0-9]{2,5}$/i, '')

    // Anlamsız kısa parçalar ("3", "tr") ayırt etmez.
    return ad.length >= 4 ? ad : null
  } catch {
    return null
  }
}
