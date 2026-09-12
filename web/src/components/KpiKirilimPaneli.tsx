import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { OpportunityMatch } from '@/api/types'
import { ErrorBox, Loading } from '@/components/Common'
import MatchTable, { type VurguSutunu } from '@/components/MatchTable'
import { KPI_TANIMLARI, kpiKirilimiHesapla, toplamTutuyorMu, type KpiAnahtari } from '@/lib/kpiKirilimi'

/** Panelin kimliği; kartlar <c>aria-controls</c> ile buna bağlanır. */
export const KPI_PANEL_ID = 'kpi-kirilim-paneli'

/**
 * Sunucu sayfa boyutunu 200'de sınırlar (<c>PageRequest.MaxPageSize</c>).
 * Daha fazla değerlendirme varsa panel kalan sayfaları sırayla alır.
 */
const SAYFA_BOYUTU = 200

/**
 * Bir özet kartının kırılımı.
 *
 * <p>
 * Liste panodan DEĞİL eşleşme ucundan gelir: pano yalnızca ilk 10 kaydı taşır
 * (<c>Take(10)</c>), dolayısıyla "10 şartlı uygun" yazan kartın tamamı orada yoktur.
 * Panodan beslenseydi liste sessizce eksik kalırdı.
 * </p>
 *
 * <p>
 * Veri yalnızca kart açıldığında istenir; kapalıyken panoya ek yük binmez.
 * </p>
 */
export default function KpiKirilimPaneli({
  anahtar,
  companyId,
  karttakiSayi,
  onKapat,
}: {
  anahtar: KpiAnahtari
  companyId: string
  karttakiSayi: number
  onKapat: () => void
}) {
  const tanim = KPI_TANIMLARI[anahtar]

  const { data, isLoading, error } = useQuery({
    queryKey: ['kpi-kirilim', companyId],
    queryFn: async () => {
      // İlk sayfa çoğu firmada tek başına yeterlidir; katalog büyüdüğünde kalan
      // sayfalar da alınır. Eksik liste, kartla tutmayan bir sayı demek olurdu.
      const ilk = await api.listMatches(companyId, { page: 1, pageSize: SAYFA_BOYUTU })
      const kayitlar: OpportunityMatch[] = [...ilk.items]

      for (let sayfa = 2; kayitlar.length < ilk.totalCount && sayfa <= ilk.totalPages; sayfa++) {
        const sonraki = await api.listMatches(companyId, { page: sayfa, pageSize: SAYFA_BOYUTU })

        if (sonraki.items.length === 0) break

        kayitlar.push(...sonraki.items)
      }

      return kayitlar
    },
  })

  const kirilim = data ? kpiKirilimiHesapla(anahtar, data) : null
  const tutuyor = kirilim ? toplamTutuyorMu(anahtar, kirilim, karttakiSayi) : true
  const bicimle = (n: number) => (anahtar === 'ortalama' ? n.toFixed(1) : String(n))

  return (
    <div className="card kpi-kirilim" id={KPI_PANEL_ID}>
      <div className="page-header" style={{ marginBottom: 8 }}>
        <div>
          <h2 style={{ margin: 0 }}>{tanim.baslik}</h2>
          <p className="muted" style={{ margin: '4px 0 0', fontSize: 13 }}>
            {tanim.aciklama}
          </p>
        </div>
        <button type="button" onClick={onKapat}>
          Kapat
        </button>
      </div>

      {isLoading ? <Loading /> : null}
      {error ? <ErrorBox error={error} /> : null}

      {kirilim ? (
        <>
          <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
            {tanim.pay
              ? `${kirilim.toplam} adet · ${kirilim.firsatSayisi} fırsat`
              : `${kirilim.firsatSayisi} fırsat`}
          </p>

          {/*
            Kartla liste ayrışırsa SESSİZ KALINMAZ. Sayıyı kendiliğinden düzeltmek,
            hangisinin doğru olduğunu bilmeden birini gizlemek olurdu.
          */}
          {tutuyor ? null : (
            <p className="uyari" style={{ fontSize: 13 }}>
              Karttaki sayı ({bicimle(karttakiSayi)}) ile listedeki ({bicimle(kirilim.toplam)})
              tutmuyor. Skorlar kart yüklendikten sonra yenilenmiş olabilir; “Yeniden skorla” ile
              tazeleyin.
            </p>
          )}

          <MatchTable
            matches={kirilim.kayitlar}
            vurgu={vurguSutunu(anahtar)}
            bosMesaj="Bu ölçüte giren fırsat yok."
          />
        </>
      ) : null}
    </div>
  )
}

function vurguSutunu(anahtar: KpiAnahtari): VurguSutunu | undefined {
  const baslik = KPI_TANIMLARI[anahtar].vurguBasligi

  if (!baslik) return undefined

  const deger =
    anahtar === 'sartli'
      ? (m: OpportunityMatch) => m.missingConditionCount
      : anahtar === 'belge'
        ? (m: OpportunityMatch) => m.missingMandatoryDocumentCount
        : (m: OpportunityMatch) => m.dataGapCount

  return { baslik, deger }
}
