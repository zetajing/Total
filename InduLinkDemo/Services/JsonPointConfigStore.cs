using System;
using InduLink.Runtime;
using InduLink.Runtime.Configuration;

namespace InduLinkDemo.Services
{
    internal static class JsonPointConfigStore
    {
        public static void Save(
            JsonConfigurationValidationService validationService,
            string filePath,
            string json)
        {
            if (validationService == null) throw new ArgumentNullException(nameof(validationService));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Point configuration path cannot be empty.", nameof(filePath));

            var validation = validationService.ValidateForSave(
                JsonConfigurationDocument.Points,
                json,
                validationService.ConfigDirectory);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.ToDisplayText());

            TagTable.FromJson(json).SaveJson(filePath);
        }
    }
}
