using BlogApp.DAL;
using BlogApp.DAL.DALs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddService();
builder.Services.AddDbContext<BlogAppDbContext>(option =>
{
    option.UseSqlServer(builder.Configuration.GetConnectionString("MySqlHome"));
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
