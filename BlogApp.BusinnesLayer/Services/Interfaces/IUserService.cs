namespace BlogApp.BusinnesLayer.Services.Interfaces;

public interface IUserService
{
    Task<string> CreateAsync();
    Task UserDeleteAsync(string username);
}
