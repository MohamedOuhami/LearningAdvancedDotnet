# Async/Await in .NET — Review Notes

## 1. Why async/await exists

A blocking (synchronous) call ties up a thread for the entire duration of a wait — no other work can happen on that thread until it returns.

```csharp
var data = DownloadData(); // thread frozen for however long this takes
Console.WriteLine("Done!");
```

In a UI app, this freezes the interface. In a server, it eats up threads from the pool, so other incoming requests have to queue until a thread frees up.

`await` lets the thread give itself back while waiting on I/O (network, disk, database), instead of sitting idle. When the awaited work finishes, .NET resumes your method (often on a different thread from the pool).

**Blocking vs awaiting — same wait time, different thread behavior:**

| | What the thread does during the wait |
|---|---|
| Blocking call | Sits frozen, unusable for anything else |
| `await` | Released back to the pool/OS, free to do other work, comes back when ready |

## 2. Core syntax

```csharp
public async Task<string> DownloadDataAsync()
{
    string result = await client.GetStringAsync(url);
    return result;
}
```

- **`async`** — modifier that allows `await` to be used inside the method. On its own, it does *not* make anything non-blocking — it just unlocks `await`.
- **`await`** — pauses execution at that line until the awaited `Task` completes, and releases the thread during the wait.
- **`Task` / `Task<T>`** — the return type of an async method; a placeholder/"receipt" for a value that will exist later.

## 3. Execution order

```csharp
Task<string> task = DownloadDataAsync(); // starts the work, returns immediately
Console.WriteLine("A");                  // runs right away
string result = await task;              // pauses HERE until data is ready
Console.WriteLine("B");                  // only runs after data arrives
```

Order: **A → (wait) → B**. `await` is a hard stop before the next line, not something checked after the fact.

## 4. The `.Result` / `.Wait()` trap

```csharp
string data = GetDataAsync().Result; // BLOCKS the calling thread
```

`.Result` and `.Wait()` force the calling thread to block until the task finishes — no benefit of `await` at all. Worse, mixing this with `await` inside the called method can **deadlock** in environments with a single UI/context thread (classic ASP.NET, WPF, WinForms): the thread blocks on `.Result`, but the inner `await` wants to resume on that same blocked thread. Nobody moves.

**Rule: "async all the way."** Once one method in a chain is `async`, everything above it should be `async`/`await` too, up to the entry point. Don't use `.Result`/`.Wait()` to "convert back" to sync.

**Exception:** `.Result` is safe *after* a task is already known to be complete — e.g., right after `Task.WhenAll` has finished awaiting it. The danger is only in *forcing* a wait, not in reading an already-finished value.

## 5. Async void vs async Task

- `Main` can be `async Task Main()`.
- Event handlers (e.g. button clicks) are wired up by a framework to a fixed signature, usually `void` — so they must be `async void`. This is the one place you can't avoid it.

```csharp
private void SubmitButton_Click(object sender, EventArgs e) { }
```

**Why `async void` is dangerous:** exceptions thrown inside `async Task` methods are captured inside the `Task` object and only surface (catchable) when awaited. Exceptions thrown inside `async void` methods have no `Task` to hide in — they propagate straight to the framework's message loop and can crash the app. No external try/catch around the *call site* can catch them.

**Fix:** always wrap the body of an `async void` method in try/catch:

```csharp
private async void SubmitButton_Click(object sender, EventArgs e)
{
    try
    {
        await CallApiAsync();
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Something went wrong: {ex.Message}");
    }
}
```

**Rule: use `async Task` / `async Task<T>` everywhere except event handlers.**

## 6. Concurrency

Awaiting sequentially runs things one after another — no parallelism:

```csharp
var r1 = await DownloadDataAsync(url1); // ~2s
var r2 = await DownloadDataAsync(url2); // ~2s
var r3 = await DownloadDataAsync(url3); // ~2s
// total: ~6s
```

Start all tasks first (no `await` yet), *then* await them together:

```csharp
Task<string> t1 = DownloadDataAsync(url1); // starts immediately
Task<string> t2 = DownloadDataAsync(url2); // starts immediately
Task<string> t3 = DownloadDataAsync(url3); // starts immediately

string[] results = await Task.WhenAll(t1, t2, t3);
// total: ~2s (time of the slowest one)
```

- **`Task.WhenAll`** — completes when *all* tasks finish. If any task faults (throws, or is cancelled), the whole `WhenAll` faults — even tasks that succeeded don't get their results delivered to you.
- **`Task.WhenAny`** — completes as soon as the *first* task finishes. Useful for races (e.g. fastest mirror wins) or implementing timeouts by racing against `Task.Delay`.

## 7. Cancellation

`CancellationTokenSource` (the "remote control," owns the power to cancel) and `CancellationToken` (the read-only "listener" passed downstream) are split deliberately: only the code that owns the source can trigger cancellation; everything downstream can only check/react to it. Cancellation flows one-way, down the call chain.

```csharp
CancellationTokenSource cts = new CancellationTokenSource();
cts.CancelAfter(1500); // auto-cancels after 1500ms, no manual Task.Delay needed

try
{
    string[] results = await Task.WhenAll(
        SimulateDownloadAsync("user1", 1000, cts.Token),
        SimulateDownloadAsync("user2", 2000, cts.Token),
        SimulateDownloadAsync("user3", 3000, cts.Token)
    );

    foreach (var r in results) Console.WriteLine(r);
}
catch (OperationCanceledException)
{
    Console.WriteLine("One or more downloads were cancelled.");
}
```

- Built-in async methods (like `Task.Delay(ms, token)`, `HttpClient.GetStringAsync(url, token)`) throw `OperationCanceledException` (or `TaskCanceledException`) automatically when their token is cancelled.
- In your own long-running/manual code (e.g. a loop), call `token.ThrowIfCancellationRequested()` at natural checkpoints to check and throw manually.
- `token.IsCancellationRequested` gives a plain `true`/`false` if you want to clean up before bailing, instead of letting the exception unwind for you.
- Because `WhenAll` needs *every* task to succeed, a cancellation of even one task fails the whole group — completed results from other tasks are discarded.

## 8. Full worked example

```csharp
using LearnAsyncAwait;

DownloadService downloadService = new DownloadService();
CancellationTokenSource cts = new CancellationTokenSource();
cts.CancelAfter(1500);

try
{
    string[] downloadResults = await Task.WhenAll(
        downloadService.SimulateDownloadAsync("user1", 1000, cts.Token),
        downloadService.SimulateDownloadAsync("user2", 2000, cts.Token),
        downloadService.SimulateDownloadAsync("user3", 3000, cts.Token)
    );

    foreach (var result in downloadResults)
    {
        Console.WriteLine(result);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("One or more downloads were cancelled");
}

namespace LearnAsyncAwait
{
    public class DownloadService
    {
        public async Task<string> SimulateDownloadAsync(string name, int delayMs, CancellationToken token)
        {
            await Task.Delay(delayMs, token);
            return $"{name} downloaded";
        }
    }
}
```

Expected output: `"One or more downloads were cancelled"` only — user1 finishes at 1000ms but its result is discarded because `WhenAll` faults as a whole once user2/user3 are cancelled at 1500ms.

---

## Self-test: interview-style questions

Try answering these from memory before checking back against the notes above.

1. What's the practical difference between a method that blocks a thread and one that `await`s? What happens to the thread in each case?
2. Does adding the `async` keyword to a method make it run without blocking, by itself? What does `async` actually do?
3. Given `var t = FooAsync(); DoSomethingElse(); var result = await t;` — explain the order of execution and why.
4. Why is calling `.Result` or `.Wait()` on a task dangerous? Under what specific condition can it deadlock?
5. Is there a situation where calling `.Result` on a task is actually safe? Describe it.
6. What is `async void` used for, and why is it considered risky? What's the mitigation?
7. Why can't `Main` or most methods be `async void` — why is `async Task` preferred everywhere else?
8. You need to download data from 3 different endpoints. Write (or describe) the difference in total time between awaiting each call sequentially vs. starting all three then awaiting together.
9. What's the difference between `Task.WhenAll` and `Task.WhenAny`? Give a use case for each.
10. If one task in a `Task.WhenAll` group throws or is cancelled, what happens to the results of the other tasks that succeeded?
11. What are `CancellationTokenSource` and `CancellationToken`, and why are they two separate types instead of one?
12. What exception type is typically thrown when a cancellation token is triggered?
13. In your own long-running loop (not calling a library method), how do you make it respect a `CancellationToken`?
14. What's the difference between `token.IsCancellationRequested` and `token.ThrowIfCancellationRequested()` — when would you use one over the other?
15. What does `CancellationTokenSource.CancelAfter(ms)` do, and how does it compare to manually using `Task.Delay` + `Cancel()`?