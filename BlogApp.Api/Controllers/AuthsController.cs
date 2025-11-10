using BlogApp.BusinnesLayer.DTOs.UserDTOs;
using BlogApp.BusinnesLayer.DTOs.UserDTOs.BalanceDTO;
using BlogApp.BusinnesLayer.Services.Interfaces;
using BlogApp.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlogApp.Api.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AuthsController(IAuthService _service, IUserService _ser, IEmailService _emailService) : ControllerBase
    {
        [AllowAnonymous]
        [HttpPost()]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var token = await _service.LoginAsync(dto);

            // 🔥 Token-i HttpOnly Cookie-yə yazırıq
            Response.Cookies.Append("AuthToken", token, new CookieOptions
            {
                HttpOnly = false,       // JS oxuya bilmir → təhlükəsizlik
                Secure = false,         // HTTPS varsa
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddHours(24) // ✅ 24 saat aktiv
            });

            return Ok(token);
        }

        [AllowAnonymous]
        [HttpPost()]
        public async Task<IActionResult> Register(RegisterCreateDto dto)
        {
            await _service.RegisterAsync(dto);
            return Ok("Registerasiya olundu");
        }

        [HttpDelete]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Delete(string username)
        {
            await _ser.UserDeleteAsync(username);
            return NoContent();
        }

        [HttpGet]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> GetByUserName(string username)
        {
            return Ok(await _service.GetByUserNameAsync(username));
        }


        [HttpPut]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> GetByUserId(int id)
        {
            return Ok(await _service.GetByUserIdAsync(id));
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> BalanceUpdate(BalanceDto balance)
        {
            await _service.UpdateBalance(balance.Id, balance.Amout);
            return Ok("Balansa" + " " + balance.Amout + " " + "coin elave olundu");
        }
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentUser()
        {
            var username = User.Identity?.Name;
            if (username == null)
                return BadRequest();
            var user = await _service.GetByUserNameAsync(username);
            return Ok(new
            {
                username = user.UserName,
                email = user.Email,
                Role = user.Role,
                Balance = user.Balance,
                isAdmin = user.Role == 1,
            });
        }
        [HttpGet]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> GetAllUser()
        {
            return Ok(await _service.GetAllAsync());
        }


        [HttpPost("forget-password")]
        public async Task<IActionResult> ForgetPassword([FromBody] ForgetPasswordRequest request)
        {
            var user = await _service.GetByEmailAsync(request.Email);
            if (user == null) return BadRequest(new { Message = "Email tapılmadı." });

            // Token yarat
            user.PasswordResetToken = Guid.NewGuid().ToString();
            user.TokenExpiry = DateTime.UtcNow.AddHours(1);

            // Update
            await _ser.Update(user);
            await _ser.SaveChangesAsync();

            // Email göndər (link: frontend/reset-password?token=...)
            var resetLink = $"https://yourfrontend.com/reset-password?token={user.PasswordResetToken}&email={user.Email}";
            await _emailService.SendPasswordResetEmail(user.Email, resetLink);

            return Ok(new { Message = "Şifrə sıfırlama linki emailinə göndərildi." });
        }


        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var success = await _service.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);

            if (!success)
                return BadRequest(new { Message = "Token səhvdir və ya müddəti bitib." });

            return Ok(new { Message = "Şifrə uğurla dəyişdirildi." });
        }

    }
}
