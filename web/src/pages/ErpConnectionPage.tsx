import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { ErpAuthMode, ErpFieldMap, ErpVendor } from '@/api/types'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, InfoBox, Loading, SuccessBox } from '@/components/Common'
import { FieldLabel } from '@/components/HelpTip'
import { formatDate } from '@/lib/format'

/**
 * ERP Bağlantısı.
 *
 * Firmanın ciro, personel kırılımı ve belgeleri kendi ERP'sinden okunur; profil elle
 * güncellenmeyi beklemeden güncel kalır.
 *
 * Ekranın iki sözü var:
 *
 *   1. Kayıtlı kimlik bilgisi ASLA geri gösterilmez. Gösterilen bir sır, ekran
 *      görüntüsüne ve tarayıcı geçmişine düşer. Alan boş bırakılırsa mevcut kimlik korunur.
 *   2. Deneme çekimi hangi alanların bulunduğunu ve hangilerinin BULUNAMADIĞINI söyler.
 *      Kullanıcı bunu görmeden eşlemeyi düzeltemez.
 */
export default function ErpConnectionPage() {
  const { selectedCompanyId } = useCompanies()
  const queryClient = useQueryClient()

  const [vendor, setVendor] = useState<ErpVendor>('Logo')
  const [baseUrl, setBaseUrl] = useState('')
  const [authMode, setAuthMode] = useState<ErpAuthMode>('ApiKeyHeader')
  const [secret, setSecret] = useState('')
  const [isOnPremise, setIsOnPremise] = useState(false)
  const [hata, setHata] = useState<unknown>(null)
  const [sonuc, setSonuc] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['erp-connection', selectedCompanyId],
    queryFn: () => api.getErpConnection(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  useEffect(() => {
    if (!data) return

    setVendor(data.vendor)
    setBaseUrl(data.baseUrl)
    setAuthMode(data.authMode)
    setIsOnPremise(data.isOnPremise)
    // Kimlik bilgisi BİLEREK doldurulmaz; sunucu da geri göndermez.
  }, [data])

  const kaydet = useMutation({
    mutationFn: () =>
      api.upsertErpConnection(selectedCompanyId!, {
        vendor,
        baseUrl,
        authMode,
        secret: secret.trim() === '' ? null : secret,
        isOnPremise,
      }),
    onSuccess: async () => {
      setHata(null)
      setSecret('')
      setSonuc('Bağlantı kaydedildi.')
      await queryClient.invalidateQueries({ queryKey: ['erp-connection', selectedCompanyId] })
    },
    onError: (e) => setHata(e),
  })

  const cek = useMutation({
    mutationFn: () => api.pullErpProfile(selectedCompanyId!),
    onSuccess: async (r) => {
      setHata(null)
      setSonuc(r.message)
      await queryClient.invalidateQueries({ queryKey: ['erp-connection', selectedCompanyId] })
    },
    onError: (e) => setHata(e),
  })

  if (!selectedCompanyId) {
    return <EmptyState>Bağlantıyı görmek için önce bir firma seçin.</EmptyState>
  }

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />

  return (
    <>
      <div className="page-header">
        <div>
          <h1>ERP Bağlantısı</h1>
          <p>
            Firmanın yıllık cirosu, personel kırılımı (kadın, genç, Ar-Ge, engelli) ve
            belgeleri kendi ERP sisteminden okunur. Bağlantı kurulduğunda profil her gece
            kendiliğinden tazelenir; elle güncelleme gerekmez.
          </p>
        </div>
      </div>

      {hata ? <ErrorBox error={hata} /> : null}
      {sonuc ? <SuccessBox>{sonuc}</SuccessBox> : null}

      <InfoBox>
        Bağlantı <strong>yalnızca okur</strong>. ERP'nize yazan bir yol yoktur: muhasebe ve
        bordro kayıtlarınıza yazma yetkisi istemek, entegrasyonun riskini faydasının çok
        ötesine taşırdı.
      </InfoBox>

      {data ? <Durum data={data} /> : null}

      <section style={{ marginTop: 24 }}>
        <h2>Bağlantı Ayarları</h2>

        <div className="field">
          <FieldLabel htmlFor="vendor" field="erpUrun">ERP ürünü</FieldLabel>
          <select id="vendor" value={vendor} onChange={(e) => setVendor(e.target.value as ErpVendor)}>
            <option value="Logo">Logo</option>
            <option value="Netsis">Netsis</option>
            <option value="Mikro">Mikro</option>
            <option value="Sap">SAP</option>
            <option value="Nebim">Nebim</option>
            <option value="GenericRest">Diğer (alan eşlemesini kendim tanımlayacağım)</option>
          </select>
        </div>

        <div className="field">
          <FieldLabel htmlFor="baseUrl" field="erpAdres">Veri adresi</FieldLabel>
          <input
            id="baseUrl"
            type="url"
            value={baseUrl}
            placeholder="https://erp.firmaniz.com/api/govai"
            onChange={(e) => setBaseUrl(e.target.value)}
          />
        </div>

        <div className="field">
          <FieldLabel htmlFor="authMode" field="erpKimlikBicimi">Kimlik biçimi</FieldLabel>
          <select
            id="authMode"
            value={authMode}
            onChange={(e) => setAuthMode(e.target.value as ErpAuthMode)}
          >
            <option value="ApiKeyHeader">API anahtarı (X-API-Key başlığı)</option>
            <option value="BearerToken">Jeton (Bearer)</option>
            <option value="BasicAuth">Kullanıcı adı ve parola</option>
          </select>
        </div>

        <div className="field">
          <FieldLabel htmlFor="secret" field="erpKimlik">Kimlik bilgisi</FieldLabel>
          <input
            id="secret"
            type="password"
            autoComplete="new-password"
            value={secret}
            placeholder={data?.hasSecret ? 'Kayıtlı — değiştirmek için yazın' : ''}
            onChange={(e) => setSecret(e.target.value)}
          />
          <small>
            {data?.hasSecret
              ? 'Kayıtlı kimlik güvenlik gereği geri gösterilmez. Boş bırakırsanız korunur.'
              : 'Şifrelenerek saklanır; hiçbir ekranda ve kayıtta geri gösterilmez.'}
          </small>
        </div>

        <div className="field">
          <label htmlFor="onprem">
            <input
              id="onprem"
              type="checkbox"
              checked={isOnPremise}
              onChange={(e) => setIsOnPremise(e.target.checked)}
            />
            {' '}ERP sunucusu şirket ağımızın içinde
          </label>
          <small>
            Kurum içi adreslere (ör. 192.168.x.x) yalnızca bu kutu işaretliyken gidilir.
            Bu bir güvenlik beyanıdır ve kayda geçer.
          </small>
        </div>

        <div className="toolbar">
          <button
            type="button"
            className="primary"
            onClick={() => kaydet.mutate()}
            disabled={kaydet.isPending || baseUrl.trim() === ''}
          >
            {kaydet.isPending ? 'Kaydediliyor…' : 'Kaydet'}
          </button>

          <button
            type="button"
            onClick={() => cek.mutate()}
            disabled={cek.isPending || !data}
          >
            {cek.isPending ? 'Çekiliyor…' : 'Şimdi Dene ve Çek'}
          </button>
        </div>
      </section>

      {data ? <Esleme esleme={data.fieldMap} /> : null}
    </>
  )
}

function Durum({ data }: { data: NonNullable<Awaited<ReturnType<typeof api.getErpConnection>>> }) {
  const durumAdlari = {
    NeverRun: 'Henüz çalışmadı',
    Succeeded: 'Başarılı',
    NoChange: 'Değişiklik yok',
    Failed: 'Başarısız',
  } as const

  return (
    <section style={{ marginTop: 16 }}>
      <h2>Son Durum</h2>
      <div className="table-wrap">
        <table>
          <tbody>
            <tr>
              <th>Durum</th>
              <td>
                {durumAdlari[data.lastRunStatus]}
                {!data.isEnabled ? ' · bağlantı durduruldu' : null}
              </td>
            </tr>
            <tr>
              <th>Son çalıştırma</th>
              <td>{data.lastRunAt ? formatDate(data.lastRunAt) : '—'}</td>
            </tr>
            <tr>
              <th>Açıklama</th>
              <td>{data.lastRunMessage ?? '—'}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {!data.isEnabled ? (
        <InfoBox>
          Bağlantı üst üste başarısız olduğu için durduruldu. Bu bilinçlidir: yanlış
          kimlikle her gece denemeye devam etmek, ERP'nizde hesabı kilitletir. Ayarları
          düzeltip kaydettiğinizde bağlantı yeniden açılır.
        </InfoBox>
      ) : null}
    </section>
  )
}

/**
 * Hangi ERP alanının hangi GOVAI alanına karşılık geldiği.
 *
 * Salt okunur gösterilir: kullanıcı "Şimdi Dene ve Çek" ile hangi alanların bulunduğunu,
 * hangilerinin bulunamadığını görür. Eşlemenin elle düzenlenmesi şu an API üzerinden
 * yapılır; ekranın işi önce durumu göstermektir.
 */
function Esleme({ esleme }: { esleme: ErpFieldMap }) {
  const satirlar: { ad: string; yol: string | null }[] = [
    { ad: 'Yıllık ciro', yol: esleme.annualRevenue },
    { ad: 'Bilanço büyüklüğü', yol: esleme.balanceSize },
    { ad: 'Özkaynak', yol: esleme.equity },
    { ad: 'İhracat cirosu', yol: esleme.exportRevenue },
    { ad: 'Toplam çalışan', yol: esleme.employeeCount },
    { ad: 'Kadın çalışan', yol: esleme.womenEmployeeCount },
    { ad: 'Genç çalışan', yol: esleme.youngEmployeeCount },
    { ad: 'Ar-Ge personeli', yol: esleme.rAndDEmployeeCount },
    { ad: 'Engelli çalışan', yol: esleme.disabledEmployeeCount },
    { ad: 'Belgeler', yol: esleme.certificates },
  ]

  return (
    <section style={{ marginTop: 24 }}>
      <h2>Alan Eşlemesi</h2>
      <p className="muted">
        ERP yanıtındaki hangi alanın hangi bilgiye karşılık geldiği. Ürün seçimine göre
        varsayılan gelir; kurulumunuzda alan adları farklıysa "Şimdi Dene ve Çek"
        sonucunda hangi alanların bulunamadığı yazar.
      </p>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Bilgi</th>
              <th>ERP'deki alan</th>
            </tr>
          </thead>
          <tbody>
            {satirlar.map((s) => (
              <tr key={s.ad}>
                <td>{s.ad}</td>
                <td>{s.yol ?? <span className="muted">tanımlı değil</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
