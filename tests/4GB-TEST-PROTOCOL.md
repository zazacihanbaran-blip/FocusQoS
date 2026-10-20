# 4 GB doğrulama protokolü

Windows Sandbox destekleyen bir Windows Pro/Enterprise cihazda:

1. `C:\AFO-Test` klasörüne yayın ZIP'ini çıkarın.
2. `4gb-sandbox.wsb.example` dosyasını `.wsb` uzantısıyla kopyalayın.
3. Sandbox ağının kapalı ve eşlenen klasörün salt okunur olduğunu doğrulayın.
4. Sandbox içinde kurucuyu çalıştırın ve `4–8 GB Düşük Bellek` profilini seçin.
5. Etki testini en az üç kez çalıştırın.
6. `%LocalAppData%\FocusQoS\benchmarks` altındaki JSON sonuçlarını kaydedin.
7. Medyan P95 değişimini, Windows build numarasını ve toplam RAM'i yayın notuna ekleyin.

Başarı ölçütü: üç koşunun en az ikisinde optimizasyonlu P95 gecikmesi daha düşük olmalı; uygulama çökmesi, giriş donması veya ölçüm sonrasında kalıcı yüksek/idle öncelik kalmamalıdır.

Windows Sandbox için `MemoryInMB=4096` gerçek bir 4 GB sınır sağlar; ancak fiziksel düşük-RAM cihazdaki disk ve üretici sürücüsü davranışını tamamen temsil etmez. Nihai pazarlama iddiası için ayrıca gerçek 4 GB cihaz testi önerilir.

