# Güvenlik politikası

## Desteklenen sürümler

| Sürüm | Destek |
|---|---|
| 2.2.x | Güvenlik güncellemeleri |
| 2.1.x | Kritik güvenlik güncellemeleri |
| 2.0.x | Yalnızca kritik düzeltmeler |
| PowerShell prototipi | Desteklenmiyor |

## Güvenlik açığı bildirme

Hassas ayrıntıları herkese açık issue içine yazmayın. GitHub deposundaki **Security > Report a vulnerability** özel bildirim akışını kullanın. Özel bildirim kullanılamıyorsa ayrıntı vermeden bir iletişim issue'su açın.

Bildirimde sürüm, Windows sürümü, yeniden üretme adımları, olası etki ve varsa kavram kanıtını ekleyin. İlk alındı yanıtı için hedef süre 7 gün, doğrulanmış kritik sorun için düzeltme hedefi 30 gündür.

## Güvenlik özellikleri

- Kullanıcı seviyesinde ve `asInvoker` çalışır.
- Yönetici yetkisi, hizmet, sürücü veya zamanlanmış görev kurmaz.
- Ağ istemcisi, güncelleyici ve telemetri içermez.
- Windows güvenlik ve sistem süreçleri korumalıdır.
- `High` ve `Realtime` işlem sınıfları kullanılmaz.
- Kurucu yükleri kendi içine gömer; çalışma anında internetten kod indirmez.
- Yayın paketleri SHA-256 ve GitHub artifact attestation ile doğrulanabilir.

## Yayın güvenliği kontrol listesi

1. Korunan dal ve zorunlu inceleme etkinleştirilir.
2. CodeQL ve CI başarılı olmalıdır.
3. Sürüm etiketi sabit bir commit'e işaret etmelidir.
4. GitHub Actions provenance attestation üretilmelidir.
5. Üretim dağıtımı öncesi Authenticode sertifikasıyla imzalama yapılmalıdır.
6. İmzalı dosya temiz Windows 10, Windows 11 ve 4 GB test ortamında yeniden doğrulanmalıdır.

