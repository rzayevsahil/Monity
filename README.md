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
- **Dashboard:** Seçilen gün için toplam süre, kullanım kaydı sayısı, “şu an aktif” uygulama; tarih seçici ve yenile butonu.
- **Saatlik grafik:** Gün içi kullanım dağılımı (LiveCharts2).
- **Uygulama listesi:** Günlük kullanım süresi ve yüzde ile tablo.
- **Ayarlar:**
  - Boşta kalma süresi (saniye), 10–600 arası.
  - **Takip hariç tutulacak uygulamalar:** Hem daha önce kullanılan (DB’deki) hem de **kurulu programlar** (Windows Uninstall kayıtlarından) listelenir; arama kutusu ile filtreleme. Monity ve Windows Gezgini varsayılan olarak hariçtir.

### Veri
- **SQLite:** Veritabanı `%LocalAppData%\Monity\monity.db` konumunda.
- **Uygulama eşleştirme:** Aynı exe farklı kullanıcı yollarında (örn. `...\User\...` ve `...\KullanıcıAdı\...`) tek uygulama olarak birleştirilir.

---

## Gereksinimler

- **Windows** 10 veya 11  
- **.NET 8 SDK**

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

Release derlemesi:

```bash
dotnet publish src/Monity.App/Monity.App.csproj -c Release -r win-x64 --self-contained
```

Çıktı: `src/Monity.App/bin/Release/net8.0-windows/win-x64/publish/`

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
    └── Monity.App/
        ├── Views/               # DashboardPage, SettingsPage
        ├── Power/               # PowerEventHandler (WM_POWERBROADCAST)
        └── App.xaml(.cs)
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
| `apps`           | Uygulama meta (process_name, exe_path, display_name). Aynı exe farklı yolda tek kayıt. |
| `usage_sessions` | Ham oturumlar: app_id, started_at, ended_at, duration_seconds, is_idle, day_date. |
| `daily_summary`  | Günlük özet (app_id, date, total_seconds, session_count, idle_seconds). |
| `app_settings`   | Ayarlar: idle_threshold_seconds, ignored_processes. |

Veriler yerel saat ile tutulur; dashboard sorguları `daily_summary` üzerinden yapılır.

---

## Ayarlar (özet)

| Ayar | Açıklama |
|------|----------|
| **Boşta kalma süresi** | 10–600 saniye. Bu süre boyunca girdi yoksa süre sayılmaz. |
| **Takip hariç tutulacak uygulamalar** | Listeden işaretlenen uygulamaların process adları `ignored_processes` olarak kaydedilir. Liste: DB’deki kullanılmış uygulamalar + Windows kurulu programlar (Uninstall kayıtları). |

---

## Lisans

Proje sahibinin belirleyeceği lisans geçerlidir.
