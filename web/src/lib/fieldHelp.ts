/**
 * Form alanlarının **sözlük anlamı** — tek yerde.
 *
 * Neden var: alan adı kısa olmak zorunda ("Ar-Ge personeli sayısı") ama terimin ne
 * demek olduğu addan çıkmıyor. Kullanıcı "Ar-Ge personeli yaş aralığı olarak mı
 * geçiyor?" diye sorduğunda cevabı ekranda bulamadı.
 *
 * Kural: burada **terimin gerçek anlamı** yazılır, sistemin o alanla ne yaptığı DEĞİL.
 * "Bu alan skoru şöyle etkiler" cinsinden cümleler ipucunu uzatıyor ve kullanıcının
 * sorduğu soruyu cevaplamıyordu. Bir cümle, en fazla iki satır.
 *
 * Uydurulmaz: motorda karşılığı olmayan bir ölçüt yazılmaz. Ar-Ge personeli için
 * sistemde yaş ya da unvan koşulu yoktur, bu yüzden tanımda yaş geçmez.
 */

/** Aynı terim birden çok ekranda geçtiğinde tanım tek yerde durur. */
const CALISAN = 'SGK’ya bildirilen toplam sigortalı çalışan sayısı.'
const KADIN_CALISAN = 'Şirkette çalışan kadın sigortalı personel sayısı.'
const ARGE_CALISAN =
  'Araştırma-geliştirme ve tasarım faaliyetlerinde görevlendirilmiş personel sayısı.'
const CIRO = 'Bir mali yılda elde edilen toplam satış hasılatı (KDV hariç).'

export const fieldHelp = {
  // ── Senaryo simülasyonu ──
  scenarioName: 'Denemeye verdiğiniz ad; senaryoları birbirinden ayırmak içindir.',
  scenarioEmployeeCount: CALISAN,
  scenarioWomenCount: KADIN_CALISAN,
  scenarioRndCount: ARGE_CALISAN,
  scenarioRevenue: CIRO,
  scenarioCertificates:
    'Bağımsız belgelendirme kuruluşlarınca verilen kalite ve yönetim sistemi belgeleri (ISO 9001, ISO 14001 gibi).',

  // ── Firma kartı: kimlik ──
  legalName: 'Ticaret siciline tescil edilmiş resmî şirket unvanı.',
  shortName: 'Şirketin günlük kullanımdaki kısa adı.',
  taxNumber:
    'Gelir İdaresi Başkanlığı tarafından gerçek ve tüzel kişilere verilen 10 haneli özel numara.',
  country: 'Şirketin ticaret siciline tescilli olduğu ülke.',
  legalType:
    'Şirketin ticaret hukukundaki türü: limited, anonim, şahıs işletmesi ya da kooperatif.',
  taxOffice: 'Şirketin vergi mükellefiyetinin bağlı olduğu vergi dairesi.',
  mersisNumber:
    'MERSİS (Merkezi Sicil Kayıt Sistemi) tarafından her şirkete verilen 16 haneli tekil numara.',
  tradeRegistryNumber:
    'Ticaret sicili müdürlüğünce tescil sırasında verilen sicil numarası.',
  foundedOn: 'Şirketin ticaret siciline tescil edildiği tarih.',

  // ── Firma kartı: faaliyet ──
  mainSector: 'Şirketin gelirinin çoğunu elde ettiği ekonomik faaliyet alanı.',
  primaryNaceCode:
    'NACE, Avrupa Birliği’nin ekonomik faaliyet sınıflamasıdır; kod şirketin ana faaliyetini tanımlar.',
  secondaryNaceCodes: 'Şirketin ana faaliyeti dışında tescilli diğer faaliyet kodları.',
  subSectors: 'Ana sektör dışında faaliyet gösterilen alanlar.',
  city: 'Şirket merkezinin bulunduğu il.',
  targetCountries: 'Ürün veya hizmetin satıldığı ya da satılması hedeflenen ülkeler.',

  // ── Firma kartı: personel ve mali ──
  employeeCount: CALISAN,
  womenEmployeeCount: KADIN_CALISAN,
  rAndDEmployeeCount: ARGE_CALISAN,
  youngEmployeeCount: 'Belirlediğiniz yaş sınırının altındaki sigortalı çalışan sayısı.',
  youngEmployeeMaxAge:
    'Genç çalışan sayısını hangi yaşın altı için verdiğiniz; teşvikler 25, 29 ve 30 sınırlarını kullanır.',
  disabledEmployeeCount: 'Engelli statüsünde çalıştırılan sigortalı personel sayısı.',
  annualRevenue: CIRO,
  balanceSize:
    'Bilançonun aktif toplamı; şirketin sahip olduğu tüm varlıkların toplam değeri.',

  // ── Firma kartı: iletişim ve durum ──
  website: 'Şirketin kurumsal internet sitesi adresi.',
  phone: 'Şirketin kurumsal telefon numarası.',
  corporateEmail: 'Şirket adına kullanılan resmî e-posta adresi.',
  address: 'Şirket merkezinin açık posta adresi.',
  isInTechnopark:
    'Teknopark, Ar-Ge ve yazılım firmalarına vergi avantajı sağlayan, kanunla kurulmuş teknoloji geliştirme bölgesidir.',
  exportFlag: 'Yurt dışına mal veya hizmet satışı yapılıp yapılmadığı.',

  // ── Grup ve hiyerarşi ──
  groupName: 'Aynı sermayedarlara bağlı şirketlerin oluşturduğu topluluğun adı.',
  groupDescription: 'Grup hakkında kısa açıklama.',
  groupId: 'Şirketin üyesi olduğu şirketler topluluğu.',
  parentCompanyId: 'Şirketin sermayesinde kontrol sahibi olan üst şirket.',
  relationshipType:
    'Üst şirketle kurulan bağın türü: bağlı ortaklık, iştirak ya da bağımsız.',
  isHeadCompany: 'Grubu temsil eden ve konsolide raporlamanın merkezinde olan şirket.',

  // ── Üyelik ve davet ──
  newMemberUserId: 'Panele kayıtlı kullanıcı hesabı.',
  newMemberRole: 'Kullanıcının bu şirkette neleri yapabileceğini belirleyen yetki düzeyi.',
  inviteEmail: 'Davet bağlantısının gönderileceği e-posta adresi.',
  inviteRole: 'Davet kabul edildiğinde kullanıcıya verilecek yetki düzeyi.',

  // ── Mevzuat filtreleri ──
  regulationDomain: 'Mevzuatın düzenlediği konu alanı.',
  regulationJurisdiction:
    'Mevzuatın yürürlükte olduğu hukuk düzeni; ulusal mevzuat ile AB mevzuatını ayırır.',
} as const satisfies Record<string, string>

export type FieldHelpKey = keyof typeof fieldHelp
