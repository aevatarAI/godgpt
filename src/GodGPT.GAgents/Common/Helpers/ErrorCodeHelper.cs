using System.Collections;
using Aevatar.Application.Grains.ChatManager.UserQuota;

namespace Aevatar.GAgents.Common.Helpers;

/// <summary>
/// Helper class for parsing error codes and context data from exceptions
/// </summary>
public static class ErrorCodeHelper
{
    /// <summary>
    /// Extracts error code from exception data
    /// </summary>
    /// <param name="exception">The exception to parse</param>
    /// <returns>Error code as integer, or null if not found</returns>
    public static int? GetErrorCode(Exception exception)
    {
        if (exception?.Data == null || !exception.Data.Contains("Code"))
            return null;

        var codeString = exception.Data["Code"]?.ToString();
        if (int.TryParse(codeString, out var code))
            return code;

        return null;
    }

    /// <summary>
    /// Extracts context data from exception data
    /// </summary>
    /// <param name="exception">The exception to parse</param>
    /// <returns>Context data dictionary, or null if not found</returns>
    public static Dictionary<string, object>? GetContextData(Exception exception)
    {
        if (exception?.Data == null || !exception.Data.Contains("ContextData"))
            return null;

        return exception.Data["ContextData"] as Dictionary<string, object>;
    }

    /// <summary>
    /// Creates a formatted error message with context data
    /// </summary>
    /// <param name="exception">The exception to format</param>
    /// <returns>Formatted error message</returns>
    public static string FormatErrorMessage(Exception exception)
    {
        var errorCode = GetErrorCode(exception);
        var contextData = GetContextData(exception);
        var baseMessage = exception.Message;

        if (errorCode == null)
            return baseMessage;

        var contextInfo = "";
        if (contextData != null && contextData.Count > 0)
        {
            var contextItems = contextData.Select(kvp => $"{kvp.Key}: {kvp.Value}");
            contextInfo = $" (Context: {string.Join(", ", contextItems)})";
        }

        return $"Error {errorCode}: {baseMessage}{contextInfo}";
    }

    /// <summary>
    /// Checks if the exception is a specific error code
    /// </summary>
    /// <param name="exception">The exception to check</param>
    /// <param name="errorCode">The error code to match</param>
    /// <returns>True if the exception has the specified error code</returns>
    public static bool IsErrorCode(Exception exception, int errorCode)
    {
        var actualCode = GetErrorCode(exception);
        return actualCode == errorCode;
    }

    /// <summary>
    /// Gets a specific value from context data
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="exception">The exception to parse</param>
    /// <param name="key">The key to look for</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The value from context data, or default value</returns>
    public static T? GetContextValue<T>(Exception exception, string key, T? defaultValue = default)
    {
        var contextData = GetContextData(exception);
        if (contextData == null || !contextData.ContainsKey(key))
            return defaultValue;

        var value = contextData[key];
        if (value is T typedValue)
            return typedValue;

        return defaultValue;
    }
} 