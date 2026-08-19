using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class DisabledFileSecurityScanner : IFileSecurityScanner
{
    public Task ScanAsync(Stream stream, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Streams uploads to clamd using its INSTREAM protocol.</summary>
public sealed class ClamAvFileSecurityScanner(
    IOptions<MalwareScanningOptions> options,
    ILogger<ClamAvFileSecurityScanner> logger) : IFileSecurityScanner
{
    private readonly MalwareScanningOptions _options = options.Value;
    private readonly ILogger<ClamAvFileSecurityScanner> _logger = logger;

    public async Task ScanAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek)
            throw new InvalidOperationException("The upload stream must be seekable for security scanning.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

            stream.Position = 0;
            var buffer = new byte[64 * 1024];
            var lengthPrefix = new byte[4];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)count);
                await network.WriteAsync(lengthPrefix, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            await network.WriteAsync(new byte[4], timeout.Token);
            await network.FlushAsync(timeout.Token);

            var responseBuffer = new byte[4096];
            var responseLength = await network.ReadAsync(responseBuffer, timeout.Token);
            var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength).TrimEnd('\0', '\r', '\n');

            if (response.EndsWith("FOUND", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Malware scanner rejected an uploaded file: {ScannerResponse}", response);
                throw new InvalidOperationException("The uploaded file failed the malware safety scan.");
            }
            if (!response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The malware scanner could not verify the uploaded file.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Malware scanner is unavailable");
            throw new InvalidOperationException(
                "The uploaded file could not be security-scanned. Please try again later.", ex);
        }
        finally
        {
            stream.Position = 0;
        }
    }
}
