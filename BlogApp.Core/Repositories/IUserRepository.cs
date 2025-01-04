using BlogApp.Core.Entities;

namespace BlogApp.Core.Repositories;

public interface IUserRepository : IGenericRepository<User>
{
    User GetCurrentUser();
    int GetCurrentUserId();
    Task<User> GetByUserName(string userName);
}
