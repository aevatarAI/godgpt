using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Common;

public static class InvitationCodeHelper
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const string Base62Chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly Random Random = new Random();
    
    public static string GenerateOptimizedCode(InvitationCodeType codeType, long unixTimestamp)
    {
        // 64-bit allocation:
        // Bits 63-62: Reserved (2 bits, always 0) - NOT XORed
        // Bits 61-58: Code type (4 bits, supports 16 types) - NOT XORed
        // Bits 57-32: Random value (26 bits, 67,108,864 combinations)
        // Bits 31-0:  Unix timestamp (32 bits, valid until 2106)
        var randomValue = GenerateRandomValue();
        
        var typeShifted = ((long)codeType & 0xF) << 58; // Shift to bits 61-58
        var randomShifted = (randomValue & 0x3FFFFFF) << 32; // 26-bit mask
        var timestampMasked = unixTimestamp & 0xFFFFFFFF;
        
        var combinedValue = typeShifted | randomShifted | timestampMasked;
        
        // Preserve high 6 bits, XOR only low 58 bits
        var high6Bits = combinedValue >> 58;           // Save high 6 bits
        var low58Bits = combinedValue & 0x03FFFFFFFFFFFFFFL; // Extract low 58 bits
        low58Bits ^= 0x15A5A5A5A5A5A5AL;             // XOR only low 58 bits
        combinedValue = (high6Bits << 58) | low58Bits; // Combine back
        
        return EncodeToBase36(combinedValue);
    }

    public static InvitationCodeType? GetCodeType(string code)
    {
        if (code.IsNullOrWhiteSpace())
        {
            return null;
        }

        if (IsValidFreeTrialCodeFormat(code))
        {
            var (codeType, _) = ParseCodeInfo(code);
            return codeType;
        } else if (IsValidFriendInvitationCodeFormat(code))
        {
            return InvitationCodeType.FriendInvitation;
        }
        return null;
    }

    public static bool IsValidFreeTrialCodeFormat(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 11 || code.Length > 12)
        {
            return false;
        }

        return code.All(c => Chars.Contains(c));
    }

    public static bool IsValidFriendInvitationCodeFormat(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 7)
        {
            return false;
        }

        return code.All(c => Base62Chars.Contains(c));
    }
    
    public static bool IsCodeFromBatch(string code, long batchId)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 11 || code.Length > 12)
        {
            return false;
        }

        try
        {
            var parsedTimestamp = ParseBatchTimestampFromCode(code);
            return parsedTimestamp == batchId;
        }
        catch
        {
            return false;
        }
    }
    
    public static long ParseBatchTimestampFromCode(string code)
    {
        try
        {
            var (_, timestamp) = ParseCodeInfo(code);
            return timestamp;
        }
        catch
        {
            throw new ArgumentException("Cannot parse timestamp from code - invalid format");
        }
    }
    
    public static (InvitationCodeType codeType, long unixTimestamp) ParseCodeInfo(string code)
    {
        if (!IsValidFreeTrialCodeFormat(code))
        {
            throw new InvalidOperationException("Invalid free trial code format - must be 11-12 characters");
        }
        
        var value = DecodeFromBase36(code);
        
        // Preserve high 6 bits, reverse XOR only low 58 bits
        var high6Bits = value >> 58;           // Save high 6 bits
        var low58Bits = value & 0x03FFFFFFFFFFFFFFL; // Extract low 58 bits
        low58Bits ^= 0x15A5A5A5A5A5A5AL;     // Reverse XOR only low 58 bits
        value = (high6Bits << 58) | low58Bits; // Combine back
        
        var codeType = (InvitationCodeType)((value >> 58) & 0xF); // Extract from bits 61-58
        var unixTimestamp = value & 0xFFFFFFFF;
        
        return (codeType, unixTimestamp);
    }
    
    public static string EncodeToBase36(long value)
    {
        if (value == 0)
        {
            return "0";
        }
        
        var result = new List<char>();
        while (value > 0)
        {
            result.Add(Chars[(int)(value % 36)]);
            value /= 36;
        }

        result.Reverse();
        return new string(result.ToArray());
    }
    
    private static long DecodeFromBase36(string code)
    {
        long result = 0;

        for (int i = 0; i < code.Length; i++)
        {
            var charIndex = Chars.IndexOf(code[i]);
            if (charIndex == -1)
            {
                throw new ArgumentException($"Invalid character '{code[i]}' in code");
            }
            result = result * 36 + charIndex;
        }

        return result;
    }
    
    private static long GenerateRandomValue()
    {
        //26bit
        return Random.NextInt64(0, 0x4000000);
    }
}