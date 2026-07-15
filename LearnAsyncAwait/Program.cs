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
        System.Console.WriteLine(result);
    }
}
catch (OperationCanceledException ex)
{
    System.Console.WriteLine("One or more downloads were cancelled");
}
