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