using System.ComponentModel.DataAnnotations;

namespace ScanGo.Api.Features.Auth;

// ========== Requests ==========

public class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Required]
    public bool TermsAccepted { get; set; }

    [Required]
    public bool PrivacyAccepted { get; set; }

    public string? Device { get; set; }
    public string? Platform { get; set; }       // "web" | "mobile"
}

public class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public string? Device { get; set; }
    public string? Platform { get; set; }
}

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";

    public string? Device { get; set; }
    public string? Platform { get; set; }
}

public class LogoutRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = "";

    public string? Device { get; set; }
    public string? Platform { get; set; }
}

public class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = "";
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
}

public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";
}

public class ChangePasswordRequest
{
    [Required]
    public string OldPassword { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";
}

public class DeleteAccountRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

// ========== Responses ==========

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Status { get; set; } = "";
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuthResponse
{
    public UserDto User { get; set; } = new();
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }          // seconds
}
