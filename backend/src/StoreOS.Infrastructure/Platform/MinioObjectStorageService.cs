using Amazon.S3;
using Amazon.S3.Model;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Platform;

public class MinioObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _client;

    public MinioObjectStorageService(IAmazonS3 client)
    {
        _client = client;
    }

    public async Task<string> UploadAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        await EnsureBucketExistsAsync(bucket, ct);

        using var stream = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        }, ct);

        return $"{bucket}/{key}";
    }

    public async Task<StoredObject?> GetAsync(string bucket, string key, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucket,
                Key = key
            }, ct);

            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, ct);
            return new StoredObject(memoryStream.ToArray(), response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "NotFound" || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task EnsureBucketsAsync(CancellationToken ct = default)
    {
        string[] buckets = ["storeos-media", "storeos-backups"];
        foreach (var bucket in buckets)
        {
            await EnsureBucketExistsAsync(bucket, ct);
        }
    }

    private async Task EnsureBucketExistsAsync(string bucket, CancellationToken ct)
    {
        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Bucket already exists, ignore
        }
        catch (Exception)
        {
            // Ignore if MinIO or permission creates another error for existing bucket
        }
    }
}
