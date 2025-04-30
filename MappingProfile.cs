using AutoMapper;
using StackOverStadyApi.Controllers;
using StackOverStadyApi.Models;

namespace StackOverStadyApi.Models
{
    // MappingProfile.cs
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // User -> UserInfoDto (уже есть)
            CreateMap<User, QuestionsController.UserInfoDto>();
            // Tag -> TagDto (уже есть)
            CreateMap<Tag, QuestionsController.TagDto>();
            CreateMap<User, CommentsController.UserInfoDto>();


            // Answer -> AnswerDto
            CreateMap<Answer, QuestionsController.AnswerDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author)); // Маппинг автора

            // Question -> QuestionDto (для списка)
            CreateMap<Question, QuestionsController.QuestionDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags))
                .ForMember(dest => dest.AnswerCount, opt => opt.MapFrom(src => src.Answers.Count)); // Считаем ответы

            // Question -> QuestionDetailDto (для детального просмотра)
            CreateMap<Question, QuestionsController.QuestionDetailDto>()
                .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author))
                .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.Tags))
                .ForMember(dest => dest.Answers, opt => opt.MapFrom(src => src.Answers)); // Маппим список ответов


            // Если еще не было
            CreateMap<Comment, CommentsController.CommentDto>()
              .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
        }
    }
}