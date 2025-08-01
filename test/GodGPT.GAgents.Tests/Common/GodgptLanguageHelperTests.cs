using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Shouldly;

namespace GodGPT.GAgents.Tests.Common;

public class GodgptLanguageHelperTests
{
    [Fact]
    public void GetGodgptLanguageFromContext_WhenNoContext_ShouldReturnEnglish()
    {
        // Arrange - Clear any existing context
        RequestContext.Clear();
        
        // Act
        var result = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // Assert
        result.ShouldBe(GodGPTLanguage.English);
    }
    
    [Fact]
    public void GetGodgptLanguageFromContext_WhenContextHasEnglish_ShouldReturnEnglish()
    {
        // Arrange
        RequestContext.Clear();
        RequestContext.Set("GodGPTLanguage", "English");
        
        // Act
        var result = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // Assert
        result.ShouldBe(GodGPTLanguage.English);
    }
    
    [Fact]
    public void GetGodgptLanguageFromContext_WhenContextHasTraditionalChinese_ShouldReturnTraditionalChinese()
    {
        // Arrange
        RequestContext.Clear();
        RequestContext.Set("GodGPTLanguage", "TraditionalChinese");
        
        // Act
        var result = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // Assert
        result.ShouldBe(GodGPTLanguage.TraditionalChinese);
    }
    
    [Fact]
    public void GetGodgptLanguageFromContext_WhenContextHasSpanish_ShouldReturnSpanish()
    {
        // Arrange
        RequestContext.Clear();
        RequestContext.Set("GodGPTLanguage", "Spanish");
        
        // Act
        var result = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // Assert
        result.ShouldBe(GodGPTLanguage.Spanish);
    }
    
    [Fact]
    public void GetGodgptLanguageFromContext_WhenContextHasInvalidValue_ShouldReturnEnglish()
    {
        // Arrange
        RequestContext.Clear();
        RequestContext.Set("GodGPTLanguage", "InvalidLanguage");
        
        // Act
        var result = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // Assert
        result.ShouldBe(GodGPTLanguage.English);
    }
    
    [Fact]
    public void SetGodgptLanguageInContext_ShouldSetLanguageInContext()
    {
        // Arrange
        RequestContext.Clear();
        var language = GodGPTLanguage.TraditionalChinese;
        
        // Act
        GodGPTLanguageHelper.SetGodgptLanguageInContext(language);
        
        // Assert
        var contextValue = RequestContext.Get("GodGPTLanguage");
        contextValue.ShouldBe("TraditionalChinese");
    }
    
    [Fact]
    public void GodgptLanguage_EnumValues_ShouldBeCorrect()
    {
        // Assert
        ((int)GodGPTLanguage.English).ShouldBe(0);
        ((int)GodGPTLanguage.TraditionalChinese).ShouldBe(1);
        ((int)GodGPTLanguage.Spanish).ShouldBe(2);
    }
} 