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
    /// Build detailed calculation formula string with introductory text
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
        
        // Build intro text + formula
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            var intro = "数字能量由你的出生日期与今日日期相加计算而来：";
            var formula = $"出生日期 {birthDateStr} ({birthDigits}={birthSum}) + 今日日期 {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
            return $"{intro}\n{formula}";
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            var intro = "La energía numérica se calcula sumando tu fecha de nacimiento y la fecha de hoy:";
            var formula = $"Fecha de nacimiento {birthDateStr} ({birthDigits}={birthSum}) + Fecha de hoy {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
            return $"{intro}\n{formula}";
        }
        else // English (default)
        {
            var intro = "Numerical energy is calculated by adding your birth date and today's date:";
            var formula = $"Birth date {birthDateStr} ({birthDigits}={birthSum}) + Today {predictionDateStr} ({predictionDigits}={predictionSum}) = {totalSum}{reductionSteps}";
            return $"{intro}\n{formula}";
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
    /// Get 4 random keywords from the predefined pool for each number
    /// </summary>
    private static string GetNumberDescription(int digit, string language)
    {
        // Get keyword pool for this number and language
        var keywords = GetNumberKeywords(digit, language);
        
        // Randomly select 4 keywords
        var random = new Random(digit * 1000 + DateTime.UtcNow.DayOfYear); // Seed based on number + day of year for variation
        var selectedKeywords = keywords.OrderBy(_ => random.Next()).Take(4).ToList();
        
        // Build description based on language
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            // Chinese format: 今天承载着数字7的能量，内省、智慧、神秘和真理。
            var keywordString = string.Join("、", selectedKeywords.Take(3)) + "和" + selectedKeywords.Last();
            return $"今天承载着数字{digit}的能量，{keywordString}。";
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            // Spanish format: Hoy lleva la energía del número 7, introspección, sabiduría, misterio y verdad.
            var keywordString = string.Join(", ", selectedKeywords.Take(3)) + " y " + selectedKeywords.Last();
            return $"Hoy lleva la energía del número {digit}, {keywordString}.";
        }
        else // English (default)
        {
            // English format: Today carries the energy of Number 7, introspection, wisdom, mystery, and truth.
            var keywordString = string.Join(", ", selectedKeywords.Take(3)) + ", and " + selectedKeywords.Last();
            return $"Today carries the energy of Number {digit}, {keywordString}.";
        }
    }
    
    /// <summary>
    /// Get keyword pool for each number (10 keywords per number)
    /// </summary>
    private static List<string> GetNumberKeywords(int digit, string language)
    {
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => new List<string> { "新开始", "独立", "领导力", "勇气", "创新", "自信", "先锋", "主动", "决心", "突破" },
                2 => new List<string> { "平衡", "合作", "和谐", "耐心", "直觉", "外交", "伙伴", "温柔", "理解", "同理心" },
                3 => new List<string> { "创造力", "表达", "喜悦", "乐观", "沟通", "灵感", "社交", "热情", "艺术", "想象力" },
                4 => new List<string> { "稳定", "秩序", "实用", "坚实", "纪律", "组织", "踏实", "可靠", "结构", "专注" },
                5 => new List<string> { "变化", "自由", "冒险", "灵活", "好奇", "适应", "探索", "多样", "活力", "进步" },
                6 => new List<string> { "爱", "责任", "养育", "和谐", "服务", "同情", "家庭", "治愈", "保护", "奉献" },
                7 => new List<string> { "内省", "智慧", "神秘", "真理", "灵性", "分析", "洞察", "沉思", "觉知", "深度" },
                8 => new List<string> { "力量", "成功", "丰盛", "成就", "权威", "物质", "效率", "野心", "掌控", "实现" },
                9 => new List<string> { "完成", "同情", "普世之爱", "智慧", "放下", "人道", "觉悟", "宽容", "圆满", "升华" },
                _ => new List<string> { "新开始", "独立", "领导力", "勇气" }
            };
        }
        else if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return digit switch
            {
                1 => new List<string> { "nuevos comienzos", "independencia", "liderazgo", "coraje", "innovación", "confianza", "pionero", "iniciativa", "determinación", "avance" },
                2 => new List<string> { "equilibrio", "cooperación", "armonía", "paciencia", "intuición", "diplomacia", "compañerismo", "ternura", "comprensión", "empatía" },
                3 => new List<string> { "creatividad", "expresión", "alegría", "optimismo", "comunicación", "inspiración", "sociabilidad", "entusiasmo", "arte", "imaginación" },
                4 => new List<string> { "estabilidad", "orden", "practicidad", "solidez", "disciplina", "organización", "firmeza", "fiabilidad", "estructura", "enfoque" },
                5 => new List<string> { "cambio", "libertad", "aventura", "flexibilidad", "curiosidad", "adaptación", "exploración", "diversidad", "vitalidad", "progreso" },
                6 => new List<string> { "amor", "responsabilidad", "cuidado", "armonía", "servicio", "compasión", "familia", "sanación", "protección", "devoción" },
                7 => new List<string> { "introspección", "sabiduría", "misterio", "verdad", "espiritualidad", "análisis", "percepción", "contemplación", "conciencia", "profundidad" },
                8 => new List<string> { "poder", "éxito", "abundancia", "logro", "autoridad", "materialización", "eficiencia", "ambición", "control", "realización" },
                9 => new List<string> { "culminación", "compasión", "amor universal", "sabiduría", "soltar", "humanitarismo", "iluminación", "tolerancia", "plenitud", "trascendencia" },
                _ => new List<string> { "nuevos comienzos", "independencia", "liderazgo", "coraje" }
            };
        }
        else // English (default)
        {
            return digit switch
            {
                1 => new List<string> { "new beginnings", "independence", "leadership", "courage", "innovation", "confidence", "pioneering", "initiative", "determination", "breakthrough" },
                2 => new List<string> { "balance", "cooperation", "harmony", "patience", "intuition", "diplomacy", "partnership", "gentleness", "understanding", "empathy" },
                3 => new List<string> { "creativity", "expression", "joy", "optimism", "communication", "inspiration", "sociability", "enthusiasm", "artistry", "imagination" },
                4 => new List<string> { "stability", "order", "practicality", "foundation", "discipline", "organization", "grounding", "reliability", "structure", "focus" },
                5 => new List<string> { "change", "freedom", "adventure", "flexibility", "curiosity", "adaptability", "exploration", "diversity", "vitality", "progress" },
                6 => new List<string> { "love", "responsibility", "nurturing", "harmony", "service", "compassion", "family", "healing", "protection", "devotion" },
                7 => new List<string> { "introspection", "wisdom", "mystery", "truth", "spirituality", "analysis", "insight", "contemplation", "awareness", "depth" },
                8 => new List<string> { "power", "success", "abundance", "achievement", "authority", "manifestation", "efficiency", "ambition", "mastery", "accomplishment" },
                9 => new List<string> { "completion", "compassion", "universal love", "wisdom", "release", "humanitarianism", "enlightenment", "tolerance", "fulfillment", "transcendence" },
                _ => new List<string> { "new beginnings", "independence", "leadership", "courage" }
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

