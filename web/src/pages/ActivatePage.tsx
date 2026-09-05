import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '@/api/client'
import type { PlatformActivationStatus } from '@/api/types'
import { ErrorBox, FieldError, Loading, SuccessBox } from '@/components/Common'
import { PASSWORD_MIN_LENGTH, passwordProblem } from '@/lib/password'

/**
 * Platform hesabının aktivasyon ekranı (Faz 2).
 *
 * Hesaba parola atanmaz: parolayı yalnızca bu ekranı açan kişi belirler. Bağlantı
 * tek kullanımlıktır ve 24 saat sonra geçersizleşir; bu yüzden ekran formu
 * göstermeden önce bağlantının hâlâ kullanılabilir olduğunu sorar.
 */
export default function ActivatePage() {
  const { token } = useParams<{ token: string }>()
  const navigate = useNavigate()

  const [durum, setDurum] = useState<PlatformActivationStatus | null>(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  const [parola, setParola] = useState('')
  const [tekrar, setTekrar] = useState('')
  const [hata, setHata] = useState<unknown>(null)
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [bitti, setBitti] = useState(false)

  useEffect(() => {
    let iptal = false

    api
      .getActivationStatus(token!)
      .then((d) => {
        if (!iptal) setDurum(d)
      })
      .catch((e) => {
        if (!iptal) setHata(e)
      })
      .finally(() => {
        if (!iptal) setYukleniyor(false)
      })

    return () => {
      iptal = true
    }
  }, [token])

  // Kural istemcide de uygulanır: kullanıcı sunucuya gidip gelmeden ne beklendiğini
  // görür. Sunucu tarafı yine de kendi denetimini yapar; istemci denetimi güvenlik
  // sınırı DEĞİLDİR.
  const sorun = parola ? passwordProblem(parola) : null
  const eslesmiyor = tekrar.length > 0 && parola !== tekrar
  const gonderilebilir = !sorun && !eslesmiyor && parola.length > 0 && tekrar.length > 0

  async function gonder(event: FormEvent) {
    event.preventDefault()
    setHata(null)
    setGonderiliyor(true)

    try {
      await api.completeActivation({
        token: token!,
        password: parola,
        passwordConfirmation: tekrar,
      })

      setBitti(true)
      // Parola bellekte tutulmaz.
      setParola('')
      setTekrar('')
    } catch (e) {
      setHata(e)
    } finally {
      setGonderiliyor(false)
    }
  }

  if (yukleniyor) return <Loading label="Bağlantı doğrulanıyor…" />

  if (bitti) {
    return (
      <div className="login-shell">
        <div className="card login-card">
          <div className="brand" style={{ marginBottom: 6 }}>
            GOVAI
            <small>Fırsat Karar Destek Paneli</small>
          </div>

          <SuccessBox>
            Hesabınız etkinleştirildi. Artık belirlediğiniz parolayla giriş
            yapabilirsiniz. Bu bağlantı bir daha kullanılamaz.
          </SuccessBox>

          <button
            className="primary"
            type="button"
            style={{ width: '100%' }}
            onClick={() => navigate('/login', { replace: true })}
          >
            Giriş ekranına git
          </button>
        </div>
      </div>
    )
  }

  if (!durum?.isRedeemable) {
    return (
      <div className="login-shell">
        <div className="card login-card">
          <div className="brand" style={{ marginBottom: 6 }}>
            GOVAI
            <small>Fırsat Karar Destek Paneli</small>
          </div>

          <h2 style={{ marginTop: 0 }}>Bağlantı kullanılamıyor</h2>
          <p className="muted">
            {durum?.reason ??
              'Bağlantı geçersiz, süresi dolmuş ya da daha önce kullanılmış.'}
          </p>
          <p className="muted">
            Yeni bir aktivasyon bağlantısı için platform yöneticinize başvurun.
          </p>

          <Link to="/login">Giriş ekranına dön</Link>
        </div>
      </div>
    )
  }

  return (
    <div className="login-shell">
      <form className="card login-card" onSubmit={gonder}>
        <div className="brand" style={{ marginBottom: 6 }}>
          GOVAI
          <small>Fırsat Karar Destek Paneli</small>
        </div>

        <h2 style={{ marginTop: 0, marginBottom: 4 }}>Parolanızı belirleyin</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          <strong>{durum.email}</strong> hesabı için parola belirliyorsunuz. Bu bağlantı
          tek kullanımlıktır.
        </p>

        {hata ? <ErrorBox error={hata} /> : null}

        <div className="field">
          <label htmlFor="parola">Parola</label>
          <input
            id="parola"
            type="password"
            autoComplete="new-password"
            value={parola}
            onChange={(e) => setParola(e.target.value)}
            required
          />
          {sorun ? (
            <FieldError message={sorun} />
          ) : (
            <small className="muted">
              En az {PASSWORD_MIN_LENGTH} karakter; küçük harf, büyük harf, rakam ve
              simgelerden en az üçü.
            </small>
          )}
        </div>

        <div className="field">
          <label htmlFor="tekrar">Parola (tekrar)</label>
          <input
            id="tekrar"
            type="password"
            autoComplete="new-password"
            value={tekrar}
            onChange={(e) => setTekrar(e.target.value)}
            required
          />
          {eslesmiyor ? <FieldError message="Parolalar eşleşmiyor." /> : null}
        </div>

        <button
          className="primary"
          type="submit"
          disabled={gonderiliyor || !gonderilebilir}
          style={{ width: '100%' }}
        >
          {gonderiliyor ? 'Kaydediliyor…' : 'Parolayı belirle ve hesabı etkinleştir'}
        </button>
      </form>
    </div>
  )
}
