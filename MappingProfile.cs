using AutoMapper;
using StackOverStadyApi.Models;      // Для ваших моделей: User, Tag, Answer, Question, Comment
using StackOverStadyApi.Controllers; // Для DTO, которые все еще вложены в контроллеры

// Рекомендуется размещать профили маппинга в отдельном неймспейсе,
// например: namespace StackOverStadyApi.Mappings
// Однако, я оставлю ваш текущий неймспейс, чтобы соответствовать вашему коду.
namespace StackOverStadyApi.Models
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // --- User DTOs ---
            // Предполагается, что UserInfoDto все еще определен внутри QuestionsController и CommentsController
            CreateMap<User, QuestionsController.UserInfoDto>();
            CreateMap<User, CommentsController.UserInfoDto>();


            // --- Tag DTO ---
            // Теперь маппинг идет на общий TagDto из пространства имен StackOverStadyApi.Dtos
            CreateMap<Tag, TagDto>();


            // --- Answer DTO ---
            // Предполагается, что AnswerDto все еще определен внутри QuestionsController
            CreateMap<Answer, QuestionsController.AnswerDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author)); // Маппинг автора


            // --- Question DTOs ---
            // Предполагается, что QuestionDto и QuestionDetailDto все еще определены внутри QuestionsController
            // Их свойства List<TagDto> должны теперь использовать общий StackOverStadyApi.Dtos.TagDto
            CreateMap<Question, QuestionsController.QuestionDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags)) // AutoMapper применит CreateMap<Tag, TagDto>() для каждого элемента
                .ForMember(dest => dest.AnswerCount, opt => opt.MapFrom(src => src.Answers.Count))
                .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Rating)); // Добавлено, т.к. есть в DTO

            CreateMap<Question, QuestionsController.QuestionDetailDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags))     // AutoMapper применит CreateMap<Tag, TagDto>() для каждого элемента
                .ForMember(dest => dest.Answers, opt => opt.MapFrom(src => src.Answers)) // AutoMapper применит CreateMap<Answer, QuestionsController.AnswerDto>() для каждого элемента
                .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Rating));   // Добавлено, т.к. есть в DTO


            // --- Comment DTO ---
            // Предполагается, что CommentDto все еще определен внутри CommentsController
            CreateMap<Comment, CommentsController.CommentDto>()
              .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User)); // AutoMapper применит CreateMap<User, CommentsController.UserInfoDto>()
        }
    }
}