# Mimari kararlar (ADR)

Bu klasör, projenin geri döndürülmesi pahalı kararlarını ve **gerekçelerini** kaydeder.
Amaç, altı ay sonra "bu neden böyle yapılmış" sorusunun kod arkeolojisi gerektirmemesidir.

| No | Karar | Durum | Özet |
|---|---|---|---|
| [0001](0001-onion-mimarisi-ve-mediatr-yok.md) | Onion mimarisi, MediatR yok | Kabul | Kural motoru taşınabilir ve test edilebilir kalmalı; her use-case düz servis |
| [0002](0002-ai-karar-verici-degil.md) | AI karar verici değil | Kabul | Skoru kural motoru üretir; AI yalnızca kural taslağı çıkarır ve sonucu anlatır |
| [0003](0003-eksik-veri-elemez.md) | Eksik veri firmayı elemez | Kabul | `0` ile "girilmedi" ayrı şeydir; eksik profil `Indeterminate` üretir, eleme yapmaz |
| [0004](0004-net10-hedefi.md) | .NET 8 yerine .NET 10 | Kabul | .NET 8 desteği projenin 6. ayında bitiyor; .NET 10 LTS |
| [0005](0005-uzman-gorusu-skoru-degistirmez.md) | Uzman görüşü skoru değiştirmez | Kabul | Danışman kararı karar mekanizmasının girdisi değil denetçisidir; skor deterministik kalır |
| [0006](0006-erp-cekme-yolu.md) | ERP verisi çekilir, itilmeyi beklemez | Kabul | İtme ucu sahada hiç kullanılmadı; GOVAI veriyi kendisi okur, bulunamayan alan sıfır yazılmaz |
| [0007](0007-erp-servis-kimligi-ve-imzali-beyan.md) | ERP kimliği imzalı kısa ömürlü beyanla kurulur | Kabul | GOVAI hiçbir sır saklamaz; yalnızca açık anahtar tutulur, beyan 300 saniyede ölür |
| [0008](0008-kanit-eskimesi-ve-firsat-kaldiraci.md) | Kanıtın tazeliği ölçülür, eksik fırsat karşılığıyla gösterilir | Kabul | "Belge var" ile "belge hâlâ güvenilir" ayrı; eksik listesi yatırım gerekçesine çevrilir |

## Yeni ADR yazarken

- Mevcut bir ADR'yi **değiştirme**. Karar değiştiyse yeni bir ADR yaz, eskisinin durumunu
  "Değiştirildi — bkz. ADR-XXXX" yap.
- Şablon: Bağlam → Karar → Gerekçe → Sonuçlar (olumlu/olumsuz).
- "Sonuçlar" bölümündeki **olumsuz** kısım en değerli yerdir; ödediğin bedeli yaz.
- Bir kararı test koruyorsa test adını ADR'de belirt.
