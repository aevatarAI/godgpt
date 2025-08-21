
namespace Aevatar.Application.Grains.Agents.ChatManager.Common;

/// <summary>
/// GodGPT language enumeration for chat sessions
/// </summary>
public enum GodGPTLanguage
{
    /// <summary>
    /// English language
    /// </summary>
    English = 0,
    
    /// <summary>
    /// Traditional Chinese language
    /// </summary>
    TraditionalChinese = 1,
    
    /// <summary>
    /// Spanish language
    /// </summary>
    Spanish = 2,
    CN = 3
}

/// <summary>
/// Helper class for retrieving GodGPT language from RequestContext
/// </summary>
public static class GodGPTLanguageHelper
{
    private const string GodGPTLanguageKey = "GodGPTLanguage";
    private const string IsCN = "IsCN";
    public static bool CheckClientIsCNFromContext()
    {
        try
        {
            var context = RequestContext.Get(IsCN);
            if (context != null && context is bool isCN)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Log error if needed, but return default English
            return false;
        }
        
        // Return English as default when language cannot be retrieved or on exception
        return false;
    }
    /// <summary>
    /// Gets the GodGPT language from RequestContext with error handling
    /// Returns English as default if language cannot be retrieved or on exception
    /// </summary>
    /// <returns>GodgptLanguage enum value, defaults to English on error</returns>
    public static GodGPTLanguage GetGodGPTLanguageFromContext()
    {
        try
        {
            var context = RequestContext.Get(GodGPTLanguageKey);
            if (context != null && context is string languageString)
            {
                if (Enum.TryParse<GodGPTLanguage>(languageString, true, out var language))
                {
                    return language;
                }
            }
        }
        catch (Exception)
        {
            // Log error if needed, but return default English
        }
        
        // Return English as default when language cannot be retrieved or on exception
        return GodGPTLanguage.English;
    }
    
    /// <summary>
    /// Sets the GodGPT language in RequestContext
    /// </summary>
    /// <param name="language">Language to set in context</param>
    public static void SetGodgptLanguageInContext(GodGPTLanguage language)
    {
        try
        {
            RequestContext.Set(GodGPTLanguageKey, language.ToString());
        }
        catch (Exception)
        {
            // Handle exception if needed
        }
    }
    public static string AppendLanguagePrompt(this string message, GodGPTLanguage language)
    {
        var promptMsg = message;

        /*promptMsg += language switch
        {
            GodGPTLanguage.English => ".Requirement: Please reply in English.",
            GodGPTLanguage.TraditionalChinese => ".Requirement: Please reply in Chinese.",
            GodGPTLanguage.Spanish => ".Requirement: Please reply in Spanish.",
            _ => ".Requirement: Please reply in English."
        };*/

        return promptMsg;
    }
} 