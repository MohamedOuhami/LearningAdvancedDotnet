tags: [csharp, dotnet, dependency-injection, notes]

# Dependency Injection in C# / .NET — Full Notes

---

## 1. The Core Problem

If a class creates its own dependency with `new`, it's welded to that specific implementation — you can't swap it out (e.g. for a test double) without editing the class itself.

```csharp
// Bad: OrderService decides AND creates its dependency
public class OrderService
{
    private readonly EmailSender _emailSender = new EmailSender();

    public void PlaceOrder(Order order)
    {
        _emailSender.Send(order.CustomerEmail, "Order placed!");
    }
}
```

**Dependency Injection** = a class declares what it *needs* (via its constructor), and something *external* provides it. The class stops deciding how its dependencies are built.

---

## 2. Interfaces + Constructor Injection

An interface lets the consumer depend on a *contract*, not a concrete type — so any implementation (real, fake, mock) can be substituted without touching the consumer.

```csharp
public interface IEmailSender
{
    void Send(string to, string message);
}

public class EmailSender : IEmailSender
{
    public void Send(string to, string message) =>
        Console.WriteLine($"Sending to {to}: {message}");
}

public class OrderService
{
    private readonly IEmailSender _emailSender;

    // Constructor injection: dependency is handed in, not created
    public OrderService(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public void PlaceOrder(Order order) =>
        _emailSender.Send(order.CustomerEmail, "Order placed!");
}
```

Now tests can inject a `FakeEmailSender` with zero changes to `OrderService`.

---

## 3. The DI Container (IoC Container)

Hand-wiring every object graph (`new X(new Y(new Z()))`) doesn't scale — the same dependency (e.g. a DB connection) is often needed in many places, and adding one constructor parameter means hunting down every manual construction site.

A **container** handles this: you *register* mappings once, then *resolve* fully-built object graphs on demand.

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddScoped<IEmailSender, EmailSender>();
services.AddScoped<OrderService>();

var provider = services.BuildServiceProvider();
var orderService = provider.GetRequiredService<OrderService>();
```

- **Register**: "when something asks for `IEmailSender`, give it an `EmailSender`."
- **Resolve**: ask the container for the top-level object; it builds the whole chain automatically.

---

## 4. Lifetimes

| Lifetime | Instance count | Typical use |
|---|---|---|
| **Singleton** | One, for the entire application | Stateless, expensive-to-build, thread-safe resources (e.g. config, `HttpClient`) |
| **Scoped** | One per scope (e.g. one per HTTP request) | Per-request state (e.g. current user, `DbContext`) |
| **Transient** | New instance every time it's resolved | Lightweight, stateless, cheap objects |

```csharp
services.AddSingleton<IConfig, AppConfig>();
services.AddScoped<IUserContext, UserContext>();
services.AddTransient<IReportGenerator, ReportGenerator>();
```

**Rule of thumb:** don't default to Singleton "for performance" unless there's an actual reason (expensive construction, genuinely stateless/thread-safe). If a service touches per-request data, it should be Scoped.

**Dependency rule:** a service can only safely depend on things with an **equal or longer** lifetime than itself. Scoped → Singleton is fine. Singleton → Scoped is not.

---

## 5. Captive Dependencies (the classic bug)

A **captive dependency** happens when a longer-lived service captures and holds onto a shorter-lived one — freezing it far past when it should have been replaced.

```csharp
services.AddSingleton<IEmailSender, EmailSender>(); // long-lived
services.AddScoped<IUserContext, UserContext>();    // per-request

public class EmailSender : IEmailSender
{
    private readonly IUserContext _userContext;
    public EmailSender(IUserContext userContext) => _userContext = userContext;
    // ...
}
```

**What happens:** `EmailSender` is built exactly once — the first time anything asks for it. At that moment, the container must supply an `IUserContext`, and because `EmailSender` is a Singleton, that `IUserContext` gets resolved from the **root container**, not any per-request scope. It's a single, permanent instance. Every subsequent request's mutations to *their own* scoped `IUserContext` never reach `EmailSender` — it just keeps using the one it captured at startup (or worse, silently defaults, e.g. `"Unknown"`, if nothing ever mutates the root instance).

This is silent and dangerous — no exception, just wrong data.

### The safety net

```csharp
var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true });
```

With this on, the container throws immediately when a Singleton tries to consume a Scoped service:

```
InvalidOperationException: Cannot consume scoped service 'IUserContext' from singleton 'OrderService'.
```

ASP.NET Core enables this automatically in the **Development** environment.

### The fix

Match the lifetime to what the service actually needs. If a "singleton" service needs per-request data, it usually isn't really a singleton — make it Scoped.

```csharp
services.AddScoped<OrderService>(); // was AddSingleton — wrong
```

(Alternative, when you genuinely need a long-lived object to reach into shorter-lived scopes: inject `IServiceProvider` or `IServiceScopeFactory` and resolve the scoped dependency fresh, per use, instead of capturing it in the constructor.)

---

## 6. Worked Example — Full Flow

```csharp
public interface IUserContext { string CurrentUser { get; set; } }
public class UserContext : IUserContext
{
    public string CurrentUser { get; set; } = "Unknown";
}

public interface IEmailSender { void Send(string message); }
public class EmailSender : IEmailSender
{
    private readonly IUserContext _userContext;
    public EmailSender(IUserContext userContext) => _userContext = userContext;
    public void Send(string message) =>
        Console.WriteLine($"[{_userContext.CurrentUser}] {message}");
}

public class OrderService
{
    private readonly IEmailSender _emailSender;
    public OrderService(IEmailSender emailSender) => _emailSender = emailSender;
    public void PlaceOrder() => _emailSender.Send("Order placed!");
}

// Correct wiring — everything Scoped, matches the per-request data need
var services = new ServiceCollection();
services.AddScoped<IEmailSender, EmailSender>();
services.AddScoped<IUserContext, UserContext>();
services.AddScoped<OrderService>();

var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true });

using (var scope1 = provider.CreateScope())
{
    scope1.ServiceProvider.GetRequiredService<IUserContext>().CurrentUser = "Alice";
    scope1.ServiceProvider.GetRequiredService<OrderService>().PlaceOrder();
    // [Alice] Order placed!
}

using (var scope2 = provider.CreateScope())
{
    scope2.ServiceProvider.GetRequiredService<IUserContext>().CurrentUser = "Bob";
    scope2.ServiceProvider.GetRequiredService<OrderService>().PlaceOrder();
    // [Bob] Order placed!
}
```

---

## 7. What's Next (not yet covered)

- `IServiceScopeFactory` — manually creating scopes (e.g. inside a background worker with no natural request scope)
- `IHttpContextAccessor` — the real-world ASP.NET Core equivalent of `IUserContext`
- Named/keyed services, decorators, and open-generic registrations

---

## Interview-Level Review Questions

**Core Idea**
1. What problem does DI solve? Explain using the "class creates its own dependency" anti-pattern.
2. Why does the dependency need to be an interface rather than a concrete class?
3. Define constructor injection in one sentence.

**The Container**
4. What's the difference between registering and resolving a service?
5. Why use a container instead of hand-wiring dependencies in `Main`?
6. What does `services.AddScoped<IEmailSender, EmailSender>()` tell the container to do?

**Lifetimes**
7. Define Singleton, Scoped, and Transient in one sentence each.
8. What typically defines a "scope" boundary in ASP.NET Core?
9. Three services in the same HTTP request each inject a Scoped `ILogger`. How many instances exist, and is it shared?
10. What's the rule for what lifetime a service is allowed to depend on, relative to itself?

**Captive Dependencies**
11. What is a captive dependency, mechanistically — not just the definition?
12. Walk through, step by step, what happens the first time a Singleton with a Scoped dependency gets resolved, and why every future usage is affected.
13. What does `ValidateScopes = true` actually do, and when does the exception fire?
14. In a case where a Singleton captures a Scoped dependency, why might the bug show the *same wrong value every time* rather than "leaking" one request's value into another's?
15. Given a Singleton that needs per-request data, what are the two possible fixes, and which is usually correct — and why?

**Judgment**
16. When should you actually reach for Singleton, if not "for performance"?
17. True/false + explain: "A Scoped service can safely depend on a Singleton service."
18. True/false + explain: "A Transient service captured inside a Singleton still behaves like a Transient."

**Applied**
19. You're in a background worker with no HTTP request (no natural scope). You need a Scoped service inside it. What tool would you reach for?
20. Explain the entire DI story — from "why not just `new` things" to "why lifetimes matter" — in one paragraph, as if to someone who's never heard of it.

---

## Review Log
*(track dates + weak spots)*

| Date | Weak areas | Notes |
|---|---|---|
| | | |