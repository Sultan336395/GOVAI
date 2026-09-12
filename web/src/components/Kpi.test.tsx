import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Kpi } from './Common'

/**
 * Özet kartı.
 *
 * Kart tıklanabilir olduğunda gerçek bir düğme olmalıdır. Tıklanabilir bir <div>
 * ekranda aynı görünür ama klavyeyle odaklanılamaz, boşluk/enter ile açılmaz ve
 * ekran okuyucu onu "genişletilebilir" diye duyurmaz — özellik yalnızca fareyle
 * kullanılabilir hâle gelirdi.
 */

afterEach(cleanup)

describe('tıklanamayan kart', () => {
  it('KB1. onClick verilmezse düğme DEĞİLDİR', () => {
    render(<Kpi label="Uygun fırsat" value={3} />)

    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText('3')).toBeTruthy()
  })
})

describe('tıklanabilir kart', () => {
  it('KB2. Düğme olarak işaretlenir ve tıklanınca haber verir', () => {
    const tiklandi = vi.fn()
    render(<Kpi label="Uygun fırsat" value={3} onClick={tiklandi} acik={false} panelId="panel" />)

    fireEvent.click(screen.getByRole('button'))

    expect(tiklandi).toHaveBeenCalledTimes(1)
  })

  it('KB3. Açık durumu ekran okuyucuya bildirilir', () => {
    const { rerender } = render(
      <Kpi label="Uygun fırsat" value={3} onClick={() => {}} acik={false} panelId="panel" />,
    )

    expect(screen.getByRole('button').getAttribute('aria-expanded')).toBe('false')

    rerender(<Kpi label="Uygun fırsat" value={3} onClick={() => {}} acik panelId="panel" />)

    expect(screen.getByRole('button').getAttribute('aria-expanded')).toBe('true')
  })

  it('KB4. Kart hangi paneli açtığını bildirir', () => {
    // aria-controls olmadan ekran okuyucu kartla panel arasındaki bağı kuramaz.
    render(<Kpi label="Uygun fırsat" value={3} onClick={() => {}} acik panelId="kirilim" />)

    expect(screen.getByRole('button').getAttribute('aria-controls')).toBe('kirilim')
  })

  it('KB5. Klavyeyle çalışır', () => {
    // Düğme olduğu için tarayıcı enter/boşluk tuşunu tıklamaya çevirir; bu testin
    // işi o semantiğin kaybolmadığını sabitlemektir.
    const tiklandi = vi.fn()
    render(<Kpi label="Uygun fırsat" value={3} onClick={tiklandi} acik={false} panelId="panel" />)

    const dugme = screen.getByRole('button')
    dugme.focus()

    expect(document.activeElement).toBe(dugme)
    expect(dugme.getAttribute('type')).toBe('button')
  })
})
