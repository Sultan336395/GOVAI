import type { ReactNode } from 'react'
import type { EligibilityVerdict } from '@/api/types'
import { verdictClass, verdictLabels } from '@/lib/format'

export function Loading({ label = 'Yükleniyor…' }: { label?: string }) {
  return <div className="state">{label}</div>
}

export function ErrorBox({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Beklenmeyen bir hata oluştu.'
  return <div className="error-box">{message}</div>
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="state">{children}</div>
}

export function VerdictBadge({ verdict }: { verdict: EligibilityVerdict }) {
  return <span className={`badge ${verdictClass[verdict]}`}>{verdictLabels[verdict]}</span>
}

export function Kpi({
  label,
  value,
  hint,
}: {
  label: string
  value: ReactNode
  hint?: ReactNode
}) {
  return (
    <div className="card">
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {hint ? <div className="kpi-hint">{hint}</div> : null}
    </div>
  )
}

/** Skoru hem sayı hem görsel çubuk olarak gösterir; tarama sırasında hızlı karşılaştırma sağlar. */
export function ScoreCell({ score }: { score: number }) {
  return (
    <div>
      <div className="score">{score.toFixed(1)}</div>
      <div className="meter">
        <span style={{ width: `${Math.min(100, Math.max(0, score))}%` }} />
      </div>
    </div>
  )
}

/** Başarılı işlem bildirimi. Kullanıcı ne olduğunu görmeden ekran değişmemeli. */
export function SuccessBox({ children }: { children: ReactNode }) {
  return (
    <div
      className="card"
      role="status"
      style={{ borderLeft: '4px solid var(--success, #1a7f37)', marginBottom: 16 }}
    >
      {children}
    </div>
  )
}

/** Bilgilendirme kutusu: hata değil ama kullanıcının bilmesi gereken durumlar. */
export function InfoBox({ children }: { children: ReactNode }) {
  return (
    <div
      className="card"
      role="status"
      style={{ borderLeft: '4px solid var(--warning, #9a6700)', marginBottom: 16 }}
    >
      {children}
    </div>
  )
}

/**
 * Yetkisiz kullanıcıya gösterilen mesaj. Kaydın var olup olmadığını ele vermez;
 * yalnızca kullanıcının kendi rolünün yetmediğini söyler.
 */
export function NotAuthorized({ message }: { message?: string }) {
  return (
    <div className="state">
      {message ?? 'Bu ekranı görüntülemek için şirketteki rolünüz yeterli değil.'}
    </div>
  )
}

/** Form alanının altına yazılan hata metni. */
export function FieldError({ message }: { message?: string }) {
  if (!message) return null
  return (
    <div style={{ color: 'var(--danger, #b42318)', fontSize: 12, marginTop: 4 }}>{message}</div>
  )
}
