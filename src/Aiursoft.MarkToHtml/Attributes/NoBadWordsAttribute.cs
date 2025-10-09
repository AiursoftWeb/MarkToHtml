using System.ComponentModel.DataAnnotations;
using Aiursoft.MarkToHtml.Services;

namespace Aiursoft.MarkToHtml.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class NoBadWordsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Get the service from the dependency injection container.
        var badWordFilter = validationContext.GetService(typeof(BadWordFilterService)) as BadWordFilterService;

        if (badWordFilter == null)
        {
            // This should not happen if the service is registered correctly.
            throw new InvalidOperationException("BadWordFilterService is not registered.");
        }

        if (value is not string stringValue)
        {
            // This validator only applies to strings.
            return ValidationResult.Success;
        }

        if (badWordFilter.ContainsBadWord(stringValue))
        {
            // If a bad word is found, return a validation error.
            return new ValidationResult(ErrorMessage ?? $"The field {validationContext.DisplayName} contains inappropriate content.");
        }

        // If no bad words are found, the validation is successful.
        return ValidationResult.Success;
    }
}
