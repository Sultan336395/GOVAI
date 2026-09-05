import type {
  OpportunityDetail,
  OpportunityProvenance,
  OpportunityRule,
  RuleDimension,
} from '@/api/types'
import { EmptyState, InfoBox } from '@/components/Common'
import {
  NOT_PROVIDED_LABEL,
  categoryLabels,
  displayField,
  formatCurrency,
  formatDate,
  formatPercent,
} from '@/lib/format'

/**
 * Fırsat detayı (Faz 2).
 *
 * Buradaki her satır resmî kaynağa dayanır. Bir alan boşsa boş bırakılmaz:
 * "Resmî kaynakta belirtilmemiş" ya da "Bu çağrı için geçerli değil" yazar. İkisi
 * ayrı şeydir — birincisi eksik veridir, ikincisi doğru ve nihai bir cevaptır.
 *
 * Değer hiçbir koşulda tahmin edilmez.
 */
export function OpportunityDetailCard({ data }: { data: OpportunityDetail }) {
  const { fieldAvailability: durum, provenance: kanit } = data

  return (
    <>
      {!data.isOpen ? (
        <div className="card" style={{ marginBottom: 16, borderLeft: '4px solid #b45309' }}>
          <strong>Bu çağrının son başvuru tarihi geçti.</strong>
          <div className="muted" style={{ marginTop: 4 }}>
            Son başvuru {formatDate(data.deadline)}. Kayıt geçmiş kayıt olarak gösteriliyor;
            yeni başvuru yapılamaz.
          </div>
        </div>
      ) : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Fırsat künyesi</h2>

        <div className="grid two">
          <Alan ad="Fırsat türü" deger={categoryLabels[data.supportCategory]} />
          <Alan ad="Program türü" deger={data.sourceType} durum={durum.programmeType} />
          <Alan ad="Yayımlayan resmî kurum" deger={data.publisher} />
          <Alan ad="Yayın tarihi" deger={formatDate(data.publishedAt)} />
          <Alan
            ad="Son başvuru tarihi"
            deger={data.deadline ? formatDate(data.deadline) : null}
            durum={durum.deadline}
          />
          <Alan
            ad="Destek / bütçe tutarı"
            deger={
              data.budget?.maxAmount != null || data.budget?.minAmount != null
                ? `${formatCurrency(data.budget.minAmount)} – ${formatCurrency(data.budget.maxAmount)}`
                : null
            }
            durum={durum.budget}
          />
          <Alan ad="Para birimi" deger={data.budget?.currency ?? null} durum={durum.currency} />
          <Alan
            ad="Destek oranı"
            deger={data.budget?.supportRate != null ? formatPercent(data.budget.supportRate) : null}
            durum={durum.budget}
          />
          <Alan
            ad="Coğrafi kapsam"
            deger={KuralDegeri(data.rules, 'Region')}
            durum={durum.geography}
          />
          <Alan
            ad="Başvurabilecek şirket türleri"
            deger={KuralDegeri(data.rules, 'Employment') ?? KuralDegeri(data.rules, 'Financial')}
            durum={durum.eligibleApplicant}
          />
          <Alan
            ad="Başvuru yapabilecek sektörler"
            deger={KuralDegeri(data.rules, 'Sector')}
            durum={durum.sector}
          />
        </div>

        <h3 style={{ marginTop: 20, marginBottom: 6 }}>Kısa açıklama</h3>
        <p style={{ margin: 0 }} className={data.summary ? undefined : 'muted'}>
          {displayField(data.summary)}
        </p>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Başvuru koşulları ve uygunluk kriterleri</h2>
        {data.rules.length === 0 ? (
          <EmptyState>
            Resmî kaynaktan çıkarılmış bir koşul yok. Koşullar uydurulmaz; danışman elle
            ekleyebilir.
          </EmptyState>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Koşul</th>
                  <th>Boyut</th>
                  <th>Ağırlık</th>
                  <th>Kaynak metin</th>
                </tr>
              </thead>
              <tbody>
                {data.rules.map((kural) => (
                  <tr key={kural.id}>
                    <td>{kural.humanReadable}</td>
                    <td>{kural.dimension}</td>
                    <td>{kural.severity}</td>
                    <td className="muted" style={{ fontSize: 12 }}>
                      {kural.sourceExcerpt ?? NOT_PROVIDED_LABEL}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Gerekli belgeler</h2>
        {data.documentChecklist.length === 0 ? (
          <EmptyState>Resmî kaynakta belge listesi belirtilmemiş.</EmptyState>
        ) : (
          <ul style={{ margin: 0, paddingLeft: 18 }}>
            {data.documentChecklist.map((belge) => (
              <li key={belge.code}>
                {belge.name}
                {belge.isMandatory ? <strong> (zorunlu)</strong> : ' (isteğe bağlı)'}
                {belge.issuingAuthority ? (
                  <span className="muted"> · {belge.issuingAuthority}</span>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </div>

      <KanitBolumu kanit={kanit} />
    </>
  )
}

/** Kanıt ve kaynak künyesi; resmî bağlantı düğmesi burada. */
function KanitBolumu({ kanit }: { kanit: OpportunityProvenance | null }) {
  if (!kanit) {
    return (
      <div className="card" style={{ marginBottom: 16 }}>
        <h2>Kaynak ve kanıt</h2>
        <EmptyState>
          Bu kayıt bir resmî belgeye bağlı değil; kanıt zinciri gösterilemiyor.
        </EmptyState>
      </div>
    )
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <h2>Kaynak ve kanıt</h2>

      <div className="grid two">
        <Alan ad="Resmî kaynak" deger={kanit.sourceName} />
        <Alan
          ad="Kaynak doğrulama durumu"
          deger={
            kanit.sourceVerified
              ? `Doğrulandı (${formatDate(kanit.sourceVerifiedAt)}) · ${kanit.sourceHealth}`
              : `Doğrulanmadı · ${kanit.sourceHealth}`
          }
        />
        <Alan ad="Resmî alan adı" deger={kanit.officialDomain} />
        <Alan ad="Belge sürümü" deger={kanit.documentVersion != null ? `v${kanit.documentVersion}` : null} />
        <Alan ad="Belge alınma zamanı" deger={kanit.retrievedAt ? formatDate(kanit.retrievedAt) : null} />
        <Alan ad="Karakter kümesi" deger={kanit.charset} />
        <Alan ad="Sayfa sayısı" deger={kanit.pageCount} />
        <Alan ad="Ayrıştırma durumu" deger={kanit.parseStatus} />
      </div>

      {kanit.contentHash ? (
        <div className="muted" style={{ fontSize: 12, marginTop: 10, overflowWrap: 'anywhere' }}>
          İçerik hash'i (SHA-256): <code>{kanit.contentHash}</code>
        </div>
      ) : null}

      {kanit.requiresOcr ? (
        <InfoBox>
          Bu belgenin metin katmanı yetersiz; içeriği OCR olmadan güvenle okunamıyor.
          {kanit.parseError ? ` ${kanit.parseError}` : ''} Aşağıdaki kanıt parçaları belgenin
          tamamını temsil etmeyebilir.
        </InfoBox>
      ) : null}

      <h3 style={{ marginTop: 20, marginBottom: 6 }}>Kanıt bölümleri</h3>
      {kanit.evidence.length === 0 ? (
        <EmptyState>
          Bu kayıt için kanıt parçası üretilmemiş. Kanıtsız bilgi resmî sayılmaz.
        </EmptyState>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Sayfa</th>
                <th>Bölüm</th>
                <th>Metin aralığı</th>
                <th>Metin</th>
              </tr>
            </thead>
            <tbody>
              {kanit.evidence.map((parca) => (
                <tr key={parca.sequenceNumber}>
                  <td>{parca.sequenceNumber}</td>
                  <td>{parca.pageNumber ?? '—'}</td>
                  <td>{parca.sectionTitle ?? '—'}</td>
                  <td className="muted" style={{ whiteSpace: 'nowrap' }}>
                    {parca.startOffset}–{parca.endOffset}
                  </td>
                  <td>
                    {parca.text}
                    <div
                      className="muted"
                      style={{ fontSize: 11, marginTop: 4, overflowWrap: 'anywhere' }}
                    >
                      hash: <code>{parca.textHash}</code>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ResmiKaynakDugmesi kanit={kanit} />
    </div>
  )
}

/**
 * "Resmî kaynağa git" düğmesi.
 *
 * Bağlantı **doğrulanmadan gösterilmez**: adres kaynağın resmî alan adında değilse
 * düğme hiç çıkmaz ve sebebi yazılır. Kullanıcının girdiği ya da üçüncü bir siteye ait
 * bir adres resmî kaynak gibi sunulamaz.
 *
 * Dış bağlantı yeni sekmede ve `noopener noreferrer` ile açılır: açılan sayfa bizim
 * sekmemize erişemez ve nereden geldiğini öğrenemez.
 */
function ResmiKaynakDugmesi({ kanit }: { kanit: OpportunityProvenance }) {
  if (!kanit.officialUrl) {
    return (
      <div style={{ marginTop: 20 }}>
        <InfoBox>
          Resmî kaynak bağlantısı doğrulanamadı, bu yüzden gösterilmiyor.
          {kanit.officialUrlRejectionReason ? ` ${kanit.officialUrlRejectionReason}` : ''}
        </InfoBox>
      </div>
    )
  }

  return (
    <div style={{ marginTop: 20 }}>
      <a
        className="link-button"
        href={kanit.officialUrl}
        target="_blank"
        rel="noopener noreferrer"
      >
        Resmî kaynağa git ↗
      </a>
      <div className="muted" style={{ fontSize: 12, marginTop: 6, overflowWrap: 'anywhere' }}>
        {kanit.officialUrl}
      </div>
    </div>
  )
}

function Alan({
  ad,
  deger,
  durum,
}: {
  ad: string
  deger: string | number | null | undefined
  durum?: OpportunityDetail['fieldAvailability']['deadline']
}) {
  const metin = displayField(deger, durum)
  const eksik = metin === NOT_PROVIDED_LABEL || deger === null || deger === undefined

  return (
    <div style={{ marginBottom: 10 }}>
      <div className="muted" style={{ fontSize: 12 }}>
        {ad}
      </div>
      <div className={eksik ? 'muted' : undefined}>{metin}</div>
    </div>
  )
}

/**
 * Kurallardan bir boyuta ait insan okunur ifadeyi çıkarır.
 *
 * Değer üretilmez: eşleşen kural yoksa `null` döner ve alan
 * "Resmî kaynakta belirtilmemiş" gösterilir.
 */
function KuralDegeri(kurallar: OpportunityRule[], boyut: RuleDimension): string | null {
  const eslesen = kurallar.filter((k) => k.dimension === boyut)

  if (eslesen.length === 0) return null

  return eslesen.map((k) => k.humanReadable).join(' · ')
}
