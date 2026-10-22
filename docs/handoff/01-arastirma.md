# Hard Drivin' (Atari Games, 1989) — internet araştırması

Tarih: 21 Ekim 2026. Amaç: "aynı hissi modern teknolojiyle yeniden yaratma" planına temel olacak her şeyi toplamak.
Kaynak linkleri en sonda. Belirsiz / çelişen bilgiler **[?]** ile işaretli.
Referans görseller ve kılavuz: `kaynak\` klasörü.

---

## 1. Kimlik ve tarihçe

- **Yapımcı:** Atari Games, Applied Research Group (Milpitas, CA). Namco o dönemde Atari Games'in çoğunluk hissesine sahipti (1986); iki şirket ortak bir 3B prototip geliştirdi, ayrılınca Atari erken sürümden Hard Drivin' donanımını, Namco ileri sürümden System 21'i (Winning Run, 1988) çıkardı.
- **Köken:** Rick Moncrief 1984'te "arcade teknolojisiyle ucuz sürücü eğitim simülatörü" fikriyle başladı. Hedef üretim maliyeti < 4.000 $; perakende ~10.000 $ olunca sürücü kurslarına pahalı geldi, arcade'e döndü. Oyun bu yüzden sıra dışı ayrıntılı servis menüsü, çalışan gereksiz göstergeler, tam sinema jeneriği taşır.
- **Çıkış:** Kuzey Amerika 12 Şubat 1989 (açılış ekranı 1988 yazar), Avrupa Şubat 1989, Japonya Haziran 1989.
- **Üretim / fiyat:** 1.668 adet ABD + 200 adet İrlanda (arcade-history) — Wikipedia 3.318 satış der **[?]**. Satış fiyatı 7.995 $ (bir yönetici "10.000 $'lık kabini kimse almaz" diye çıkışı geciktirmişti).
- **Sürümler:** 15 resmi sürüm (11 kokpit, 4 kompakt; İngiliz/Alman/Japon varyantları; İngiliz sürümleri sağdan direksiyon). Yazılım 7.8 → 8.6; MAME varsayılanı "cockpit rev 7" = 8.6.
- **Ödüller:** Your Sinclair 1989 en iyi arcade; Golden Joystick 1989 en iyi coin-op dönüşümü; Japonya Game Machine Haziran 1989 #2 kokpit; 1990 Japonya 6. en çok kazandıran; Guinness: "gerçek kuvvet geri beslemeli ilk sürüş oyunu".
- **Rekor:** Jeff Garabedian, 531.400 puan (1989, Aladdin's Castle, Waterford MI).

### Ekip (jenerik + patentlerden)
| Kişi | Rol |
|---|---|
| Rick Moncrief | Proje lideri, oyun tasarımı, ses sistemi, mekanik tasarım, kuvvetli vites kolu, analog donanım |
| Max Behensky | Yazılım tasarımı, **araba modeli**, **kuvvet geri beslemeli direksiyon**, araçlar. Daha önce Atari Cambridge Research'te (1982-84) ilk kuvvet geri beslemeli joystick/direksiyonları yapmış |
| Stephanie Mott | Oyun programlama, görüntü yazılımı, şampiyonluk turu |
| Jed Margolin | Donanım tasarımı, self-test, anlık tekrar, tamsayı 3B algoritmaları (web sitesi hâlâ ana kaynak) |
| Erik Durfey | Teknisyen, mekanik, ses kaydı, hareketli gösterge paneli |
| Doug Milliken | Jenerikte "test sürücüsü", gerçekte araba modelinin ortak yazarı (Atari gizli tuttu). *Race Car Vehicle Dynamics* kitabının ortak yazarı; babası William Milliken 1950'lerde uçak hareket denklemlerini otomobile uyarlayan ekibi yönetti |
| Jim Morris | Görüntü matematiği |
| Don Diekneite | Müzik |
| Don Paauw | Grafik devresini gate array'e indirgedi |
| Diğer | Grafik: Sam Comstock, Kris Moser, Deborah Short, Alan Murphy; ek programlama: Gary Stark, Mike Albaugh, Ed Rotberg; kabin: Mike Jang, Ken Hata; mekanik: Jacques Aknin, Milt Loper, Geoff Barker |

### Devam oyunları ve akrabalar
- **Race Drivin' (Ağustos 1990):** ~1.200 adet, ~9.000 $. Araba modeli DSP32C'ye (kayan nokta) taşındı → 4 tekerlekli model, yalpa (roll) var, daha hızlı güncelleme. 3 manuel araba + 1 otomatik; Autocross (~30 sn) ve Super Stunt (~3 dk, tirbuşon, dağ yolu, 45° yokuş, silindirik tünel) pistleri; Buddy Race (kayıtlı rakip) ve Linked Race (iki kabin). **Panorama:** 3-5 monitör, 100 adet.
- **Hard Drivin' II: Drive Harder (1991, Domark, ST/Amiga/PC):** aynı ev motoru + 4 pist + pist editörü.
- **Hard Drivin's Airborne (1993) ve Street Drivin' (1993):** yayımlanmamış prototipler (MAME'de var). Airborne'da 300+ nesneyi döndürerek gösteren test modu var.
- **AMOS Police Trainer:** aynı donanımla profesyonel polis takip simülatörü (5 monitörlü araçlar, Mac'te 3B editör).
- **Race Drivin' a Go! Go! (PS1, Japonya 1996):** HD pisti dahil, 8 araç.
- **San Francisco Rush (1996):** weltenschule'ye göre Race Drivin' fizik motorunun son gerçek mirasçısı.
- Aynı donanım ailesi: S.T.U.N. Runner, Steel Talons.
- **Stunts (1990):** HD'den esinlenen PC oyunu; bugün hâlâ canlı topluluğu var (forum.stunts.hu), GPL yeniden yapımı Ultimate Stunts.
- **Hotshot Racing (2020):** sanat stili için HD/Winning Run/Virtua Racing'i örnek gösterdi.

---

## 2. Donanım

### Kartlar ve işlemciler
| Birim | Çip / saat | Görev |
|---|---|---|
| Ana işlemci | Motorola 68010 @ 8 MHz | Oyun mantığı. C ile yazıldı, DEC VAX'ta Green Hills derleyicisiyle çapraz derlendi (patentler) |
| GSP | TMS34010 @ 48 MHz | Poligon doldurma. 512×384×8 bit çerçeve, 32 VRAM. "Piksel başına 1 bit" hilesiyle tek bellek işleminde 16 piksel doldurur |
| MSP | TMS34010 @ 50 MHz | **Araba modeli.** Kayan nokta yok → model "iki tekerlekli" (bisiklet) ve yalnız pitch ekseni; roll yok |
| ADSP | ADSP-2100 @ 8 MHz (ADSP / ADSP II kartı) | Poligon dönüşümleri, ışıklandırma, eğim hesabı |
| Ses kartı | 68000 @ 8 MHz + TMS32010 @ 5 MHz + AM6012 12-bit DAC | Örnek (sample) çalma; 1 MB'a kadar 8-bit "sound wave ROM"; TMS5220 konuşma hatları var ama kullanılmamış |
| Motor amplifikatörü | Şebeke beslemeli, opto-izoleli, iki sürücü PCB | Direksiyon motorunu sürer (ölümcül gerilim uyarısı) |
| Vites PCB | | Vites kolunun kuvvet efektleri |
| Fren PCB | | Gerinim ölçerli fren |
| Diğer | Slapstic koruma, ZRAM (ayar + rekor), DUART | |

- **Video:** 25 kHz yatay, 60 Hz dikey; görünür 512×384 (kokpit) / 512×288 (kompakt, multisync).
- **Güç / boyut (kılavuz Tablo 1-1):** 635 W, 7 A sigorta, 102-132 VAC; 81 cm G × 160 cm D (koltuk itilmiş) × 197 cm Y; **341 kg**.

### Kontroller (kokpit)
- **Direksiyon:** motor miline bağlı 10 turlu potansiyometre; durdurucu mekanizma ile **kilitten kilide 3 tur**. Konum hem 12-bit hem 8-bit ADC'den okunur; ikisi uyuşmazsa kuvvet kapatılır. "Center-feel" (merkez hissi) + sürekli kuvvet geri beslemesi.
- **Motor:** DC fırçalı, **1 ft·lb/A tork hassasiyeti**, ~700 rpm, ~30 mH; Margolin tahmini 60-90 VDC. Kayış/kasnak redüksiyonu "çocuklar için güvenli" seçildi.
- **Gaz, debriyaj:** potansiyometre (8-bit ADC).
- **Fren:** **gerinim ölçer (350 Ω) = kuvvet algılayan pedal** (yol değil, bastırma kuvveti ölçülür; 12-bit ADC "brake force") + mikro anahtar.
- **Vites:** H-desenli 4 ileri + R; X/Y potansiyometre + kolda gerinim ölçer (itme/çekme kuvveti) + iki elektromanyetik fren diski ile değişken direnç (patent 4.949.119: PWM ile ağır kilit / senkromeç direnci / minimum direnç; vites torku ve kayma oranı araba modelinden gelir). Geri vites fabrikada mandalla kapalı.
- **Koltuk:** ileri-geri kızaklı, konum potansiyometresi var; **koltuk öndeyse (küçük sürücü) direksiyon kuvveti azaltılır**. Elektromıknatısla kilitlenir ("seat adjust" düğmesi).
- **Kontak anahtarı:** her oyun ve her kazadan sonra çevrilir. **Abort** düğmesi: tekrarı atlar, yol dışından çıkış.
- **Kompakt sürüm:** sonsuz dönen optik enkoder (dişli disk + tek delikli "merkez enkoderi"); vites düğmelerle; koltuk yok. Kaza sonrası oyun "en yakın merkezi" yeni merkez sayar.

### Kuvvet geri beslemesi — algoritma (patent US 5.044.956, süresi doldu)
- Araba modeli 3 değer üretir: **force** (tekerleklerden gelen kuvvet; virajlar, taş, yol dışı engeller), **damping** (direksiyon açısal hızıyla artan direnç), **friction** (duran arabada lastiğin asfaltı kazıması; yay+blok modeli).
- Komut = model torku + sürtünme torku + **servo torku** (oyunun istediği konuma çekme) + sönümleme; amplifikatördeki tek yönlü eddy akım sürtünmesi yazılımla diğer yöne dengelenir.
- **Döngü:** `doforcewheel` 4 ms'lik zamanlayıcı kesmesi; model değerleri her 25 ms yenilenir, alçak geçiren filtreyle "pütürlü his" giderilir. Kuvvet 4096 → 512 ölçeklenir; konum ±2047.
- **Güvenlik:** çift ADC karşılaştırması, hız sınırı, model durursa tork sıfır, yazılım çökerse donanım mandalı kuvveti keser, SWFMAX kırpması.
- **Operatör ayarı:** Steering Wheel Force = Very Light / Light / Medium / **Stiff (fabrika)**; self-test'te Send Force, sinüs/kare/üçgen dalga, kapalı çevrim yay testi.

### Zamanlama (ölçülmüş değil, patent ve gözlem)
- Video 60 Hz. Ana oyun döngüsü "saniyede en az 5 kez" (patent asgarisi). Araba modeli / ivme vektörü her **25-30 ms** (patentler). Kuvvet döngüsü **4 ms**.
- Poligon kare hızı: MAME kullanıcıları ~10-20 fps "orijinale sadık kesiklik" diyor **[?]**; ölçülmüş birincil kaynak yok.

### Çizim
- 3B matematik: Margolin'in 1978 "unit vector" (ortonormal matris) yöntemi; BattleZone, Star Wars, TomCat ve Hard Drivin'/Race Drivin'de kullanıldı.
- Düz renkli poligonlar (flat shade), nesne başına 16 renklik paletlerle **mesafeye göre pus/soluklaşma** ve iki "güneş" (Logg patenti 1991; Race Drivin' dönemi — HD'de birebir aynı mı **[?]**).
- Uzak nesneler düşük detaylı kopyayla değiştirilir (LOD; ahır uzakta çatısız 2 duvar olur). Görüş mesafesi dönemine göre çok uzun.
- **Hareketli gösterge paneli (patent 5.005.148):** yanal ivmeye göre pano, dikiz aynası ve direk yatay kayar; baş-boyun yay+sönümleyici modeli, 30 ms'de bir güncellenen vektör filtrelenir, merkez ofseti 114.
- Ufuk dağları güçlü yalpada boşaltılır; yoldaki küçük kayalar oyundaki tek bitmap sprite (erken deney kalıntısı).

---

## 3. Oyun kuralları (kılavuz TM-327 + Lynx kılavuzu + analizler)

### Başlangıç
- Jeton → otomatik / manuel vites seçimi → **kontak anahtarı** çevrilir ("Start Engine"). Manuelde debriyaj var, motor **stop edebilir**.
- Araç: kırmızı spor araba. **Hiçbir sürümde Ferrari lisansı yok**; benzerlik tasarım esinidir.

### Pist
- Kapalı devre; başlangıçtan kısa süre sonra **"Y" kavşağı**, tabela köprüsü SPEED / STUNT. İki kol sonra birleşir; koldan kola kestirme = hile sayılır (geçersiz tur, "wrong way").
- **Speed Track:** daha uzun; düzlükler, virajlar, iki yönlü trafik; hız levhaları (40 / 60 / 90, "DIP", ok levhaları).
- **Stunt Track:** tepe (kavşakla), **açık köprü atlayışı** (60-80 mph pencere), **360° dikey loop** (girişte ≥ 60 mph ve sağa yaslan — Lynx kılavuzu), **180° eğimli viraj** ("barrel turn"), alt geçit/üst geçit.
- **Gizli pist:** başlangıçta sert sola dönünce uzun düzlük + kule etrafında yuvarlak **skid pad** (ev sürümlerinin çoğunda yok).
- **Dekor:** ahır (arkasında "33" yazılı yeşil kapı easter egg'i), evler, dükkân/benzinci, kamuflaj boyalı "THE HUT" (yalnız arkadan görünür; önünde durunca kapıda anahtar deliği belirir), **inekler (çarpınca möö)**, raysız tren, çiçekler, devrilince "çınn" sesi çıkaran sarı levhalar, sehpa bariyerler, ağaç/tarla alanları.
- Resmi pist haritası afişte ve kılavuzda yok; açılış ekranındaki el çizimi ve süre bitince çıkan bayraklı harita var. Ev sürümü (HD2) editöründen dikilmiş harita: `kaynak\pist-haritasi-HD2-editor-weltenschule.png`.

### Süreler (kılavuz Tablo 2-5, saniye)
| Seçenek | Very Hard | Hard | **Medium (fabrika)** | Easy |
|---|---|---|---|---|
| Initial Time | 78 | 84 | **90** | 96 |
| First Extended (ilk tur bitişi) | 39 | 42 | **45** | 48 |
| Second Extended | 39 | 42 | **45** | 48 |
| Extended After Second | 39 | 42 | **45** | 48 |
Custom aralığı: initial 72-102, extended 37-52. Yarı yolda **kontrol noktası** ek süre verir (ev sürümlerinde ayrı ayar).
Diğer seçenekler: Game Difficulty (Easy/Medium/Hard/Very Hard/Custom), Champ Lap Qualification, Steering During Attract, Steering Wheel Force, High Score Name Censor, rekor tablosu temizleme, mph/km-h, Reverse Gear.

### Kaza, yol dışı, tekrar
- Kaza = trafik, bina, çit, bariyer çarpışması veya kötü iniş. Ardından ~10 sn **"Instant Replay"** (kuşbakışı; kendi müziği var; abort/anahtarla atlanır) → araba son **dönüş noktasına** (return point) konur; bu nokta kaza yerinden epey geride olabilir. Arcade'de loop'ta kazada loop'un *sonrasına* konur (ev sürümünde öncesine).
- **Yol dışı:** 10 sn içinde yola dönülmezse önceki dönüş noktasına alınır. Yol dışı "çamur gibi" yavaşlatır ve kaydırır; abort ile çıkılabilir. Ters yön algılanır.
- Çarpışmada rakip araçlar deforme olur / kaputtan alev çıkar; hafif temas "bump" sesi.

### Puan ve şampiyonluk
- Puan yolda kat edilen mesafeye verilir (yol dışı puan vermez).
- **Championship Lap / "grudge match":** yeterlilik tur süresini geçen oyuncu, makinedeki **en iyi sürücünün kaydedilmiş yolunu** süren **Phantom Photon**'a karşı tek tur kapışır. Hayalet tel kafes çizilir, içinden geçilebilir. Kazanan yeni şampiyon olur, isim girişi (sansür seçeneği). Bu mekanik 3 patentle korundu (aşağıda, süresi doldu).
- İstatistik ekranı: pist başına tur (ortak / speed / stunt), uzatma oranları, otomatik vites tercihi, tekrar süreleri vb.

### Trafik ("drone" arabalar)
- Önceden kaydedilmiş yörüngeleri oyuncunun başlangıç konumuna göre kaydırarak oynatır; fiziği yok (raylı gibi, çimene de sapar). En fazla 12; türler: sedan, pickup, Corvette, van, kamyon, "car", Porsche; renkler yeşil/turuncu/kahve/gri/kızıl/mavi; her birinin şerit, sıra, yön (gelen/giden), hız parametresi var.

### Sesler
- Oyun içi müzik yok. Müzik: anlık tekrar, rekor girişi, tur bitince sessiz bir melodi, şampiyonluk melodisi (Don Diekneite).
- Efektler: motor uğultusu (örnek tabanlı), lastik ciyaklaması, çarpma, bump, levha "çınn", inek "möö", tren rayı sesi, kontrol noktası/bitiş bleep'leri, vites sesi, motor stop.
- Donanım karakteri: 12-bit DAC, 8-bit örnekler → "1989 dijital" dokusu.

---

## 4. Fizik modeli — bilinenler

- Hedef: "mümkün olduğunca doğru". Motor, şanzıman, yaylar, amortisörler ve lastiklerin birbirine, yola ve sürücüye tepkisi matematiksel olarak modellendi. Önceki araba modelleri dar hız aralıklarında çalışırdı; Milliken + Behensky modeli **tüm hızlarda** ve modlar arasında pürüzsüz geçişle çalışır.
- **HD (MSP, sabit nokta):** iki tekerlekli (bisiklet) model, yalnız pitch; **RD (DSP32C, kayan nokta):** 4 tekerlek + roll, daha sık güncelleme ("fark çok belirgin"). Model C ile yazıldığı için RD'ye "bir öğleden sonrada" taşındı.
- Gözlemler: sert direksiyonda ağır kayma; yol dışı çamur gibi; 70+ mph'de spin; loop için ≥60 mph; köprü 60-80 mph; eğimli viraj yavaş alınmalı; hız levhaları gerçekten anlamlı; "100 mph üstü viraj = ölüm dileği".
- **Denklemler yayımlanmış değil.** Patentler kuvvet algoritmasını verir, lastik modelini vermez. En yakın referans: Milliken & Milliken, *Race Car Vehicle Dynamics* (SAE, 1995) + Pacejka "Magic Formula".
- Sızan ev kaynağındaki `carmodel.c` (Jürgen Friedrich, Turbo C, Ağu-Eki 1989) **Behensky'nin modeli değil**: sabit noktalı, 4 temas noktalı basit model (MaxPower 450, devir sınırı 6500, 5 vites, otomatik 2000/5200 rpm, >70 mph spin). Yalnız ev sürümünü anlamak için.

---

## 5. Pist geometrisi verisi

- **HD2 editöründen segment tablosu** (ev sürümü; arcade tasarımından türediği muhtemel **[?]**): düzlükler 0,75 / 1 / 3 / 5 "slab"; virajlar A 5° (2 slab), B 15° (3), C 26,23° (3), D 32° (2), E 52,43° (3); 90° dönüş (4); **eğimli viraj 180° (10 slab)**; tepe 12 slab; kavşak A (tepe + 90° sağ 7 slab), kavşak B/C (5 slab + 90° 4 slab); alt geçit, üst geçit, beyaz köprü, loop, açık köprü.
- **1 slab ≈ 26,82 m = tam 88 ft** (100 mph'de 60 slab 36 sn). 1 km ≈ 38,5 slab.
- Pist ızgaraya bağlı değil, nesneler serbest açıda (Stunts'tan farkı). Yol segmentleri bağlı liste; arcade'de görsel yol ile fiziksel yol ayrı modeller (senkron kaçınca görünmez duvarlar).
- Sızan ev kaynağı `struct.h`: segment = iki uç nokta (x,y,z) + yaw/pitch/roll + bayraklar (junct1/2, hill1-4, underpass, loop, invisible, checkpoint, finish, returnpoint); nesne = köşe/normal/poligon sayısı, küçük-büyük model (LOD), sınıf (sign, chicane, building, car, hill, loop, standard, barrier); kayıt = x,z,y,yaw,pitch,roll (+ motor). Veri dosyaları: `track.dat` 12 KB, `objects.dat` 21 KB, `.sha` şekiller, `trackdef.zip` 66 KB.
- **Arcade verisi:** 68010 program ROM'larında (nesne veritabanı ~2 MB, Logg patenti RD için) + ADSP ROM; herkese açık çıkarma aracı **yok**. MAME `harddriv` sürücüsü emülasyon; MiSTer FPGA çekirdeği (2025); geecab'ın MAME çatalı (direksiyon düzeltmeleri, dallar HARDDRIV/HARDDRIV2/HARDDRIV_YOLO).

---

## 6. Haklar ve yasal durum

- **Zincir:** Atari Games → Time Warner Interactive (1993) → WMS/Midway (1996) → Midway Games West (2000) → **Warner Bros. Entertainment (2009, Midway iflası)**. Bütün patentlerde güncel sahip WB görünüyor; telif de aynı yerde.
- **Patentler (hepsinin süresi doldu):** 4.949.119 vites kolu (1990), 4.960.117 kabin/koltuk (1990), 5.005.148 hareketli pano (1991), 5.044.956 **direksiyon kuvvet geri beslemesi** (1991), 5.269.687 / 5.354.202 / 5.577.913 **hayalet araba / recursive driver training** (1993-96) + Midway'in 6.755.654 (2004). Hayalet patenti PGR, Ridge Racer 6, OutRun 2006, GripShift, NFS Underground arcade'e lisanslandı; 2011-2013'te bitti. **Mekanikler bugün serbest.**
- **Korunan:** "Hard Drivin'" adı/markası (tescil durumu kontrol edilmedi **[?]**), Atari/Atari Games logoları, pist tasarımı ve 3B modeller (telif), sesler/müzik, kabin tasarımı (tasarım patenti D318.073 doldu).
- **Yasal oynama yolları:** Midway Arcade Treasures 2 (PS2/Xbox/GC, 2004), MAT Deluxe Edition (PC 2006; StarForce DRM modern Windows'ta çalışmaz, sonraki DVD baskısı DRM'siz), Race Drivin' a Go! Go! (PS1 JP), ev portları ikinci el; **Antstream'de doğrulanamadı**. MAME için ROM = sahip olunan karttan dump.
- **WB bu katalogla az ilgileniyor:** MAT (2004-06), Midway Arcade Origins (2012, HD yok), LEGO Dimensions (2016), Arcade1Up Midway Legacy. Adı kullanmak istenirse muhatap WB Games lisanslama.
- **Sızan kaynak:** Atari ST/PC ev sürümü (Jürgen Friedrich); Internet Archive'a 25 Ocak 2024'te yüklendi, GitHub `malespiaut/hard-drivin-st` Linux'ta derlenir hale getirildi (SDL2 port hedefi). Yayım durumu belirsiz; ürün içinde kullanmak risk, anlamak için okumak gri alan. Strong Müzesi (Tengen kayıtları) aynı disketleri arşivlemiş; müzenin Atari coin-op koleksiyonunda 8" disketlerde arcade kaynakları var ama HD olup olmadığı bilinmiyor **[?]**.

---

## 7. Topluluk ve araçlar

- **jmargolin.com:** şemalar (ADSP, motor amp, DSK), unit vector 3B matematiği, Atari patent listesi, 1982-92 iç yazışmalar, Race Drivin' Y2K ROM yaması.
- **weltenschule.de analizi + Stunts forumu konusu:** en derin teknik gözlem seti (segment tablosu, drone parametreleri, glitch'ler, dosya biçimleri). Metni `kaynak\weltenschule-analiz-metni.txt`.
- **bannister.org:** direksiyon mekanizması (10 tur pot, 3 tur stop), kompakt optik enkoder gizemi, geecab'ın MAME düzeltmeleri.
- **FFB Arcade Plugin (Boomslangnz):** MAME'de Hard Drivin' için tüketici direksiyonuna kuvvet geri beslemesi (Logitech DFGT ile "çok daha eğlenceli" raporu). Not: MAME sürücüsü motor latch'ini (`hd68k_wr2_write`, "Steering Wheel Latch") saklar ama dışarı vermez.
- **MiSTer FPGA:** retrogarage/harddrivin-mister (cockpit rev 7; kuvvet geri beslemesi yok).
- **BeamNG "The Nostalgia" (2014):** HD pistinin yeniden yapımı (speed + stunt yolu; 1989 görünümlü varyant).
- **Videolar:** "Fixing some of the Hard Drivin/Race Drivin' bugs in MAME" (gizli kod parçaları), "Hard Drivin' (1989) La revolución 3D de Atari" (İspanyolca tarihçe), AMOS tanıtım videoları, "We Fix Atari's Legendary Hard Drivin" (tamir; mekanik ayrıntılar).
- **Behensky / Moncrief röportajı bulunamadı.** Moncrief'in bugünkü şirketi General Simulation (MBFARR) kısa bir tarihçe yayınlamış.

---

## 8. Bulunamayanlar / boşluklar

1. Arcade kaynak kodu — yok, sızmamış.
2. Lastik/süspansiyon denklemleri — yayımlanmamış.
3. Ölçülmüş poligon kare hızı — yok (anekdot ~10-20 fps).
4. Resmi pist haritası / koordinatları — yok; ev editöründen dikilmiş harita var.
5. Geliştirici röportajı — yok.
6. "Hard Drivin'" marka tescil durumu — kontrol edilmedi.
7. TCRF (tcrf.net) sayfaları ulaşılamadı (503).

---

## Kaynaklar

- Wikipedia: https://en.wikipedia.org/wiki/Hard_Drivin%27 , https://en.wikipedia.org/wiki/Race_Drivin%27
- arcade-history (jenerik, sürümler, trivia): https://www.arcade-history.com/?n=hard-drivin-model-136052&page=detail&id=1083
- Jed Margolin şemalar/notlar: https://www.jmargolin.com/schem/schems.htm ; unit vector 3B: https://www.jmargolin.com/uvmath/uvmenu.htm ; Atari patentleri: https://www.jmargolin.com/patents/atpats.htm ; Y2K: https://www.jmargolin.com/rdy2k/rdy2k.htm
- weltenschule analizi: http://weltenschule.de/vgames/HardDrivin/HardDrivin_analysis.html ; Stunts forumu: https://forum.stunts.hu/index.php?topic=4155.0
- bannister.org: https://forums.bannister.org/ubbthreads.php?ubb=showflat&Number=118185 , https://forums.bannister.org/ubbthreads.php?ubb=showflat&Number=123436
- Patentler: https://patents.google.com/patent/US5044956A/en (direksiyon), https://patents.google.com/patent/US4949119A/en (vites), https://patents.google.com/patent/US4960117A/en (kabin), https://patents.google.com/patent/US5005148A/en (pano), https://patents.google.com/patent/US5269687A/en , https://patents.google.com/patent/US5354202A/en , https://patents.google.com/patent/US5577913A/en (hayalet), https://patents.google.com/patent/US5415549A/en (poligon renk), https://patents.google.com/patent/US5368484A/en , https://patents.google.com/patent/US5366376A/en
- Hayalet patenti hikâyesi: https://www.gamedeveloper.com/business/hard-drivin-hard-bargainin-investigating-midway-s-ghost-racer-patent
- Operatör kılavuzu TM-327 (arşiv): https://archive.org/details/arcade-game-manuals/ ; afiş: https://flyers.arcade-museum.com/videogames/show/457 ; Lynx kılavuzu: https://archive.org/details/Hard_Drivin_1991
- MAME sürücüsü: https://github.com/mamedev/mame/blob/master/src/mame/atari/harddriv.cpp (+ harddriv_m.cpp, harddriv_a.cpp); geecab çatalı: https://github.com/geecab/mamefork ; MiSTer: https://github.com/retrogarage/harddrivin-mister ; FFB plugin: https://github.com/Boomslangnz/FFBArcadePlugin ; MAME arcade sayfası: https://archive.org/details/arcade_harddriv
- Sızan ev kaynağı: https://github.com/malespiaut/hard-drivin-st , https://archive.org/details/game-source-atari-st-hard-drivin , https://forums.atariage.com/topic/360076-hard-drivin-source-code/ ; Strong Müzesi: https://archives.museumofplay.org/repositories/3/archival_objects/19381
- General Simulation tarihçe: https://www.generalsimulation.com/?q=node/53 ; Guinness: https://www.guinnessworldrecords.com/world-records/first-driving-game-to-feature-true-force-feedback
- FRGCB port karşılaştırması: http://frgcb.blogspot.com/2015/01/hard-drivin-atari-gamesdomark-1989.html ; BeamNG: https://www.beamng.com/threads/the-nostalgia-remake-of-the-original-hard-drivin-track.7249/ ; System16: https://www.system16.com/hardware.php?id=770
- Motor seçimi için: Unity-DirectInput https://github.com/MrTimcakes/Unity-DirectInput ; UE5 "Simple Controller" https://www.fab.com/listings/b41984a2-53dc-4187-a564-cfd23af5df08 ; Godot FFB durumu https://docs.godotengine.org/en/stable/tutorials/inputs/controllers_gamepads_joysticks.html
