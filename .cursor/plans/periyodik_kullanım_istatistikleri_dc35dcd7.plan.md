---
name: Periyodik kullanım istatistikleri
overview: Günlük mevcut yapı korunur; haftalık, aylık ve yıllık dönemler için hem genel toplam/ortalama hem uygulama bazlı toplam/ortalama gösterilecek. Yeni bir İstatistikler sayfası ve repository’de tarih aralığı toplamı eklenecek.
todos: []
isProject: false
---

# Günlük / Haftalık / Aylık / Yıllık Kullanım ve Ortalama Planı

## Hedef

- **Genel:** Seçilen dönemde toplam kullanım süresi + dönemdeki gün sayısına göre **günlük ortalama**.
- **Uygulama bazlı:** Her uygulama için aynı dönemde toplam süre + günlük ortalama (ve istenirse yüzde).

Dönemler: **Günlük** (mevcut), **Haftalık**, **Aylık**, **Yıllık**. Ortalama = toplam süre / dönemdeki gün sayısı.

---

## 1. Veri katmanı (Repository)

**Dosya:** [src/Monity.Infrastructure/Persistence/IUsageRepository.cs](src/Monity.Infrastructure/Persistence/IUsageRepository.cs)  
**Dosya:** [src/Monity.Infrastructure/Persistence/UsageRepository.cs](src/Monity.Infrastructure/Persistence/UsageRepository.cs)

- **Yeni metot:** `GetRangeTotalAsync(DateTime startDate, DateTime endDate, bool excludeIdle, CancellationToken)`  
  - Dönüş: `Task<DailyTotal>` (mevcut `DailyTotal`: `TotalSeconds`, `SessionCount`).  
  - Aralıktaki genel toplamı döndürecek.  
  - Veri kaynağı: `daily_summary` üzerinden `date BETWEEN @Start AND @End` ile `SUM(total_seconds)` ve `SUM(session_count)` (idle zaten daily_summary’de ayrılmış; excludeIdle için sadece total_seconds kullanılır).  
  - Aralıkta hiç kayıt yoksa `QueryFirstOrDefaultAsync` ile 0 değer dön (mevcut `GetDailyTotalAsync` mantığı).
- **Mevcut:** `GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, ...)` zaten herhangi bir tarih aralığı için uygulama bazlı toplam veriyor; haftalık/aylık/yıllık için aynı metot kullanılacak, ek repository metodu gerekmez.
- **Özet:** Sadece `GetRangeTotalAsync` eklenecek; uygulama listesi için mevcut `GetWeeklyUsageAsync` kullanılacak.

---

## 2. Dönem ve tarih aralığı hesabı

**Yer:** Yeni sayfa veya paylaşılan helper (tercihen [src/Monity.App](src/Monity.App) içinde bir helper/static sınıf).

- **Günlük:** Tek gün → `startDate = endDate = seçilen tarih`; gün sayısı = 1.
- **Haftalık:** Pazartesi–Pazar (ISO haftası). Seçilen tarihin içinde olduğu haftanın Pazartesi ve Pazar’ı; gün sayısı = 7.
- **Aylık:** Seçilen ayın 1’i ve son günü; gün sayısı = o aydaki gün sayısı (28–31).
- **Yıllık:** Seçilen yılın 1 Ocak ve 31 Aralık; gün sayısı = 365/366.

Bu hesaplar UI’da (ör. İstatistikler sayfası code-behind) yapılabilir; repository sadece `startDate`, `endDate` alacak.

---

## 3. UI: İstatistikler sayfası

**Yeni dosyalar:**  

- `src/Monity.App/Views/StatisticsPage.xaml`  
- `src/Monity.App/Views/StatisticsPage.xaml.cs`

**Yerleşim (özet):**

- **Üst:** Dönem seçici (Günlük / Haftalık / Aylık / Yıllık) — RadioButton veya ComboBox.
- **Tarih seçimi (döneme göre):**
  - Günlük: DatePicker (tek gün).
  - Haftalık: DatePicker veya “Hafta seç” (seçilen tarihin haftası kullanılır).
  - Aylık: Ay + yıl (ComboBox veya DatePicker ay görünümü).
  - Yıllık: Yıl (ComboBox veya spinner).
- **Genel özet kartları (mevcut Dashboard’dakine benzer):**
  - **Toplam süre:** Seçilen dönem toplamı (FormatDuration ile “X sa Y dk”).
  - **Ortalama:** “Günde ortalama Z sa W dk” (toplam / dönem gün sayısı).
  - İsteğe bağlı: Kayıt sayısı (session count) aynı mantıkla.
- **Saatlik grafik:** Sadece **Günlük** seçiliyken anlamlı; diğer dönemlerde gizlenebilir veya “Bu dönem için saatlik dağılım gösterilmez” mesajı.
- **Uygulama listesi (ListView/DataGrid):**
  - Sütunlar: Uygulama adı, **Toplam** (süre), **Ortalama** (günde ortalama süre), **Yüzde** (dönem toplamına oran).
  - Arama kutusu: Mevcut Dashboard’daki gibi (placeholder “Ara”).
  - Veri: `GetWeeklyUsageAsync(startDate, endDate)`; ortalama = TotalSeconds / dayCount; yüzde = (TotalSeconds / genelToplam) * 100.

**Veri akışı:**  
Dönem + tarih/ay/yıl seçimi değişince → startDate, endDate ve dayCount hesapla → `UpdateDailySummaryAsync`’i aralıktaki her gün için çağırmak maliyetli olabileceği için, sadece `GetRangeTotalAsync` ve `GetWeeklyUsageAsync` kullanılacak; gerekirse ilk kullanımda eksik günler için `UpdateDailySummaryAsync` toplu veya arka planda çağrılabilir (opsiyonel, performans ihtiyacına göre).

---

## 4. Navigasyon

**Dosya:** [src/Monity.App/MainWindow.xaml](src/Monity.App/MainWindow.xaml)  
Header’a “İstatistikler” butonu eklenir (Ayarlar’ın yanına).

**Dosya:** [src/Monity.App/MainWindow.xaml.cs](src/Monity.App/MainWindow.xaml.cs)  
“İstatistikler” tıklanınca `MainFrame.Navigate(new StatisticsPage(Services))`.  
İstatistikler sayfasında “Geri” veya “Dashboard” ile `MainFrame.Navigate(new DashboardPage(Services))` (isteğe bağlı).

Dashboard mevcut haliyle kalır (günlük odaklı); detaylı dönem analizi İstatistikler sayfasında yapılır.

---

## 5. Teknik notlar

- **Süre formatı:** Mevcut `FormatDuration` (sa/dk/sn) Türkçe kullanılmaya devam eder; İstatistikler sayfası da aynı helper’ı kullanabilir (paylaşım için `FormatDuration` Dashboard’da static kalabilir veya ortak bir yardımcı sınıfa taşınabilir).
- **Performans:** Haftalık/aylık/yıllık için `GetWeeklyUsageAsync` ve `GetRangeTotalAsync` doğrudan `usage_sessions` veya `daily_summary` üzerinden aralık sorgusu yapacak; indeksler (`day_date`, `date`) mevcut.
- **Boş dönem:** Toplam 0 ise ortalama 0, liste boş; mevcut “veri yok” davranışı (ClearDashboardData benzeri) uygulanır.

---

## 6. Uygulama sırası (özet)

1. **Repository:** `GetRangeTotalAsync` ekle; `IUsageRepository` ve `UsageRepository` güncelle.
2. **Ortak helper (opsiyonel):** `FormatDuration` ve dönem aralığı hesabı (start/end/dayCount) için tek yer.
3. **StatisticsPage:** XAML + code-behind; dönem seçici, tarih/ay/yıl seçimi, genel toplam/ortalama, uygulama listesi (toplam + ortalama + yüzde), arama.
4. **Navigasyon:** MainWindow’a İstatistikler butonu ve `StatisticsPage`’e geçiş.

Bu planla günlükle birlikte haftalık, aylık ve yıllık hem genel hem uygulama bazlı toplam ve günlük ortalama tek sayfada tutarlı şekilde gösterilmiş olur.