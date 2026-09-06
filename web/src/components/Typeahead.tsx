import { useEffect, useId, useRef, useState } from 'react'

/**
 * Öneri listesinden seçim yapılan alan ("bunu mu demek istediniz?").
 *
 * Neden var: sektör ve NACE kodu serbest metin olarak giriliyordu. Sonucu sahada
 * görüldü — bir firmanın sektörü "inşaat" yazarken NACE kodu 2562 (metal işleme)
 * kalmıştı; motor NACE koduna baktığı için firma kendi sektöründeki ihalelerde
 * "sektör uyumsuz" görünüyordu. Serbest metin ayrıca aynı sektörün onlarca yazımını
 * üretir ve hiçbiri eşleşmez.
 *
 * Bu yüzden alan **yazılmaz, seçilir**. Kullanıcının yazdığı metin yalnızca arama
 * sorgusudur; forma giden değer her zaman listeden seçilmiş bir kayıttır. Liste
 * sunucudan gelir — arayüz ile kayıt doğrulaması aynı kataloğu kullanır, aksi hâlde
 * arayüzde geçerli görünen bir seçim sunucuda reddedilirdi.
 */

export interface TypeaheadOption {
  /** Forma ve sunucuya giden kanonik değer. */
  value: string
  /** Listede görünen ana metin. */
  label: string
  /** Listede ana metnin altında görünen açıklama. */
  hint?: string
}

interface TypeaheadProps {
  id: string
  /** Seçili kanonik değer; hiçbir şey seçilmemişse boş dizi. */
  value: string
  onChange: (value: string) => void
  search: (q: string) => Promise<TypeaheadOption[]>
  /** Öneri listesinin açılması için gereken en az karakter. */
  minChars?: number
  placeholder?: string
  /** Seçili değeri listede bulmak için; NACE'de "2562" ile "25.62" aynı koddur. */
  matches?: (option: TypeaheadOption, value: string) => boolean
  disabled?: boolean
  /** Seçim sonrası alanı temizle (çoklu seçimde kullanılır). */
  clearOnSelect?: boolean
  autoFocus?: boolean
}

const DEBOUNCE_MS = 200

export function Typeahead({
  id,
  value,
  onChange,
  search,
  minChars = 3,
  placeholder,
  matches = (option, v) => option.value === v,
  disabled = false,
  clearOnSelect = false,
  autoFocus = false,
}: TypeaheadProps) {
  const listId = useId()

  const [text, setText] = useState('')
  const [options, setOptions] = useState<TypeaheadOption[]>([])
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [loading, setLoading] = useState(false)

  /** Dışarıdan gelen değerin listede karşılığı bulunamadı — kullanıcı yeniden seçmeli. */
  const [unresolved, setUnresolved] = useState(false)

  const wrapper = useRef<HTMLDivElement>(null)
  const resolvedFor = useRef<string | null>(null)

  // Düzenleme formunda alan dolu gelir ama etiketi bilinmez. Değeri bir kez aratıp
  // etiketini buluruz; bulunamazsa (serbest metin döneminden kalan kayıt) alan boş
  // kalır ve kullanıcı uyarılır — eski değer sessizce geçerli sayılmaz.
  useEffect(() => {
    if (clearOnSelect) return

    if (!value) {
      resolvedFor.current = null
      setText('')
      setUnresolved(false)
      return
    }

    if (resolvedFor.current === value) return
    resolvedFor.current = value

    let iptal = false

    search(value)
      .then((list) => {
        if (iptal) return

        const hit = list.find((option) => matches(option, value))

        if (hit) {
          setText(hit.label)
          setUnresolved(false)
          // Katalogdaki kanonik yazıma hizala ("2562" -> "25.62").
          if (hit.value !== value) {
            resolvedFor.current = hit.value
            onChange(hit.value)
          }
        } else {
          setText('')
          setUnresolved(true)
        }
      })
      .catch(() => {
        if (!iptal) setUnresolved(true)
      })

    return () => {
      iptal = true
    }
    // `search`, `matches` ve `onChange` her render'da yeniden üretilebilir; bu etki
    // yalnızca değerin kendisi değiştiğinde çalışmalıdır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, clearOnSelect])

  // Yazarken öneri getir. Gecikme (debounce) her tuş için istek göndermeyi önler.
  useEffect(() => {
    if (!open) return

    const q = text.trim()

    if (q.length < minChars) {
      setOptions([])
      setLoading(false)
      return
    }

    setLoading(true)
    let iptal = false

    const zamanlayici = window.setTimeout(() => {
      search(q)
        .then((list) => {
          if (iptal) return
          setOptions(list)
          setActive(0)
        })
        .catch(() => {
          if (!iptal) setOptions([])
        })
        .finally(() => {
          if (!iptal) setLoading(false)
        })
    }, DEBOUNCE_MS)

    return () => {
      iptal = true
      window.clearTimeout(zamanlayici)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text, open, minChars])

  // Dışarı tıklanınca kapan. Açık kalan liste altındaki alanları örter.
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

  const sec = (option: TypeaheadOption) => {
    resolvedFor.current = option.value
    setUnresolved(false)
    setOpen(false)
    setOptions([])
    setText(clearOnSelect ? '' : option.label)
    onChange(option.value)
  }

  const tusaBasildi = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setOpen(true)
      setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (event.key === 'Enter') {
      // Enter yalnızca listeden seçer; serbest metni ASLA kabul etmez.
      event.preventDefault()
      if (open && options[active]) sec(options[active])
    } else if (event.key === 'Escape') {
      setOpen(false)
    }
  }

  const yaziliyor = text.trim().length
  const azKarakter = open && yaziliyor > 0 && yaziliyor < minChars
  const sonucYok = open && !loading && yaziliyor >= minChars && options.length === 0

  return (
    <div className="typeahead" ref={wrapper}>
      <input
        id={id}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        autoComplete="off"
        autoFocus={autoFocus}
        disabled={disabled}
        placeholder={placeholder}
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          setOpen(true)
          // Yazmaya başlayınca önceki seçim düşer: alanda görünen metnin forma giden
          // değerle uyuşmadığı bir an olmamalı.
          if (!clearOnSelect && value) {
            resolvedFor.current = null
            onChange('')
          }
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={tusaBasildi}
      />

      {open && (loading || options.length > 0 || azKarakter || sonucYok) ? (
        <ul className="typeahead-list" id={listId} role="listbox">
          {azKarakter ? (
            <li className="typeahead-note">En az {minChars} karakter yazın.</li>
          ) : null}

          {loading && !azKarakter ? <li className="typeahead-note">Aranıyor…</li> : null}

          {sonucYok ? <li className="typeahead-note">Eşleşen kayıt yok.</li> : null}

          {options.map((option, index) => (
            <li
              key={option.value}
              role="option"
              aria-selected={index === active}
              className={index === active ? 'typeahead-option active' : 'typeahead-option'}
              onMouseEnter={() => setActive(index)}
              onMouseDown={(e) => {
                e.preventDefault()
                sec(option)
              }}
            >
              <span className="typeahead-label">{option.label}</span>
              {option.hint ? <span className="typeahead-hint">{option.hint}</span> : null}
            </li>
          ))}
        </ul>
      ) : null}

      {unresolved ? (
        <div className="field-warning">
          Kayıtlı değer güncel listede yok; lütfen listeden yeniden seçin.
        </div>
      ) : null}
    </div>
  )
}

interface TypeaheadMultiProps extends Omit<TypeaheadProps, 'value' | 'onChange' | 'clearOnSelect'> {
  values: string[]
  onChange: (values: string[]) => void
  /** Seçilen değerin etiketi; rozet üstünde gösterilir. */
  labelOf?: (value: string) => string
}

/** Çoklu seçim: seçilenler rozet olarak durur, alan her seçimden sonra temizlenir. */
export function TypeaheadMulti({
  id,
  values,
  onChange,
  labelOf = (v) => v,
  ...rest
}: TypeaheadMultiProps) {
  return (
    <div>
      {values.length > 0 ? (
        <div className="chip-row">
          {values.map((value) => (
            <span className="chip" key={value}>
              {labelOf(value)}
              <button
                type="button"
                className="chip-remove"
                aria-label={`${labelOf(value)} kaldır`}
                onClick={() => onChange(values.filter((v) => v !== value))}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      ) : null}

      <Typeahead
        {...rest}
        id={id}
        value=""
        clearOnSelect
        onChange={(value) => {
          if (value && !values.includes(value)) onChange([...values, value])
        }}
      />
    </div>
  )
}
