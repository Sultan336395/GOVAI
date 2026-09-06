import { useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { ScenarioRequest } from '@/api/types'
import { useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, Kpi, VerdictBadge } from '@/components/Common'
import { FieldLabel } from '@/components/HelpTip'
import { categoryLabels } from '@/lib/format'
import { fieldHelp } from '@/lib/fieldHelp'

/**
 * "What-if" ekranı: firmanın kaydına dokunmadan, değiştirilmiş bir profil üzerinde
 * aynı kural motorunu çalıştırır ve fırsat havuzunun nasıl değiştiğini gösterir.
 */
export default function SimulationPage() {
  const { selectedCompanyId } = useCompanies()

  const [name, setName] = useState('Personel ve belge senaryosu')
  const [employeeCount, setEmployeeCount] = useState('')
  const [womenEmployeeCount, setWomenEmployeeCount] = useState('')
  const [rAndDEmployeeCount, setRAndDEmployeeCount] = useState('')
  const [annualRevenue, setAnnualRevenue] = useState('')
  const [certificates, setCertificates] = useState('')

  const simulate = useMutation({
    mutationFn: (scenario: ScenarioRequest) => api.simulate(selectedCompanyId!, scenario, false),
  })

  if (!selectedCompanyId) return <EmptyState>Önce bir firma seçin.</EmptyState>

  function handleSubmit(event: FormEvent) {
    event.preventDefault()

    const scenario: ScenarioRequest = { name }
    if (employeeCount) scenario.employeeCount = Number(employeeCount)
    if (womenEmployeeCount) scenario.womenEmployeeCount = Number(womenEmployeeCount)
    if (rAndDEmployeeCount) scenario.rAndDEmployeeCount = Number(rAndDEmployeeCount)
    if (annualRevenue) scenario.annualRevenue = Number(annualRevenue)

    const codes = certificates
      .split(',')
      .map((code) => code.trim().toUpperCase())
      .filter(Boolean)

    if (codes.length > 0) scenario.addCertificateCodes = codes

    simulate.mutate(scenario)
  }

  const result = simulate.data

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Senaryo simülasyonu</h1>
          <p>
            Boş bıraktığınız alanlar firmanın mevcut değerini korur. Simülasyon firma kaydını
            değiştirmez.
          </p>
        </div>
      </div>

      <div className="grid two">
        <form className="card" onSubmit={handleSubmit}>
          <h2>Senaryo tanımı</h2>

          <div className="field">
            <FieldLabel htmlFor="scenario-name" field="scenarioName">Senaryo adı</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioName.short}</div>
            <input
              id="scenario-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="employee-count" field="scenarioEmployeeCount">Toplam çalışan sayısı</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioEmployeeCount.short}</div>
            <input
              id="employee-count"
              type="number"
              min={0}
              placeholder="Değiştirmek istemiyorsanız boş bırakın"
              value={employeeCount}
              onChange={(e) => setEmployeeCount(e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="women-count" field="scenarioWomenCount">Kadın çalışan sayısı</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioWomenCount.short}</div>
            <input
              id="women-count"
              type="number"
              min={0}
              value={womenEmployeeCount}
              onChange={(e) => setWomenEmployeeCount(e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="rnd-count" field="scenarioRndCount">Ar-Ge personeli sayısı</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioRndCount.short}</div>
            <input
              id="rnd-count"
              type="number"
              min={0}
              value={rAndDEmployeeCount}
              onChange={(e) => setRAndDEmployeeCount(e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="revenue" field="scenarioRevenue">Yıllık ciro (TL)</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioRevenue.short}</div>
            <input
              id="revenue"
              type="number"
              min={0}
              value={annualRevenue}
              onChange={(e) => setAnnualRevenue(e.target.value)}
            />
          </div>

          <div className="field">
            <FieldLabel htmlFor="certs" field="scenarioCertificates">Alınacak belgeler</FieldLabel>
            <div className="field-hint">{fieldHelp.scenarioCertificates.short}</div>
            <input
              id="certs"
              placeholder="ISO9001, ISO14001"
              value={certificates}
              onChange={(e) => setCertificates(e.target.value)}
            />
          </div>

          <button type="submit" className="primary" disabled={simulate.isPending}>
            {simulate.isPending ? 'Hesaplanıyor…' : 'Senaryoyu çalıştır'}
          </button>
        </form>

        <div className="card">
          <h2>Sonuç</h2>

          {simulate.error ? <ErrorBox error={simulate.error} /> : null}

          {!result ? (
            <p className="muted" style={{ margin: 0 }}>
              Soldaki formu doldurup senaryoyu çalıştırın.
            </p>
          ) : (
            <div className="grid kpis">
              <Kpi
                label="Uygun fırsat"
                value={`${result.baselineEligibleCount} → ${result.simulatedEligibleCount}`}
                hint={`${result.eligibleCountDelta >= 0 ? '+' : ''}${result.eligibleCountDelta} fırsat`}
              />
              <Kpi
                label="Ortalama skor"
                value={`${result.baselineAverageScore.toFixed(1)} → ${result.simulatedAverageScore.toFixed(1)}`}
                hint={`${result.averageScoreDelta >= 0 ? '+' : ''}${result.averageScoreDelta.toFixed(1)} puan`}
              />
              <Kpi
                label="Değerlendirilen"
                value={result.evaluatedOpportunityCount}
                hint="Açık çağrı sayısı"
              />
            </div>
          )}
        </div>
      </div>

      {result && result.impacts.length > 0 ? (
        <div className="card" style={{ marginTop: 16 }}>
          <h2>Etkilenen fırsatlar</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Fırsat</th>
                  <th>Destek türü</th>
                  <th>Mevcut skor</th>
                  <th>Senaryo skoru</th>
                  <th>Fark</th>
                  <th>Karar değişimi</th>
                </tr>
              </thead>
              <tbody>
                {result.impacts.map((impact) => (
                  <tr key={impact.opportunityId}>
                    <td>
                      {impact.opportunityTitle}
                      {impact.becameEligible ? (
                        <div style={{ fontSize: 12, color: 'var(--success)', fontWeight: 600 }}>
                          Bu senaryoda uygun hâle geliyor
                        </div>
                      ) : null}
                    </td>
                    <td>{categoryLabels[impact.supportCategory]}</td>
                    <td>{impact.baselineScore.toFixed(1)}</td>
                    <td>{impact.simulatedScore.toFixed(1)}</td>
                    <td
                      style={{
                        color: impact.delta > 0 ? 'var(--success)' : impact.delta < 0 ? 'var(--danger)' : undefined,
                        fontWeight: 600,
                      }}
                    >
                      {impact.delta > 0 ? '+' : ''}
                      {impact.delta.toFixed(1)}
                    </td>
                    <td>
                      <VerdictBadge verdict={impact.baselineVerdict} />{' '}
                      <span className="muted">→</span> <VerdictBadge verdict={impact.simulatedVerdict} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {result && result.impacts.length === 0 ? (
        <div className="card" style={{ marginTop: 16 }}>
          <EmptyState>Bu senaryo hiçbir fırsatın skorunu değiştirmedi.</EmptyState>
        </div>
      ) : null}
    </>
  )
}
