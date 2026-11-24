// The Workspace's AI assistant (AssistantSession.cs):
//  * the Claude API key kept in the iOS Keychain (this device only, readable after the first unlock),
//    never in a file or the app's settings;
//  * answers read aloud with AVSpeechSynthesizer (Polish or English voice), through the AirPods. While it
//    speaks, "start", "word:<location>:<length>" (the UTF-16 range of the word now spoken in the text) and
//    "end" go to the GameObject "AssistantNative" (OnSpeech), so the hologram can pulse with each word.
#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <AVFoundation/AVFoundation.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* const kService = @"CardboardXR.Assistant";

static NSMutableDictionary* Query(NSString* account)
{
    return [@{ (__bridge id)kSecClass : (__bridge id)kSecClassGenericPassword,
               (__bridge id)kSecAttrService : kService,
               (__bridge id)kSecAttrAccount : account } mutableCopy];
}

// 1 = stored
extern "C" int CardboardXR_KeychainSet(const char* account, const char* value)
{
    if (!account || !value)
        return 0;
    NSString* name = [NSString stringWithUTF8String:account];
    NSData* data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
    SecItemDelete((__bridge CFDictionaryRef)Query(name));
    NSMutableDictionary* item = Query(name);
    item[(__bridge id)kSecValueData] = data;
    item[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
    return SecItemAdd((__bridge CFDictionaryRef)item, NULL) == errSecSuccess ? 1 : 0;
}

// The value, or NULL. The returned string is malloc'd: the C# marshaller frees it.
extern "C" char* CardboardXR_KeychainGet(const char* account)
{
    if (!account)
        return NULL;
    NSMutableDictionary* query = Query([NSString stringWithUTF8String:account]);
    query[(__bridge id)kSecReturnData] = @YES;
    query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
    CFTypeRef result = NULL;
    if (SecItemCopyMatching((__bridge CFDictionaryRef)query, &result) != errSecSuccess || !result)
        return NULL;
    NSData* data = (__bridge_transfer NSData*)result;
    NSString* text = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    return text ? strdup(text.UTF8String) : NULL;
}

extern "C" void CardboardXR_KeychainDelete(const char* account)
{
    if (account)
        SecItemDelete((__bridge CFDictionaryRef)Query([NSString stringWithUTF8String:account]));
}

static AVSpeechSynthesizer* s_Synth;

static void SendSpeech(NSString* message)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage("AssistantNative", "OnSpeech", message.UTF8String);
    });
}

@interface CXRSpeechDelegate : NSObject <AVSpeechSynthesizerDelegate>
@end

@implementation CXRSpeechDelegate
- (void)speechSynthesizer:(AVSpeechSynthesizer*)synthesizer didStartSpeechUtterance:(AVSpeechUtterance*)utterance
{
    SendSpeech(@"start");
}
- (void)speechSynthesizer:(AVSpeechSynthesizer*)synthesizer didFinishSpeechUtterance:(AVSpeechUtterance*)utterance
{
    SendSpeech(@"end");
}
- (void)speechSynthesizer:(AVSpeechSynthesizer*)synthesizer didCancelSpeechUtterance:(AVSpeechUtterance*)utterance
{
    SendSpeech(@"end");
}
- (void)speechSynthesizer:(AVSpeechSynthesizer*)synthesizer willSpeakRangeOfSpeechString:(NSRange)range
                utterance:(AVSpeechUtterance*)utterance
{
    SendSpeech([NSString stringWithFormat:@"word:%lu:%lu", (unsigned long)range.location, (unsigned long)range.length]);
}
@end

static CXRSpeechDelegate* s_SpeechDelegate; // the synthesizer holds its delegate weakly

extern "C" void CardboardXR_Speak(const char* text, const char* language)
{
    if (!text)
        return;
    NSString* words = [NSString stringWithUTF8String:text];
    NSString* lang = language ? [NSString stringWithUTF8String:language] : @"pl-PL";
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!s_Synth)
        {
            s_Synth = [AVSpeechSynthesizer new];
            s_SpeechDelegate = [CXRSpeechDelegate new];
            s_Synth.delegate = s_SpeechDelegate;
        }
        [s_Synth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
        AVSpeechUtterance* utterance = [AVSpeechUtterance speechUtteranceWithString:words];
        utterance.voice = [AVSpeechSynthesisVoice voiceWithLanguage:lang];
        [s_Synth speakUtterance:utterance];
    });
}

extern "C" void CardboardXR_StopSpeaking(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [s_Synth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    });
}
