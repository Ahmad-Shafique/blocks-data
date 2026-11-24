using Amazon;
using Amazon.S3;
using Blocks.Genesis.Entities.Data;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AwsS3CompatibleStorageService : AwsS3StorageService
    {
        public AwsS3CompatibleStorageService(StorageConfiguration configuration)
            : base(CreateS3Client(configuration))
        {
            // S3_PUBLIC_ENDPOINT (I02-WP11): the address a browser can reach, used only to sign URLs for it.
            // Server-side calls keep configuration.Host (for example http://s3:8333, which only resolves in the container network).
            var publicEndpoint = Environment.GetEnvironmentVariable("S3_PUBLIC_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(publicEndpoint))
            {
                _presignClient = CreateS3Client(configuration, publicEndpoint.Trim());
            }
        }

        private static AmazonS3Client CreateS3Client(StorageConfiguration configuration, string? endpointOverride = null)
        {
            // Force Signature Version 4 for MinIO/S3-compatible storage
            AWSConfigsS3.UseSignatureVersion4 = true;

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpointOverride ?? configuration.Host, // e.g., https://s3.minio.seliseblocks.com (S3 compatible API endpoint)
                ForcePathStyle = true
            };
            var accessKey = configuration.AccessKey;
            var secretKey = configuration.SecretKey;

            return new AmazonS3Client(accessKey, secretKey, s3Config);
        }
    }
}
