// src/Dto/UserAchievementDto.cs
using System;

namespace StackOverStadyApi.Dto
{
    public class AchievementInfoDto // DTO для информации об ачивке
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconName { get; set; }
        public string Code { get; set; }
    }

    public class UserAchievementDto
    {
        public AchievementInfoDto Achievement { get; set; }
        public DateTime AwardedAt { get; set; }
    }
}