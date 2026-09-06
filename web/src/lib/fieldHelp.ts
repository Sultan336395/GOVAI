/**
 * Form alanlarının ne anlama geldiği — tek yerde.
 *
 * Neden var: alan adları kısa olmak zorunda ("Ar-Ge personeli sayısı") ama sistemin o
 * alandan ne anladığı kısa addan çıkmıyor. Kullanıcı "Ar-Ge personeli bir yaş aralığı
 * mı?" diye sorduğunda cevabı ekranda bulamıyordu. Yanlış doldurulan bir alan yanlış
 * skora, yanlış skor yanlış fırsat listesine yol açar.
 *
 * Metinler **sistemin gerçekte yaptığını** anlatır, mevzuatın ne dediğini değil.
 * Motorda karşılığı olmayan bir kural buraya YAZILMAZ: örneğin genç çalışan için 29 yaş
 * sınırı `Workforce.YoungEmployeeCount` üzerinde tanımlıdır ve yazılabilir; Ar-Ge
 * personeli için sistemde herhangi bir yaş ya da unvan koşulu YOKTUR, o yüzden
 * uydurulmaz — beyan edilen sayı neyse odur.
 *
 * `short` etiketin hemen altında görünebilecek kadar kısadır; `detail` "?" düğmesinin
 * ipucunda çıkar ve alanın skoru nasıl etkilediğini söyler.
 */

export interface FieldHelp {
  short: string
  detail: string
}

export const fieldHelp = {
  // ── Senaryo simülasyonu ──
  scenarioName: {
    short: 'Bu denemeyi sonradan tanımak için verdiğiniz ad.',
    detail:
      'Yalnızca sizin için bir etikettir; skora etkisi yoktur. Senaryo kaydedilirse ' +
      'listede bu adla görünür.',
  },
  scenarioEmployeeCount: {
    short: 'Firmanın toplam çalışan sayısı kaç olsaydı?',
    detail:
      'Boş bırakırsanız firmanın kayıtlı değeri kullanılır. Bu sayı hem "asgari çalışan" ' +
      'koşullarını hem de KOBİ ölçeğinizi (mikro/küçük/orta/büyük) ve kadın-genç-Ar-Ge ' +
      'oranlarının paydasını değiştirir.',
  },
  scenarioWomenCount: {
    short: 'Kadın çalışan sayısı kaç olsaydı?',
    detail:
      'Kadın istihdamı teşviklerinde hem sayı hem oran aranır. Oran, kadın çalışan ' +
      'sayısının toplam çalışan sayısına bölümüdür. Toplam çalışan sayısını aşamaz.',
  },
  scenarioRndCount: {
    short: 'Ar-Ge personeli sayısı kaç olsaydı?',
    detail:
      'Sistemin Ar-Ge personeli için bir yaş ya da unvan koşulu YOKTUR: Ar-Ge ve ' +
      'tasarım faaliyetlerinde fiilen çalıştırdığınız, sizin beyan ettiğiniz kişi ' +
      'sayısıdır. Kurallar hem sayıya hem orana (Ar-Ge personeli ÷ toplam çalışan) ' +
      'bakabilir. Firma kartındaki aynı alanın geçici olarak değiştirilmiş hâlidir; ' +
      'kayıt değişmez.',
  },
  scenarioRevenue: {
    short: 'Son mali yılın toplam cirosu (TL) kaç olsaydı?',
    detail:
      'Kapanmış son mali yılın toplam satış hasılatı. KDV hariç ve TL olarak, nokta ya ' +
      'da virgül kullanmadan yazın. Hem ciro üst/alt sınırı koşullarında hem de KOBİ ' +
      'ölçeğinin belirlenmesinde kullanılır.',
  },
  scenarioCertificates: {
    short: 'Bu belgeleri alsaydınız hangi fırsatlar açılırdı?',
    detail:
      'Firmanın mevcut belgelerine EKLENİR, hiçbirini silmez. Kod olarak yazın ve ' +
      'virgülle ayırın (ISO9001, ISO14001, ISO27001, ISO45001, ISO50001). Belge ' +
      'kontrol listesinde eksik görünen kalemleri kapatır.',
  },

  // ── Firma kartı: kimlik ──
  legalName: {
    short: 'Ticaret sicilinde yazan tam unvan.',
    detail:
      'Kısaltma değil, resmî unvanın kendisi. Başvuru evrakı bu adla düzenlendiği için ' +
      'sicildeki yazımla birebir aynı olmalıdır.',
  },
  shortName: {
    short: 'Ekranlarda kullanılacak kısa ad.',
    detail: 'Yalnızca gösterim içindir; resmî hiçbir alanda kullanılmaz.',
  },
  taxNumber: {
    short: 'Vergi kimlik numarası.',
    detail:
      'Türkiye için 10 hane ve yalnızca rakam. Firmayı benzersiz tanımlayan alandır; ' +
      'kaydedildikten sonra değiştirilemez, farklı tüzel kişilik için yeni kayıt açılır.',
  },
  country: {
    short: 'Firmanın kayıtlı olduğu ülke.',
    detail: 'İki harfli ülke kodu (TR). Vergi numarası doğrulaması buna göre yapılır.',
  },
  legalType: {
    short: 'Şirketin hukuki yapısı.',
    detail:
      'Bazı destekler yalnızca belirli yapılara açıktır (ör. sermaye şirketi şartı). ' +
      'Yanlış seçim, hak ettiğiniz bir çağrıda elenmenize yol açabilir.',
  },
  taxOffice: {
    short: 'Bağlı olduğunuz vergi dairesi.',
    detail: 'Başvuru evrakında istenir; skorlamada kullanılmaz.',
  },
  mersisNumber: {
    short: 'MERSİS numarası.',
    detail: 'Merkezi Sicil Kayıt Sistemi numarası. Evrak içindir; skorlamada kullanılmaz.',
  },
  tradeRegistryNumber: {
    short: 'Ticaret sicil numarası.',
    detail: 'Evrak içindir; skorlamada kullanılmaz.',
  },
  foundedOn: {
    short: 'Kuruluş tarihi.',
    detail:
      'Birçok destek asgari faaliyet süresi arar ("en az 2 yıldır faaliyette"). İşletme ' +
      'yaşı bu tarihten hesaplanır, bu yüzden boş bırakılırsa o koşullar ' +
      '"değerlendirilemedi" sayılır.',
  },

  // ── Firma kartı: faaliyet ──
  mainSector: {
    short: 'Firmanın ana faaliyet alanı.',
    detail:
      'Eşleştirmenin BİRİNCİL ölçütüdür: fırsatlar önce sektöre göre sıralanır. ' +
      'Listeden seçilir, elle yazılmaz. Seçtiğiniz sektör, altındaki NACE kodu ' +
      'listesini de belirler.',
  },
  primaryNaceCode: {
    short: 'Ana faaliyetinizin NACE kodu.',
    detail:
      'Motor sektör uyumunu bu koda bakarak hesaplar; sektör adına değil. Çağrılar çoğu ' +
      'zaman ana grubu verir ("25") ve eşleşme önekten hesaplanır, bu yüzden kodunuzun ' +
      'ilk haneleri tutuyorsa uyum sağlanır. Yalnızca seçtiğiniz sektöre ait kodlar ' +
      'listelenir.',
  },
  secondaryNaceCodes: {
    short: 'Varsa diğer faaliyet kodlarınız.',
    detail:
      'Ana kodun yanında değerlendirilir; herhangi biri çağrıyla tutuyorsa sektör uyumu ' +
      'sağlanır. Başka bir alanda da faaliyet gösteriyorsanız önce onu alt sektör olarak ' +
      'ekleyin, kodları o zaman listelenir.',
  },
  subSectors: {
    short: 'Ana sektör dışındaki faaliyet alanlarınız.',
    detail:
      'Beyan ettiğiniz her alt sektör, "Diğer NACE kodları" listesine o alanın kodlarını ' +
      'ekler. Sıralamada ana sektör esas alınır.',
  },
  city: {
    short: 'Merkezinizin bulunduğu il.',
    detail:
      'Bölgesel çağrılar il ve istatistiki bölge koduna (ör. TR62) bakar. Boş bırakılırsa ' +
      'bölge koşulları "değerlendirilemedi" sayılır ve firma elenmez, ama tam puan da almaz.',
  },
  targetCountries: {
    short: 'İhracat yaptığınız veya hedeflediğiniz ülkeler.',
    detail: 'Pazara giriş ve ihracat destekleri için bilgi amaçlıdır; skoru doğrudan değiştirmez.',
  },

  // ── Firma kartı: personel ve mali ──
  employeeCount: {
    short: 'SGK bildirimine göre toplam çalışan sayısı.',
    detail:
      'Kadın, genç ve Ar-Ge oranlarının paydası budur; ayrıca KOBİ ölçeğinizi belirler ' +
      '(10 altı mikro, 50 altı küçük, 250 altı orta). Sıfır bırakılırsa tüm personel ' +
      'koşulları "veri yok" sayılır ve o çağrılarda karar verilemez.',
  },
  womenEmployeeCount: {
    short: 'Kadın çalışan sayısı.',
    detail:
      'Toplam çalışan sayısını aşamaz. Kadın istihdamı teşvikleri çoğu zaman sayı değil ' +
      'ORAN arar (ör. "en az %30"); oran bu sayının toplama bölümüdür.',
  },
  rAndDEmployeeCount: {
    short: 'Ar-Ge ve tasarım faaliyetlerinde çalışan kişi sayısı.',
    detail:
      'Sistemin bu alan için yaş ya da unvan koşulu YOKTUR — sizin beyanınızdır. ' +
      'Ar-Ge/tasarım merkezi destekleri hem sayıya hem orana (Ar-Ge personeli ÷ toplam ' +
      'çalışan) bakabilir. Toplam çalışan sayısını aşamaz.',
  },
  annualRevenue: {
    short: 'Son mali yılın toplam cirosu (TL).',
    detail:
      'Kapanmış son mali yılın satış hasılatı, KDV hariç. Ciro üst sınırı koşullarında ' +
      've KOBİ ölçeğinin belirlenmesinde kullanılır.',
  },
  balanceSize: {
    short: 'Son mali yılın bilanço aktif toplamı (TL).',
    detail:
      'KOBİ tanımı ciro VEYA bilanço eşiğine bakabilir; bazı çağrılar ikisini birden ister. ' +
      'Boş bırakılırsa o koşullar "değerlendirilemedi" sayılır.',
  },

  // ── Firma kartı: iletişim ve durum ──
  website: { short: 'Kurumsal web siteniz.', detail: 'Evrak ve iletişim içindir; skorlamada kullanılmaz.' },
  phone: { short: 'Kurumsal telefon.', detail: 'Evrak ve iletişim içindir; skorlamada kullanılmaz.' },
  corporateEmail: {
    short: 'Kurumsal e-posta adresi.',
    detail: 'Bildirimler ve başvuru yazışmaları için kullanılır; skorlamada kullanılmaz.',
  },
  address: { short: 'Açık adres.', detail: 'Evrak içindir. Bölge eşleşmesi il alanından hesaplanır, adresten değil.' },
  isInTechnopark: {
    short: 'Teknoparkta yerleşik bir tesisiniz var mı?',
    detail:
      'Teknopark yerleşikliği bazı çağrılarda doğrudan uygunluk şartıdır. İşaretlemek, ' +
      'kural motorunda "teknoparkta yerleşik" koşulunu sağlar.',
  },
  exportFlag: {
    short: 'İhracat yapıyor musunuz?',
    detail:
      'Pazara giriş ve ihracat destekleri bu bilgiyi arar. İhracat cirosu ayrı bir alandır; ' +
      'bu kutucuk yalnızca "yapıyor/yapmıyor" sorusunu cevaplar.',
  },

  // ── Grup ve hiyerarşi ──
  groupName: { short: 'Şirketler grubunun adı.', detail: 'Aynı gruba bağlı firmaları birlikte görmek içindir; skoru etkilemez.' },
  groupDescription: {
    short: 'Grubu tanımlayan kısa not.',
    detail: 'Yalnızca bilgi amaçlıdır; skorlamaya ve yetkilere hiçbir etkisi yoktur.',
  },
  groupId: {
    short: 'Firmanın bağlı olduğu şirketler grubu.',
    detail: 'Grup üyeliği ile ana/bağlı şirket ilişkisi AYRI kavramlardır; ikisi birlikte kullanılabilir.',
  },
  parentCompanyId: {
    short: 'Hukuki üst şirket.',
    detail:
      'Aynı çalışma alanındaki bir firma seçilebilir; döngü kurulamaz (A, B\'nin altındayken ' +
      'B, A\'nın altına alınamaz).',
  },
  relationshipType: {
    short: 'Üst şirketle ilişkinin türü.',
    detail: 'Bağlı, iştirak veya bağımsız. Bazı desteklerde grup büyüklüğü bu ilişkiye göre hesaplanır.',
  },
  isHeadCompany: {
    short: 'Bu firma grubun ana şirketi mi?',
    detail: 'Grup içinde tek bir ana şirket işaretlenir; raporlarda grup bu firma üzerinden özetlenir.',
  },

  // ── Üyelik ve davet ──
  newMemberUserId: {
    short: 'Çalışma alanındaki mevcut bir kullanıcı.',
    detail: 'Yalnızca aynı çalışma alanına kayıtlı kullanıcılar bu şirkete bağlanabilir.',
  },
  newMemberRole: {
    short: 'Kullanıcının bu şirketteki yetkisi.',
    detail:
      'Sahip: her şey, üye yönetimi dahil. Yönetici: profil ve işlemler. Uzman: işlem ' +
      'yapar, profili değiştiremez. Görüntüleyici: yalnızca okur.',
  },
  inviteEmail: {
    short: 'Davet edilecek kişinin e-posta adresi.',
    detail: 'Kayıtlı olmayan biri davet edilirse hesabını davet bağlantısıyla kendisi açar.',
  },
  inviteRole: { short: 'Davet edilen kişiye verilecek yetki.', detail: 'Davet kabul edildiğinde bu rolle bağlanır.' },

  // ── Mevzuat filtreleri ──
  regulationDomain: {
    short: 'Mevzuatın konu alanı.',
    detail: 'Vergi, teşvik, çalışma hayatı gibi alanlara göre daraltır.',
  },
  regulationJurisdiction: {
    short: 'Mevzuatın geçerli olduğu yetki alanı.',
    detail: 'Ulusal mevzuat ile AB mevzuatını ayırmak için kullanılır.',
  },
} as const satisfies Record<string, FieldHelp>

export type FieldHelpKey = keyof typeof fieldHelp
