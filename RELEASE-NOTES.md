# Sürüm 2.2.0

## AI odaklı çekirdek

- Tek PID yerine ChatGPT/Codex süreç ailesi algılama eklendi.
- Ana süreç, bilinen AI işçileri ve CPU kullanan sıcak alt süreçler birlikte optimize ediliyor.
- AI çalışırken güvenli arka plan baskısı daha erken başlıyor.
- Arka plan adaylarına CPU önceliğine ek olarak düşük bellek önceliği ve EcoQoS uygulanıyor.
- Her değişiklik öncesinde özgün CPU, bellek ve güç durumu kaydediliyor; geri dönüş artık körlemesine `Normal` yapmıyor.
- Tanınan AI web uygulamaları Chrome, Edge, Firefox, Brave ve Opera pencere başlığından saptanıyor; bütün sekmeler yerine yalnız ana süreç ve sıcak çocuklar seçiliyor.
- Durum çıktısına `ai_focus` ve `optimized_processes` tanılama alanları eklendi.

# Sürüm 2.1.0

## Yeni

- PowerShell prototipi yerel, terminal açmayan Windows GUI uygulamasıyla değiştirildi.
- Tek dosyalı kullanıcı-başına kurucu ve Windows Yüklü Uygulamalar kaldırma kaydı eklendi.
- Sistem tepsisi kontrolü, 15 dakikalık duraklatma ve düzenlenebilir bellek eşikleri eklendi.
- RAM kapasitesine göre ilk kurulum eşikleri otomatik seçiliyor.
- Tek örnek koruması, güvenli kapanış ve öncelikleri geri yükleme eklendi.
- Eski zamanlanmış görev otomatik kaldırılıyor.
- Ağ erişimi ve telemetri yok.
- Güvenli, Dengeli ve 4–8 GB Düşük Bellek profilleri eklendi.
- Tek tık sentetik etki testi; P95/maksimum gecikme karşılaştırması ve JSON raporu eklendi.

## Doğrulananlar

- Windows 11 build 26200 üzerinde kurulum, kaldırma ve yeniden kurulum.
- Görünür terminal oluşturmayan Windows GUI alt sistemi.
- Ön plan işlem kimliğinin algılanması ve `AboveNormal` uygulanması.
- İkinci uygulama örneğinin engellenmesi.
- Kurucudaki gömülü uygulama ve SHA-256 paket bütünlüğü.
- Test sisteminde yaklaşık 35 MB çalışma belleği ve 10 saniyede 0,031 CPU saniyesi.

## Bilinen sınırlar

- Kurucu henüz Authenticode sertifikasıyla imzalanmadı; SmartScreen bilinmeyen yayıncı uyarısı gösterebilir.
- Otomatik güncelleme sistemi yoktur.
- Yönetici yetkisiyle çalışan başka uygulamaların önceliği, standart kullanıcı oturumundan değiştirilemeyebilir.
- Araç fiziksel RAM eklemez; GPU, sıcaklık, disk ve ağ darboğazlarını çözmez.
- Çok süreçli tarayıcılarda pencere sahibi süreç önceliklendirilir; sekme zamanlamasının geri kalanı tarayıcı tarafından yönetilir.

