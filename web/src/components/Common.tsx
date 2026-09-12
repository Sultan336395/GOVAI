import type { ReactNode } from 'react'
import type { EligibilityVerdict, SectorFit } from '@/api/types'
import { sectorFitClass, sectorFitLabels, verdictClass, verdictLabels } from '@/lib/format'

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

/**
 * Sektör uyumu rozeti. Liste sektöre göre sıralandığı için kullanıcının satırın neden
 * o sırada durduğunu görebilmesi gerekir; sıralama açıklanmadan "saçma" görünür.
 */
export function SectorFitBadge({ fit }: { fit: SectorFit }) {
  return <span className={`badge ${sectorFitClass[fit]}`}>{sectorFitLabels[fit]}</span>
}

export function Kpi({
  label,
  value,
  hint,
  onClick,
  acik,
  panelId,
}: {
  label: string
  value: ReactNode
  hint?: ReactNode
  /** Verilirse kart tıklanabilir olur ve altında kırılım paneli açılır. */
  onClick?: () => void
  acik?: boolean
  panelId?: string
}) {
  const icerik = (
    <>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {hint ? <div className="kpi-hint">{hint}</div> : null}
    </>
  )

  if (!onClick) {
    return <div className="card">{icerik}</div>
  }

  // Gerçek bir <button>: klavyeyle odaklanır, boşluk/enter ile açılır ve ekran
  // okuyucuya "genişletilebilir" olduğunu söyler. Tıklanabilir <div> bunların
  // hiçbirini vermez ve kart sadece fareyle kullanılabilir olurdu.
  return (
    <button
      type="button"
      className={`card kpi-tiklanabilir${acik ? ' kpi-acik' : ''}`}
      onClick={onClick}
      aria-expanded={acik}
      aria-controls={panelId}
    >
      {icerik}
      <span className="kpi-isaret" aria-hidden="true">
        {acik ? 'Kapat' : 'Listele'}
      </span>
    </button>
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
