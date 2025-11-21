using System;
using System.ComponentModel.DataAnnotations;

namespace Media.Storage
{

    public class StorageOptions
    {
        [Required] public string Provider { get; set; } = "Disk";

        // S3
        [RequiredIfProvider("S3")] public string BucketName { get; set; }
        [RequiredIfProvider("S3")] public string ServiceUrl { get; set; }
        [RequiredIfProvider("S3")] public string Region { get; set; }
        [RequiredIfProvider("S3")] public string AccessKey { get; set; }
        [RequiredIfProvider("S3")] public string SecretKey { get; set; }

        // Disk
        [RequiredIfProvider("Disk")] public string RootPath { get; set; }
    }

    // Кастомный атрибут (добавьте в проект)
    public class RequiredIfProviderAttribute : ValidationAttribute
    {
        private readonly string _provider;
        public RequiredIfProviderAttribute(string provider) => _provider = provider;
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            var opts = (StorageOptions)context.ObjectInstance;
            if (opts.Provider.Equals(_provider, StringComparison.OrdinalIgnoreCase) && value == null)
                return new ValidationResult($"Required when Provider is {_provider}");
            return ValidationResult.Success;
        }
    }
}