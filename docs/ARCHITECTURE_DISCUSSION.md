# Backend Template - Mimari Tartışma Dökümanı

## 🎯 Hedefler
- Domain-heavy business logic yönetimi
- MediatR ile CQRS ve Pipeline behaviors
- AutoMapper ile mapping stratejileri
- Güçlü Business Rules pattern
- Esnek Role-based authorization mekanizması

---

## 📐 Önerilen Katmanlı Mimari

```
┌─────────────────────────────────────────────────────────────────┐
│                        Presentation Layer                        │
│                    (API Controllers, Filters)                    │
├─────────────────────────────────────────────────────────────────┤
│                        Application Layer                         │
│         (Commands, Queries, Handlers, Validators, DTOs)         │
├─────────────────────────────────────────────────────────────────┤
│                         Domain Layer                             │
│    (Entities, Value Objects, Domain Services, Business Rules)   │
├─────────────────────────────────────────────────────────────────┤
│                      Infrastructure Layer                        │
│        (Repositories, External Services, Persistence)           │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔄 MediatR Pipeline Architecture

### Pipeline Behavior Sıralaması (Önerilen)

```
Request → [1] Logging → [2] Validation → [3] Authorization → [4] Business Rules → [5] Caching → [6] Transaction → Handler → Response
```

### 1. LoggingBehavior
```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // Her request'in başlangıç ve bitiş zamanını loglar
    // Performance metrikleri toplar
}
```

### 2. ValidationBehavior (FluentValidation ile)
```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    
    // Tüm validatorları çalıştırır
    // Hata varsa ValidationException fırlatır
}
```

### 3. AuthorizationBehavior
```csharp
public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // ISecuredRequest interface'ini kontrol eder
    // Role ve Permission kontrolü yapar
}
```

### 4. BusinessRulesBehavior ⭐
```csharp
public class BusinessRulesBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // IBusinessRuleRequest interface'ini kontrol eder
    // Business rule'ları çalıştırır
}
```

### 5. CachingBehavior
```csharp
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // ICacheable interface'ini kontrol eder
    // Cache'den okur veya cache'e yazar
}
```

### 6. TransactionBehavior
```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // ITransactional interface'ini kontrol eder
    // Unit of Work pattern ile transaction yönetimi
}
```

---

## 📋 Business Rules Pattern - Detaylı Tasarım

### Seçenek 1: Interface-Based Business Rules (Önerilen ⭐)

```csharp
// Temel interface
public interface IBusinessRule
{
    string Message { get; }
    Task<bool> IsBrokenAsync();
}

// Abstract base class
public abstract class BusinessRule : IBusinessRule
{
    public abstract string Message { get; }
    public abstract Task<bool> IsBrokenAsync();
}

// Örnek kullanım
public class UserEmailMustBeUniqueRule : BusinessRule
{
    private readonly IUserRepository _userRepository;
    private readonly string _email;
    
    public UserEmailMustBeUniqueRule(IUserRepository userRepository, string email)
    {
        _userRepository = userRepository;
        _email = email;
    }
    
    public override string Message => "Bu email adresi zaten kullanılıyor.";
    
    public override async Task<bool> IsBrokenAsync()
    {
        return await _userRepository.ExistsAsync(u => u.Email == _email);
    }
}
```

### Seçenek 2: Specification Pattern ile Business Rules

```csharp
public interface ISpecification<T>
{
    bool IsSatisfiedBy(T entity);
    string FailureMessage { get; }
}

public abstract class CompositeSpecification<T> : ISpecification<T>
{
    public abstract bool IsSatisfiedBy(T entity);
    public abstract string FailureMessage { get; }
    
    public ISpecification<T> And(ISpecification<T> other) 
        => new AndSpecification<T>(this, other);
    
    public ISpecification<T> Or(ISpecification<T> other) 
        => new OrSpecification<T>(this, other);
    
    public ISpecification<T> Not() 
        => new NotSpecification<T>(this);
}
```

### Seçenek 3: Rule Engine Pattern (Karmaşık senaryolar için)

```csharp
public interface IBusinessRuleEngine
{
    Task<RuleExecutionResult> ExecuteAsync<T>(T context, params IBusinessRule[] rules);
    Task<RuleExecutionResult> ExecuteAsync<T>(T context, IEnumerable<IBusinessRule> rules);
}

public class BusinessRuleEngine : IBusinessRuleEngine
{
    public async Task<RuleExecutionResult> ExecuteAsync<T>(T context, params IBusinessRule[] rules)
    {
        var brokenRules = new List<BrokenRule>();
        
        foreach (var rule in rules)
        {
            if (await rule.IsBrokenAsync())
            {
                brokenRules.Add(new BrokenRule(rule.GetType().Name, rule.Message));
            }
        }
        
        return new RuleExecutionResult(brokenRules);
    }
}
```

### Business Rules Handler'da Kullanım

```csharp
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IBusinessRuleEngine _ruleEngine;
    private readonly IUserRepository _userRepository;
    
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Business Rules kontrolü
        var ruleResult = await _ruleEngine.ExecuteAsync(request,
            new UserEmailMustBeUniqueRule(_userRepository, request.Email),
            new UserAgeMustBeValidRule(request.Age),
            new UsernameMustNotContainBadWordsRule(request.Username)
        );
        
        if (ruleResult.HasBrokenRules)
            throw new BusinessRuleException(ruleResult.BrokenRules);
        
        // Domain logic devam eder...
    }
}
```

---

## 🔐 Role ve Authorization Mekanizması

### Seçenek 1: Attribute-Based Authorization

```csharp
// Marker interfaces
public interface ISecuredRequest
{
    string[] Roles { get; }
    string[] Permissions { get; }
}

// Command örneği
public class DeleteUserCommand : IRequest<Unit>, ISecuredRequest
{
    public int UserId { get; set; }
    
    public string[] Roles => new[] { "Admin", "SuperAdmin" };
    public string[] Permissions => new[] { "Users.Delete" };
}
```

### Seçenek 2: Policy-Based Authorization (Daha Esnek ⭐)

```csharp
public interface IAuthorizationPolicy
{
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, object resource);
}

public class CanDeleteUserPolicy : IAuthorizationPolicy
{
    public async Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, object resource)
    {
        // Admin her şeyi silebilir
        if (user.IsInRole("Admin")) return true;
        
        // Kullanıcı sadece kendi hesabını silebilir
        if (resource is DeleteUserCommand command)
        {
            var userId = user.FindFirst("sub")?.Value;
            return command.UserId.ToString() == userId;
        }
        
        return false;
    }
}
```

### Seçenek 3: Hierarchical Role System

```csharp
public class RoleHierarchy
{
    private static readonly Dictionary<string, int> RoleLevels = new()
    {
        { "Guest", 0 },
        { "User", 1 },
        { "Moderator", 2 },
        { "Admin", 3 },
        { "SuperAdmin", 4 }
    };
    
    public bool HasAccess(string userRole, string requiredRole)
    {
        return RoleLevels.GetValueOrDefault(userRole, 0) >= 
               RoleLevels.GetValueOrDefault(requiredRole, int.MaxValue);
    }
}
```

### Permission-Based System (Granüler Kontrol)

```csharp
public class Permission
{
    public string Resource { get; set; }  // "Users", "Orders", "Products"
    public string Action { get; set; }     // "Create", "Read", "Update", "Delete"
    
    public override string ToString() => $"{Resource}.{Action}";
}

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(int userId, string resource, string action);
    Task<IEnumerable<Permission>> GetUserPermissionsAsync(int userId);
}
```

---

## 🗺️ AutoMapper Stratejileri

### Profile Organization

```
Mapping/
├── Profiles/
│   ├── UserMappingProfile.cs
│   ├── OrderMappingProfile.cs
│   └── ProductMappingProfile.cs
├── Converters/
│   ├── DateTimeConverter.cs
│   └── MoneyConverter.cs
├── Resolvers/
│   ├── UserAvatarResolver.cs
│   └── OrderTotalResolver.cs
└── Extensions/
    └── MappingExtensions.cs
```

### Value Resolvers (Karmaşık mapping'ler için)

```csharp
public class UserAvatarResolver : IValueResolver<User, UserDto, string>
{
    private readonly IStorageService _storageService;
    
    public string Resolve(User source, UserDto destination, string destMember, ResolutionContext context)
    {
        if (string.IsNullOrEmpty(source.AvatarPath))
            return _storageService.GetDefaultAvatarUrl();
            
        return _storageService.GetFullUrl(source.AvatarPath);
    }
}
```

### Conditional Mapping

```csharp
public class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.Email, opt => 
                opt.Condition(src => src.EmailVisible))
            .ForMember(dest => dest.PhoneNumber, opt => 
                opt.MapFrom((src, dest, _, context) =>
                {
                    var isAdmin = context.Items["IsAdmin"] as bool? ?? false;
                    return isAdmin ? src.PhoneNumber : "***";
                }));
    }
}
```

---

## 📁 Önerilen Proje Yapısı

```
BackendTemplate/
├── src/
│   ├── BackendTemplate.Domain/
│   │   ├── Entities/
│   │   │   ├── BaseEntity.cs
│   │   │   ├── User.cs
│   │   │   └── AuditableEntity.cs
│   │   ├── ValueObjects/
│   │   │   ├── Email.cs
│   │   │   ├── Money.cs
│   │   │   └── Address.cs
│   │   ├── Enums/
│   │   │   ├── UserStatus.cs
│   │   │   └── OrderStatus.cs
│   │   ├── Events/
│   │   │   ├── IDomainEvent.cs
│   │   │   └── UserCreatedEvent.cs
│   │   ├── Exceptions/
│   │   │   ├── DomainException.cs
│   │   │   └── BusinessRuleException.cs
│   │   └── Rules/
│   │       ├── IBusinessRule.cs
│   │       ├── BusinessRule.cs
│   │       └── User/
│   │           ├── UserEmailMustBeUniqueRule.cs
│   │           └── UserMustBeActiveRule.cs
│   │
│   ├── BackendTemplate.Application/
│   │   ├── Abstractions/
│   │   │   ├── IUnitOfWork.cs
│   │   │   ├── IRepository.cs
│   │   │   └── ICurrentUserService.cs
│   │   ├── Behaviors/
│   │   │   ├── LoggingBehavior.cs
│   │   │   ├── ValidationBehavior.cs
│   │   │   ├── AuthorizationBehavior.cs
│   │   │   ├── BusinessRulesBehavior.cs
│   │   │   ├── CachingBehavior.cs
│   │   │   └── TransactionBehavior.cs
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   │   ├── ISecuredRequest.cs
│   │   │   │   ├── ICacheableRequest.cs
│   │   │   │   ├── ITransactionalRequest.cs
│   │   │   │   └── IBusinessRuleRequest.cs
│   │   │   ├── Models/
│   │   │   │   ├── Result.cs
│   │   │   │   └── PaginatedResult.cs
│   │   │   └── Mappings/
│   │   │       └── MappingProfile.cs
│   │   ├── Features/
│   │   │   ├── Users/
│   │   │   │   ├── Commands/
│   │   │   │   │   ├── CreateUser/
│   │   │   │   │   │   ├── CreateUserCommand.cs
│   │   │   │   │   │   ├── CreateUserCommandHandler.cs
│   │   │   │   │   │   ├── CreateUserCommandValidator.cs
│   │   │   │   │   │   └── CreateUserBusinessRules.cs
│   │   │   │   │   └── UpdateUser/
│   │   │   │   │       └── ...
│   │   │   │   ├── Queries/
│   │   │   │   │   ├── GetUserById/
│   │   │   │   │   │   ├── GetUserByIdQuery.cs
│   │   │   │   │   │   └── GetUserByIdQueryHandler.cs
│   │   │   │   │   └── GetUsers/
│   │   │   │   │       └── ...
│   │   │   │   └── DTOs/
│   │   │   │       ├── UserDto.cs
│   │   │   │       └── UserDetailDto.cs
│   │   │   └── Orders/
│   │   │       └── ...
│   │   └── Services/
│   │       ├── BusinessRuleEngine.cs
│   │       └── AuthorizationService.cs
│   │
│   ├── BackendTemplate.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Configurations/
│   │   │   │   └── UserConfiguration.cs
│   │   │   ├── Repositories/
│   │   │   │   ├── BaseRepository.cs
│   │   │   │   └── UserRepository.cs
│   │   │   └── UnitOfWork.cs
│   │   ├── Services/
│   │   │   ├── CurrentUserService.cs
│   │   │   ├── CacheService.cs
│   │   │   └── EmailService.cs
│   │   └── DependencyInjection.cs
│   │
│   └── BackendTemplate.API/
│       ├── Controllers/
│       │   ├── BaseController.cs
│       │   └── UsersController.cs
│       ├── Filters/
│       │   └── ExceptionFilter.cs
│       ├── Middleware/
│       │   ├── ExceptionMiddleware.cs
│       │   └── RequestLoggingMiddleware.cs
│       └── Program.cs
│
├── tests/
│   ├── BackendTemplate.Domain.Tests/
│   ├── BackendTemplate.Application.Tests/
│   └── BackendTemplate.API.Tests/
│
└── docs/
    ├── ARCHITECTURE_DISCUSSION.md
    └── API.md
```

---

## 🤔 Tartışma Noktaları

### 1. Business Rules Yerleşimi
**Soru:** Business rules Domain layer'da mı yoksa Application layer'da mı olmalı?

| Domain Layer | Application Layer |
|--------------|-------------------|
| ✅ Domain logic'e yakın | ✅ Infrastructure erişimi kolay |
| ✅ Pure domain rules | ✅ Cross-cutting concerns |
| ❌ Repository erişimi yok | ❌ Domain'den bağımsız |

**Önerim:** Hybrid yaklaşım
- Pure domain rules → Domain Layer (ör: yaş kontrolü)
- Database gerektiren rules → Application Layer (ör: email unique kontrolü)

### 2. Result Pattern vs Exception
**Soru:** Business rule ihlallerinde exception mı fırlatalım, Result pattern mi kullanalım?

```csharp
// Option A: Exception
throw new BusinessRuleException("Email zaten kullanılıyor");

// Option B: Result Pattern
return Result<UserDto>.Failure("Email zaten kullanılıyor");
```

**Trade-offs:**
- Exception: Daha temiz happy path, ama performans maliyeti
- Result: Daha explicit, functional programming friendly

### 3. CQRS Separation Level
**Soru:** Commands ve Queries aynı database'i mi kullanmalı?

| Aynı DB | Ayrı DB (Event Sourcing) |
|---------|--------------------------|
| ✅ Basit | ✅ Scalable |
| ✅ Consistency kolay | ✅ Read optimization |
| ❌ Read/Write aynı model | ❌ Eventual consistency |

### 4. Authorization Granularity
**Soru:** Role-based mi, Permission-based mi, Policy-based mi?

**Önerim:** Katmanlı yaklaşım
```
Role → Permission → Policy
Admin → Users.* → CanDeleteAnyUser
User → Users.Read, Users.UpdateOwn → CanUpdateOwnProfile
```

---

## 💡 Sorularım

1. **Domain Complexity:** Kaç tane ana entity/aggregate düşünüyorsunuz?

2. **Business Rules Density:** Ortalama bir command için kaç business rule olabilir?

3. **Multi-tenancy:** Çoklu kiracı (tenant) desteği gerekli mi?

4. **Event-Driven:** Domain events kullanacak mıyız? (ör: UserCreated → SendWelcomeEmail)

5. **Caching Strategy:** Hangi query'ler cache'lenmeli?

6. **Audit Trail:** Tüm değişikliklerin loglanması gerekli mi?

---

## 🚀 Sonraki Adımlar

Tartışmamıza göre:

1. [ ] Proje yapısını oluştur
2. [ ] Base class'ları ve interface'leri implement et
3. [ ] MediatR pipeline'ları kur
4. [ ] Business Rules engine'i implement et
5. [ ] Authorization mekanizmasını kur
6. [ ] Örnek bir feature (User CRUD) implement et
7. [ ] Test projelerini kur

---

## 📚 Referanslar

- [MediatR Documentation](https://github.com/jbogard/MediatR)
- [Clean Architecture - Jason Taylor](https://github.com/jasontaylordev/CleanArchitecture)
- [Domain-Driven Design - Eric Evans](https://www.domainlanguage.com/)
- [Specification Pattern](https://enterprisecraftsmanship.com/posts/specification-pattern-c-implementation/)
