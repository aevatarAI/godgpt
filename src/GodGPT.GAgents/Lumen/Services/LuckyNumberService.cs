using System.Globalization;

namespace Aevatar.Application.Grains.Lumen.Services;

/// <summary>
/// Lucky Number Service - calculates personalized lucky number based on birth date + today's date
/// Supports multi-language descriptions for numbers 0-9
/// </summary>
public static class LuckyNumberService
{
    /// <summary>
    /// Calculate lucky number from birth date and prediction date
    /// </summary>
    public static LuckyNumberResult CalculateLuckyNumber(DateOnly birthDate, DateOnly predictionDate, string language = "en")
    {
        // Step 1: Calculate sum of digits from birth date
        var birthSum = SumDigits(birthDate);
        
        // Step 2: Calculate sum of digits from prediction date
        var predictionSum = SumDigits(predictionDate);
        
        // Step 3: Add both sums
        var totalSum = birthSum + predictionSum;
        
        // Step 4: Reduce to single digit (1-9)
        var luckyDigit = ReduceToSingleDigit(totalSum);
        
        // Step 5: Build calculation formula
        var formula = BuildCalculationFormula(birthDate, birthSum, predictionDate, predictionSum, totalSum, luckyDigit, language);
        
        // Step 6: Get description for this number
        var description = GetNumberDescription(luckyDigit, language);
        var numberWord = GetNumberWord(luckyDigit, language);
        
        return new LuckyNumberResult
        {
            Digit = luckyDigit,
            NumberWord = numberWord,
            Description = description,
            CalculationFormula = formula
        };
    }
    
    /// <summary>
    /// Sum all digits in a date
    /// </summary>
    private static int SumDigits(DateOnly date)
    {
        // Convert to string format: YYYYMMDD (e.g., 19900515 or 20251127)
        var dateString = date.ToString("yyyyMMdd");
        
        // Sum all digits
        var sum = 0;
        foreach (var ch in dateString)
        {
            if (char.IsDigit(ch))
            {
                sum += ch - '0'; // Convert char digit to int
            }
        }
        
        return sum;
    }
    
    /// <summary>
    /// Reduce a number to single digit (1-9) using numerology rules
    /// </summary>
    private static int ReduceToSingleDigit(int number)
    {
        while (number > 9)
        {
            var sum = 0;
            while (number > 0)
            {
                sum += number % 10;
                number /= 10;
            }
            number = sum;
        }
        
        // If result is 0, return 9 (in numerology, 0 is treated as 9)
        return number == 0 ? 9 : number;
    }
    
    /// <summary>
    /// Build detailed calculation formula string
    /// </summary>
    private static string BuildCalculationFormula(
        DateOnly birthDate, 
        int birthSum,
        DateOnly predictionDate, 
        int predictionSum,
        int totalSum,
        int result,
        string language)
    {
        var birthDateStr = birthDate.ToString("M-d-yyyy");
        var predictionDateStr = predictionDate.ToString("M-d-yyyy");
        
        // Build birth date digits breakdown
        var birthDigits = string.Join("+", birthDate.ToString("yyyyMMdd").Select(c => c.ToString()));
        
        // Build prediction date digits breakdown
        var predictionDigits = string.Join("+", predictionDate.ToString("yyyyMMdd").Select(c => c.ToString()));
        
        // Build reduction steps if needed
        var reductionSteps = "";
        if (totalSum > 9)
        {
            reductionSteps = $" → {string.Join("+", totalSum.ToString().Select(c => c.ToString()))}={result}";
        }
        
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return $"出生日期 {birthDateStr} ({birthDigits}={birthSum}) + 今日日期 {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return $"Fecha de nacimiento {birthDateStr} ({birthDigits}={birthSum}) + Fecha de hoy {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
        }
        else // English (default)
        {
            return $"Birth date {birthDateStr} ({birthDigits}={birthSum}) + Today {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
        }
    }
    
    /// <summary>
    /// Get number word in target language
    /// </summary>
    private static string GetNumberWord(int digit, string language)
    {
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => "一 (1)",
                2 => "二 (2)",
                3 => "三 (3)",
                4 => "四 (4)",
                5 => "五 (5)",
                6 => "六 (6)",
                7 => "七 (7)",
                8 => "八 (8)",
                9 => "九 (9)",
                _ => "一 (1)"
            };
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => "Uno (1)",
                2 => "Dos (2)",
                3 => "Tres (3)",
                4 => "Cuatro (4)",
                5 => "Cinco (5)",
                6 => "Seis (6)",
                7 => "Siete (7)",
                8 => "Ocho (8)",
                9 => "Nueve (9)",
                _ => "Uno (1)"
            };
        }
        else // English (default)
        {
            return digit switch
            {
                1 => "One (1)",
                2 => "Two (2)",
                3 => "Three (3)",
                4 => "Four (4)",
                5 => "Five (5)",
                6 => "Six (6)",
                7 => "Seven (7)",
                8 => "Eight (8)",
                9 => "Nine (9)",
                _ => "One (1)"
            };
        }
    }
    
    /// <summary>
    /// Get description for each number in target language
    /// </summary>
    private static string GetNumberDescription(int digit, string language)
    {
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => "今天承载着数字1的能量，象征着新的开始、独立和领导力。勇敢地迈出第一步，相信自己的直觉。",
                2 => "今天承载着数字2的能量，象征着平衡、合作和和谐。与他人建立联系，寻找共同点。",
                3 => "今天承载着数字3的能量，象征着创造力、表达和喜悦。让你的想象力自由飞翔，分享你的天赋。",
                4 => "今天承载着数字4的能量，象征着稳定、秩序和实用。专注于建立坚实的基础，脚踏实地。",
                5 => "今天承载着数字5的能量，象征着变化、自由和冒险。拥抱新体验，保持灵活性。",
                6 => "今天承载着数字6的能量，象征着爱、责任和养育。关心他人，创造和谐的环境。",
                7 => "今天承载着数字7的能量，象征着内省、智慧和神秘。深入探索你的内心世界，寻求真理。",
                8 => "今天承载着数字8的能量，象征着力量、成功和丰盛。专注于你的目标，展现你的能力。",
                9 => "今天承载着数字9的能量，象征着完成、同情和普世之爱。放下旧事物，拥抱更高的视野。",
                _ => "今天承载着数字1的能量，象征着新的开始、独立和领导力。"
            };
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => "Hoy lleva la energía del número 1, simbolizando nuevos comienzos, independencia y liderazgo. Da el primer paso con valentía y confía en tu intuición.",
                2 => "Hoy lleva la energía del número 2, simbolizando equilibrio, cooperación y armonía. Conecta con los demás y busca puntos en común.",
                3 => "Hoy lleva la energía del número 3, simbolizando creatividad, expresión y alegría. Deja volar tu imaginación y comparte tus dones.",
                4 => "Hoy lleva la energía del número 4, simbolizando estabilidad, orden y practicidad. Enfócate en construir bases sólidas y mantente centrado.",
                5 => "Hoy lleva la energía del número 5, simbolizando cambio, libertad y aventura. Abraza nuevas experiencias y mantén la flexibilidad.",
                6 => "Hoy lleva la energía del número 6, simbolizando amor, responsabilidad y cuidado. Nutre a los demás y crea un ambiente armonioso.",
                7 => "Hoy lleva la energía del número 7, simbolizando introspección, sabiduría y misterio. Explora profundamente tu mundo interior y busca la verdad.",
                8 => "Hoy lleva la energía del número 8, simbolizando poder, éxito y abundancia. Concéntrate en tus metas y demuestra tu capacidad.",
                9 => "Hoy lleva la energía del número 9, simbolizando culminación, compasión y amor universal. Suelta lo viejo y abraza una perspectiva más elevada.",
                _ => "Hoy lleva la energía del número 1, simbolizando nuevos comienzos, independencia y liderazgo."
            };
        }
        else // English (default)
        {
            return digit switch
            {
                1 => "Today carries the energy of Number 1, symbolizing new beginnings, independence, and leadership. Take the first step boldly and trust your instincts.",
                2 => "Today carries the energy of Number 2, symbolizing balance, cooperation, and harmony. Connect with others and seek common ground.",
                3 => "Today carries the energy of Number 3, symbolizing creativity, expression, and joy. Let your imagination soar and share your gifts.",
                4 => "Today carries the energy of Number 4, symbolizing stability, order, and practicality. Focus on building solid foundations and stay grounded.",
                5 => "Today carries the energy of Number 5, symbolizing change, freedom, and adventure. Embrace new experiences and stay flexible.",
                6 => "Today carries the energy of Number 6, symbolizing love, responsibility, and nurturing. Care for others and create harmonious environments.",
                7 => "Today carries the energy of Number 7, symbolizing introspection, wisdom, and mystery. Explore your inner world deeply and seek truth.",
                8 => "Today carries the energy of Number 8, symbolizing power, success, and abundance. Focus on your goals and demonstrate your capability.",
                9 => "Today carries the energy of Number 9, symbolizing completion, compassion, and universal love. Release the old and embrace a higher perspective.",
                _ => "Today carries the energy of Number 1, symbolizing new beginnings, independence, and leadership."
            };
        }
    }
}

/// <summary>
/// Lucky number calculation result
/// </summary>
public class LuckyNumberResult
{
    /// <summary>
    /// The calculated lucky number (1-9)
    /// </summary>
    public int Digit { get; set; }
    
    /// <summary>
    /// Number word in target language (e.g., "Seven (7)" or "七 (7)")
    /// </summary>
    public string NumberWord { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of the number's energy and meaning
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed calculation formula showing how the number was derived
    /// </summary>
    public string CalculationFormula { get; set; } = string.Empty;
}

