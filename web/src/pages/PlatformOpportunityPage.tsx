import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { OpportunityDetailCard } from '@/components/OpportunityDetailCard'
import { ErrorBox, InfoBox, Loading } from '@/components/Common'
import { formatDate } from '@/lib/format'

/**
 * Fırsat kaydının salt-okunur incelemesi (Platform İnceleme).
 *
 * <p>
 * Bu ekran neden var: inceleyici karantinadan çıkarma ya da onarım kararını verirken
 * "bu kayıt gerçekte ne?" sorusunu cevaplayabilmeli. Kaydın tüm künyesi — bütçe
 * kalemleri, çıkarılmış koşullar, kanıt parçaları, belge sürümü ve hash'i — zaten
 * `OpportunityDetailCard` içinde duruyordu; ama o kart yalnızca bir firmanın uygunluk
 * sayfasında kullanılıyordu. Orası kiracı verisidir ve platform rolleri erişemez.
 * Sonuç: inceleyici karar verirken elindeki tek bilgi başlık ve adresti.
 * </p>
 *
 * <p>
 * Ekranda <b>hiçbir firma verisi yoktur</b>: ne puan, ne eşleşme, ne değerlendirme.
 * Platform rolünün müşteri verisine erişmemesi sınırı korunur; gösterilen her şey
 * ortak kataloğa ve resmî belgeye aittir.
 * </p>
 *
 * <p>
 * Salt okunurdur. Değişiklik yalnızca Katalog Onarımı ve Kanıt Bağlama ekranlarından,
 * plan görülüp onaylanarak yapılır.
 * </p>
 */
export default function PlatformOpportunityPage() {
  const { opportunityId = '' } = useParams()

  const { data, isLoading, error } = useQuery({
    queryKey: ['platform-opportunity', opportunityId],
    queryFn: () => api.getOpportunity(opportunityId),
    enabled: opportunityId.length > 0,
  })

  if (isLoading) return <Loading />
  if (error) return <ErrorBox error={error} />
  if (!data) return null

  const karantinada = data.quarantineReason && data.quarantineReason !== 'None'

  return (
    <section className="page" data-sayfa="platform-firsat">
      <header className="page-header">
        <Link to="/quarantine" className="muted small" data-alan="geri">
          ← Platform İnceleme
        </Link>

        <h1>{data.title}</h1>

        <p className="muted">
          {data.publisher}
          {data.publishedAt ? ` · ${formatDate(data.publishedAt)}` : ''}
        </p>
      </header>

      {karantinada && (
        <InfoBox>
          <strong>Bu kayıt karantinada.</strong> Katalogda görünmüyor, skorlanmıyor ve
          firmalara fırsat olarak gösterilmiyor. Kayıt <b>silinmedi</b>; belgesi, ham
          içeriği ve kanıtları yerinde duruyor.
          {data.quarantineNote ? <div className="small">Not: {data.quarantineNote}</div> : null}
        </InfoBox>
      )}

      <InfoBox>
        Bu ekran <strong>salt okunurdur</strong> ve firma verisi içermez. Değişiklik
        yalnızca Katalog Onarımı ve Kanıt Bağlama ekranlarından, plan görülüp
        onaylanarak yapılır.
      </InfoBox>

      <OpportunityDetailCard data={data} />
    </section>
  )
}
