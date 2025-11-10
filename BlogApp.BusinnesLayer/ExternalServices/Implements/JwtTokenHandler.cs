using BlogApp.BusinnesLayer.DTOs.Options;
using BlogApp.BusinnesLayer.ExternalServices.Interfaces;
using BlogApp.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BlogApp.BusinnesLayer.ExternalServices.Implements;

public class JwtTokenHandler : IJwtTokenHandler
{
    private readonly JwtOptions opt;
    public JwtTokenHandler(IOptions<JwtOptions> _opt)
    {
        opt = _opt.Value;
    }
    public string CreateToken(User user, int hours = 1440)
    {
        List<Claim> claims = [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name,user.UserName),
            new Claim(ClaimTypes.Email,user.Email),
            new Claim(ClaimTypes.Role,user.Role.ToString()),
            new Claim(ClaimTypes.GivenName,user.Name+""+user.Surname),
            ];
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SecretKey));
        SigningCredentials credential = new(key, SecurityAlgorithms.HmacSha256);
        JwtSecurityToken secToken = new(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims = claims,
            notBefore: DateTime.Now,
            expires: DateTime.Now.AddSeconds(hours),
            signingCredentials: credential
            );
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(secToken);
    }
}
