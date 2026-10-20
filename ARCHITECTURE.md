# Çalışma şekli

FocusQoS kullanıcı oturumunda, yönetici yetkisi olmadan çalışan küçük bir Windows uygulamasıdır.

1. `GetForegroundWindow` ve `GetWindowThreadProcessId` ile etkin pencerenin işlem kimliğini bulur.
2. Toolhelp süreç ağacıyla bunun ChatGPT, Codex, Claude, Cursor veya Windsurf gibi tanınan bir AI ailesine ait olup olmadığını saptar.
3. AI ailesinde ana süreç, pencere süreci, bilinen AI işçileri ve CPU zamanı artan en sıcak alt süreçleri seçer. Tarayıcıda başlık ChatGPT, Codex, Claude, Gemini, Copilot, Perplexity veya Grok içeriyorsa yalnız ana süreç ve en sıcak çocuklar seçilir. Yalnız bu küme `AboveNormal`, normal bellek önceliği ve HighQoS alır.
4. `GlobalMemoryStatusEx` ile kullanılabilir fiziksel belleği izler. AI odağında baskı eşiği genel profile göre daha erken devreye girer.
5. Baskıda yalnız açık güvenli aday listesindeki arka plan süreçlerini `BelowNormal`, bellek önceliği 4 ve EcoQoS yapar.
6. Kritik eşikte ve kullanıcı izin verdiyse aday çalışma kümelerini seyrek şekilde daraltır.
7. Odak veya baskı değişince her sürecin CPU önceliğini, bellek önceliğini ve güç politikasını kaydedilen özgün değerine döndürür. Bu, Chromium'un kendi `Idle` kararlarını bozmamayı sağlar.

Windows sistem, güvenlik, ses, oturum açma ve masaüstü süreçleri korumalı listededir. Uygulama süreç sonlandırmaz, askıya almaz, sürücü kurmaz ve ağ bağlantısı kullanmaz.

## Profiller

| Profil | Amaç | Bellek daraltma | Tarama aralığı |
|---|---|---:|---:|
| Güvenli | En az müdahale | Kapalı | 3 saniye |
| Dengeli | Genel kullanım | Kritik durumda, 90 sn beklemeli | 2 saniye |
| 4–8 GB Düşük Bellek | Düşük RAM'li cihazlar | Kritik durumda, 60 sn beklemeli | 1,5 saniye |

Eşiklerde histerezis kullanılır: baskı başladıktan sonra daha yüksek toparlanma eşiğine ulaşılmadan politika kapatılmaz. Bu, önceliklerin sık sık ileri geri değişmesini engeller.

## Etki testi

`FocusQoSBenchmark.exe` iki aşamalı yerel sentetik test çalıştırır:

- Temel aşama: optimizasyon geçici olarak duraklatılır.
- Optimizasyonlu aşama: ön plan işlemi yükseltilir, sentetik arka plan işçisi düşürülür.

Her aşamada 10 saniye boyunca aynı CPU yükü ve en fazla 128 MB test belleği kullanılır. P95 ve maksimum zamanlayıcı gecikmesi karşılaştırılır. Sonuçlar `%LocalAppData%\FocusQoS\benchmarks` altında JSON ve metin olarak saklanır.

Bu test zamanlama etkisini ölçer; uygulama açılış süresi, GPU, disk, ağ veya sıcaklık darboğazlarını ölçmez. AI süreç-ağacı seçimi ayrıca `status.txt` içindeki `ai_focus` ve `optimized_processes` alanlarıyla gözlenebilir.

