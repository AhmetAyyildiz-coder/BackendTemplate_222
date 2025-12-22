# Audit Logging & Infrastructure - Detaylı Tartışma

## 📊 Enterprise Audit Logging Seviyeleri

### Seviye 1: Basic Audit (Çoğu projenin yaptığı)
```
- CreatedAt, CreatedBy
- UpdatedAt, UpdatedBy
- IsDeleted (Soft Delete)
```
**Sorun:** Kim ne değiştirdi bilmiyorsunuz, sadece son durumu görüyorsunuz.

---

### Seviye 2: Change Tracking (Orta seviye)
```
- Her değişiklikte önceki/sonraki değer
- Ayrı bir AuditLog tablosu
- JSON olarak diff tutma
```

```csharp
public class AuditLog
{
    public long Id { get; set; }
    public string TableName { get; set; }
    public string RecordId { get; set; }
    public string Action { get; set; }  // INSERT, UPDATE, DELETE
    public string OldValues { get; set; }  // JSON
    public string NewValues { get; set; }  // JSON
    public string ChangedColumns { get; set; }  // JSON array
    public int UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
}
```

**Sorun:** Aynı DB'de, performans etkisi, disk şişmesi

---

### Seviye 3: Enterprise Audit (ERP Seviyesi - Sizin Yaklaşımınız) ⭐

```
┌─────────────────┐      Trigger/CDC      ┌─────────────────┐
│   Main DB       │ ──────────────────►   │   Audit DB      │
│   (PostgreSQL)  │                       │   (Separate)    │
└─────────────────┘                       └─────────────────┘
                                                  │
                                                  ▼
                                          ┌─────────────────┐
                                          │  Long-term      │
                                          │  Archive DB     │
                                          └─────────────────┘
```

**Avantajları:**
- ✅ Main DB performansını etkilemez
- ✅ Farklı retention policy
- ✅ Compliance (SOX, GDPR, HIPAA) uyumlu
- ✅ Tamper-proof (ana DB'den bağımsız)
- ✅ Ayrı backup stratejisi

> Not (önemli): “Trigger ile ayrı DB’ye INSERT” DB engine’e göre değişir.
> - SQL Server’da cross-database write trigger ile oldukça doğaldır.
> - PostgreSQL’de trigger doğrudan başka bir DB’ye yazamaz; `postgres_fdw` (foreign table) veya `dblink` gibi mekanizmalar gerekir.
>   Alternatif olarak audit’i aynı DB’de ayrı schema’da tutup (örn. `audit.*`) replication/CDC ile ayrı DB/warehouse’a akıtmak çoğu zaman daha güvenlidir.

### PostgreSQL’de MSSQL gibi “direkt diğer DB’ye yazan trigger” yapabilir miyiz?

Pratik cevap: **MSSQL’deki kadar “native ve rahat” değil**. PostgreSQL’de bir trigger’ın başka bir DB’ye doğrudan `INSERT` atması yok; ama “aynı sonucu” veren 3 ana yaklaşım var:

1) **Audit’i önce aynı DB’de `audit` schema’ya yaz (trigger), sonra CDC ile Audit DB’ye akıt** (önerilen default)
    - Trigger sadece local write yapar (en stabil)
    - Audit DB “ayrı” ihtiyacı logical replication / Debezium ile çözülür
    - Failure mode’lar yönetilebilir (audit akışı durursa main DB durmaz)

2) **`postgres_fdw` ile Audit DB tablosunu foreign table olarak bağla, trigger foreign table’a yazar**
    - “Ayrı DB’ye yazıyorum” hissini verir
    - Ama network/permission/latency/failure handling daha zor
    - Audit DB down olursa main transaction’ı bloklama riski var (ya accept edeceksin ya da async’e çevireceksin)

3) **Outbox (main DB) → Worker → Audit DB** (çok pratik bir enterprise pattern)
    - Main transaction içinde sadece `audit_outbox` (local) tablosuna insert
    - Hangfire/RabbitMQ consumer/HostedService, outbox’u okuyup Audit DB’ye yazar
    - Avantaj: cross-DB failure main işlemi bozmaz; yine de “tamlık” garantisi outbox ile korunur

### Outbox yaklaşımının dezavantajları (gerçek hayatta)

- **Eventual consistency:** Audit DB’ye yazım gecikmeli olabilir (saniyeler/dakikalar). “İşlem oldu ama audit DB’de hemen görünmedi” senaryosu normaldir.
- **Main DB kaybı riski:** Outbox main DB’dedir; main DB tamamen yok olursa outbox da gider.
  - Bunun enterprise çözümü: replication + PITR backup (outbox dahil) ve mümkünse audit pipeline’ı hızlı çalıştırmak.
- **Çift yazım/duplicate:** Worker retry yaparken aynı mesaj iki kere işlenebilir; idempotency (unique key) şart.
- **Poison message:** Bazı mesajlar sürekli fail eder; dead-letter/karantina ve alert şart.
- **Operasyonel yük:** Job/worker health, backlog büyümesi, alarm mekanizması gerekir.

Bu üçünden (1) ve (3) genelde en az sürprizli olanlar.

### “Enterprise’da strict olmalı, audit yazılamazsa ana işlem iptal” konusu

Bu cümlede iki farklı “strict” var; ayırmak kritik:

1) **Strict #1 (önerilen ve uygulanabilir):** Ana transaction içinde outbox’a yazılamazsa ana işlem de fail.
    - Bu zaten outbox pattern’in temel garantisi.
    - Yani audit kaydı *en azından outbox’a* düşmüyorsa business state de commit olmaz.

2) **Strict #2 (çok pahalı/sert):** Ana işlem ancak Audit DB’ye (uzak DB) başarıyla yazıldıysa commit.
    - Bu, pratikte distributed transaction/2PC veya senkron cross-DB write gibi bir şeye yaklaşır.
    - Audit DB veya network sorunu üretimi durdurabilir (çoğu enterprise sistem bunu istemez).

Pratik enterprise politika:
- Default: Strict #1 + güçlü retry + alarm + (opsiyonel) backlog threshold aşıldıysa yazma operasyonlarını “fail closed”.
- Çok kritik finansal aksiyonlarda: ayrıca “audit pipeline healthy değilse işlem alma” gibi bir feature flag.

---

### Seviye 4: Event Sourcing (En Üst Seviye)

```
Her şey EVENT olarak saklanır. Current state = tüm event'lerin replay'i

UserCreatedEvent → UserEmailChangedEvent → UserDeactivatedEvent → ...
     ↓                    ↓                       ↓
  [Store]             [Store]                 [Store]
     ↓                    ↓                       ↓
     └──────────────── Event Store ─────────────────┘
                           │
                           ▼
                    [Projection/Read Model]
```

**Avantajları:**
- ✅ Complete history (hiçbir şey kaybolmaz)
- ✅ Time travel (herhangi bir ana geri dönüş)
- ✅ Audit inherent (audit ayrı değil, sistemin kendisi)
- ✅ Debug/replay capability

**Dezavantajları:**
- ❌ Karmaşık
- ❌ Eventual consistency
- ❌ Storage maliyeti

### ERP analojisiyle Event Sourcing (kolay anlatım)

ERP’de iki farklı “kayıt” dünyası var:

- **Yevmiye/fiş mantığı (append-only):** “Şu tarihte şu işlem oldu” diye kayıt düşersin.
- **Güncel durum (current state):** Stok bakiyesi, cari bakiye, sipariş durumu gibi “son hal”.

Event Sourcing şu demek:
- Sistem “gerçeği” **fiş gibi event’ler** olarak saklar (append-only).
- “Güncel tablo” dediğin şey, bu event’lerin bir **projection**’ı (rapor/okuma modeli) olur.

ERP’den örnek:
- `StockMovementCreated` (giriş/çıkış fişi)
- `InvoicePosted` (fatura işlendi)
- `PaymentReceived` (tahsilat alındı)

Bugünkü stok/cari bakiye = bu event’lerin toplanmış/projeksiyonlanmış hali.

Ne zaman GEREKMEYEBİLİR?
- Sadece “audit trail” istiyorsan (kim ne değiştirdi) event sourcing çoğu projede overkill.
- Domain çok karmaşık değilse, “değişiklik log’u + domain event (integration)” genelde yeter.

---

## 🔥 RAM/Temp DB Muhabbeti Hakkında

Bahsettiğiniz durum şu pattern:

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Hot Data    │     │  Warm Data   │     │  Cold Data   │
│  (Redis)     │ ──► │  (PostgreSQL)│ ──► │  (S3/Archive)│
│  0-7 gün     │     │  7-90 gün    │     │  90+ gün     │
└──────────────┘     └──────────────┘     └──────────────┘
```

### Bu yaklaşımın "şov" kısmı:
**Söyledikleri:** "Biz log'ları Redis'te tutuyoruz, çok hızlı!"

**Gerçek:** 
- Redis'te 2-3 ay log tutmak RAM maliyeti olarak PAHALI
- Genelde sadece "recent" log'lar Redis'te
- Eski log'lar zaten başka yerde (ama bunu söylemiyorlar)
- Query capability kısıtlı

### Doğru yaklaşım:

```
                    ┌─────────────────────────────────────────┐
                    │           LOG PIPELINE                   │
                    └─────────────────────────────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
        ▼                             ▼                             ▼
┌───────────────┐           ┌───────────────┐           ┌───────────────┐
│    Redis      │           │ Elasticsearch │           │   PostgreSQL  │
│  (Real-time)  │           │  (Search)     │           │   (Audit)     │
│               │           │               │           │               │
│ • Last 24h    │           │ • Last 90 days│           │ • Forever     │
│ • Alerts      │           │ • Full-text   │           │ • Compliance  │
│ • Dashboards  │           │ • Analytics   │           │ • Legal hold  │
└───────────────┘           └───────────────┘           └───────────────┘
        │                             │                             │
        │                             │                             │
        └─────────────────────────────┼─────────────────────────────┘
                                      │
                                      ▼
                            ┌───────────────┐
                            │   S3/Glacier  │
                            │  (Archive)    │
                            │  7+ yıl       │
                            └───────────────┘
```

---

## ✅ Kararlar (20 Aralık 2025)

- Event Bus: RabbitMQ
- Search/Log Analytics: Elasticsearch
- Multi-tenancy: Yok
- Auth: Modern monolith + JWT (IdentityServer yok)
- Jobs: Hangfire (eğilim)
- Elasticsearch topology: Başlangıçta tek node (eğilim)

---

## 🧩 Code-First Audit: Attribute Based Trigger Provisioning (Elle SQL yazmadan)

İstediğiniz davranış: entity üstünde attribute varsa audit aktif olsun; migration çalışınca gerekli trigger/fonksiyonlar otomatik oluşsun; audit altyapısı yoksa da güvenli biçimde kurulabilsin.

### Gerçekçilik notu

Trigger bir DB objesi olduğu için *sonuçta* DB’ye DDL göndermek şart; ama bunu “developer tek tek SQL yazsın” halinden çıkarabiliriz.

### Önerilen yaklaşım (EF Core migrations pipeline)

1) Entity’ye attribute:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute : Attribute
{
    public AuditedAttribute(bool enabled = true) => Enabled = enabled;
    public bool Enabled { get; }
}

[Audited]
public sealed class Order
{
    public Guid Id { get; set; }
    public decimal TotalAmount { get; set; }
}
```

2) `OnModelCreating` içinde attribute → model annotation:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
        var clrType = entityType.ClrType;
        if (clrType is null) continue;

        if (Attribute.IsDefined(clrType, typeof(AuditedAttribute)))
            entityType.SetAnnotation("Audit:Enabled", true);
    }
}
```

3) Migration üretiminde (otomatik) “audit trigger operation” üret:

- `AuditTriggerOperation : MigrationOperation` gibi bir operation tanımlanır.
- `IMigrationsModelDiffer` decorate edilerek, `Audit:Enabled` annotation’ı olan tablolar için operation eklenir.

4) Provider’a özel SQL üretimi:

- `IMigrationsSqlGenerator` decorate edilerek `AuditTriggerOperation` → SQL’e çevrilir.
- SQL “tek bir template” olur; tablo adı/PK/kolon listesi metadata’dan otomatik türetilir.

Sonuç: Elle tablo bazlı SQL yazmazsın; attribute ekleyince migration’da otomatik gelir.

### “Audit DB check” nasıl yapılır?

DB engine’e göre:

- PostgreSQL: pratik başlangıç **aynı DB’de `audit` schema** (trigger kolay, failure mode az).
- PostgreSQL’de “ayrı DB şart” ise `postgres_fdw`/`dblink` veya CDC (Debezium/Logical Replication) gerekir.
- SQL Server: ayrı DB’ye trigger ile yazmak daha doğal.

### “100 tablo var, AuditLog şişer” problemi nasıl yönetilir?

Bu problem normal; çözümü “tek dev tablo” fikrini doğru modellemekten geçiyor:

- **Partitioning (çok kritik):** `audit_log` tablosunu zaman bazlı partition et (aylık/haftalık)
    - PostgreSQL’de declarative partitioning ile hem performans hem retention kolaylaşır
- **Retention:** Audit ihtiyacına göre katmanlı tut
    - ES: 30–90 gün searchable
    - Audit DB: 1–7 yıl (compliance)
    - Archive: daha uzun süre (ucuz storage)
- **Diff/Change-set tut:** Her update’te komple row yerine (mümkünse) sadece değişen alanları sakla
    - `changed_properties`, `old_values`, `new_values` JSON(B)
- **Index disiplini:** Audit tablolarına “her kolona index” değil; tipik sorgulara göre (tarih, entity_type, entity_id, user_id)
- **Büyük payload’ları ayır:** request/response body gibi büyük alanları ayrı tabloda/objede tut (opsiyonel)

Bu yüzden “AuditLog şişer” tek başına red flag değil; doğru partition/retention ile yönetilebilir.

### EF Core Interceptors ile CDC: Esnek mi?

Evet esnek, ama sınırları var:

- ✅ EF üzerinden yapılan değişiklikleri çok iyi yakalar (before/after, user, trace id)
- ❌ DB’ye EF dışında yazan başka süreçler varsa (SQL script, başka app), onları göremez

Enterprise pratik:
- Gerçek “tamlık” gerekiyorsa DB-level (trigger/CDC) tercih edilir.
- Uygulama içi zengin context gerekiyorsa (user/ip/request) Interceptor + Outbox çok iyi çalışır.

## 🏗️ Önerdiğim Mimari

### Ana Akış

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   API       │────►│  MediatR    │────►│  Handler    │
│   Request   │     │  Pipeline   │     │             │
└─────────────┘     └─────────────┘     └─────────────┘
                          │                    │
                          │                    │
              ┌───────────┴────────────┐       │
              │                        │       │
              ▼                        ▼       ▼
      ┌─────────────┐          ┌─────────────────────┐
      │ AuditBehavior│          │   Domain Events     │
      │ (Request Log)│          │   (Business Events) │
      └─────────────┘          └─────────────────────┘
              │                        │
              │                        │
              ▼                        ▼
      ┌─────────────┐          ┌─────────────┐
      │   Redis     │          │  RabbitMQ/  │
      │  (Hot Log)  │          │  Kafka      │
      └─────────────┘          └─────────────┘
              │                        │
              │                        │
              ▼                        ▼
      ┌─────────────┐          ┌─────────────┐
      │Elasticsearch│          │  Event      │
      │ (Search)    │          │  Handlers   │
      └─────────────┘          └─────────────┘
              │                        │
              │                        │
              ▼                        ▼
      ┌─────────────┐          ┌─────────────┐
      │  Audit DB   │          │  Email/SMS  │
      │ (Permanent) │          │  Notifications│
      └─────────────┘          └─────────────┘
```

---

## 📝 Audit Log Türleri

### 1. Request/Response Audit (API Seviyesi)
```csharp
public class RequestAuditLog
{
    public Guid Id { get; set; }
    public string TraceId { get; set; }          // Correlation için
    public string RequestPath { get; set; }
    public string HttpMethod { get; set; }
    public string RequestBody { get; set; }       // Sensitive data masked
    public string ResponseBody { get; set; }      // Sensitive data masked
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 2. Data Change Audit (Entity Seviyesi)
```csharp
public class DataChangeAuditLog
{
    public Guid Id { get; set; }
    public string TraceId { get; set; }          // Request ile ilişkilendirme
    public string EntityType { get; set; }
    public string EntityId { get; set; }
    public ChangeType ChangeType { get; set; }   // Create, Update, Delete
    public string OldValues { get; set; }        // JSON
    public string NewValues { get; set; }        // JSON
    public string ChangedProperties { get; set; } // JSON array
    public int? UserId { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum ChangeType
{
    Create,
    Update,
    Delete,
    SoftDelete,
    Restore
}
```

### 3. Business Event Audit (Domain Seviyesi)
```csharp
public class BusinessEventLog
{
    public Guid Id { get; set; }
    public string TraceId { get; set; }
    public string EventType { get; set; }        // "OrderPlaced", "PaymentReceived"
    public string EventData { get; set; }        // JSON
    public string AggregateType { get; set; }    // "Order", "User"
    public string AggregateId { get; set; }
    public int? UserId { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 4. Security Audit (Güvenlik Seviyesi)
```csharp
public class SecurityAuditLog
{
    public Guid Id { get; set; }
    public SecurityEventType EventType { get; set; }
    public string Description { get; set; }
    public int? UserId { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public bool IsSuccessful { get; set; }
    public string FailureReason { get; set; }
    public string AdditionalData { get; set; }   // JSON
    public DateTime Timestamp { get; set; }
}

public enum SecurityEventType
{
    LoginSuccess,
    LoginFailed,
    LogoutSuccess,
    PasswordChanged,
    PasswordResetRequested,
    TwoFactorEnabled,
    TwoFactorDisabled,
    RoleChanged,
    PermissionChanged,
    AccountLocked,
    AccountUnlocked,
    SuspiciousActivity,
    ApiKeyCreated,
    ApiKeyRevoked
}
```

---

## 🔴 Redis Kullanım Stratejisi

### Redis'i NE İÇİN Kullanmalı:

```csharp
// 1. Caching (Primary use case)
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task RemoveAsync(string key);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);
}

// 2. Distributed Locking
public interface IDistributedLockService
{
    Task<IDisposable?> AcquireLockAsync(string resource, TimeSpan expiry);
}

// 3. Rate Limiting
public interface IRateLimiter
{
    Task<bool> IsAllowedAsync(string key, int maxRequests, TimeSpan window);
}

// 4. Real-time Counters
public interface ICounterService
{
    Task<long> IncrementAsync(string key);
    Task<long> GetAsync(string key);
}

// 5. Session/Token Storage
public interface ISessionStore
{
    Task<UserSession?> GetSessionAsync(string sessionId);
    Task SetSessionAsync(string sessionId, UserSession session, TimeSpan expiry);
    Task InvalidateSessionAsync(string sessionId);
}

// 6. Pub/Sub for Real-time Events
public interface IRealtimeEventPublisher
{
    Task PublishAsync<T>(string channel, T message);
}
```

### Redis'i NE İÇİN KULLANMAMALI:

```
❌ Primary data storage (volatile!)
❌ Long-term log storage (expensive RAM)
❌ Complex queries (limited query capability)
❌ Relational data (no joins)
❌ Large objects (memory inefficient)
```

---

## 🔍 Elasticsearch Kullanım Stratejisi

### Elasticsearch'ü NE İÇİN Kullanmalı:

```csharp
// 1. Log Search & Analytics
public interface ILogSearchService
{
    Task<SearchResult<RequestAuditLog>> SearchLogsAsync(LogSearchQuery query);
    Task<AggregationResult> GetLogStatisticsAsync(DateRange range);
}

// 2. Full-text Search
public interface ISearchService
{
    Task<SearchResult<ProductDocument>> SearchProductsAsync(string query, SearchFilters filters);
    Task IndexDocumentAsync<T>(T document) where T : ISearchableDocument;
}

// 3. Analytics & Dashboards
public interface IAnalyticsService
{
    Task<DashboardData> GetDashboardDataAsync(DateRange range);
    Task<IEnumerable<TrendPoint>> GetTrendAsync(string metric, DateRange range);
}
```

### Index Stratejisi:

```
logs-2025.01        (aylık index, ILM ile yönetim)
logs-2025.02
logs-2025.03
  │
  └── ILM Policy:
      - Hot: 0-7 gün (SSD)
      - Warm: 7-30 gün (HDD)
      - Cold: 30-90 gün (Compressed)
      - Delete: 90+ gün (veya Archive'a taşı)
```

---

## 🏛️ Önerilen Database Mimarisi

```
┌─────────────────────────────────────────────────────────────────┐
│                         DATABASES                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────────┐ │
│  │   Main DB        │  │   Audit Store    │  │  Read DB       │ │
│  │   (PostgreSQL)   │  │ (See note below) │  │  (PostgreSQL)  │ │
│  │                  │  │                  │  │                │ │
│  │  • Entities      │  │  • DataChangeLog │  │  • Projections │ │
│  │  • Transactions  │  │  • SecurityLog   │  │  • Reports     │ │
│  │  • Current State │  │  • BusinessEvent │  │  • Aggregates  │ │
│  │                  │  │  • Tamper-proof  │  │                │ │
│  └──────────────────┘  └──────────────────┘  └────────────────┘ │
│           │                     ▲                    ▲          │
│           │                     │                    │          │
│           └─────────────────────┴────────────────────┘          │
│                    Triggers / CDC (DB’ye göre)                  │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐                     │
│  │   Redis          │  │   Elasticsearch  │                     │
│  │                  │  │                  │                     │
│  │  • Cache         │  │  • Log Search    │                     │
│  │  • Sessions      │  │  • Full-text     │                     │
│  │  • Rate Limit    │  │  • Analytics     │                     │
│  │  • Pub/Sub       │  │  • Dashboards    │                     │
│  │  • Real-time     │  │                  │                     │
│  └──────────────────┘  └──────────────────┘                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

Not:
- PostgreSQL’de “audit ayrı DB” gerekiyorsa `postgres_fdw`/`dblink` veya CDC (Debezium/Logical Replication) düşünmek gerekir.
- PostgreSQL için pratik başlangıç: aynı DB’de `audit` schema + retention/partitioning; gerekiyorsa ayrı store’a akıtma.

---

## 🔄 Event-Driven Architecture

### Domain Events

```csharp
// Base event
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
    string EventType { get; }
}

public abstract class DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}

// Örnek event
public class OrderPlacedEvent : DomainEvent
{
    public override string EventType => "OrderPlaced";
    
    public Guid OrderId { get; }
    public int CustomerId { get; }
    public decimal TotalAmount { get; }
    public List<OrderItemDto> Items { get; }
    
    public OrderPlacedEvent(Guid orderId, int customerId, decimal totalAmount, List<OrderItemDto> items)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmount = totalAmount;
        Items = items;
    }
}
```

### Event Dispatcher

```csharp
public interface IEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

public class EventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;  // RabbitMQ/Kafka
    private readonly ILogger<EventDispatcher> _logger;

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // 1. In-process handlers (immediate)
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlers = _serviceProvider.GetServices(handlerType);
        
        foreach (var handler in handlers)
        {
            await ((dynamic)handler).HandleAsync((dynamic)domainEvent, cancellationToken);
        }
        
        // 2. External event bus (async)
        await _eventBus.PublishAsync(domainEvent, cancellationToken);
    }
}
```

### Event Handlers

```csharp
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

// Örnek: Order placed olduğunda email gönder
public class SendOrderConfirmationEmailHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ICustomerRepository _customerRepository;

    public async Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(domainEvent.CustomerId, cancellationToken);
        
        await _emailService.SendOrderConfirmationAsync(
            customer.Email,
            domainEvent.OrderId,
            domainEvent.TotalAmount);
    }
}

// Örnek: Order placed olduğunda stok düş
public class UpdateInventoryHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IInventoryService _inventoryService;

    public async Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken)
    {
        foreach (var item in domainEvent.Items)
        {
            await _inventoryService.ReserveStockAsync(item.ProductId, item.Quantity, cancellationToken);
        }
    }
}

// Örnek: Audit log
public class AuditOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IAuditLogService _auditLogService;

    public async Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken)
    {
        await _auditLogService.LogBusinessEventAsync(domainEvent, cancellationToken);
    }
}
```

---

## 📊 Log Seviyeleri ve Kullanımı

```csharp
public static class LoggingConfiguration
{
    /*
    ┌─────────────────────────────────────────────────────────────────┐
    │                      LOG LEVELS                                  │
    ├─────────────────────────────────────────────────────────────────┤
    │                                                                  │
    │  TRACE   → Development only, very detailed                      │
    │           "Entering method X with params Y"                     │
    │           → Console only, never to Elasticsearch                │
    │                                                                  │
    │  DEBUG   → Development & Staging                                │
    │           "Cache miss for key X, fetching from DB"              │
    │           → Console + File (if enabled)                         │
    │                                                                  │
    │  INFO    → Normal operations                                    │
    │           "User X logged in", "Order Y created"                 │
    │           → Console + Elasticsearch + File                      │
    │                                                                  │
    │  WARNING → Potential issues                                     │
    │           "Rate limit approaching", "Slow query detected"       │
    │           → Console + Elasticsearch + File + Alert queue        │
    │                                                                  │
    │  ERROR   → Errors that need attention                          │
    │           "Payment failed", "External service timeout"          │
    │           → Console + Elasticsearch + File + Alert              │
    │                                                                  │
    │  CRITICAL→ System failures                                      │
    │           "Database connection lost", "Out of memory"           │
    │           → All channels + Immediate alert                      │
    │                                                                  │
    └─────────────────────────────────────────────────────────────────┘
    */
}
```

### Structured Logging

```csharp
// ❌ Kötü
_logger.LogInformation($"User {userId} created order {orderId} for ${amount}");

// ✅ İyi (Structured)
_logger.LogInformation(
    "User {UserId} created order {OrderId} for {Amount:C}",
    userId, orderId, amount);

// Bu sayede Elasticsearch'te:
// - UserId'ye göre filtreleme
// - OrderId'ye göre arama
// - Amount'a göre aggregation
// yapabilirsiniz
```

---

## 🎯 Sonuç ve Öneri

### Sizin için önerdiğim setup:

```
┌─────────────────────────────────────────────────────────────────┐
│                    RECOMMENDED STACK                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Primary DB:     PostgreSQL (Main + Read Replica)               │
│  Audit Store:    PostgreSQL (önce `audit` schema; gerekirse ayrı store/CDC) │
│  Cache:          Redis (Cache, Session, Rate Limit)             │
│  Search/Logs:    Elasticsearch (Log analytics, Full-text)       │
│  Message Queue:  RabbitMQ (Domain Events, Background Jobs)      │
│  Archive:        Object Storage (S3-compatible; MinIO self-host) │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│                    RETENTION POLICY                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Redis:          24 saat - 7 gün (hot data only)                │
│  Elasticsearch:  90 gün (searchable logs)                       │
│  Audit DB:       7 yıl (compliance, legal)                      │
│  Archive:        ∞ (legal hold, compliance)                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

Not (Archive):
- “S3” burada bir ürün adı gibi değil, bir **object storage API standardı** gibi düşün.
- Cloud şart değil: **MinIO’yu kendi local sunucunda/NAS üzerinde** kurup aynı modeli kullanabilirsin.
- Amaç: ucuz disk + uzun süre saklama + (istersen) immutable/WORM benzeri politikalar.

---

## ❓ Tartışma Soruları

1. **Audit DB için ayrı PostgreSQL mı, yoksa farklı bir DB (TimescaleDB, ClickHouse) mi?**
   - TimescaleDB: Time-series data için optimize
   - ClickHouse: Analytics için çok hızlı, ama operational cost yüksek

2. **Event Bus: RabbitMQ**
    - Karar: RabbitMQ (Kafka ancak streaming/replay gibi ihtiyaçta)

3. **CDC (Change Data Capture) nasıl yapalım?**
   - PostgreSQL trigger'ları
   - Debezium (Kafka ile)
   - EF Core Interceptors

4. **Elasticsearch cluster mı, tek node mu?**
   - Başlangıç için tek node yeterli
   - Production'da en az 3 node önerilir

---

## 🔐 Auth: JWT vs IdentityServer (IdentityServer ne işe yarar?)

JWT bir token formatı; asıl mesele token’ı **kim** üretiyor ve ekosistemi nasıl yönetiyorsun.

**IdentityServer (Duende)** bir OAuth2/OIDC Authorization Server’dır:
- Birden fazla client/app varsa (web/mobil/başka API’ler), hepsine token üretir
- SSO sağlar (tek login, çok uygulama)
- Refresh token, scope/consent, external identity provider federation gibi konuları standartlaştırır

Ne zaman gerekli?
- Mikroservis / çok client / SSO / üçüncü parti entegrasyon varsa

Ne zaman gereksiz?
- Tek API + basit login ise: ASP.NET Core Identity + JWT genelde yeter

Not:
- Duende lisanslıdır; OSS alternatif: `OpenIddict`. Dış ürün: Keycloak/Auth0/Azure AD.

---

## 🧾 API Versioning: Katma değer

Swagger dokümantasyon şart ama versioning’in amacı “dış client’ı kırmadan evrim geçirmek”.

İlk etap öneri:
- Şimdilik versioning zorunlu değilse eklemeyin.
- Ama response’larda `error.code` gibi stabil alanlar ve geriye dönük uyumluluk disiplini koyun.

---

## 🌍 Multi-language (i18n): Zor mu?

Zor değil; en büyük fark “mesajı string yazmak” yerine “code + params” üretmek.

- API hata modeli: `code` sabit, `message` lokalize
- `.NET`: `IStringLocalizer` + `.resx` ile, `Accept-Language` header’a göre
- FluentValidation mesajları da lokalize edilebilir

---

## ⏱️ Jobs: Hangfire mı Quartz mı?

- Hangfire: background job processing tarafı çok güçlü (persist, retry, dashboard). Çoğu ürün işi için en hızlı değer.
- Quartz: scheduler semantiği çok güçlü (karmaşık takvimler/misfire). Job processing/dash için ekstra iş çıkarabilir.

RabbitMQ ile birlikte pratik öneri:
- Event-driven işler: RabbitMQ consumer
- Zamanlanmış işler: Hangfire recurring

Düşünceleriniz?
