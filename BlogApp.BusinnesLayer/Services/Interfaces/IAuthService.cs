using BlogApp.BusinnesLayer.DTOs.UserDTOs;

namespace BlogApp.BusinnesLayer.Services.Interfaces;

public interface IAuthService
{
    Task RegisterAsync(RegisterCreateDto dto);
    Task<string> LoginAsync(LoginDto dto);
}
