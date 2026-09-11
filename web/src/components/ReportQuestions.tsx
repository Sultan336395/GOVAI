import { Fragment, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { ReportInquiry, ReportQuestion } from '@/api/types'
import { EmptyState, ErrorBox, Loading } from '@/components/Common'
import { satirlaraBol, vurguyuAyir } from '@/lib/cevapMetni'

/**
 * Rapora soru sorma bölümü.
 *
 * Kullanıcı soru <b>yazmaz</b>, listeden seçer. Bu bir kolaylık değil doğruluk
 * kararıdır: serbest metin cevabı raporun dışına taşır ve sistemin bilmediği bir şeye
 * cevap uydurmasına zemin hazırlar. Sabit soru kümesinde her sorunun rapordaki hangi
 * veriden cevaplanacağı önceden bellidir.
 *
 * Hak <b>cevap üretilince</b> harcanır. Sorulmuş bir soruyu yeniden açmak harcamaz;
 * aksi hâlde kullanıcı okuduğu cevaba geri dönmekten çekinirdi.
 */
export default function ReportQuestions({ reportId }: { reportId: string }) {
  const queryClient = useQueryClient()

  // Açık cevap ekranda tutulur; listeye dönmek sunucuya yeni istek atmaz.
  const [acik, setAcik] = useState<ReportInquiry | null>(null)
  const [sonrakiler, setSonrakiler] = useState<ReportQuestion[]>([])

  const durum = useQuery({
    queryKey: ['report-questions', reportId],
    queryFn: () => api.getReportQuestions(reportId),
  })

  const sor = useMutation({
    mutationFn: (anahtar: string) =>
      api.answerReportQuestion(reportId, anahtar, acik?.id ?? null),
    onSuccess: async (sonuc) => {
      setAcik(sonuc.inquiry)
      setSonrakiler(sonuc.followUps)

      await queryClient.invalidateQueries({ queryKey: ['report-questions', reportId] })
    },
  })

  if (durum.isLoading) return <Loading />
  if (durum.error) return <ErrorBox error={durum.error} />
  if (!durum.data) return <EmptyState>Soru listesi alınamadı.</EmptyState>

  const { available, asked, usedCount, remainingCount, totalQuota } = durum.data

  const listeyeDon = () => {
    setAcik(null)
    setSonrakiler([])
    sor.reset()
  }

  return (
    <section className="card" style={{ marginTop: 24 }}>
      <div className="soru-baslik">
        <div>
          <h2 style={{ marginBottom: 4 }}>Rapora Soru Sor</h2>
          <p className="muted" style={{ margin: 0 }}>
            Sorular bu raporun verisinden üretildi; cevapları da rapordan hesaplanır.
          </p>
        </div>

        <HakSayaci kullanilan={usedCount} kalan={remainingCount} toplam={totalQuota} />
      </div>

      {sor.error ? <ErrorBox error={sor.error} /> : null}

      {acik ? (
        <Cevap
          kayit={acik}
          sonrakiler={sonrakiler}
          kalan={remainingCount}
          bekliyor={sor.isPending}
          onGeri={listeyeDon}
          onSor={(anahtar) => sor.mutate(anahtar)}
        />
      ) : (
        <SoruListesi
          acikSorular={available}
          sorulanlar={asked}
          kalan={remainingCount}
          bekliyor={sor.isPending}
          onSor={(anahtar) => sor.mutate(anahtar)}
          onAc={(kayit) => {
            setAcik(kayit)
            setSonrakiler([])
          }}
        />
      )}
    </section>
  )
}

function HakSayaci({
  kullanilan, kalan, toplam,
}: { kullanilan: number; kalan: number; toplam: number }) {
  return (
    <div className="hak-sayaci" aria-label={`${toplam} soru hakkından ${kalan} tanesi kaldı`}>
      <strong>{kalan}</strong>
      <span className="muted"> / {toplam} soru hakkı</span>
      <div className="hak-noktalar" aria-hidden="true">
        {Array.from({ length: toplam }, (_, i) => (
          <span key={i} className={i < kullanilan ? 'hak-nokta dolu' : 'hak-nokta'} />
        ))}
      </div>
    </div>
  )
}

function SoruListesi({
  acikSorular, sorulanlar, kalan, bekliyor, onSor, onAc,
}: {
  acikSorular: ReportQuestion[]
  sorulanlar: ReportInquiry[]
  kalan: number
  bekliyor: boolean
  onSor: (anahtar: string) => void
  onAc: (kayit: ReportInquiry) => void
}) {
  const hakBitti = kalan <= 0

  return (
    <>
      {acikSorular.length > 0 ? (
        <>
          {/*
            Hak bittiğinde sorular gizlenmez, yalnızca pasifleşir: kaybolan bir liste
            kullanıcıya "bu rapor için soru kalmadı" izlenimi verirdi.
          */}
          {hakBitti ? (
            <p className="muted">
              Bu rapor için soru hakkının tamamı kullanıldı. Sorduğunuz soruların
              cevaplarını okumaya devam edebilirsiniz.
            </p>
          ) : null}

          <div className="soru-listesi">
            {acikSorular.map((s) => (
              <button
                key={s.key}
                type="button"
                className="soru-dugmesi"
                disabled={hakBitti || bekliyor}
                onClick={() => onSor(s.key)}
              >
                {s.text}
              </button>
            ))}
          </div>
        </>
      ) : (
        <EmptyState>Bu rapordan üretilebilecek başka soru kalmadı.</EmptyState>
      )}

      {sorulanlar.length > 0 ? (
        <div style={{ marginTop: 20 }}>
          <h3 style={{ fontSize: 14 }}>Sorduğunuz sorular</h3>
          <p className="muted" style={{ marginTop: 0 }}>
            Bunları yeniden açmak hak harcamaz.
          </p>

          <div className="soru-listesi">
            {sorulanlar.map((k) => (
              <button
                key={k.id}
                type="button"
                className="soru-dugmesi sorulmus"
                onClick={() => onAc(k)}
              >
                {k.questionText}
              </button>
            ))}
          </div>
        </div>
      ) : null}
    </>
  )
}

function Cevap({
  kayit, sonrakiler, kalan, bekliyor, onGeri, onSor,
}: {
  kayit: ReportInquiry
  sonrakiler: ReportQuestion[]
  kalan: number
  bekliyor: boolean
  onGeri: () => void
  onSor: (anahtar: string) => void
}) {
  return (
    <div>
      <button type="button" className="inline-link" onClick={onGeri}>
        ← Soru listesine dön
      </button>

      <h3 style={{ marginTop: 12 }}>{kayit.questionText}</h3>

      <div className="soru-cevap">
        {satirlaraBol(kayit.answerText).map((satir, sira) => (
          <div key={sira} className={satir.length === 0 ? 'cevap-bosluk' : undefined}>
            {vurguyuAyir(satir).map((p, i) => (
              <Fragment key={i}>
                {p.vurgulu ? <strong>{p.text}</strong> : p.text}
              </Fragment>
            ))}
          </div>
        ))}
      </div>

      {sonrakiler.length > 0 ? (
        <div style={{ marginTop: 16 }}>
          <h4 style={{ fontSize: 13, marginBottom: 6 }}>Bu cevabın ardından sorulabilir</h4>

          {kalan <= 0 ? (
            <p className="muted">Soru hakkı kalmadığı için bunlar sorulamaz.</p>
          ) : null}

          <div className="soru-listesi">
            {sonrakiler.map((s) => (
              <button
                key={s.key}
                type="button"
                className="soru-dugmesi"
                disabled={kalan <= 0 || bekliyor}
                onClick={() => onSor(s.key)}
              >
                {s.text}
              </button>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}
