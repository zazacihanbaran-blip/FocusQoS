# Tehdit modeli

## Korunan varlıklar

- Kullanıcının açık çalışmaları ve uygulama kararlılığı
- Windows oturumunun yanıt verebilirliği
- Kurulum ve otomatik başlangıç bütünlüğü
- Yayın paketinin kaynağı ve değişmezliği

## Güven sınırları

- Uygulama yalnızca mevcut kullanıcının erişebildiği süreçlere müdahale eder.
- Yükseltilmiş veya başka kullanıcıya ait süreçler güven sınırının dışındadır.
- Ayarlar ve günlükler `%LocalAppData%` altında mevcut kullanıcı tarafından değiştirilebilir; güvenlik kararı veya sır içermez.
- GitHub Actions yayın zinciri ayrı bir tedarik zinciri güven sınırıdır.

## Başlıca tehditler ve önlemler

| Tehdit | Önlem |
|---|---|
| Sistem sürecini yavaşlatma | Sabit korumalı liste ve kullanıcı seviyesi izinler |
| Öncelik nedeniyle sistem açlığı | Yalnızca `AboveNormal` / `BelowNormal`; `High` ve `Realtime` yok |
| Aşırı bellek daraltma ve sayfalama | Kritik eşik, bekleme süresi, kapatılabilir özellik |
| Sahte kurucu | SHA-256, GitHub attestation ve planlanan Authenticode imzası |
| Otomatik başlangıç kalıcılığının kötüye kullanılması | Yalnızca açık HKCU Run kaydı; uygulama içinden ve kaldırıcıdan silinebilir |
| Güncelleme zinciri saldırısı | Uygulama içinden ağ güncellemesi yok; sürümler GitHub release üzerinden manuel |
| Benchmark ile kaynak tüketimi | En fazla 60 saniye, 512 MB kod sınırı; ürün testi 10 saniye/128 MB kullanır |

## Kapsam dışı

- Zararlı veya ele geçirilmiş yönetici hesabına karşı koruma
- Kernel sürücüsü, donanım yazılımı veya hipervizör saldırıları
- Başka uygulamaların kendi iç zamanlayıcı hataları
- Fiziksel RAM, disk, GPU veya termal kapasite eksikliği

