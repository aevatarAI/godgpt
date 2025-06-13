using Aevatar.Application.Grains.ChatManager.UserBilling;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Common;

public static class AppStoreHelper
{
    // Helper method for decoding JWT payload from App Store
    public static T DecodeJwtPayload<T>(string jwt) where T : class
    {
        // JWT format: header.payload.signature
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            throw new SystemException("Invalid JWT format");
        }
            
        // Decode payload part (second part)
        var payloadBase64 = parts[1];
            
        // Add necessary padding
        var padding = 4 - (payloadBase64.Length % 4);
        if (padding < 4)
        {
            payloadBase64 += new string('=', padding);
        }
            
        // Replace URL-safe characters
        payloadBase64 = payloadBase64.Replace('-', '+').Replace('_', '/');
            
        // Base64 decode
        var payloadBytes = Convert.FromBase64String(payloadBase64);
        var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
            
        // Deserialize JSON
        return JsonConvert.DeserializeObject<T>(payloadJson);
    }
    
    // Decode V2 notification payload
    public static ResponseBodyV2DecodedPayload DecodeV2Payload(string signedPayload)
    {
        // JWT format: header.payload.signature
        var parts = signedPayload.Split('.');
        if (parts.Length != 3)
        {
            throw new SystemException("Invalid JWT format");
        }
            
        // Decode payload part (second part)
        var payloadBase64 = parts[1];
            
        // Add necessary padding
        var padding = 4 - (payloadBase64.Length % 4);
        if (padding < 4)
        {
            payloadBase64 += new string('=', padding);
        }
            
        // Replace URL-safe characters
        payloadBase64 = payloadBase64.Replace('-', '+').Replace('_', '/');
            
        // Base64 decode
        var payloadBytes = Convert.FromBase64String(payloadBase64);
        var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
            
        // Deserialize JSON
        var decodedPayload = JsonConvert.DeserializeObject<ResponseBodyV2DecodedPayload>(payloadJson);
            
        return decodedPayload;
    }
}