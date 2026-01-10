using System;

namespace DiscordQuestGUI.Models
{
    public class CaptchaRequiredDetails
    {
        public string? Sitekey { get; set; }
        public string? Rqdata { get; set; }
        public string? Rqtoken { get; set; }
        public string? Service { get; set; }
        public string? SessionId { get; set; }
    }
}