# Monity

Windows masaüstünde **uygulama kullanım süresini** takip eden WPF uygulaması. Hangi uygulamada ne kadar vakit geçirdiğinizi günlük özet, saatlik grafik ve uygulama listesiyle gösterir.

---

## Özellikler

### Takip
- **Foreground uygulama takibi:** Odaktaki pencerenin (Chrome, Cursor, oyunlar vb.) süresi kaydedilir.
- **UWP desteği:** ApplicationFrameHost üzerinden gerçek UWP process (Hesap Makinesi, Fotoğraflar vb.) çözümlenir.
- **Boşta kalma (idle):** Klavye/fare kullanılmadığı süre sayılmaz (ayarlanabilir eşik, 10–600 saniye).
- **Güç olayları:** Sleep/Wake’te oturumlar veritabanına yazılır.
- **Toplu yazma:** Session buffer (20 kayıt veya 5 dakika) ile performanslı SQLite yazımı.

### Arayüz
- **Dashboard:** Seçilen gün için **bugün başlangıç** (ilk kullanım saati veya "—"), toplam süre, kullanım kaydı sayısı, “şu an aktif” uygulama; **tarih** ve **kategori** filtresi, yenile butonu. Hariç tutulan uygulamalar listede ve toplamlarda gösterilmez.
- **Aylık kullanım yoğunluğu (ısı haritası):** Seçilen ay için takvim grid (Pzt–Paz); her hücrede gün numarası ve yoğunluk rengi (tema uyumlu), altta Az–Çok legend.
- **Saatlik grafik:** Gün içi kullanım dağılımı (LiveCharts2 bar chart).
- **Uygulama listesi:** Günlük kullanım süresi ve yüzde ile tablo; arama kutusu ile filtreleme.
- **İstatistikler:** Ana menüden erişilen ayrı sayfa:
  - **Dönem seçici:** Günlük, haftalık, aylık veya yıllık.
  - **Tarih ve kategori seçimi:** Seçilen döneme göre toplam süre, günde ortalama ve kullanım kaydı sayısı.
  - **Haftalık karşılaştırma:** Bu hafta / geçen hafta toplam süre ve fark (mutlak + yüzde).
  - **Zaman dağılımı grafiği:** Günlük modda saatlik bar grafik; haftalık/aylık/yıllık modda günlük toplam bar grafik.
  - **Uygulama dağılımı grafiği:** En çok kullanılan uygulamaların pasta (pie) grafiği; dilim ve tooltip değerleri 2 ondalık basamakla.
  - **Uygulama kullanımı tablosu:** Toplam, ortalama ve yüzde sütunları; arama kutusu ile filtreleme. Hariç tutulan uygulamalar listede ve toplamlarda gösterilmez.
  - **Dashboard’a dön:** Sayfa başlığının yanında ve sayfa sonunda geri dönüş butonu.
- **Ayarlar:**
  - **Tema:** Açık veya Koyu; seçim anında uygulanır, tercih saklanır.
  - **Dil:** Türkçe veya English; arayüz metinleri ve kategori listeleri seçilen dile göre değişir.
  - **Windows başlangıcında otomatik başlat:** İsteğe bağlı (Registry Run veya görev zamanlayıcı).
  - **Boşta kalma süresi** (saniye), 10–600 arası.
  - **Minimum oturum süresi** (saniye): 0 = kapalı, 1–600 arası; bu süreden kısa oturumlar kaydedilmez.
  - **Takip hariç tutulacak uygulamalar:** Hem daha önce kullanılan (DB’deki) hem de **kurulu programlar** (Windows Uninstall kayıtlarından) listelenir; arama kutusu ile filtreleme. Monity ve Windows Gezgini varsayılan olarak hariçtir.
  - **Günlük süre kısıtları:** Uygulama bazlı dakika limiti (1–1440); limit aşıldığında tray bildirimi. İsteğe bağlı **“Limit aşıldığında uygulamayı kapat”** seçeneği.
  - **Uygulama kategorileri:** Her uygulamaya kategori atanabilir (Tarayıcı, Geliştirme, Sosyal, Eğlence, Ofis, Diğer, Kategorisiz); Dashboard ve İstatistikler’de kategoriye göre filtreleme.
  - **Veri yönetimi:** 30 / 90 / 365 günden eski verileri sil veya tüm verileri sil (onay dialogu ile).
  - **Hakkında:** Sürüm numarası, geliştirici linki ve GitHub sürümler sayfası linki.
- **Footer:** Ana pencerede sürüm bilgisi ve geliştirici linki.
- **Tray:** Pencereyi gizle, çift tıkla tekrar aç; dil değişiminde bildirim metinleri seçilen dile göre.

### Veri
- **SQLite:** Veritabanı `%LocalAppData%\Monity\monity.db` konumunda.
- **Uygulama eşleştirme:** Aynı exe farklı kullanıcı yollarında (örn. `...\User\...` ve `...\KullanıcıAdı\...`) tek uygulama olarak birleştirilir.

---

## Gereksinimler

- **Windows** 10 veya 11  
- **.NET 8** (veya aşağıdaki self-contained zip ile kurulum; ayrıca .NET yüklemeniz gerekmez)

---

## İndir ve kur

1. [Releases](https://github.com/rzayevsahil/Monity/releases) sayfasına gidin.
2. En son sürümde **Monity-Setup-x.x.x.exe** (önerilen) veya **Monity-x.x.x-win-x64.zip** (taşınabilir) dosyasını indirin.
3. **Setup exe:** Kurulum sihirbazını çalıştırın; masaüstü kısayolu ve Başlat menüsü otomatik oluşturulur. Kurulum yeri varsayılan: `%LocalAppData%\Monity`. Programlar ve Özellikler'den kaldırılabilir.
4. **Zip:** Bir klasöre açıp **Monity.App.exe** çalıştırın. Uygulama içi güncelleme bu zip'i kullanır.

**Güncelleme:** Uygulama her açılışta GitHub’daki **en son release**’i (`releases/latest`) kontrol eder. Sürüm numarası (tag, örn. v2.0.0) yüklü sürümden büyükse pencerede "Yeni sürüm mevcut (x.x.x)" ve **Güncelle** butonu çıkar; tek tıklamayla zip indirilir ve kurulur.

**Güncelleme görünmüyorsa:** Ağ hatası veya GitHub API limiti (saatte 60 istek) nedeniyle kontrol bazen başarısız olabilir. [Releases](https://github.com/rzayevsahil/Monity/releases) sayfasından en son sürümü manuel indirip kurabilirsiniz. Hata detayı `%LocalAppData%\Monity\Logs\monity-*.log` dosyasında "Update check failed" ile kaydedilir.

---

## Derleme ve çalıştırma

```bash
# Çözümü klonladıktan sonra
cd monity

# Derleme
dotnet build Monity.sln

# Çalıştırma
dotnet run --project src/Monity.App/Monity.App.csproj
```

Release derlemesi ve dağıtım zip’i:

```bash
# Uygulama (self-contained, kullanıcı .NET kurmak zorunda kalmaz)
dotnet publish src/Monity.App/Monity.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false

# Updater (tek exe, release zip’e eklenecek)
dotnet publish src/Monity.Updater/Monity.Updater.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Uygulama çıktısı: `src/Monity.App/bin/Release/net8.0-windows/win-x64/publish/`  
Updater çıktısı: `src/Monity.Updater/bin/Release/net8.0/win-x64/publish/Monity.Updater.exe`  

**Tek komutla kurulum:** `.\build-release.ps1` (Inno Setup 6 kurulu olmalı) ile App + Updater publish edilir ve Setup exe derlenir. Çıktı: `installer/Output/Monity-Setup-1.0.0.exe`.

Release zip’i oluşturmak için: Uygulama publish klasörünün içeriğini zip’leyin, **Updater.exe** dosyasını da bu zip’in içine ekleyin. Zip adı: `Monity-1.0.0-win-x64.zip` (sürüm numarasıyla). GitHub’da yeni release açıp bu zip’i ekleyin; tag örn. `v1.0.0`.

---

## Proje yapısı

```
monity/
├── Monity.sln
├── README.md
└── src/
    ├── Monity.Domain/           # Entity'ler: UsageSession, ForegroundProcessInfo vb.
    ├── Monity.Infrastructure/
    │   ├── WinApi/              # P/Invoke: GetForegroundWindow, GetGUIThreadInfo, GetLastInputInfo
    │   ├── Tracking/            # TrackingEngine, SessionBuffer, UsageTrackingService
    │   ├── Persistence/         # SQLite (Dapper), DatabaseMigrator, UsageRepository
    │   ├── InstalledApps/       # InstalledAppsProvider (Uninstall registry)
    │   └── AppDisplayNameResolver
    ├── Monity.App/
    │   ├── Services/            # UpdateService, ThemeService, LanguageService, StartupService, DailyLimitCheckService
    │   ├── Resources/           # Strings.tr.xaml, Strings.en.xaml (i18n)
    │   ├── Themes/              # Light.xaml, Dark.xaml (ResourceDictionary)
    │   ├── Views/               # DashboardPage, StatisticsPage, SettingsPage
    │   ├── Helpers/             # DurationAndPeriodHelper, Strings (resource lookup)
    │   ├── Power/               # PowerEventHandler (WM_POWERBROADCAST)
    │   └── App.xaml(.cs)
    └── Monity.Updater/          # Güncelleme yardımcısı (tek tık güncelleme)
```

---

## Teknoloji

| Bileşen        | Teknoloji |
|----------------|-----------|
| UI             | WPF (.NET 8) |
| Veritabanı     | SQLite + Dapper |
| Grafik        | LiveCharts2 (SkiaSharp) |
| Loglama        | Serilog (dosya) |
| DI             | Microsoft.Extensions.DependencyInjection |

---

## Veritabanı şeması

| Tablo            | Açıklama |
|------------------|----------|
| `apps`           | Uygulama meta (process_name, exe_path, display_name, category_id). Aynı exe farklı yolda tek kayıt. |
| `app_categories` | Kategori adları (id, name). Uygulama kategorileri için. |
| `usage_sessions` | Ham oturumlar: app_id, started_at, ended_at, duration_seconds, is_idle, day_date. |
| `daily_summary`  | Günlük özet (app_id, date, total_seconds, session_count, idle_seconds). |
| `app_settings`   | Ayarlar: idle_threshold_seconds, min_session_seconds, ignored_processes, theme (light/dark), language (tr/en), daily_limits (JSON), limit_exceeded_action (notify/close_app), start_with_windows. |

Veriler yerel saat ile tutulur; dashboard sorguları `daily_summary` üzerinden yapılır.

---

## Ayarlar (özet)

| Ayar | Açıklama |
|------|----------|
| **Tema** | Açık veya Koyu. Uygulama açılışında ve Ayarlar’dan Kaydet ile anında uygulanır. |
| **Dil** | Türkçe veya English. Arayüz ve kategori listeleri seçilen dile göre değişir. |
| **Windows başlangıcında otomatik başlat** | Açıkken uygulama Windows ile birlikte başlar. |
| **Boşta kalma süresi** | 10–600 saniye. Bu süre boyunca girdi yoksa süre sayılmaz. |
| **Minimum oturum süresi** | 0 = kapalı, 1–600 saniye. Bu süreden kısa oturumlar kaydedilmez. |
| **Takip hariç tutulacak uygulamalar** | Listeden işaretlenen uygulamalar Dashboard ve İstatistikler’de gösterilmez. Liste: DB’deki kullanılmış uygulamalar + Windows kurulu programlar. |
| **Günlük süre kısıtları** | Uygulama bazlı dakika limiti; aşımda tray bildirimi. İsteğe bağlı “Limit aşıldığında uygulamayı kapat”. |
| **Uygulama kategorileri** | Uygulamalara kategori atanır; Dashboard ve İstatistikler’de kategori filtresi kullanılır. |
| **Veri yönetimi** | 30 / 90 / 365 günden eski veya tüm verileri sil (onay ile). |

---

## Lisans

Proje sahibinin belirleyeceği lisans geçerlidir.
