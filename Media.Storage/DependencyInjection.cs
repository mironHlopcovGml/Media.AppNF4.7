using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Media.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System;
using System.Net;

public static class DependencyInjection
{
    public static IServiceCollection AddStorageModule(this IServiceCollection services, IConfiguration config)
    {
        // Конфигурация без ValidateOnStart (нет в netstandard2.1)
        services.AddOptions<StorageOptions>()
                .Bind(config.GetSection("Storage"));

        string provider = config.GetValue<string>("Storage:Provider");
        if (provider != null)
            provider = provider.ToLowerInvariant();
        else
            provider = "disk";

        if (provider == "s3")
        {
            // Считываем настройки вручную, не используя null-forgiving (!)
            var opts = new StorageOptions();
            config.GetSection("Storage").Bind(opts);

            // Создаём AWS креденшелы
            var awsCreds = new BasicAWSCredentials(opts.AccessKey, opts.SecretKey);

            var s3Config = new AmazonS3Config
            {
                ServiceURL = opts.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = opts.Region
            };

            // Retry policy (Polly 6.x, совместимо с netstandard2.1)
            AsyncRetryPolicy retryPolicy = Policy
                .Handle<AmazonS3Exception>(ex =>
                    ex.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)ex.StatusCode >= 500)
                .WaitAndRetryAsync(
                    3,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
                );

            services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(awsCreds, s3Config));
            services.AddSingleton<AsyncRetryPolicy>(retryPolicy);
            services.AddSingleton<IStorageService, Media.Storage.Implementations.S3StorageService>();
        }
        else
        {
            services.AddSingleton<IStorageService, Media.Storage.Implementations.DiskStorageService>();
        }

        return services;
    }
}
