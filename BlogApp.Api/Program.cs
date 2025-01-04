using BlogApp.Api;
using BlogApp.BusinnesLayer;
using BlogApp.BusinnesLayer.ExternalServices.Implements;
using BlogApp.BusinnesLayer.ExternalServices.Interfaces;
using BlogApp.BusinnesLayer.Services.Abstracts;
using BlogApp.BusinnesLayer.Services.Implements;
using BlogApp.BusinnesLayer.Services.Interfaces;
using BlogApp.DAL;
using BlogApp.DAL.DALs;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "My API",
        Version = "v1"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please insert JWT with Bearer into field",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
   {
     new OpenApiSecurityScheme
     {
       Reference = new OpenApiReference
       {
         Type = ReferenceType.SecurityScheme,
         Id = "Bearer"
       }
      },
      new string[] { }
    }
  });
});
builder.Services.AddService();
builder.Services.AddDbContext<BlogAppDbContext>(option =>
{
    option.UseSqlServer(builder.Configuration.GetConnectionString("MySqlHome"));
});
builder.Services.AddService();
builder.Services.AddFluentValidation();
builder.Services.AddAutoMapper();
builder.Services.AddAuthentication(builder.Configuration);
builder.Services.AddJwtOptions(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IJwtTokenHandler, JwtTokenHandler>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(x =>
    {
        x.EnablePersistAuthorization();
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
