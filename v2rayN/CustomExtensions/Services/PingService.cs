using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace v2rayN.CustomExtensions.Services;

public sealed record PingResult<TNode>(
    TNode Node,
    long? LatencyMilliseconds,
    bool IsAlive,
    string? Failure = null);

public sealed class PingService : IDisposable
{
    private static readonly Uri DefaultProbeUri = new("http://www.gstatic.com/generate_204");
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public PingService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsClient = httpClient is null;
    }

    public async Task<IReadOnlyList<PingResult<TNode>>> MeasureAsync<TNode>(
        IEnumerable<TNode> nodes,
        Func<TNode, Uri>? getProbeUri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        using var semaphore = new SemaphoreSlim(100, 100);
        var results = new ConcurrentBag<PingResult<TNode>>();

        var tasks = nodes.Select(async node =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var uri = getProbeUri?.Invoke(node) ?? DefaultProbeUri;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                var timer = Stopwatch.StartNew();
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                timer.Stop();

                var status = (int)response.StatusCode;
                var alive = status is >= 200 and < 400 && timer.ElapsedMilliseconds <= 5000;
                results.Add(alive
                    ? new PingResult<TNode>(node, timer.ElapsedMilliseconds, true)
                    : new PingResult<TNode>(node, null, false, $"HTTP {(int)response.StatusCode} or RTT exceeded 5000 ms"));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new PingResult<TNode>(node, null, false, ex.Message));
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(new PingResult<TNode>(node, null, false, ex.Message));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results
            .OrderByDescending(result => result.IsAlive)
            .ThenBy(result => result.LatencyMilliseconds ?? long.MaxValue)
            .ToArray();
    }

    public async Task<IReadOnlyList<PingResult<TNode>>> MeasureActiveAsync<TNode>(
        IEnumerable<TNode> nodes,
        Func<TNode, Uri>? getProbeUri = null,
        CancellationToken cancellationToken = default)
    {
        var results = await MeasureAsync(nodes, getProbeUri, cancellationToken);
        return results.Where(result => result.IsAlive).ToArray();
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
