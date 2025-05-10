using AutoMapper;
using StackOverStadyApi.Models;      // Для ваших моделей: User, Tag, Answer, Question, Comment
using StackOverStadyApi.Controllers; // Для DTO, которые вложены в контроллеры
using System.Linq;                   // Для использования .Any()

// Рекомендуется размещать профили маппинга в отдельном неймспейсе,
// например: namespace StackOverStadyApi.Mappings
// Однако, я оставлю ваш текущий неймспейс, чтобы соответствовать вашему коду.
namespace StackOverStadyApi.Models // или StackOverStadyApi.Mappings, если решишь перенести
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // --- User DTOs ---
            // Если UserInfoDto используется из нескольких контроллеров и идентичен,
            // лучше вынести его в общее пространство имен (например, StackOverStadyApi.Dtos)
            // и мапить User на этот общий UserInfoDto.
            // Пока оставляю как есть, если они действительно разные или так задумано.
            CreateMap<User, QuestionsController.UserInfoDto>();
            CreateMap<User, CommentsController.UserInfoDto>(); // Убедись, что это не один и тот же DTO по структуре


            // --- Tag DTO ---
            // Предположим, TagDto из QuestionsController используется для QuestionDto
            // Если у тебя есть глобальный Dtos.TagDto, то маппинг должен быть на него.
            CreateMap<Tag, TagDto>(); // Используем TagDto из QuestionsController для консистентности с QuestionDto
                                                          // Если есть глобальный Dtos.TagDto, замени на него: CreateMap<Tag, Dtos.TagDto>();


            // --- Answer DTO ---
            // Маппинг на AnswerDto из QuestionsController
            CreateMap<Answer, QuestionsController.AnswerDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author));


            // --- Question DTOs ---
            // Маппинг на QuestionDto из QuestionsController
            CreateMap<Question, QuestionsController.QuestionDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags)) // AutoMapper применит CreateMap<Tag, QuestionsController.TagDto>()
                .ForMember(dest => dest.AnswerCount, opt => opt.MapFrom(src => src.Answers.Count))
                // .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Rating)) // Это поле обычно мапится по имени, если совпадает
                .ForMember(dest => dest.HasAcceptedAnswer, opt => opt.MapFrom(src => src.Answers.Any(a => a.IsAccepted))); // <--- ИСПРАВЛЕНИЕ: Добавлено вычисление HasAcceptedAnswer

            // Маппинг на QuestionDetailDto из QuestionsController
            CreateMap<Question, QuestionsController.QuestionDetailDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags)) // AutoMapper применит CreateMap<Tag, QuestionsController.TagDto>()
                .ForMember(dest => dest.Answers, opt => opt.MapFrom(src => src.Answers)) // AutoMapper применит CreateMap<Answer, QuestionsController.AnswerDto>()
                                                                                         // .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Rating)) // Аналогично, мапится по имени
                                                                                         // Если в QuestionDetailDto тоже нужно поле HasAcceptedAnswer, добавь его аналогично:
                                                                                         // .ForMember(dest => dest.HasAcceptedAnswer, opt => opt.MapFrom(src => src.Answers.Any(a => a.IsAccepted)))
                ;


            // --- Comment DTO ---
            // Маппинг на CommentDto из CommentsController
            CreateMap<Comment, CommentsController.CommentDto>()
              .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User)); // AutoMapper применит CreateMap<User, CommentsController.UserInfoDto>()
        }
    }
}