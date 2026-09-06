# Geri dönüş noktaları

Bir faz kapanırken veya riskli bir çalışmaya başlarken buraya bir satır eklenir.
Amaç tek: "bir şey bozulursa nereye döneceğiz" sorusunun cevabı aranmadan bulunsun.

Bir geri dönüş noktası **üç parçadan** oluşur ve üçü birden olmadan işe yaramaz:
kodun commit'i, çalışan imajların digest'i ve veritabanının o andaki dökümü.
İmaj etiketi tek başına yetmez — etiket taşınabilir, digest taşınmaz.

---

## RP-1 · Faz 2 kapanışı — 06.09.2026

Faz 2'nin tamamı canlıda ve doğrulanmış durumdayken alındı. Faz 3 çalışmalarına
bu noktadan başlandı.

### Kod

| | |
|---|---|
| Dal | `feature/regtech-source-pipeline` |
| Commit | `972e900` |
| Etiket | `release-972e900` |
| Uzak | GitHub'a itilmiş |

### Çalışan imajlar (digest ile sabit)

```
ghcr.io/sultan336395/govai-api@sha256:f49e4be312f3e51c441a2c372677fffe86496f51c03906adcaf3a933d1cd3da9
ghcr.io/sultan336395/govai-worker@sha256:670f35a3ac8f89d54a7a91eb3881bc7fd8ef4c462abb089e25259b7ebb152f21
ghcr.io/sultan336395/govai-web@sha256:636d7839990eaa132e7ce41b28b2d1799c7bb3121df5d91f8736bd0186016610
```

### Veritabanı dökümü

| | |
|---|---|
| Dosya | `/opt/govai/yedek/faz2-kapanis-20260906-233151.dump` |
| Biçim | `pg_dump -Fc --no-owner` |
| SHA-256 | `ec00b7ec1f0397ce7eeb0e7ee378bf2a936dd20d0b055bbb98c676398b6d1777` |
| İzin | `600 root:root` |

**Gerçekten geri yüklenerek doğrulandı.** Döküm geçici bir veritabanına yüklendi,
karşılaştırıldı ve geçici veritabanı düşürüldü; gerçek veritabanına dokunulmadı.

| Kontrol | Sonuç |
|---|---|
| SHA-256 | Tutuyor |
| Tablo listesi | 27 / 27, birebir aynı |
| `companies` | 2 / 2 |
| `opportunities` | 9 / 9 |
| `opportunity_rules` | 22 / 22 |
| `eligibility_assessments` | 511 / 511 |
| `source_documents` | 8 / 8 |
| `users` | 3 / 3 |
| `audit_log` | 1640 / 1640 |

Doğrulama sonrası sunucuda yalnızca `govai` veritabanı kaldı.

### Bu noktada ne çalışıyor

- Sektöre göre eşleştirme ve sıralama (`SectorFit`), sektör-NACE tutarlılık denetimi
- Sektöre göre daralan NACE seçimi, tıklamayla açılan liste
- Firma düzenlemesinin yeniden skorlamayı tetiklemesi
- Denetim kaydının yalnızca gerçekleşen işlemi yazması
- Form alanlarında sözlük tanımlı `?` açıklamaları
- 489 .NET, 222 Python, 55 arayüz testi geçiyor

### Nasıl dönülür

Kod için etikete, imajlar için digest'e, veri için döküme dönülür. Veri geri
yüklemesi **yalnızca kullanıcı açıkça isterse** yapılır: dökümün üzerine yazmak,
o noktadan sonra girilen her kaydı siler.
