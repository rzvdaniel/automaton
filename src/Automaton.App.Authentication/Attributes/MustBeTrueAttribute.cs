using System.ComponentModel.DataAnnotations;

public class MustBeTrueAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        if (value is bool boolValue && boolValue)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(ErrorMessage ?? "You must agree with the terms and privacy policy.");
    }
}