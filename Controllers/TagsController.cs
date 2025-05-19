using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization; // Для [Authorize]
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Dto;
using StackOverStadyApi.Models; // Для Tag и UserRole
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StackOverStadyApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TagsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public TagsController(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        // GET: api/Tags
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TagWithCountDto>>> GetAllTagsWithCount()
        {
            var tagsWithCount = await _context.Tags
                .AsNoTracking()
                .Select(t => new TagWithCountDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    QuestionCount = t.Questions.Count()
                })
                .OrderByDescending(t => t.QuestionCount)
                .ThenBy(t => t.Name)
                .ToListAsync();
            return Ok(tagsWithCount);
        }

        // GET: api/Tags/suggest?query=rea
        [HttpGet("suggest")]
        public async Task<ActionResult<IEnumerable<TagDto>>> SuggestTags([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(new List<TagDto>());
            }
            var normalizedQuery = query.Trim().ToLower();
            var suggestedTags = await _context.Tags
                .AsNoTracking()
                .Where(t => t.Name.ToLower().Contains(normalizedQuery))
                .Take(10)
                .OrderBy(t => t.Name)
                .ProjectTo<TagDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
            return Ok(suggestedTags);
        }

        // DELETE: api/Tags/{tagId}
        [HttpDelete("{tagId}")]
        [Authorize(Policy = "RequireModeratorRole")] // Или "RequireAdminRole", если только админы
        public async Task<IActionResult> DeleteTag(int tagId)
        {
            var tag = await _context.Tags
                .Include(t => t.Questions) // Загружаем связанные вопросы, чтобы проверить, используется ли тег
                .FirstOrDefaultAsync(t => t.Id == tagId);

            if (tag == null)
            {
                return NotFound(new { message = "Тег не найден." });
            }

            // Опциональная проверка: Запретить удаление, если тег используется
            /*
            if (tag.Questions.Any())
            {
                return BadRequest(new { message = $"Тег '{tag.Name}' используется в {tag.Questions.Count()} вопросах и не может быть удален." });
            }
            */

            // Если удаление разрешено даже если тег используется,
            // то связи в QuestionTags должны быть удалены каскадно (или вручную, если не настроено).
            // В твоем ApplicationDbContext для Question <-> Tag настроено .UsingEntity(j => j.ToTable("QuestionTags"));
            // По умолчанию, при удалении Tag, записи из QuestionTags, ссылающиеся на этот Tag, должны удаляться.

            try
            {
                _context.Tags.Remove(tag);
                await _context.SaveChangesAsync();
                return NoContent(); // Успешное удаление
            }
            catch (DbUpdateException ex)
            {
                // Логирование ошибки, особенно InnerException
                Console.WriteLine($"[DB ERROR DeleteTag ID: {tagId}]: {ex.InnerException?.Message ?? ex.Message}");
                return StatusCode(500, new { message = "Ошибка базы данных при удалении тега. Возможно, он все еще используется." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR DeleteTag ID: {tagId}]: {ex.ToString()}");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера при удалении тега." });
            }
        }
    }
}