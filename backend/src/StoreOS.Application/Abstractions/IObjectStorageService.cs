namespace StoreOS.Application.Abstractions;

public interface IObjectStorageService
{
    Task<string> UploadAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct);
    Task<StoredObject?> GetAsync(string bucket, string key, CancellationToken ct);
    Task EnsureBucketsAsync(CancellationToken ct = default);
}

public sealed record StoredObject(byte[] Content, string ContentType);
