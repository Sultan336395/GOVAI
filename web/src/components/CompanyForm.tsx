import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, api } from '@/api/client'
import type { CompanyGroup, MyCompany } from '@/api/types'
import { ErrorBox, FieldError } from '@/components/Common'
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
            <label htmlFor="legalName">Ticari unvan *</label>
            <input
              id="legalName"
              value={values.legalName}
              onChange={(e) => set('legalName', e.target.value)}
              placeholder="Örnek Sanayi ve Ticaret A.Ş."
            />
            <FieldError message={fieldError('legalName')} />
          </div>

          <div className="field">
            <label htmlFor="shortName">Kısa ad</label>
            <input
              id="shortName"
              value={values.shortName ?? ''}
              onChange={(e) => set('shortName', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="taxNumber">Vergi numarası *</label>
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
            <label htmlFor="country">Ülke *</label>
            <input
              id="country"
              value={values.country}
              onChange={(e) => set('country', e.target.value.toUpperCase())}
              maxLength={2}
            />
            <FieldError message={fieldError('country')} />
          </div>

          <div className="field">
            <label htmlFor="legalType">Tüzel kişilik türü</label>
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
            <label htmlFor="taxOffice">Vergi dairesi</label>
            <input
              id="taxOffice"
              value={values.taxOffice ?? ''}
              onChange={(e) => set('taxOffice', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="mersisNumber">MERSİS numarası</label>
            <input
              id="mersisNumber"
              value={values.mersisNumber ?? ''}
              onChange={(e) => set('mersisNumber', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="tradeRegistryNumber">Ticaret sicil numarası</label>
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
            <label htmlFor="mainSector">Ana sektör *</label>
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
            <label htmlFor="primaryNaceCode">Ana NACE kodu *</label>
            <Typeahead
              id="primaryNaceCode"
              value={values.primaryNaceCode}
              onChange={(value) => set('primaryNaceCode', value)}
              search={anaSektorAramasi}
              matches={naceEsleser}
              disabled={!sektorSecildi}
              placeholder={
                sektorSecildi ? 'Kod ya da tanım yazın (ör. 412 veya tesisat)' : 'Önce ana sektörü seçin'
              }
            />
            <div className="field-hint">
              {sektorSecildi
                ? `Yalnızca "${values.mainSector}" sektörünün kodları listelenir.`
                : 'Ana sektör seçilince o sektöre ait kodlar listelenir.'}
            </div>
            <FieldError message={fieldError('primaryNaceCode')} />
          </div>

          <div className="field">
            <label htmlFor="secondaryNaceCodes">Diğer NACE kodları</label>
            <TypeaheadMulti
              id="secondaryNaceCodes"
              values={values.secondaryNaceCodes ?? []}
              onChange={(list) => set('secondaryNaceCodes', list)}
              search={tumSektorlerAramasi}
              disabled={!sektorSecildi}
              placeholder={sektorSecildi ? 'Ekleyeceğiniz kodu arayın' : 'Önce ana sektörü seçin'}
            />
            <div className="field-hint">
              Ana sektörün ve eklediğiniz alt sektörlerin kodları listelenir. Başka bir
              alanda da faaliyet gösteriyorsanız önce onu alt sektör olarak ekleyin.
            </div>
            <FieldError message={fieldError('secondaryNaceCodes')} />
          </div>

          <div className="field">
            <label htmlFor="subSectors">Alt sektörler</label>
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
            <label htmlFor="city">İl</label>
            <input
              id="city"
              value={values.city ?? ''}
              onChange={(e) => set('city', e.target.value)}
              placeholder="Mersin"
            />
          </div>

          <div className="field">
            <label htmlFor="targetCountries">Hedef pazarlar</label>
            <input
              id="targetCountries"
              value={csv(values.targetCountries)}
              onChange={(e) => set('targetCountries', parseCsv(e.target.value))}
              placeholder="DE, IT, FR"
            />
          </div>
        </div>

        <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap', marginTop: 8 }}>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={values.isInTechnopark ?? false}
              onChange={(e) => set('isInTechnopark', e.target.checked)}
            />
            Teknopark içinde
          </label>

          <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={values.exportFlag ?? false}
              onChange={(e) => set('exportFlag', e.target.checked)}
            />
            İhracat yapıyor
          </label>
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
            <label htmlFor="employeeCount">Çalışan sayısı</label>
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
            <label htmlFor="womenEmployeeCount">Kadın çalışan</label>
            <input
              id="womenEmployeeCount"
              type="number"
              min={0}
              value={values.womenEmployeeCount ?? 0}
              onChange={(e) => set('womenEmployeeCount', Number(e.target.value))}
            />
          </div>

          <div className="field">
            <label htmlFor="rAndDEmployeeCount">Ar-Ge çalışanı</label>
            <input
              id="rAndDEmployeeCount"
              type="number"
              min={0}
              value={values.rAndDEmployeeCount ?? 0}
              onChange={(e) => set('rAndDEmployeeCount', Number(e.target.value))}
            />
          </div>

          <div className="field">
            <label htmlFor="annualRevenue">Yıllık ciro (TL)</label>
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
            <label htmlFor="balanceSize">Aktif toplamı (TL)</label>
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
            <label htmlFor="website">Web sitesi</label>
            <input
              id="website"
              value={values.website ?? ''}
              onChange={(e) => set('website', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="phone">Telefon</label>
            <input
              id="phone"
              value={values.phone ?? ''}
              onChange={(e) => set('phone', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="corporateEmail">Kurumsal e-posta</label>
            <input
              id="corporateEmail"
              value={values.corporateEmail ?? ''}
              onChange={(e) => set('corporateEmail', e.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="address">Adres</label>
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
              <label htmlFor="groupId">Şirket grubu</label>
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
              <label htmlFor="parentCompanyId">Ana şirket</label>
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
              <label htmlFor="relationshipType">İlişki türü</label>
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

          <label style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8 }}>
            <input
              type="checkbox"
              checked={values.isHeadCompany ?? false}
              onChange={(e) => set('isHeadCompany', e.target.checked)}
            />
            Grubun ana şirketi
          </label>
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
