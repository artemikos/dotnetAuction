using Microsoft.AspNetCore.Mvc;
using Auction.Data;
using Auction.Models;
using Auction.Services;

namespace Auction.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuctionDbContext _context;
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuctionDbContext context, AuthService authService, ILogger<AuthController> logger)
    {
        _context     = context;
        _authService = authService;
        _logger      = logger;
    }

    /// <summary>Регистрация по университетскому e-mail (.edu).</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { message = "All fields are required" });

        if (!AuthService.IsValidUniversityEmail(request.Email))
            return BadRequest(new { message = "Only university emails (.edu) are allowed" });

        if (request.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters" });

        if (_context.Users.Any(u => u.Email.ToLower() == request.Email.ToLower()))
            return Conflict(new { message = "Email is already registered" });

        var user = new User
        {
            Email        = request.Email.ToLower(),
            DisplayName  = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt    = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = _authService.GenerateToken(user.Id, user.Email, user.DisplayName);
        _logger.LogInformation("New user registered: {Email}", user.Email);

        return Ok(new
        {
            token,
            user = new { id = user.Id, email = user.Email, displayName = user.DisplayName }
        });
    }

    /// <summary>Вход по e-mail и паролю, возвращает JWT.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required" });

        var user = _context.Users.FirstOrDefault(u => u.Email.ToLower() == request.Email.ToLower());
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        var token = _authService.GenerateToken(user.Id, user.Email, user.DisplayName);
        _logger.LogInformation("User logged in: {Email}", user.Email);

        return Ok(new
        {
            token,
            user = new { id = user.Id, email = user.Email, displayName = user.DisplayName }
        });
    }
}

public class RegisterRequest
{
    public string Email       { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password    { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
