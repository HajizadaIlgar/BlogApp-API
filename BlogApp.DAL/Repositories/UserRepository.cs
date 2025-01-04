using BlogApp.Core.Entities;
using BlogApp.Core.Repositories;
using BlogApp.DAL.DALs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BlogApp.DAL.Repositories;

public class UserRepository : GenericRepository<User>, IUserRepository
{
    readonly HttpContext _httpContext;
    readonly BlogAppDbContext _context;
    public UserRepository(BlogAppDbContext context, IHttpContextAccessor httpcontext) : base(context)
    {
        _context = context;
        httpcontext.HttpContext = _httpContext;
    }

    public async Task<User?> GetByUserName(string userName)
    {
        return await _context.Users.Where(x => x.Username == userName).FirstOrDefaultAsync();
    }

    public User GetCurrentUser()
    {
        throw new NotImplementedException();
    }

    public int GetCurrentUserId()
    {
        throw new NotImplementedException();
    }
}
