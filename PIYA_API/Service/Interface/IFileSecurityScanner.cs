namespace PIYA_API.Service.Interface;

public interface IFileSecurityScanner
{
    Task ScanAsync(Stream stream, CancellationToken cancellationToken = default);
}
