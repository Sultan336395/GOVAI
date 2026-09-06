import { useEffect, useId, useRef, useState } from 'react'
import { fieldHelp } from '@/lib/fieldHelp'
import type { FieldHelpKey } from '@/lib/fieldHelp'

/**
 * Alan adının yanındaki "?" düğmesi ve açıklama balonu.
 *
 * Neden var: alan adları kısa olmak zorunda ("Ar-Ge personeli sayısı") ama sistemin o
 * alandan ne anladığı kısa addan çıkmıyor. Yanlış doldurulan alan yanlış skora, yanlış
 * skor yanlış fırsat listesine yol açıyor.
 *
 * Balonda terimin **sözlük anlamı** durur, sistemin o alanla ne yaptığı değil: uzun
 * davranış açıklamaları ipucunu okunmaz hâle getiriyor ve kullanıcının sorduğu soruyu
 * cevaplamıyordu.
 *
 * Fare ile üzerine gelince açılır; dokunmatik cihazda fare olayı olmadığı için TIKLAMA
 * da açar. Klavye ile sekmelenebilir ve odaklanınca açılır — açıklamayı yalnızca fare
 * kullananlara sunmak, alanı dolduramayan bir kullanıcı grubu bırakırdı.
 */
export function HelpTip({ field }: { field: FieldHelpKey }) {
  const tipId = useId()
  const [open, setOpen] = useState(false)
  const wrapper = useRef<HTMLSpanElement>(null)

  // Dokunmatikte "dışarı tıklayınca kapan" gerekir; fare için hover zaten kapatır.
  useEffect(() => {
    if (!open) return

    const disariTiklandi = (event: MouseEvent) => {
      if (wrapper.current && !wrapper.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    document.addEventListener('mousedown', disariTiklandi)
    return () => document.removeEventListener('mousedown', disariTiklandi)
  }, [open])

  const tanim = fieldHelp[field]

  return (
    <span
      className="helptip"
      ref={wrapper}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
    >
      <button
        type="button"
        className="helptip-button"
        aria-label="Bu alan ne anlama geliyor?"
        aria-describedby={open ? tipId : undefined}
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') setOpen(false)
        }}
      >
        ?
      </button>

      {open ? (
        <span className="helptip-bubble" id={tipId} role="tooltip">
          {tanim}
        </span>
      ) : null}
    </span>
  )
}

/**
 * Etiket + "?" düğmesi. Alanların çoğunda etiketin tek işi budur; her ekranda aynı
 * yapıyı elle kurmak yerine tek bileşen kullanılır ki hizalama ve erişilebilirlik
 * her yerde aynı olsun.
 */
export function FieldLabel({
  htmlFor,
  children,
  field,
}: {
  htmlFor: string
  children: React.ReactNode
  field: FieldHelpKey
}) {
  return (
    <label htmlFor={htmlFor} className="field-label">
      <span>{children}</span>
      <HelpTip field={field} />
    </label>
  )
}
