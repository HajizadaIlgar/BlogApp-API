using BlogApp.BusinnesLayer.Exceptions.Common;
using BlogApp.BusinnesLayer.Services.Interfaces;
using BlogApp.Core.Entities;
using BlogApp.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BlogApp.BusinnesLayer.Services.Implements;

public class UserService(IUserRepository _repo) : IUserService
{
    public Task<string> CreateAsync()
    {
        throw new NotImplementedException();
    }

    public async Task UserDeleteAsync(string username)
    {
        var user = await _repo.GetAll().Where(x => x.Username == username).FirstOrDefaultAsync();
        if (user is null)
            throw new NotFoundException<User>();
        await _repo.DeleteAsync(user.Id);
    }
}
