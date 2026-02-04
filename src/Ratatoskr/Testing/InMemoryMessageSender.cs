using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Ratatoskr.Core;

namespace Ratatoskr.Testing;

/// <summary>
/// A message sender that stores messages in memory for testing.
/// Thread-safe for parallel test execution.
/// </summary>
public class InMemoryMessageSender(ChannelRegistry? registry = null) : IMessageSender
{
    private readonly ConcurrentBag<SentMessage> _messages = new();
    private readonly List<(Func<SentMessage, bool> Predicate, TaskCompletionSource<SentMessage> Tcs)> _waiters = new();
    private readonly object _waiterLock = new();

    /// <summary>
    /// Gets the message type registry used by this sender.
    /// </summary>
    public ChannelRegistry? Registry { get; } = registry;

    /// <summary>
    /// All messages that have been sent.
    /// </summary>
    public IReadOnlyCollection<SentMessage> SentMessages => _messages.ToArray();

    /// <summary>
    /// Event triggered when a message is sent.
    /// </summary>
    public event Action<SentMessage>? MessageSent;
    
    public Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        var message = new SentMessage(content, props, DateTimeOffset.UtcNow);
        _messages.Add(message);
        
        MessageSent?.Invoke(message);
        NotifyWaiters(message);
        
        return Task.CompletedTask;
    }

    private void NotifyWaiters(SentMessage message)
    {
        lock (_waiterLock)
        {
            // Iterate backwards to allow removal
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                var (predicate, tcs) = _waiters[i];
                if (predicate(message))
                {
                    // Move callback to a different thread to avoid blocking the sender
                    Task.Run(() => tcs.TrySetResult(message));
                    _waiters.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Waits for a message matching the predicate to be sent.
    /// checkExisting: If true, checks messages already sent before waiting.
    /// </summary>
    public async Task<SentMessage> WaitForMessageAsync(Func<SentMessage, bool> predicate, TimeSpan? timeout = null, bool checkExisting = true)
    {
        // 1. Check existing messages first if requested
        if (checkExisting)
        {
            var existing = _messages.FirstOrDefault(predicate);
            if (existing != null)
            {
                return existing;
            }
        }

        // 2. Setup waiter
        var tcs = new TaskCompletionSource<SentMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        lock (_waiterLock)
        {
            // Double check inside lock in case one arrived just now
            if (checkExisting)
            {
                 var existing = _messages.FirstOrDefault(predicate);
                 if (existing != null) return existing;
            }
            
            _waiters.Add((predicate, tcs));
        }

        // 3. Wait with timeout
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var timeoutTask = Task.Delay(actualTimeout);
        
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
        
        if (completedTask == timeoutTask)
        {
            lock (_waiterLock)
            {
                _waiters.RemoveAll(x => x.Tcs == tcs);
            }
            throw new TimeoutException($"Timed out waiting for message after {actualTimeout.TotalSeconds}s");
        }
        
        return await tcs.Task;
    }
    
    /// <summary>
    /// Clears all sent messages. Call this in test setup/teardown.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
        lock (_waiterLock)
        {
            foreach (var waiter in _waiters)
            {
                waiter.Tcs.TrySetCanceled();
            }
            _waiters.Clear();
        }
    }
}

/// <summary>
/// Represents a message that was sent via InMemoryMessageSender.
/// </summary>
public record SentMessage(byte[] Content, MessageProperties Properties, DateTimeOffset SentAt)
{
    /// <summary>
    /// Deserializes the message content to the specified type.
    /// </summary>
    public T? Deserialize<T>(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(Content, options);
    }
    
    /// <summary>
    /// Gets the content as a string (assumes UTF-8 encoding).
    /// </summary>
    public string ContentAsString => Encoding.UTF8.GetString(Content);
}
