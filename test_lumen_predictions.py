#!/usr/bin/env python3
"""
Test script for Lumen Predictions (Daily, Yearly, Lifetime)
Tests if LLM returns valid TSV format according to current prompts

Usage:
    export LUMEN_API_KEY="your-api-key-here"
    python3 test_lumen_predictions.py

Or set API_KEY directly in this file (line 18)
"""

import requests
import json
import time
import os
from datetime import datetime

# API Configuration
API_URL = "https://hyperecho-proxy.aelf.dev/v1/chat/completions"
MODEL = "fortune-telling-testnet"
API_KEY = os.environ.get("LUMEN_API_KEY", "hy-Wlw4YehUJ9pGyxvwI0_XYdvtEEvB2dHAELXVHHH2zOE")  # Set via environment variable or modify this line

if not API_KEY:
    print("ERROR: API_KEY not set. Please set LUMEN_API_KEY environment variable or modify API_KEY in script.")
    print("Usage: export LUMEN_API_KEY='your-api-key' && python3 test_lumen_predictions.py")
    exit(1)

# Test User Data
TEST_USER = {
    "userId": "test_user_123",
    "fullName": "James Chen",
    "firstName": "James",
    "gender": "Male",
    "birthDate": "1990-03-21",
    "birthTime": "14:30:00",
    "birthCity": "Los Angeles, USA",
    "latLong": "34.0522, -118.2437",
    "sunSign": "Aries",
    "moonSign": "Taurus",
    "risingSign": "Gemini",
    "birthYearZodiac": "Metal Horse (庚午)",
    "birthYearAnimal": "Horse",
    "birthYearElement": "Metal",
    "currentAge": 34
}

def build_daily_prompt(target_language="en"):
    """Build Daily prediction prompt"""
    user_info = f"{TEST_USER['firstName']} {TEST_USER['gender']} {TEST_USER['birthDate']}"
    
    # Language names mapping
    language_map = {
        "en": "English",
        "zh": "简体中文",
        "zh-tw": "繁體中文",
        "es": "Español"
    }
    language_name = language_map.get(target_language, "English")
    
    system_prompt = f"""You are a professional astrology consultant providing personalized insights.

===== CRITICAL LANGUAGE REQUIREMENT =====
Write all FIELD VALUES in {language_name} ONLY.
Field names remain in English.
DO NOT mix languages in field values.
============================================

IMPORTANT: All content is for entertainment and self-reflection purposes only."""

    # Language instruction in target language
    if target_language == "zh":
        language_instruction = """===== 语言要求 =====
必须用简体中文书写所有字段的值（value）。
字段名（field name）保持英文不变。
示例：
  dayTitle	反思与和谐之日     ← 值用简体中文
  card_name	月亮                ← 值用简体中文
  career	专注于团队协作      ← 值用简体中文

重要提醒：
- 下面OUTPUT STRUCTURE中的英文示例（如 "The Day of...", "To [verb]..."）仅为结构说明
- 你必须将这些模板文本也翻译成简体中文
- 例如：path_title 不要写 "James's Path Today"，要写成 "James 今日之路"
- 例如：spell_intent 不要写 "To ignite..."，要写成 "点燃..."
- 例如：fortune_tip 不要写 "Today's turning point"，要写成 "今日转折点"
==================="""
    elif target_language == "zh-tw":
        language_instruction = """===== 語言要求 =====
必須用繁體中文書寫所有字段的值（value）。
字段名（field name）保持英文不變。
示例：
  dayTitle	反思與和諧之日     ← 值用繁體中文
  card_name	月亮                ← 值用繁體中文
  career	專注於團隊協作      ← 值用繁體中文
==================="""
    elif target_language == "es":
        language_instruction = """===== REQUISITO DE IDIOMA =====
Escribe todos los valores de campo en ESPAÑOL.
Los nombres de campo permanecen en inglés.
Ejemplo:
  dayTitle	El Día de Reflexión  ← valor en español
  card_name	La Luna              ← valor en español
  career	Enfócate en el trabajo en equipo  ← valor en español
================================"""
    else:
        language_instruction = f"""===== LANGUAGE REQUIREMENT =====
Write all field VALUES in {language_name}.
Field names remain in English.
Example:
  dayTitle	The Day of Reflection  ← value in {language_name}
  card_name	The Moon               ← value in {language_name}
================================"""

    today_date = datetime.now().strftime('%Y-%m-%d')
    
    user_prompt = language_instruction + f"""

EXCEPTIONS:
- User names: Keep unchanged (don't translate)
- Chinese stems/branches (天干地支): Can include Chinese and pinyin like "甲子 (Jiǎzǐ)"

FORMAT REQUIREMENT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER (\\t) between field name and value
- Arrays: item1|item2|item3 (pipe separator)
- NO JSON, NO markdown, NO extra text
- Start immediately with the data

Create personalized daily insights for {today_date}.
User: {user_info}

========== PRE-CALCULATED VALUES (Use for personalization) ==========
Display Name: {TEST_USER['firstName']} (Use this in greetings and personalized messages. NEVER translate this name.)
Sun Sign: {TEST_USER['sunSign']}
Zodiac Element: Fire
Birth Year Zodiac: {TEST_USER['birthYearZodiac']}
Chinese Element: {TEST_USER['birthYearElement']}

FORMAT (TSV - Tab-Separated Values):
Return data in simple key-value pairs, ONE per line: key	value
Use TAB (\\t) to separate key from value. Arrays use pipe | separator.

OUTPUT STRUCTURE (26 fields organized in 4 sections):

=== 1. DAY THEME ===
dayTitle	The Day of [word1] and [word2]

=== 2. TODAY'S READING ===
# Tarot Card (3 fields)
card_name	Card name (VARIED for {TEST_USER['sunSign']}/Fire/today's energy)
card_essence	1-2 words, comma-separated if two
card_orient	Upright or Reversed

# Your Path (3 fields)
path_title	{TEST_USER['firstName']}'s Path Today - A [adjective] Path
path_intro	15-25 words starting 'Hi {TEST_USER['firstName']}'
path_detail	30-40 words of wisdom

# Life Areas (4 fields)
career	10-20 words advice
love	10-20 words advice
prosperity	10-20 words advice
wellness	10-15 words advice

# Takeaway (1 field)
takeaway	15-25 words '{TEST_USER['firstName']}, your...'

=== 3. LUCKY ALIGNMENTS ===
# Number (4 fields)
lucky_num	Word (digit) e.g. Eight (8)
lucky_digit	1-9
num_meaning	15-20 words for THIS user
num_calc	12-18 words showing actual formula (e.g. 'November (11) + 18 + Metal element = 7 vibration')

# Stone (3 fields)
stone	Stone for Fire element
stone_power	15-20 words how it helps
stone_use	15-20 words 'Meditate:' or 'Practice:'

# Spell (3 fields)
spell	2 words poetic
spell_words	20-30 words affirmation in quotes
spell_intent	10-12 words 'To [verb]...'

=== 4. TWIST OF FORTUNE ===
fortune_title	4-8 words poetic metaphor
fortune_do	activity1|activity2|activity3|activity4|activity5
fortune_avoid	activity1|activity2|activity3|activity4|activity5
fortune_tip	10-15 words 'Today's turning point...'

CONTENT REQUIREMENTS:
- Each line: exactly ONE TAB CHARACTER (\\t) between field and value
- Use actual tab character (not spaces, not literal word 'TAB')
- Array values: EXACTLY 5 items for each array, each item 2-3 words
- No line breaks within field values

PERSONALIZATION RULES:
- Tarot Card: Select DIFFERENT card for each user based on {TEST_USER['sunSign']}/Fire/today's energy
- Lucky Stone by element: Fire→Carnelian/Ruby/Garnet, Earth→Jade/Emerald/Moss Agate, Air→Citrine/Aquamarine, Water→Moonstone/Pearl/Lapis Lazuli
- Lucky Number: Generate VARIED numbers (1-9), ensure variety across users
- All content: Generate NEW perspectives each time, tailored to THIS user
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks"""
    
    return system_prompt, user_prompt


def build_yearly_prompt():
    """Build Yearly prediction prompt"""
    user_info = f"{TEST_USER['firstName']} {TEST_USER['gender']} {TEST_USER['birthDate']}"
    current_year = datetime.now().year
    
    system_prompt = f"""You are a professional astrology and divination expert.

===== CRITICAL LANGUAGE REQUIREMENT =====
Write all FIELD VALUES in English ONLY.
Field names remain in English.
DO NOT mix languages in field values.
============================================

IMPORTANT DISCLAIMER: All predictions are for entertainment and self-reflection purposes only."""

    user_prompt = f"""===== LANGUAGE REQUIREMENT =====
Write all field VALUES in English.
Field names remain in English.
Example:
  dayTitle	The Day of Reflection  ← value in English
  card_name	The Moon               ← value in English
================================

EXCEPTIONS:
- User names: Keep unchanged (don't translate)
- Chinese stems/branches (天干地支): Can include Chinese and pinyin like "甲子 (Jiǎzǐ)"

FORMAT REQUIREMENT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER (\\t) between field name and value
- Arrays: item1|item2|item3 (pipe separator)
- NO JSON, NO markdown, NO extra text
- Start immediately with the data

Generate yearly prediction for {current_year}.
User: {user_info}

========== PRE-CALCULATED VALUES (Use these EXACT values, do NOT recalculate) ==========
Sun Sign: {TEST_USER['sunSign']}
Birth Year Zodiac: {TEST_USER['birthYearZodiac']}
Yearly Year ({current_year}): Wood Snake (乙巳)
Taishui Relationship: Clashing (冲太岁)

FORMAT (TSV - Tab-Separated Values):
Each field on ONE line: fieldName	value

Use actual TAB character (not spaces) as separator. For arrays: Use pipe | to separate items.

Output format (use TAB between field and value, shown as whitespace below):
astro_overlay	{TEST_USER['sunSign']} Sun · Warrior Archetype — {current_year} [Key planetary transits]
theme_title	[VARIED: 4-7 words using 'of' structure]
theme_glance	[VARIED: 15-20 words on what both systems agree]
theme_detail	[VARIED: 60-80 words in 3 parts (double space): P1 combination/clash, P2 what it creates, P3 define year 'not X but Y']
career_score	[1-5 based on analysis]
career_tag	[10-15 words starting 'Your superpower this year:']
career_do	item1|item2
career_avoid	item1|item2
career_detail	[50-70 words in 3 parts: formula, feeling, meaning]
love_score	[1-5]
love_tag	[10-15 words philosophical]
love_do	item1|item2
love_avoid	item1|item2
love_detail	[50-70 words in 3 parts: formula, emotional state, relationship needs]
prosperity_score	[1-5]
prosperity_tag	[10-15 words]
prosperity_do	item1|item2
prosperity_avoid	item1|item2
prosperity_detail	[50-70 words in 3 parts: formula, climate, abundance needs]
wellness_score	[1-5]
wellness_tag	[10-15 words]
wellness_do	item1|item2
wellness_avoid	item1|item2
wellness_detail	[50-70 words in 3 parts: formula, state, wellbeing needs]
mantra	[18-25 words using first-person 'My' declarations, 2-3 powerful statements]

CRITICAL FORMAT REQUIREMENTS:
- Each line: exactly ONE TAB CHARACTER (\\t) between field name and value
- Use actual tab character (not spaces, not literal word 'TAB')
- Array values: use | separator, NO tabs within arrays
- Scores: integer 1-5 only
- No line breaks within field values
- Return ONLY TSV format, no markdown, no extra text

RULES:
- All [VARIED] content must be FRESH for each user
- Scores: 1=challenging, 2=mixed, 3=favorable, 4=excellent, 5=outstanding
- detail fields: Use formula pattern ('X + Y = Z.'), then state, then meaning
- Career tagline starts 'Your superpower this year:', others philosophical
- Avoid fields: 3-6 specific nouns (Job Hopping, Jealousy, Gambling, Late Nights)
- Use double space not line breaks, warm tone, no special chars/emoji"""
    
    return system_prompt, user_prompt


def build_lifetime_prompt():
    """Build Lifetime prediction prompt (simplified for testing)"""
    user_info = f"{TEST_USER['firstName']} {TEST_USER['gender']} {TEST_USER['birthDate']}"
    current_year = datetime.now().year
    
    system_prompt = f"""You are a professional astrology and divination expert.

===== CRITICAL LANGUAGE REQUIREMENT =====
Write all FIELD VALUES in English ONLY.
Field names remain in English.
DO NOT mix languages in field values.
============================================

IMPORTANT DISCLAIMER: All predictions are for entertainment and self-reflection purposes only."""

    user_prompt = f"""===== LANGUAGE REQUIREMENT =====
Write all field VALUES in English.
Field names remain in English.
Example:
  dayTitle	The Day of Reflection  ← value in English
  card_name	The Moon               ← value in English
================================

EXCEPTIONS:
- User names: Keep unchanged (don't translate)
- Chinese stems/branches (天干地支): Can include Chinese and pinyin like "甲子 (Jiǎzǐ)"

FORMAT REQUIREMENT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER (\\t) between field name and value
- Arrays: item1|item2|item3 (pipe separator)
- NO JSON, NO markdown, NO extra text
- Start immediately with the data

Generate lifetime profile for user.
User: {user_info}
Current Year: {current_year}

========== PRE-CALCULATED VALUES (Use EXACT values, do NOT recalculate) ==========
Sun Sign: {TEST_USER['sunSign']} | Moon Sign: {TEST_USER['moonSign']} | Rising Sign: {TEST_USER['risingSign']}
Birth Year Zodiac: {TEST_USER['birthYearZodiac']} | Birth Year Animal: {TEST_USER['birthYearAnimal']} | Birth Year Element: {TEST_USER['birthYearElement']}
Current Year ({current_year}): Wood Snake (乙巳) | Current Year Stems: 乙巳 (Yǐ Sì)
Past Cycle: 20-29 · 2010-2019
Current Cycle: 30-39 · 2020-2029
Future Cycle: 40-49 · 2030-2039

IMPORTANT: All Chinese Zodiac content must reference USER'S Birth Year Zodiac ({TEST_USER['birthYearZodiac']}), NOT current year (Wood Snake).

FORMAT (TSV - Tab-Separated Values):
Each field on ONE line: fieldName	value

CRITICAL: Use actual TAB character (\\t) between field and value.

Output format (TAB shown as whitespace, only show first 10 fields for brevity):
pillars_id	[12-18 words addressing by name]
pillars_detail	[45-60 words using {TEST_USER['sunSign']}, 'both...yet' patterns]
cn_year	[CRITICAL: match target language - en='Year of the Horse', zh='马年']
cn_trait1	[8-12 words]
cn_trait2	[8-12 words]
whisper	[40-50 words starting 'Horse adds...', 'You are not only X, but Y']
sun_tag	You [2-5 words poetic metaphor]
sun_arch	Sun in {TEST_USER['sunSign']} - The [3-5 words archetype]
sun_desc	[18-25 words core traits using 'You']
moon_sign	{TEST_USER['moonSign']}

CRITICAL FORMAT REQUIREMENTS:
- Each line: exactly ONE TAB CHARACTER (\\t) between field name and value
- Use actual tab character (not spaces, not literal word 'TAB')
- No line breaks within field values
- Return ONLY TSV format, no markdown, no extra text

RULES:
- All [VARIED] content must be FRESH for each user
- Use 'both...yet' contrasts, 'You are here to...', 'Your power grows when...' patterns
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks"""
    
    return system_prompt, user_prompt


def call_llm(system_prompt, user_prompt, prediction_type, show_prompts=False):
    """Call LLM API and return response"""
    print(f"\n{'='*80}")
    print(f"Testing {prediction_type} Prediction")
    print(f"{'='*80}")
    
    if show_prompts:
        print(f"\n{'='*80}")
        print("SYSTEM PROMPT:")
        print(f"{'='*80}")
        print(system_prompt)
        print(f"\n{'='*80}")
        print("USER PROMPT:")
        print(f"{'='*80}")
        print(user_prompt)
        print(f"{'='*80}\n")
    
    payload = {
        "model": MODEL,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt}
        ],
        "temperature": 0.7,
        "max_tokens": 4000
    }
    
    print(f"\n[Request] Sending to {API_URL}")
    print(f"[Request] Model: {MODEL}")
    print(f"[Request] System Prompt Length: {len(system_prompt)} chars")
    print(f"[Request] User Prompt Length: {len(user_prompt)} chars")
    
    start_time = time.time()
    
    try:
        response = requests.post(
            API_URL,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {API_KEY}"
            },
            json=payload,
            timeout=60
        )
        
        elapsed = time.time() - start_time
        
        if response.status_code != 200:
            print(f"\n[Error] HTTP {response.status_code}")
            print(f"[Error] Response: {response.text}")
            return None, elapsed
        
        result = response.json()
        
        if 'choices' not in result or len(result['choices']) == 0:
            print(f"\n[Error] No choices in response")
            print(f"[Error] Response: {json.dumps(result, indent=2)}")
            return None, elapsed
        
        content = result['choices'][0]['message']['content']
        usage = result.get('usage', {})
        
        print(f"\n[Response] Duration: {elapsed:.2f}s")
        print(f"[Response] Tokens - Prompt: {usage.get('prompt_tokens', 0)}, "
              f"Completion: {usage.get('completion_tokens', 0)}, "
              f"Total: {usage.get('total_tokens', 0)}")
        
        # Check for cached tokens
        prompt_details = usage.get('prompt_tokens_details', {})
        if prompt_details:
            cached = prompt_details.get('cached_tokens', 0)
            if cached > 0:
                print(f"[Response] Cached Tokens: {cached}")
        
        return content, elapsed
        
    except requests.exceptions.Timeout:
        elapsed = time.time() - start_time
        print(f"\n[Error] Request timeout after {elapsed:.2f}s")
        return None, elapsed
    except Exception as e:
        elapsed = time.time() - start_time
        print(f"\n[Error] Exception: {str(e)}")
        return None, elapsed


def parse_tsv(content):
    """Parse TSV content and validate format"""
    lines = content.strip().split('\n')
    parsed = {}
    errors = []
    
    print(f"\n[Parsing] Total lines: {len(lines)}")
    
    for i, line in enumerate(lines, 1):
        # Skip empty lines
        if not line.strip():
            continue
        
        # Check for markdown code blocks
        if line.strip().startswith('```'):
            errors.append(f"Line {i}: Found markdown code block: {line[:50]}")
            continue
        
        # Check for JSON
        if line.strip().startswith('{') or line.strip().startswith('['):
            errors.append(f"Line {i}: Looks like JSON, not TSV: {line[:50]}")
            continue
        
        # Try to split by tab
        parts = line.split('\t')
        
        if len(parts) < 2:
            errors.append(f"Line {i}: No tab separator found: {line[:50]}")
            continue
        
        if len(parts) > 2:
            errors.append(f"Line {i}: Multiple tabs found (expected 1): {line[:50]}")
            continue
        
        key, value = parts[0].strip(), parts[1].strip()
        
        if not key:
            errors.append(f"Line {i}: Empty key")
            continue
        
        if key in parsed:
            errors.append(f"Line {i}: Duplicate key '{key}'")
        
        parsed[key] = value
    
    print(f"[Parsing] Successfully parsed: {len(parsed)} fields")
    
    if errors:
        print(f"\n[Parsing] Found {len(errors)} errors:")
        for error in errors[:10]:  # Show first 10 errors
            print(f"  - {error}")
        if len(errors) > 10:
            print(f"  ... and {len(errors) - 10} more errors")
    
    return parsed, errors


def validate_daily_fields(parsed):
    """Validate Daily prediction has required fields"""
    required_fields = [
        'dayTitle', 'card_name', 'card_essence', 'card_orient',
        'path_title', 'path_intro', 'path_detail',
        'career', 'love', 'prosperity', 'wellness', 'takeaway',
        'lucky_num', 'lucky_digit', 'num_meaning', 'num_calc',
        'stone', 'stone_power', 'stone_use',
        'spell', 'spell_words', 'spell_intent',
        'fortune_title', 'fortune_do', 'fortune_avoid', 'fortune_tip'
    ]
    
    missing = [f for f in required_fields if f not in parsed]
    
    print(f"\n[Validation] Daily Fields - Required: {len(required_fields)}, Found: {len([f for f in required_fields if f in parsed])}")
    
    if missing:
        print(f"[Validation] Missing fields: {', '.join(missing)}")
        return False
    
    # Validate array fields
    array_fields = ['fortune_do', 'fortune_avoid']
    for field in array_fields:
        if field in parsed:
            items = parsed[field].split('|')
            if len(items) != 5:
                print(f"[Validation] '{field}' should have 5 items, got {len(items)}")
                return False
    
    print("[Validation] ✅ All Daily fields present and valid")
    return True


def validate_yearly_fields(parsed):
    """Validate Yearly prediction has required fields"""
    required_fields = [
        'astro_overlay', 'theme_title', 'theme_glance', 'theme_detail',
        'career_score', 'career_tag', 'career_do', 'career_avoid', 'career_detail',
        'love_score', 'love_tag', 'love_do', 'love_avoid', 'love_detail',
        'prosperity_score', 'prosperity_tag', 'prosperity_do', 'prosperity_avoid', 'prosperity_detail',
        'wellness_score', 'wellness_tag', 'wellness_do', 'wellness_avoid', 'wellness_detail',
        'mantra'
    ]
    
    missing = [f for f in required_fields if f not in parsed]
    
    print(f"\n[Validation] Yearly Fields - Required: {len(required_fields)}, Found: {len([f for f in required_fields if f in parsed])}")
    
    if missing:
        print(f"[Validation] Missing fields: {', '.join(missing)}")
        return False
    
    # Validate scores are 1-5
    score_fields = ['career_score', 'love_score', 'prosperity_score', 'wellness_score']
    for field in score_fields:
        if field in parsed:
            try:
                score = int(parsed[field])
                if score < 1 or score > 5:
                    print(f"[Validation] '{field}' should be 1-5, got {score}")
                    return False
            except ValueError:
                print(f"[Validation] '{field}' should be integer, got '{parsed[field]}'")
                return False
    
    print("[Validation] ✅ All Yearly fields present and valid")
    return True


def validate_lifetime_fields(parsed):
    """Validate Lifetime prediction has core fields (simplified)"""
    required_fields = [
        'pillars_id', 'pillars_detail', 'cn_year',
        'sun_tag', 'sun_arch', 'sun_desc',
        'moon_sign'
    ]
    
    missing = [f for f in required_fields if f not in parsed]
    
    print(f"\n[Validation] Lifetime Fields - Required: {len(required_fields)}, Found: {len([f for f in required_fields if f in parsed])}")
    
    if missing:
        print(f"[Validation] Missing fields: {', '.join(missing)}")
        return False
    
    print("[Validation] ✅ All Lifetime core fields present")
    return True


def test_chinese():
    """Test Chinese (zh) language specifically"""
    print("\n" + "="*80)
    print("TESTING CHINESE (简体中文) LANGUAGE")
    print("="*80)
    print("Focus: Language consistency, TSV format, parsing compatibility")
    
    # Test Daily with Chinese
    system_prompt, user_prompt = build_daily_prompt(target_language="zh")
    content, elapsed = call_llm(system_prompt, user_prompt, "Daily (简体中文)", show_prompts=True)
    
    if content:
        print(f"\n[Content Full Response]:")
        print("="*80)
        print(content)
        print("="*80)
        
        parsed, errors = parse_tsv(content)
        
        print(f"\n[Language Check] Analyzing field values for language consistency...")
        
        # Check for English words in Chinese content (potential mixing)
        english_count = 0
        chinese_count = 0
        
        for key, value in parsed.items():
            # Skip certain fields that should have English/numbers
            if key in ['lucky_digit', 'card_orient']:
                continue
            
            # Count English letters and Chinese characters
            english_chars = sum(1 for c in value if c.isascii() and c.isalpha())
            chinese_chars = sum(1 for c in value if '\u4e00' <= c <= '\u9fff')
            
            if english_chars > 10:  # More than 10 English letters might indicate mixing
                english_count += 1
                print(f"  ⚠️  Field '{key}': Has {english_chars} English chars - {value[:50]}...")
            elif chinese_chars > 0:
                chinese_count += 1
        
        print(f"\n[Language Summary]")
        print(f"  Fields with Chinese: {chinese_count}")
        print(f"  Fields with significant English: {english_count}")
        
        if english_count > 3:
            print(f"  ⚠️  WARNING: Multiple fields have English content (expected Chinese)")
        else:
            print(f"  ✅ Language consistency looks good")
        
        valid = validate_daily_fields(parsed)
        
        return {
            'valid': valid,
            'elapsed': elapsed,
            'fields': len(parsed),
            'errors': len(errors),
            'chinese_fields': chinese_count,
            'english_fields': english_count
        }
    else:
        return {'valid': False, 'elapsed': elapsed, 'fields': 0, 'errors': 1}


def main():
    """Main test function"""
    print("="*80)
    print("Lumen Predictions Test Script")
    print("="*80)
    print(f"API URL: {API_URL}")
    print(f"Model: {MODEL}")
    print(f"Test User: {TEST_USER['firstName']} ({TEST_USER['sunSign']})")
    
    # Test Chinese language
    zh_result = test_chinese()
    
    print("\n" + "="*80)
    print("CHINESE TEST SUMMARY")
    print("="*80)
    
    if zh_result['valid']:
        print(f"✅ PASS - TSV Format Valid")
    else:
        print(f"❌ FAIL - TSV Format Issues")
    
    print(f"Duration: {zh_result['elapsed']:.2f}s")
    print(f"Fields: {zh_result['fields']}")
    print(f"Errors: {zh_result['errors']}")
    if 'chinese_fields' in zh_result:
        print(f"Chinese Fields: {zh_result['chinese_fields']}")
        print(f"English Fields: {zh_result['english_fields']}")
    
    return
    
    # Original tests below (commented out for now)
    results = {}
    
    # Test Daily
    system_prompt, user_prompt = build_daily_prompt()
    content, elapsed = call_llm(system_prompt, user_prompt, "Daily")
    if content:
        print(f"\n[Content Preview] First 500 chars:")
        print(content[:500])
        parsed, errors = parse_tsv(content)
        valid = validate_daily_fields(parsed)
        results['Daily'] = {'valid': valid, 'elapsed': elapsed, 'fields': len(parsed), 'errors': len(errors)}
    else:
        results['Daily'] = {'valid': False, 'elapsed': elapsed, 'fields': 0, 'errors': 1}
    
    time.sleep(2)  # Rate limiting
    
    # Test Yearly
    system_prompt, user_prompt = build_yearly_prompt()
    content, elapsed = call_llm(system_prompt, user_prompt, "Yearly")
    if content:
        print(f"\n[Content Preview] First 500 chars:")
        print(content[:500])
        parsed, errors = parse_tsv(content)
        valid = validate_yearly_fields(parsed)
        results['Yearly'] = {'valid': valid, 'elapsed': elapsed, 'fields': len(parsed), 'errors': len(errors)}
    else:
        results['Yearly'] = {'valid': False, 'elapsed': elapsed, 'fields': 0, 'errors': 1}
    
    time.sleep(2)  # Rate limiting
    
    # Test Lifetime
    system_prompt, user_prompt = build_lifetime_prompt()
    content, elapsed = call_llm(system_prompt, user_prompt, "Lifetime")
    if content:
        print(f"\n[Content Preview] First 500 chars:")
        print(content[:500])
        parsed, errors = parse_tsv(content)
        valid = validate_lifetime_fields(parsed)
        results['Lifetime'] = {'valid': valid, 'elapsed': elapsed, 'fields': len(parsed), 'errors': len(errors)}
    else:
        results['Lifetime'] = {'valid': False, 'elapsed': elapsed, 'fields': 0, 'errors': 1}
    
    # Summary
    print("\n" + "="*80)
    print("TEST SUMMARY")
    print("="*80)
    
    for pred_type, result in results.items():
        status = "✅ PASS" if result['valid'] else "❌ FAIL"
        print(f"{pred_type:10} {status:10} Duration: {result['elapsed']:.2f}s, Fields: {result['fields']}, Errors: {result['errors']}")
    
    all_valid = all(r['valid'] for r in results.values())
    print("\n" + "="*80)
    if all_valid:
        print("🎉 ALL TESTS PASSED!")
    else:
        print("⚠️  SOME TESTS FAILED - Check output above for details")
    print("="*80)


if __name__ == "__main__":
    main()

