using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Dto;  // For TagWithCountDto
using StackOverStadyApi.Models; // For Tag
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
        // Получение списка всех тегов с количеством вопросов
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TagWithCountDto>>> GetAllTagsWithCount()
        {
            var tagsWithCount = await _context.Tags
                .AsNoTracking()
                .Select(t => new TagWithCountDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    QuestionCount = t.Questions.Count() // EF Core can translate this
                })
                .OrderByDescending(t => t.QuestionCount) // Optional: sort by popularity
                .ThenBy(t => t.Name)
                .ToListAsync();

            return Ok(tagsWithCount);
        }

        // GET: api/Tags/suggest?query=rea
        // Получение предложений тегов для автодополнения
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
                .Take(10) // Limit suggestions
                .OrderBy(t => t.Name)
                .ProjectTo<TagDto>(_mapper.ConfigurationProvider) // Use AutoMapper projection
                .ToListAsync();

            return Ok(suggestedTags);
        }


        // GET: api/Tags/{name}/questions - Already handled by QuestionsController filtering
        // No need for a new endpoint here if QuestionsController's GET /api/Questions?tags=... is sufficient.
        // If you wanted a dedicated one, it would look something like:
        /*
        [HttpGet("{tagName}/questions")]
        public async Task<ActionResult<PaginatedResult<QuestionDto>>> GetQuestionsByTagName(
            string tagName,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            // This logic would be very similar to QuestionsController.GetAllQuestions
            // but pre-filtered by a single tag name.
            // For simplicity, we'll reuse QuestionsController.GetAllQuestions
            // by having the frontend navigate to /?tags=tagName
            return Ok("This endpoint can be implemented if needed, but filtering QuestionsController is preferred.");
        }
        */
    }
}