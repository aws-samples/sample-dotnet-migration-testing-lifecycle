using System.ComponentModel.DataAnnotations;

namespace PayorContracts.Web.Models;

/// <summary>
/// Form model with DataAnnotations validation.
/// Replaces: jQuery Validate rules defined in JavaScript.
/// Each [attribute] maps to a jQuery Validate rule.
/// </summary>
public class AddPayorModel
{
    /// <summary>
    /// jQuery Validate equivalent: required: true, minlength: 3, maxlength: 100
    /// </summary>
    [Required(ErrorMessage = "Payor name is required")]
    [MinLength(3, ErrorMessage = "Name must be at least 3 characters")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string PayorName { get; set; } = string.Empty;

    /// <summary>
    /// jQuery Validate equivalent: required: true, minlength: 3, maxlength: 10, pattern: /^[A-Z0-9]+$/
    /// </summary>
    [Required(ErrorMessage = "Payor code is required")]
    [MinLength(3, ErrorMessage = "Code must be at least 3 characters")]
    [MaxLength(10, ErrorMessage = "Code cannot exceed 10 characters")]
    [RegularExpression(@"^[A-Z0-9]+$", ErrorMessage = "Code must be uppercase letters and numbers only")]
    public string PayorCode { get; set; } = string.Empty;

    /// <summary>
    /// jQuery Validate equivalent: required: true, email: true
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// jQuery Validate equivalent: phoneUS: true (from additional-methods.js)
    /// </summary>
    [Phone(ErrorMessage = "Please enter a valid phone number")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// jQuery Validate equivalent: required: true, number: true, min: 1, max: 10000000
    /// </summary>
    [Required(ErrorMessage = "Contract value is required")]
    [Range(1, 10000000, ErrorMessage = "Value must be between 1 and 10,000,000")]
    public decimal? MaxContractValue { get; set; }

    /// <summary>
    /// jQuery Validate equivalent: required: true, date: true
    /// </summary>
    [Required(ErrorMessage = "Effective date is required")]
    public DateTime? EffectiveDate { get; set; }

    /// <summary>
    /// jQuery Validate equivalent: required: true (dropdown must be selected)
    /// </summary>
    [Required(ErrorMessage = "Please select a status")]
    public string? Status { get; set; }
}
