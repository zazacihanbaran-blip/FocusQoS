# FocusQoS 2.2

Windows 10 ve Windows 11 için özellikle ChatGPT, Codex ve benzeri çok süreçli AI uygulamalarına odaklanan, kullanıcı seviyesinde çalışan performans yardımcısı.

[Windows kurucusunu indir](https://github.com/zazacihanbaran-blip/FocusQoS/releases/latest/download/FocusQoS-Setup-2.2.0.exe) · [Tüm sürümler](https://github.com/zazacihanbaran-blip/FocusQoS/releases)

> FocusQoS is a local, open-source Windows 10/11 performance helper for AI workloads. It prioritizes the active ChatGPT, Codex, Claude, Cursor or Windsurf process family while reducing only allow-listed background contention. It does not use telemetry, network access, drivers, administrator privileges, `High`, or `Realtime` priority.

## Temel görevi

Bu araç RAM “temizleyici” değildir ve fiziksel RAM üretmez. Temel görevi, siz bir uygulamayla çalışırken Windows zamanlayıcısına o uygulamanın gecikmeye duyarlı olduğunu bildirmek; kullanılmayan, açıkça izin verilen arka plan işlerini CPU, bellek ve güç bakımından daha düşük önceliğe almaktır. Böylece mevcut kaynaklar etkin işe yönelir.

AI uygulamaları tek işlemden oluşmadığı için 2.2, pencere sahibi PID ile yetinmez. ChatGPT/Codex ailesindeki ana süreç, bilinen AI işçileri ve son ölçümde gerçekten çalışan en sıcak alt süreçler birlikte seçilir. Chrome, Edge, Firefox, Brave ve Opera'da pencere başlığı tanınan bir AI hizmetini gösteriyorsa yalnız tarayıcı ana süreci ile en sıcak birkaç çocuk seçilir; bütün sekmeler topluca yükseltilmez.

## Güvenlik ve davranış

- Yönetici yetkisi, sürücü veya Windows hizmeti kurmaz.
- Ağ bağlantısı ve telemetri kullanmaz.
- Uygulama kapatmaz veya askıya almaz.
- Sistem, güvenlik, ses ve masaüstü süreçlerine müdahale etmez.
- Etkin AI süreç ailesinin seçili parçalarını en fazla `AboveNormal` yapar ve HighQoS uygular.
- AI kullanılırken bellek baskısını daha erken algılar; güvenli arka plan adaylarını `BelowNormal`, düşük bellek önceliği ve EcoQoS ile sınırlar.
- Kritik bellek daraltması kullanıcı tarafından kapatılabilir.
- Odak değişince veya kapanırken CPU, bellek ve güç durumunu kaydedilen özgün değerine geri yükler.

## Modlar

- **Güvenli:** En az müdahale; çalışma kümesi daraltması kapalıdır.
- **Dengeli:** Genel kullanım için önerilir ve RAM miktarına göre eşik seçer.
- **4–8 GB Düşük Bellek:** Bellek baskısını daha erken algılar ve güvenli arka plan yüklerine daha hızlı tepki verir.

Uygulama Microsoft'un uyarısı nedeniyle `High` veya `Realtime` öncelik kullanmaz; ön plan için en fazla `AboveNormal` kullanır.

## Etki testi

Kontrol merkezindeki **Etki testi** düğmesi aynı yerel sentetik yükü önce optimizasyon kapalı, sonra açık durumda çalıştırır. P95 ve maksimum zamanlayıcı gecikmesini gösterir ve ham JSON sonucu `%LocalAppData%\FocusQoS\benchmarks` altında saklar. Test ağ kullanmaz; 10 saniyelik iki aşamada en fazla 128 MB test belleği ayırır.

Gerçek 4 GB doğrulaması için `tests/4GB-TEST-PROTOCOL.md` ve ağ erişimi kapalı Windows Sandbox şablonu sağlanır. Sentetik sonuçlar fiziksel düşük-RAM cihaz testinin yerine geçirilmemelidir.

## Uyumluluk

- Windows 10 22H2
- Windows 11 22H2, 23H2, 24H2, 25H2 ve sonraki uyumlu sürümler
- x64 ve Windows on ARM üzerindeki x64 uyumluluk katmanı
- Kullanıcı başına kurulur; kurulum yolu `%LocalAppData%\FocusQoS`

## Dağıtım

`FocusQoS-Setup-2.2.0.exe` tek dosyalı kurucudur. Kod imzalama sertifikası eklenene kadar Windows SmartScreen “bilinmeyen yayıncı” uyarısı gösterebilir. Dağıtım öncesi Authenticode imzası ve güvenilir HTTPS indirme adresi önerilir.

## Sessiz kurulum

```text
FocusQoS-Setup-2.2.0.exe /silent
```

## Kaldırma

Windows Ayarlar > Uygulamalar > Yüklü uygulamalar bölümünden kaldırılabilir.

## Proje belgeleri

- `ARCHITECTURE.md`: çalışma şekli ve profiller
- `SECURITY.md`: güvenlik politikası ve yayın kontrol listesi
- `THREAT-MODEL.md`: tehditler, güven sınırları ve önlemler
- `CONTRIBUTING.md`: katkı ve benchmark kanıt kuralları

Windows API tasarım dayanakları: [SetPriorityClass](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass), [SetProcessInformation](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation), [Memory Priority](https://learn.microsoft.com/windows/win32/api/processthreadsapi/ns-processthreadsapi-memory_priority_information), [Scheduling Priorities](https://learn.microsoft.com/windows/win32/procthread/scheduling-priorities), [Working Set](https://learn.microsoft.com/windows/win32/memory/working-set).
