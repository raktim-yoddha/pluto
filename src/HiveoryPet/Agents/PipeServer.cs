using System.IO.Pipes;
using System.Security.Principal;

namespace HiveoryPet.Agents;

/// <summary>
/// Where the relay delivers hook events: a named pipe only this Windows user can open (CurrentUserOnly on both
/// ends, so neither side talks to another account's pipe). One message per connection.
/// </summary>
public sealed class PipeServer(Action<byte[]> onMessage) : IDisposable
{
    private const int MaxMessage = 2 << 20;

    private readonly CancellationTokenSource _stop = new();

    /// <summary>Same name as the relay's; the SID keeps two Windows users apart.</summary>
    public static string Name => "hiveory-pet-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

    public void Start()
    {
        // A few listeners at once: parallel agents fire hooks in bursts.
        for (var i = 0; i < 4; i++)
        {
            _ = Task.Run(ListenAsync);
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    Name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var memory = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await pipe.ReadAsync(chunk, _stop.Token)) > 0 && memory.Length < MaxMessage)
                {
                    memory.Write(chunk, 0, read);
                }

                onMessage(memory.ToArray());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(50); // a client that hung up early; listen again
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
