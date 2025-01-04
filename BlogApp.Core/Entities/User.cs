namespace BlogApp.Core.Entities;

public class User : BaseEntity
{
    public string Username { get; set; }
    public string Image { get; set; }
    public string Name { get; set; }
    public string Surname { get; set; }
    public string PasswordHash { get; set; }
    public string Email { get; set; }
    public bool IsMale { get; set; }
    public int Role { get; set; } = 1;
}
