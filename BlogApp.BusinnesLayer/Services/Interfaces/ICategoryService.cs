using BlogApp.BusinnesLayer.DTOs.CategoryDTOs;

namespace BlogApp.BusinnesLayer.Services.Interfaces;

public interface ICategoryService
{
    Task<IEnumerable<CategoryListItem>> GetAllAsync();
    Task<int> CreateAsync(CategoryCreateDto dto);
}
