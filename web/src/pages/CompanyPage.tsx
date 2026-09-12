import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, Kpi, Loading } from '@/components/Common'
import EvidenceReliabilityPanel from '@/components/EvidenceReliabilityPanel'
import { categoryLabels, formatCurrency, formatDate, formatPercent } from '@/lib/format'

const sizeLabels: Record<string, string> = {
  Micro: 'Mikro',
  Small: 'Küçük',
  Medium: 'Orta',
  Large: 'Büyük',
}

const legalTypeLabels: Record<string, string> = {
  Unknown: 'Belirtilmemiş',
  SoleProprietorship: 'Şahıs işletmesi',
  LimitedCompany: 'Limited şirket',
  JointStockCompany: 'Anonim şirket',
  Cooperative: 'Kooperatif',
  Association: 'Dernek',
  Foundation: 'Vakıf',
  PublicEntity: 'Kamu kurumu',
}

export default function CompanyPage() {
  const { selectedCompanyId } = useCompanies()

  const { data, isLoading, error } = useQuery({
    queryKey: ['company', selectedCompanyId],
    queryFn: () => api.getCompany(selectedCompanyId!),
    enabled: Boolean(selectedCompanyId),
  })

  if (!selectedCompanyId) return <EmptyState>Önce bir firma seçin.</EmptyState>
  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return <EmptyState>Firma bulunamadı.</EmptyState>

  return (
    <>
      <div className="page-header">
        <div>
          <h1>{data.legalName}</h1>
          <p>
            VKN {data.taxNumber} · {legalTypeLabels[data.legalType] ?? data.legalType} ·{' '}
            {sizeLabels[data.size] ?? data.size} ölçek · profil v{data.profileVersion}
          </p>
        </div>
      </div>

      {data.profileCompleteness < 1 ? (
        <div className="card" style={{ marginBottom: 16, borderLeft: '4px solid var(--warning)' }}>
          <strong>Profil doluluğu {formatPercent(data.profileCompleteness)}</strong>
          <p className="muted" style={{ margin: '6px 0 0' }}>
            Eksik alanlar, kural motorunun bazı koşulları değerlendirememesine ve fırsatların
            "belirsiz" olarak işaretlenmesine yol açar. Profili tamamladıkça skorlar netleşir.
          </p>
        </div>
      ) : null}

      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <Kpi label="Toplam çalışan" value={data.workforce.employeeCount} />
        {/*
          Beyan edilmemiş alan "0" olarak GÖSTERİLMEZ. Gösterilseydi kullanıcı firmanın
          kadın çalışanı olmadığını sanır, girilmemiş bir alanı düzeltmesi gerektiğini
          hiç fark etmezdi.
        */}
        <Kpi
          label="Kadın çalışan"
          value={beyan(data.workforce.womenEmployeeCount)}
          hint={oran(data.workforce.womenEmployeeCount, data.workforce.employeeCount)}
        />
        <Kpi
          label="Ar-Ge personeli"
          value={beyan(data.workforce.rAndDEmployeeCount)}
          hint={oran(data.workforce.rAndDEmployeeCount, data.workforce.employeeCount)}
        />
        <Kpi label="Yıllık ciro" value={formatCurrency(data.financials.annualRevenue)} />
        <Kpi label="Bilanço" value={formatCurrency(data.financials.balanceSize)} />
        <Kpi
          label="İhracat oranı"
          value={
            data.financials.annualRevenue > 0
              ? formatPercent(data.financials.exportRevenue / data.financials.annualRevenue)
              : '—'
          }
        />
      </div>

      <div className="grid two" style={{ marginBottom: 16 }}>
        <div className="card">
          <h2>Faaliyet Kodları (NACE)</h2>
          {data.naceCodes.length === 0 ? (
            <p className="muted" style={{ margin: 0 }}>
              NACE kodu girilmemiş — sektörel eşleşme değerlendirilemiyor.
            </p>
          ) : (
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {data.naceCodes.map((nace) => (
                <li key={nace.code}>
                  <strong>{nace.code}</strong>
                  {nace.isPrimary ? ' (birincil)' : ''} {nace.description ? `— ${nace.description}` : ''}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="card">
          <h2>Lokasyonlar</h2>
          {data.locations.length === 0 ? (
            <p className="muted" style={{ margin: 0 }}>
              Lokasyon girilmemiş — bölgesel uygunluk değerlendirilemiyor.
            </p>
          ) : (
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {data.locations.map((location, index) => (
                <li key={`${location.city}-${index}`}>
                  {location.city}
                  {location.district ? ` / ${location.district}` : ''}
                  {location.nuts2Code ? ` · ${location.nuts2Code}` : ''}
                  {location.isHeadquarters ? ' · merkez' : ''}
                  {location.isInTechnopark ? ' · teknopark' : ''}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      {/*
        Kanıt güvenilirliği belge listesinin ÜSTÜNDE durur: liste "neyim var" sorusunu,
        panel "hangisine bugün güvenebilirim" sorusunu cevaplar ve ikincisi aksiyon
        gerektiren sorudur.
      */}
      <div style={{ marginBottom: 16 }}>
        <EvidenceReliabilityPanel companyId={data.id} />
      </div>

      <div className="grid two">
        <div className="card">
          <h2>Belgeler ve Sertifikalar</h2>
          {data.certificates.length === 0 ? (
            <p className="muted" style={{ margin: 0 }}>
              Kayıtlı belge yok.
            </p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Belge</th>
                    <th>Kod</th>
                    <th>Geçerlilik</th>
                  </tr>
                </thead>
                <tbody>
                  {data.certificates.map((certificate) => (
                    <tr key={certificate.code}>
                      <td>{certificate.name}</td>
                      <td>{certificate.code}</td>
                      <td>{certificate.validUntil ? formatDate(certificate.validUntil) : 'Süresiz'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div className="card">
          <h2>Aktif Yatırımlar</h2>
          {data.activeInvestments.length === 0 ? (
            <p className="muted" style={{ margin: 0 }}>
              Kayıtlı yatırım planı yok.
            </p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Yatırım</th>
                    <th>İlgili Destek</th>
                    <th>Bütçe</th>
                  </tr>
                </thead>
                <tbody>
                  {data.activeInvestments.map((investment) => (
                    <tr key={investment.title}>
                      <td>{investment.title}</td>
                      <td>{categoryLabels[investment.relatedCategory]}</td>
                      <td>{formatCurrency(investment.plannedBudget)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </>
  )
}

/**
 * Beyan edilmemiş sayıyı sıfır gibi göstermez.
 *
 * <b>0</b> firmanın "yok" beyanıdır ve öyle gösterilir; alan hiç girilmemişse bu
 * ayrı bir durumdur ve kullanıcının görmesi gereken şey odur.
 */
function beyan(deger: number | null | undefined): string {
  return deger === null || deger === undefined ? 'Beyan edilmedi' : String(deger)
}

/** Beyan yoksa oran da hesaplanmaz; 0 göstermek "oranı sıfır" demek olurdu. */
function oran(pay: number | null | undefined, toplam: number): string {
  if (pay === null || pay === undefined || toplam <= 0) {
    return '—'
  }

  return formatPercent(pay / toplam)
}
