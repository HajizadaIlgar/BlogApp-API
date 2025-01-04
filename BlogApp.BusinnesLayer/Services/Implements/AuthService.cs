using AutoMapper;
using BlogApp.BusinnesLayer.DTOs.UserDTOs;
using BlogApp.BusinnesLayer.Exceptions.Common;
using BlogApp.BusinnesLayer.Exceptions.UserExceptions;
using BlogApp.BusinnesLayer.ExternalServices.Interfaces;
using BlogApp.BusinnesLayer.Helpers;
using BlogApp.BusinnesLayer.Services.Interfaces;
using BlogApp.Core.Entities;
using BlogApp.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BlogApp.BusinnesLayer.Services.Implements;

public class AuthService(IUserRepository _repo, IMapper _mapper, IJwtTokenHandler _tokenHandler) : IAuthService
{
    public async Task<string> LoginAsync(LoginDto dto)
    {
        User? user = null;
        if (dto.UserNameOrEmail.Contains('@'))
        {
            user = await _repo.GetAll().Where(x => x.Email == dto.UserNameOrEmail).FirstOrDefaultAsync();
        }
        else
        {
            user = await _repo.GetAll().Where(x => x.Username == dto.UserNameOrEmail).FirstOrDefaultAsync();
        }
        if (user == null)
            throw new NotFoundException<User>();
        if (!HashHelper.VerifyHashedPassword(user.PasswordHash, dto.Password))
            throw new IncorrectPasswordException("Password is wrong, please check again");
        if (dto.UserNameOrEmail is null)
            throw new NotFoundException<User>();

        return _tokenHandler.CreateToken(user, 36);
    }

    public async Task RegisterAsync(RegisterCreateDto dto)
    {
        var user = await _repo.GetAll().Where(x => x.Username == dto.Username || x.Email == dto.Email).FirstOrDefaultAsync();
        if (user is not null)
        {
            if (user.Email == dto.Email)
            {
                throw new ExistsException<User>("Email already using by another user");
            }

            else if (user.Username == dto.Username)
            {
                throw new ExistsException<User>("Username already using by another user");
            }
        }
        user = _mapper.Map<User>(dto);
        await _repo.AddAsync(user);
        await _repo.SaveAsync();
    }
}
