using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Application.Grains.Agents.ChatManager.Common;

public class CommonHelper
{
    // Salt for IP hashing - should be stored securely in production
    private static readonly string IP_HASH_SALT = "AnonymousGodGPT_2025_Salt_Key";
    
    public static Guid StringToGuid(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(hash);
        }
    }

    public static Guid GetSessionManagerConfigurationId()
    {
        return StringToGuid("GetStreamSessionManagerConfigurationId15");
    }
    
    public static string GetUserQuotaGAgentId(Guid chatManagerId)
    {
        return string.Join("_", chatManagerId.ToString(), "Quota");
    }  
    
    public static string GetUserBillingGAgentId(Guid chatManagerId)
    {
        return string.Join("_", chatManagerId.ToString(), "Billing");
    }

    public static Guid GetAppleUserPaymentGrainId(string transactionId)
    {
        return StringToGuid(string.Join("_", transactionId, "AppStore"));
    }
    
    /// <summary>
    /// Generate consistent Grain ID for Anonymous User GAgent based on IP address hash
    /// Uses SHA256 with salt to protect user privacy while maintaining consistency
    /// </summary>
    /// <param name="ipAddress">Client IP address</param>
    /// <returns>Deterministic hashed identifier for the AnonymousUserGAgent</returns>
    public static string GetAnonymousUserGAgentId(string ipAddress)
    {
        // Hash IP address with salt for privacy protection
        var hashedIp = HashIpAddress(ipAddress);
        return $"AnonymousUser_{hashedIp}";
    }
    
    /// <summary>
    /// Hash IP address with salt using SHA256 for privacy protection
    /// </summary>
    /// <param name="ipAddress">Original IP address</param>
    /// <returns>SHA256 hash (first 16 characters for shorter identifiers)</returns>
    private static string HashIpAddress(string ipAddress)
    {
        using (var sha256 = SHA256.Create())
        {
            var saltedInput = $"{ipAddress}_{IP_HASH_SALT}";
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedInput));
            var hashString = Convert.ToHexString(hashBytes);
            // Use first 16 characters for shorter, more manageable identifiers
            return hashString[..16].ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// A method to load the content of a file.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>Returns the complete content of the file as a string</returns>
    public static string LoadFileContent(string filePath)  
    {  
        // Check if the file exists  
        if (!File.Exists(filePath))  
        {  
            throw new FileNotFoundException($"File not found: {filePath}");  
        }  
  
        // Use a StringBuilder to efficiently concatenate strings  
        StringBuilder fileContent = new StringBuilder();  
        using (StreamReader reader = new StreamReader(filePath))
        {  
            string? line;  
            while ((line = reader.ReadLine()) != null)  
            {  
                fileContent.Append(line);  
            }  
        }  
        
        // Return the concatenated content, trimming the last '\n' if needed  
        return fileContent.ToString().TrimEnd('\n');  
    }
    
    /// <summary>
    /// A method to load file content from the current directory.
    /// </summary>
    /// <param name="fileName">The name of the file located in the current directory</param>
    /// <returns>Returns the complete content of the file as a string</returns>
    public static string LoadFileFromCurrentDirectory(string fileName)
    {
        string currentDirectory = Directory.GetCurrentDirectory();

        string fullPath = Path.Combine(currentDirectory, fileName);

        return LoadFileContent(fullPath);
    }
}