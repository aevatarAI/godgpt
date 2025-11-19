#!/usr/bin/env python3
"""
Test Lifetime prediction generation performance and quality
"""

import requests
import time
import json
from datetime import datetime

# Configuration
API_BASE = "https://aevatar-godgpt-dev-api.aelf.com"
TEST_TOKEN = "your_token_here"  # Replace with actual token

# Test user profile
TEST_USER = {
    "firstName": "凯文",
    "lastName": "王",
    "birthDate": "1995-07-15",
    "gender": "Male",
    "calendarType": 0,  # Solar
    "currentResidence": "Beijing",
    "occupation": "Software Engineer"
}

def register_or_update_user(token):
    """Register or update test user profile"""
    url = f"{API_BASE}/api/lumen/user-profile"
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json"
    }
    
    print("📝 Registering/Updating user profile...")
    response = requests.post(url, headers=headers, json=TEST_USER)
    
    if response.status_code == 200:
        print("✅ User profile updated successfully")
        return True
    else:
        print(f"❌ Failed to update profile: {response.status_code}")
        print(f"Response: {response.text}")
        return False

def get_prediction_status(token, pred_type="lifetime"):
    """Get prediction status"""
    url = f"{API_BASE}/api/lumen/prediction/status"
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept-Language": "zh"
    }
    params = {"type": pred_type}
    
    response = requests.get(url, headers=headers, params=params)
    if response.status_code == 200:
        return response.json()
    else:
        print(f"❌ Failed to get status: {response.status_code}")
        return None

def trigger_lifetime_generation(token, language="zh"):
    """Trigger lifetime prediction generation"""
    url = f"{API_BASE}/api/lumen/prediction/lifetime"
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept-Language": language
    }
    
    print(f"\n🚀 Triggering Lifetime prediction generation (language: {language})...")
    start_time = time.time()
    
    response = requests.get(url, headers=headers)
    
    generation_time = time.time() - start_time
    
    if response.status_code == 200:
        data = response.json()
        return {
            "success": True,
            "generation_time": generation_time,
            "data": data
        }
    else:
        return {
            "success": False,
            "generation_time": generation_time,
            "status_code": response.status_code,
            "error": response.text
        }

def analyze_lifetime_content(data):
    """Analyze the generated lifetime content for quality and issues"""
    print("\n📊 Content Analysis:")
    print("=" * 80)
    
    if not data or not isinstance(data, dict):
        print("❌ No valid data to analyze")
        return
    
    # Check if generation was refused
    refused_patterns = [
        "抱歉", "sorry", "无法", "cannot", "can't", "unable",
        "不能", "refuse", "拒绝"
    ]
    
    has_refusal = False
    refusal_fields = []
    
    # Count fields
    field_count = 0
    empty_fields = []
    
    for key, value in data.items():
        if key in ['predictionId', 'generatedAt', 'predictionDate', 'userId', 
                   'type', 'availableLanguages', 'requestedLanguage', 'returnedLanguage']:
            continue
            
        field_count += 1
        
        if isinstance(value, str):
            # Check for refusal patterns
            value_lower = value.lower()
            if any(pattern in value_lower for pattern in refused_patterns):
                has_refusal = True
                refusal_fields.append(key)
                print(f"⚠️  Field '{key}' contains refusal pattern:")
                print(f"   {value[:100]}...")
            
            # Check for empty fields
            if not value.strip():
                empty_fields.append(key)
    
    print(f"\n📈 Statistics:")
    print(f"   Total fields: {field_count}")
    print(f"   Empty fields: {len(empty_fields)}")
    if empty_fields:
        print(f"   Empty: {', '.join(empty_fields)}")
    
    if has_refusal:
        print(f"\n❌ REFUSAL DETECTED in {len(refusal_fields)} fields:")
        for field in refusal_fields:
            print(f"   - {field}")
    else:
        print(f"\n✅ No refusal patterns detected")
    
    # Sample some key fields
    print(f"\n📝 Sample Content:")
    sample_fields = ['fourPillars_coreIdentity', 'chineseAstrology_zodiacWhisper', 
                     'westernOverview_sunArchetype', 'destiny_pathIntro']
    
    for field in sample_fields:
        if field in data:
            value = data[field]
            if isinstance(value, str) and value:
                print(f"\n   {field}:")
                print(f"   {value[:150]}{'...' if len(value) > 150 else ''}")

def run_performance_test(token, test_count=3, language="zh"):
    """Run multiple tests to measure average performance"""
    print(f"\n{'='*80}")
    print(f"🧪 LIFETIME PERFORMANCE TEST")
    print(f"{'='*80}")
    print(f"Test count: {test_count}")
    print(f"Language: {language}")
    print(f"Time: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    
    # First, register user
    if not register_or_update_user(token):
        return
    
    # Wait a bit for registration to complete
    time.sleep(2)
    
    results = []
    
    for i in range(test_count):
        print(f"\n{'─'*80}")
        print(f"Test {i+1}/{test_count}")
        print(f"{'─'*80}")
        
        result = trigger_lifetime_generation(token, language)
        results.append(result)
        
        if result["success"]:
            print(f"✅ Generation completed in {result['generation_time']:.2f} seconds")
            
            # Analyze content
            analyze_lifetime_content(result["data"])
            
        else:
            print(f"❌ Generation failed: {result['status_code']}")
            print(f"   Error: {result['error'][:200]}...")
        
        # Wait between tests
        if i < test_count - 1:
            print(f"\n⏱️  Waiting 5 seconds before next test...")
            time.sleep(5)
    
    # Summary
    print(f"\n{'='*80}")
    print(f"📊 SUMMARY")
    print(f"{'='*80}")
    
    successful = [r for r in results if r["success"]]
    failed = [r for r in results if not r["success"]]
    
    print(f"Total tests: {test_count}")
    print(f"Successful: {len(successful)}")
    print(f"Failed: {len(failed)}")
    
    if successful:
        times = [r["generation_time"] for r in successful]
        print(f"\n⏱️  Generation Times:")
        print(f"   Average: {sum(times)/len(times):.2f}s")
        print(f"   Min: {min(times):.2f}s")
        print(f"   Max: {max(times):.2f}s")
    
    if failed:
        print(f"\n❌ Failed tests:")
        for i, r in enumerate(failed):
            print(f"   Test {i+1}: {r['status_code']} - {r['error'][:100]}")

if __name__ == "__main__":
    import sys
    
    if len(sys.argv) < 2:
        print("Usage: python test_lifetime_performance.py <token> [test_count] [language]")
        print("Example: python test_lifetime_performance.py eyJhbG... 3 zh")
        sys.exit(1)
    
    token = sys.argv[1]
    test_count = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    language = sys.argv[3] if len(sys.argv) > 3 else "zh"
    
    run_performance_test(token, test_count, language)

