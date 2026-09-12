import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, api } from '@/api/client'
import type { CompanyGroup, MyCompany } from '@/api/types'
import { ErrorBox, FieldError } from '@/components/Common'
import { FieldLabel, HelpTip } from '@/components/HelpTip'
import { Typeahead, TypeaheadMulti } from '@/components/Typeahead'
import type { TypeaheadOption } from '@/components/Typeahead'
import { ayniMi, sadeceRakam } from '@/lib/turkce'
import { validateCompanyForm } from '@/lib/companyForm'
import type { CompanyFormValues } from '@/lib/companyForm'
import {
  legalTypeLabels,
  legalTypeOrder,
  relationshipLabels,
  relationshipOrder,
} from '@/lib/companyLabels'

/**
 * Şirket ekleme ve düzenleme formu. İki ekran da aynı alanları kullanır; tek fark
 * vergi numarasının düzenlemede kilitli olmasıdır (sunucu da değiştirilmesine izin vermez).
 *
 * Doğrulama iki yerde çalışır: burada kullanıcı beklemeden uyarılsın diye, sunucuda ise
 * asıl karar için. Sunucudan dönen alan hataları da aynı alanların altına yazılır.
 */

/**
 * Sektör ve NACE önerileri sunucudan gelir; arayüzde kopya liste TUTULMAZ.
 * İki taraf ayrı listelerden beslenseydi arayüzde geçerli görünen bir seçim
 * sunucuda reddedilirdi.
 */
const sektorAra = async (q: string): Promise<TypeaheadOption[]> =>
  (await api.searchSectors(q)).map((s) => ({ value: s.name, label: s.name }))

/**
 * NACE önerileri **seçilen sektörle sınırlıdır**.
 *
 * Sahada görülen hata: sektörü "İnşaat ve taahhüt" seçilmiş firmaya beton ürünleri
 * imalatı kodu (23.61) atandı. İki alan da tek tek geçerliydi ama farklı faaliyetleri
 * anlatıyordu; motor NACE'ye baktığı için firma kendi sektöründeki ihalelerde "uyumsuz"
 * göründü. Kullanıcı tutmayan kodu göremezse seçemez de — filtre asıl güvence budur,
 * sunucu doğrulaması ikinci hattır.
 */
const naceArayici =
  (sektorler: string[]) =>
  async (q: string): Promise<TypeaheadOption[]> =>
    (await api.searchNace(q, sektorler.filter(Boolean))).map((n) => ({
      value: n.code,
      label: `${n.code} — ${n.title}`,
      hint: n.sector,
    }))

/** "2562" ile "25.62" aynı koddur; serbest metin döneminden kalan kayıtlar da bulunsun. */
const naceEsleser = (option: TypeaheadOption, value: string) =>
  sadeceRakam(option.value) === sadeceRakam(value)

const sektorEsleser = (option: TypeaheadOption, value: string) => ayniMi(option.value, value)

interface Props {
  values: CompanyFormValues
  onChange: (values: CompanyFormValues) => void
  onSubmit: () => Promise<void>
  submitLabel: string
  /** Düzenleme ekranında vergi numarası kilitlidir. */
  lockTaxNumber?: boolean
  groups?: CompanyGroup[]
  parentCandidates?: MyCompany[]
  onCancel?: () => void
}

export default function CompanyForm({
  values,
  onChange,
  onSubmit,
  submitLabel,
  lockTaxNumber = false,
  groups = [],
  parentCandidates = [],
  onCancel,
}: Props) {
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<unknown>(null)
  const [isSaving, setIsSaving] = useState(false)

  const serverFieldErrors =
    serverError instanceof ApiError ? serverError.fieldErrors : ({} as Record<string, string[]>)

  // Sunucu alan adlarını PascalCase döner ("TaxNumber"); formdaki adla eşleştirilir.
  const fieldError = (name: string): string | undefined => {
    if (clientErrors[name]) return clientErrors[name]

    const key = Object.keys(serverFieldErrors).find(
      (candidate) => candidate.toLowerCase() === name.toLowerCase(),
    )
    return key ? serverFieldErrors[key][0] : undefined
  }

  const set = <K extends keyof CompanyFormValues>(key: K, value: CompanyFormValues[K]) =>
    onChange({ ...values, [key]: value })

  // Ana sektör değişince NACE seçimleri düşer: eski kodlar yeni sektöre ait değildir ve
  // kayıtta reddedilirlerdi. Kullanıcıyı kaydet düğmesinde şaşırtmak yerine alanı
  // burada boşaltmak dürüst davranış.
  const setMainSector = (value: string) =>
    onChange({ ...values, mainSector: value, primaryNaceCode: '', secondaryNaceCodes: [] })

  const sektorSecildi = Boolean(values.mainSector)
  const anaSektorAramasi = naceArayici([values.mainSector])
  const tumSektorlerAramasi = naceArayici([values.mainSector, ...(values.subSectors ?? [])])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()

    // Çift tıklama koruması: kaydetme sürerken ikinci istek gönderilmez.
    if (isSaving) return

    const errors = validateCompanyForm(values)
    setClientErrors(errors)
    if (Object.keys(errors).length > 0) return

    setIsSaving(true)
    setServerError(null)
    try {
      await onSubmit()
    } catch (error) {
      setServerError(error)
    } finally {
      setIsSaving(false)
    }
  }

  const csv = (list: string[] | undefined) => (list ?? []).join(', ')
  const parseCsv = (text: string) =>
    text
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean)

  const columns = { gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))' }

  return (
    <form onSubmit={handleSubmit} noValidate>
      {serverError && Object.keys(serverFieldErrors).length === 0 ? (
        <ErrorBox error={serverError} />
      ) : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Kimlik bilgileri</h2>

        <div className="grid" style={columns}>
          <div className="field">
            <FieldLabel htmlFor="legalName" field="legalName">Ticari unvan *</FieldLabel>
            <input
              id="legalName"
              value={values.legalName}
              onChange={(e) => set('legalName', e.target.value)}
              placeholder="Örnek Sanayi ve Ticaret A.Ş."
            />
            <FieldError message={fieldError('legalName')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="shortName" field="shortName">Kısa ad</FieldLabel>
            <input
              id="shortName"
              value={values.shortName ?? ''}
              onChange={(e) => set('shortName', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="taxNumber" field="taxNumber">Vergi numarası *</FieldLabel>
            <input
              id="taxNumber"
              value={values.taxNumber}
              disabled={lockTaxNumber}
              inputMode="numeric"
              onChange={(e) => set('taxNumber', e.target.value)}
              placeholder="10 haneli"
            />
            <FieldError message={fieldError('taxNumber')} />
            {lockTaxNumber ? (
              <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                Vergi numarası değiştirilemez. Farklı bir tüzel kişilik için yeni şirket ekleyin.
              </div>
            ) : null}
          </div>

          <div className="field">
            <FieldLabel htmlFor="country" field="country">Ülke *</FieldLabel>
            <input
              id="country"
              value={values.country}
              onChange={(e) => set('country', e.target.value.toUpperCase())}
              maxLength={2}
            />
            <FieldError message={fieldError('country')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="legalType" field="legalType">Tüzel kişilik türü</FieldLabel>
            <select
              id="legalType"
              value={values.legalType ?? 'LimitedCompany'}
              onChange={(e) => set('legalType', e.target.value as CompanyFormValues['legalType'])}
            >
              {legalTypeOrder.map((type) => (
                <option key={type} value={type}>
                  {legalTypeLabels[type]}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <FieldLabel htmlFor="taxOffice" field="taxOffice">Vergi dairesi</FieldLabel>
            <input
              id="taxOffice"
              value={values.taxOffice ?? ''}
              onChange={(e) => set('taxOffice', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="mersisNumber" field="mersisNumber">MERSİS numarası</FieldLabel>
            <input
              id="mersisNumber"
              value={values.mersisNumber ?? ''}
              onChange={(e) => set('mersisNumber', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="tradeRegistryNumber" field="tradeRegistryNumber">Ticaret sicil numarası</FieldLabel>
            <input
              id="tradeRegistryNumber"
              value={values.tradeRegistryNumber ?? ''}
              onChange={(e) => set('tradeRegistryNumber', e.target.value)}
            />
          </div>
        </div>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Faaliyet</h2>

        <div className="grid" style={columns}>
          <div className="field">
            <FieldLabel htmlFor="mainSector" field="mainSector">Ana sektör *</FieldLabel>
            <Typeahead
              id="mainSector"
              value={values.mainSector}
              onChange={setMainSector}
              search={sektorAra}
              matches={sektorEsleser}
              placeholder="En az 3 harf yazın, listeden seçin"
            />
            <FieldError message={fieldError('mainSector')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="primaryNaceCode" field="primaryNaceCode">Ana NACE kodu *</FieldLabel>
            <Typeahead
              id="primaryNaceCode"
              value={values.primaryNaceCode}
              onChange={(value) => set('primaryNaceCode', value)}
              search={anaSektorAramasi}
              matches={naceEsleser}
              disabled={!sektorSecildi}
              minChars={0}
              placeholder={
                sektorSecildi ? 'Tıklayın ve listeden seçin' : 'Önce ana sektörü seçin'
              }
            />
            <div className="field-hint">
              {sektorSecildi
                ? `Tıklayınca "${values.mainSector}" sektörünün kodları listelenir; daraltmak için yazabilirsiniz.`
                : 'Ana sektör seçilince o sektöre ait kodlar listelenir.'}
            </div>
            <FieldError message={fieldError('primaryNaceCode')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="secondaryNaceCodes" field="secondaryNaceCodes">Diğer NACE kodları</FieldLabel>
            <TypeaheadMulti
              id="secondaryNaceCodes"
              values={values.secondaryNaceCodes ?? []}
              onChange={(list) => set('secondaryNaceCodes', list)}
              search={tumSektorlerAramasi}
              disabled={!sektorSecildi}
              minChars={0}
              placeholder={sektorSecildi ? 'Tıklayın ve listeden seçin' : 'Önce ana sektörü seçin'}
            />
            <div className="field-hint">
              Ana sektörün ve eklediğiniz alt sektörlerin kodları listelenir. Başka bir
              alanda da faaliyet gösteriyorsanız önce onu alt sektör olarak ekleyin.
            </div>
            <FieldError message={fieldError('secondaryNaceCodes')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="subSectors" field="subSectors">Alt sektörler</FieldLabel>
            <TypeaheadMulti
              id="subSectors"
              values={values.subSectors ?? []}
              onChange={(list) => set('subSectors', list)}
              search={sektorAra}
              placeholder="Ekleyeceğiniz sektörü arayın"
            />
            <FieldError message={fieldError('subSectors')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="city" field="city">İl</FieldLabel>
            <input
              id="city"
              value={values.city ?? ''}
              onChange={(e) => set('city', e.target.value)}
              placeholder="Mersin"
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="targetCountries" field="targetCountries">Hedef pazarlar</FieldLabel>
            <input
              id="targetCountries"
              value={csv(values.targetCountries)}
              onChange={(e) => set('targetCountries', parseCsv(e.target.value))}
              placeholder="DE, IT, FR"
            />
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center', marginTop: 8 }}>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={values.isInTechnopark ?? false}
              onChange={(e) => set('isInTechnopark', e.target.checked)}
            />
            Teknopark içinde
          </label>
          <HelpTip field="isInTechnopark" />

          <label style={{ display: 'flex', gap: 8, alignItems: 'center', marginLeft: 16 }}>
            <input
              type="checkbox"
              checked={values.exportFlag ?? false}
              onChange={(e) => set('exportFlag', e.target.checked)}
            />
            İhracat yapıyor
          </label>
          <HelpTip field="exportFlag" />
        </div>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Ölçek</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          Bu alanlar boş bırakılabilir. Boş bırakılan alan "hayır" değil "bilinmiyor" sayılır ve
          firmayı elemez; yalnızca ilgili koşullar belirsiz kalır.
        </p>

        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
          <div className="field">
            <FieldLabel htmlFor="employeeCount" field="employeeCount">Çalışan sayısı</FieldLabel>
            <input
              id="employeeCount"
              type="number"
              min={0}
              value={values.employeeCount ?? 0}
              onChange={(e) => set('employeeCount', Number(e.target.value))}
            />
            <FieldError message={fieldError('employeeCount')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="womenEmployeeCount" field="womenEmployeeCount">Kadın çalışan</FieldLabel>
            <input
              id="womenEmployeeCount"
              type="number"
              min={0}
              placeholder="Beyan edilmedi"
              value={values.womenEmployeeCount ?? ''}
              onChange={(e) => set('womenEmployeeCount', e.target.value === '' ? undefined : Number(e.target.value))}
            />
            <div className="field-hint">Boş bırakmak "beyan edilmedi" demektir; <b>0</b> ise "yok" beyanıdır.</div>
            <FieldError message={fieldError('womenEmployeeCount')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="youngEmployeeCount" field="youngEmployeeCount">Genç çalışan</FieldLabel>
            <input
              id="youngEmployeeCount"
              type="number"
              min={0}
              step={1}
              inputMode="numeric"
              value={values.youngEmployeeCount ?? ''}
              onChange={(e) => set('youngEmployeeCount', e.target.value === '' ? undefined : Number(e.target.value))}
            />
            <div className="field-hint">Boş bırakmak "beyan edilmedi" demektir; <b>0</b> ise "yok" beyanıdır.</div>
            <FieldError message={fieldError('youngEmployeeCount')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="youngEmployeeMaxAge" field="youngEmployeeMaxAge">
              Genç çalışan yaş sınırı
            </FieldLabel>
            <input
              id="youngEmployeeMaxAge"
              type="number"
              min={15}
              max={65}
              step={1}
              inputMode="numeric"
              placeholder="ör. 29"
              value={values.youngEmployeeMaxAge ?? ''}
              onChange={(e) =>
                set('youngEmployeeMaxAge', e.target.value === '' ? null : Number(e.target.value))
              }
            />
            <div className="field-hint">
              Kaç yaşın altını saydığınız. Belirtilmezse teşviklerin genç şartı
              &quot;doğrulanamadı&quot; kalır.
            </div>
            <FieldError message={fieldError('youngEmployeeMaxAge')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="disabledEmployeeCount" field="disabledEmployeeCount">
              Engelli çalışan
            </FieldLabel>
            <input
              id="disabledEmployeeCount"
              type="number"
              min={0}
              step={1}
              inputMode="numeric"
              value={values.disabledEmployeeCount ?? ''}
              onChange={(e) => set('disabledEmployeeCount', e.target.value === '' ? undefined : Number(e.target.value))}
            />
            <div className="field-hint">Boş bırakmak "beyan edilmedi" demektir; <b>0</b> ise "yok" beyanıdır.</div>
            <FieldError message={fieldError('disabledEmployeeCount')} />
            <div className="field-hint">İsteğe bağlı. Yalnızca toplam sayı; kişi bilgisi istenmez.</div>
            <FieldError message={fieldError('disabledEmployeeCount')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="rAndDEmployeeCount" field="rAndDEmployeeCount">Ar-Ge çalışanı</FieldLabel>
            <input
              id="rAndDEmployeeCount"
              type="number"
              min={0}
              value={values.rAndDEmployeeCount ?? ''}
              onChange={(e) => set('rAndDEmployeeCount', e.target.value === '' ? undefined : Number(e.target.value))}
            />
            <div className="field-hint">Boş bırakmak "beyan edilmedi" demektir; <b>0</b> ise "yok" beyanıdır.</div>
            <FieldError message={fieldError('rAndDEmployeeCount')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="annualRevenue" field="annualRevenue">Yıllık ciro (TL)</FieldLabel>
            <input
              id="annualRevenue"
              type="number"
              min={0}
              value={values.annualRevenue ?? 0}
              onChange={(e) => set('annualRevenue', Number(e.target.value))}
            />
            <FieldError message={fieldError('annualRevenue')} />
          </div>

          <div className="field">
            <FieldLabel htmlFor="balanceSize" field="balanceSize">Aktif toplamı (TL)</FieldLabel>
            <input
              id="balanceSize"
              type="number"
              min={0}
              value={values.balanceSize ?? 0}
              onChange={(e) => set('balanceSize', Number(e.target.value))}
            />
          </div>
        </div>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>İletişim</h2>

        <div className="grid" style={columns}>
          <div className="field">
            <FieldLabel htmlFor="website" field="website">Web sitesi</FieldLabel>
            <input
              id="website"
              value={values.website ?? ''}
              onChange={(e) => set('website', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="phone" field="phone">Telefon</FieldLabel>
            <input
              id="phone"
              value={values.phone ?? ''}
              onChange={(e) => set('phone', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="corporateEmail" field="corporateEmail">Kurumsal e-posta</FieldLabel>
            <input
              id="corporateEmail"
              value={values.corporateEmail ?? ''}
              onChange={(e) => set('corporateEmail', e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="address" field="address">Adres</FieldLabel>
            <input
              id="address"
              value={values.address ?? ''}
              onChange={(e) => set('address', e.target.value)}
            />
          </div>
        </div>
      </div>

      {groups.length > 0 || parentCandidates.length > 0 ? (
        <div className="card" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Grup bağlantısı</h2>

          <div className="grid" style={columns}>
            <div className="field">
              <FieldLabel htmlFor="groupId" field="groupId">Şirket grubu</FieldLabel>
              <select
                id="groupId"
                value={values.groupId ?? ''}
                onChange={(e) => set('groupId', e.target.value || null)}
              >
                <option value="">Gruba bağlı değil</option>
                {groups.map((group) => (
                  <option key={group.id} value={group.id}>
                    {group.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <FieldLabel htmlFor="parentCompanyId" field="parentCompanyId">Ana şirket</FieldLabel>
              <select
                id="parentCompanyId"
                value={values.parentCompanyId ?? ''}
                onChange={(e) => set('parentCompanyId', e.target.value || null)}
              >
                <option value="">Ana şirketi yok</option>
                {parentCandidates.map((company) => (
                  <option key={company.id} value={company.id}>
                    {company.legalName}
                  </option>
                ))}
              </select>
              <FieldError message={fieldError('parentCompanyId')} />
            </div>

            <div className="field">
              <FieldLabel htmlFor="relationshipType" field="relationshipType">İlişki türü</FieldLabel>
              <select
                id="relationshipType"
                value={values.relationshipType ?? 'Independent'}
                onChange={(e) =>
                  set('relationshipType', e.target.value as CompanyFormValues['relationshipType'])
                }
              >
                {relationshipOrder.map((type) => (
                  <option key={type} value={type}>
                    {relationshipLabels[type]}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginTop: 8 }}>
            <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <input
                type="checkbox"
                checked={values.isHeadCompany ?? false}
                onChange={(e) => set('isHeadCompany', e.target.checked)}
              />
              Grubun ana şirketi
            </label>
            <HelpTip field="isHeadCompany" />
          </div>
        </div>
      ) : null}

      <div className="toolbar">
        <button type="submit" disabled={isSaving}>
          {isSaving ? 'Kaydediliyor…' : submitLabel}
        </button>
        {onCancel ? (
          <button type="button" onClick={onCancel} disabled={isSaving}>
            Vazgeç
          </button>
        ) : null}
      </div>
    </form>
  )
}
