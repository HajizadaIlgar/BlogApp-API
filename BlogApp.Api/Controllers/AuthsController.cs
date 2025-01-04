using BlogApp.BusinnesLayer.DTOs.UserDTOs;
using BlogApp.BusinnesLayer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BlogApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthsController(IAuthService _service, IUserService _ser) : ControllerBase
    {
        [HttpPost("[action]")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            return Ok(await _service.LoginAsync(dto));
        }
        [HttpPost("[action]")]
        public async Task<IActionResult> Register(RegisterCreateDto dto)
        {
            await _service.RegisterAsync(dto);
            return Created();
        }
        [HttpDelete]
        public async Task<IActionResult> Delete(string username)
        {
            await _ser.UserDeleteAsync(username);
            return NoContent();
        }
    }
}
