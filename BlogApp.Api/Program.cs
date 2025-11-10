using BlogApp.Api;
using BlogApp.Api.Hubs;
using BlogApp.Api.Hubs.Services;
using BlogApp.BusinnesLayer;
using BlogApp.BusinnesLayer.ExternalServices.Implements;
using BlogApp.BusinnesLayer.ExternalServices.Interfaces;
using BlogApp.BusinnesLayer.Helpers;
using BlogApp.BusinnesLayer.Services.Abstracts;
using BlogApp.BusinnesLayer.Services.Implements;
using BlogApp.BusinnesLayer.Services.Interfaces;
using BlogApp.Core.Entities;
using BlogApp.Core.Enums;
using BlogApp.DAL;
using BlogApp.DAL.DALs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://192.168.100.52:5173",
                "http://localhost:5173",
                "https://localhost:5173",
                "http://localhost:5173",
                "https://localhost:5173",
                "http://192.168.100.26:5173"
            ).AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // SignalR v? token üçün vacib
    });
});

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
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddService();

builder.Services.AddDbContext<BlogAppDbContext>(option =>
{
    option.UseSqlServer(builder.Configuration.GetConnectionString("MYSqlHome"));
});

// SignalR konfiqurasiyas?
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true; // Development üçün detailed errors
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB max mesaj ölçüsü
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
       policy.RequireClaim(ClaimTypes.Role, "1"));
});

builder.Services.AddFluentValidation();
builder.Services.AddAutoMapper();

// JWT Authentication konfiqurasiyas?
builder.Services.AddAuthentication(builder.Configuration);
builder.Services.AddJwtOptions(builder.Configuration);

// ? JWT Bearer Events - H?M AdminChatHub H?M d? LotoHub üçün
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // SignalR üçün query string-d?n token oxu
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // ? DÜZ?LI?: H?M adminChatHub H?M d? lotoHub üçün
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/adminChatHub") ||
                 path.StartsWithSegments("/lotoHub") ||
                 path.StartsWithSegments("/dominohub")))
            {
                context.Token = accessToken;
                Console.WriteLine($"? Token set for SignalR: {path}");
            }

            return Task.CompletedTask;
        },

        // ? Authentication failed zaman? log
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"? JWT Authentication failed: {context.Exception.Message}");
            Console.WriteLine($"   Path: {context.HttpContext.Request.Path}");
            return Task.CompletedTask;
        },

        // ? Token validated zaman? log
        OnTokenValidated = context =>
        {
            var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = context.Principal?.Identity?.Name;
            Console.WriteLine($"? JWT Token validated: User={userName}, ID={userId}");
            return Task.CompletedTask;
        }
    };
});

// CORS - ?g?r frontend ayr? domain-d?dirs?
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("AllowChatClients", policy =>
//    {
//        policy.WithOrigins(
//                "http://localhost:3000",
//                "http://192.168.100.52:5173",
//                "http://localhost:5173",
//                "https://localhost:5173"
//            )
//              .AllowAnyHeader()
//              .AllowAnyMethod()
//              .AllowCredentials(); // SignalR üçün vacibdir
//    });
//});

builder.Services.AddHttpContextAccessor();

// Services
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtTokenHandler, JwtTokenHandler>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<AutoLotoService>();
builder.Services.AddSingleton<LotoRoomManager>();
builder.Services.AddSingleton<DominoRoomManager>();
builder.Services.AddSingleton<OkeyRoomManager>();

var app = builder.Build();

// Seed Users
using (var scope = app.Services.CreateScope())
{
    var _context = scope.ServiceProvider.GetRequiredService<BlogAppDbContext>();
    var seedUsersSection = builder.Configuration.GetSection("SeedUsers");
    var seedUsers = seedUsersSection.Get<List<UserSeed>>();

    if (seedUsers != null && seedUsers.Any())
    {
        foreach (var u in seedUsers)
        {
            if (!_context.Users.Any(x => x.Email == u.Email))
            {
                var user = new User
                {
                    UserName = u.UserName,
                    Image = "admin.jpg",
                    Name = "AdminAdmin",
                    Surname = "A",
                    Email = u.Email,
                    PasswordHash = HashHelper.HashPassword(u.Password),
                    Role = u.Role,
                    CreateDate = DateTime.Now,
                    IsMale = true,
                    Balance = 0
                };
                await _context.Users.AddAsync(user);
            }
        }
        await _context.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(x =>
    {
        x.EnablePersistAuthorization();
    });
}

app.UseHttpsRedirection();

// ? CORS - Middleware order vacibdir!
app.UseCors("AllowFrontend");

// Static Files - Default wwwroot
app.UseStaticFiles();
app.UseDefaultFiles();
// Chat ??kill?ri üçün ?lav? static files middleware
var uploadsPath = Path.Combine(builder.Environment.WebRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

var chatUploadsPath = Path.Combine(uploadsPath, "chat");
if (!Directory.Exists(chatUploadsPath))
{
    Directory.CreateDirectory(chatUploadsPath);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        // Cache control headers
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800");
    }
});

app.UseRouting();

// ? CRITICAL: Authentication UseAuthorization-dan ?VV?L olmal?d?r
app.UseAuthentication();
app.UseAuthorization();

//app.UseMiddleware<SingleSessionMiddleware>();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();

    // SignalR Hub mapping
    endpoints.MapHub<AdminChatHub>("/adminChatHub");
    endpoints.MapHub<LotoHub>("/lotoHub");
    endpoints.MapHub<DominoHub>("/dominoHub");
    endpoints.MapHub<OkeyHub>("/okeyHub");
});

Console.WriteLine("?? Server ba?lad?!");
Console.WriteLine("?? SignalR Hubs:");
Console.WriteLine("   - /adminChatHub");
Console.WriteLine("   - /lotoHub");

var cleanupTimer = new System.Threading.Timer(async _ =>
{
    try
    {
        var roomManager = app.Services.GetRequiredService<LotoRoomManager>();
        roomManager.CleanupEmptyRooms();

        var roomCount = roomManager.GetRoomCount();
        var playerCount = roomManager.GetTotalPlayers();

        Console.WriteLine($"?? Cleanup completed: {roomCount} rooms, {playerCount} players online");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"? Cleanup error: {ex.Message}");
    }
}, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

Console.WriteLine("?? LOTO Server is running...");

app.Run();