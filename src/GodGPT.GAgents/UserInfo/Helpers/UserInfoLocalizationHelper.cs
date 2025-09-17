using Aevatar.Application.Grains.UserInfo.Enums;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserInfo.Dtos;

namespace Aevatar.Application.Grains.UserInfo.Helpers;

/// <summary>
/// Helper class for managing multi-language text for user info options
/// </summary>
public static class UserInfoLocalizationHelper
{
    /// <summary>
    /// Get localized text for seeking interest based on language
    /// </summary>
    public static string GetSeekingInterestText(SeekingInterestEnum interest, GodGPTLanguage language)
    {
        return language switch
        {
            GodGPTLanguage.English => GetEnglishSeekingInterestText(interest),
            GodGPTLanguage.TraditionalChinese => GetTraditionalChineseSeekingInterestText(interest),
            GodGPTLanguage.Spanish => GetSpanishSeekingInterestText(interest),
            _ => GetEnglishSeekingInterestText(interest) // Default to English
        };
    }

    /// <summary>
    /// Get localized text for source channel based on language
    /// </summary>
    public static Tuple<string, string> GetSourceChannelText(SourceChannelEnum channel, GodGPTLanguage language)
    {
        return language switch
        {
            GodGPTLanguage.English => GetEnglishSourceChannelText(channel),
            GodGPTLanguage.TraditionalChinese => GetTraditionalChineseSourceChannelText(channel),
            GodGPTLanguage.Spanish => GetSpanishSourceChannelText(channel),
            _ => GetEnglishSourceChannelText(channel) // Default to English
        };
    }
    

    #region English Text Methods

    private static string GetEnglishSeekingInterestText(SeekingInterestEnum interest)
    {
        return interest switch
        {
            SeekingInterestEnum.Companionship => "Companionship",
            SeekingInterestEnum.SelfDiscovery => "Self-discovery",
            SeekingInterestEnum.SpiritualGrowth => "Spiritual growth",
            SeekingInterestEnum.LoveAndRelationships => "Love & relationships",
            SeekingInterestEnum.DailyFortuneTelling => "Daily fortune telling",
            SeekingInterestEnum.CareerGuidance => "Career guidance",
            _ => "Unknown"
        };
    }

    private static Tuple<string, string> GetEnglishSourceChannelText(SourceChannelEnum channel)
    {
        return channel switch
        {
            SourceChannelEnum.AppStorePlayStore => new ("App Store / Play Store", ""),
            SourceChannelEnum.SocialMedia => new ("Social media", "(Instagram, TikTok, X/Twitter, LinkedIn, Facebook)"),
            SourceChannelEnum.SearchEngine => new ("Search engine", ""),
            SourceChannelEnum.FriendReferral => new ("Friend referral", ""),
            SourceChannelEnum.EventConference => new ("Event / conference", ""),
            SourceChannelEnum.Advertisement => new ("Advertisement", "(online ad, banner, etc.)"),
            SourceChannelEnum.Other => new ("Other", ""),
            _ => new ("Unknown", "")
        };
    }

    #endregion

    #region Traditional Chinese Text Methods

    private static string GetTraditionalChineseSeekingInterestText(SeekingInterestEnum interest)
    {
        return interest switch
        {
            SeekingInterestEnum.Companionship => "夥伴關係",
            SeekingInterestEnum.SelfDiscovery => "自我探索",
            SeekingInterestEnum.SpiritualGrowth => "靈性成長",
            SeekingInterestEnum.LoveAndRelationships => "愛情與人際",
            SeekingInterestEnum.DailyFortuneTelling => "每日運勢占卜",
            SeekingInterestEnum.CareerGuidance => "職涯指引",
            _ => "未知"
        };
    }

    private static Tuple<string, string> GetTraditionalChineseSourceChannelText(SourceChannelEnum channel)
    {
        return channel switch
        {
            SourceChannelEnum.AppStorePlayStore => new ("App Store／Play 商店", ""),
            SourceChannelEnum.SocialMedia => new ("社群媒體", "(Instagram, TikTok, X/Twitter, LinkedIn, Facebook)"),
            SourceChannelEnum.SearchEngine => new ("搜尋引擎", ""),
            SourceChannelEnum.FriendReferral => new ("朋友推薦", ""),
            SourceChannelEnum.EventConference => new ("活動／會議", ""),
            SourceChannelEnum.Advertisement => new ("廣告", "(線上廣告、橫幅等)"),
            SourceChannelEnum.Other => new ("其他", ""),
            _ => new ("未知", "")
        };
    }

    #endregion

    #region Spanish Text Methods

    private static string GetSpanishSeekingInterestText(SeekingInterestEnum interest)
    {
        return interest switch
        {
            SeekingInterestEnum.Companionship => "Compañía",
            SeekingInterestEnum.SelfDiscovery => "Autodescubrimiento",
            SeekingInterestEnum.SpiritualGrowth => "Crecimiento espiritual",
            SeekingInterestEnum.LoveAndRelationships => "Amor y relaciones",
            SeekingInterestEnum.DailyFortuneTelling => "Horóscopo diario",
            SeekingInterestEnum.CareerGuidance => "Orientación profesional",
            _ => "Desconocido"
        };
    }

    private static Tuple<string, string> GetSpanishSourceChannelText(SourceChannelEnum channel)
    {
        return channel switch
        {
            SourceChannelEnum.AppStorePlayStore => new ("Tienda de Aplicaciones / Tienda Play", ""),
            SourceChannelEnum.SocialMedia => new ("Redes sociales", "(Instagram, TikTok, X/Twitter, LinkedIn, Facebook)"),
            SourceChannelEnum.SearchEngine => new ("Motor de búsqueda", ""),
            SourceChannelEnum.FriendReferral => new ("Recomendación de amigo", ""),
            SourceChannelEnum.EventConference => new ("Evento / conferencia", ""),
            SourceChannelEnum.Advertisement => new ("Publicidad", "(Anuncio en línea, banner, etc.)"),
            SourceChannelEnum.Other => new ("Otro", ""),
            _ => new ("Otro", "")
        };
    }

    #endregion


    /// <summary>
    /// Validate if the provided codes are valid seeking interest enum codes
    /// </summary>
    public static bool IsValidSeekingInterestEnumCodes(List<int> codes)
    {
        var validCodes = Enum.GetValues<SeekingInterestEnum>().Select(x => (int)x).ToHashSet();
        return codes.All(code => validCodes.Contains(code));
    }

    /// <summary>
    /// Validate if the provided codes are valid source channel enum codes
    /// </summary>
    public static bool IsValidSourceChannelEnumCodes(List<int> codes)
    {
        var validCodes = Enum.GetValues<SourceChannelEnum>().Select(x => (int)x).ToHashSet();
        return codes.All(code => validCodes.Contains(code));
    }

    /// <summary>
    /// Get all seeking interest options with localized text
    /// </summary>
    public static List<SeekingInterestOptionDto> GetSeekingInterestEnumOptions(GodGPTLanguage language)
    {
        var options = new List<SeekingInterestOptionDto>();
        foreach (SeekingInterestEnum interest in Enum.GetValues<SeekingInterestEnum>())
        {
            options.Add(new SeekingInterestOptionDto
            {
                Code = (int)interest,
                Text = GetSeekingInterestText(interest, language)
            });
        }
        return options;
    }

    /// <summary>
    /// Get all source channel options with localized text
    /// </summary>
    public static List<SourceChannelOptionDto> GetSourceChannelEnumOptions(GodGPTLanguage language)
    {
        var options = new List<SourceChannelOptionDto>();
        foreach (SourceChannelEnum channel in Enum.GetValues<SourceChannelEnum>())
        {
            var (text, desc) = GetSourceChannelText(channel, language);
            options.Add(new SourceChannelOptionDto
            {
                Code = (int)channel,
                Text = text,
                Desc = desc
            });
        }
        return options;
    }
}
