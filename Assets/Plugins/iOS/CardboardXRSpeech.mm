// Speech-to-text for the Spatial Workspace's voice dictation: Apple's SFSpeechRecognizer fed by an
// AVAudioEngine microphone tap. VoiceDictation.cs is the C# side.
//
// Results go back to Unity with UnitySendMessage, to the GameObject named kReceiver (a component
// with OnNativeState / OnNativePartial / OnNativeFinal / OnNativeError methods, each taking a string):
//   OnNativeState  "auth:ok" | "auth:denied" | "started" | "ended"
//   OnNativePartial  the text so far, while the person is still talking
//   OnNativeFinal  the finished text
//   OnNativeError  a short message
//
// Needs NSSpeechRecognitionUsageDescription and NSMicrophoneUsageDescription in Info.plist (see
// Assets/Editor/ProjectBootstrap/IosSpeech.cs, which adds the first and links Speech.framework; the
// second is Player Settings > microphoneUsageDescription).
//
// While it listens, the audio session is switched to play-and-record with Bluetooth allowed, so that
// AirPods' microphone can be used, and put back as it was afterwards. AirPods drop to call quality
// for that time, as they do in any app that records.

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Speech/Speech.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static const char* const kReceiver = "VoiceDictationNative";

static SFSpeechRecognizer* s_Recognizer;
static SFSpeechAudioBufferRecognitionRequest* s_Request;
static SFSpeechRecognitionTask* s_Task;
static AVAudioEngine* s_Engine;
static BOOL s_Active;
static AVAudioSessionCategory s_SavedCategory;
static AVAudioSessionMode s_SavedMode;
static AVAudioSessionCategoryOptions s_SavedOptions;

static void Send(const char* method, NSString* text)
{
    NSString* copy = [text copy] ?: @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage(kReceiver, method, [copy UTF8String]);
    });
}

static void RestoreAudioSession()
{
    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;
    [session setCategory:s_SavedCategory mode:s_SavedMode options:s_SavedOptions error:&error];
}

// Stops everything that is running; safe to call more than once.
static void Teardown(BOOL notify)
{
    if (!s_Active)
        return;
    s_Active = NO;

    if (s_Engine)
    {
        [s_Engine stop];
        [s_Engine.inputNode removeTapOnBus:0];
        s_Engine = nil;
    }
    if (s_Request)
    {
        [s_Request endAudio];
        s_Request = nil;
    }
    if (s_Task)
    {
        [s_Task cancel];
        s_Task = nil;
    }
    RestoreAudioSession();
    if (notify)
        Send("OnNativeState", @"ended");
}

extern "C" void CardboardXR_SpeechRequestAuthorization()
{
    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
        if (status != SFSpeechRecognizerAuthorizationStatusAuthorized)
        {
            Send("OnNativeState", @"auth:denied");
            return;
        }
        void (^micDone)(BOOL) = ^(BOOL granted) {
            Send("OnNativeState", granted ? @"auth:ok" : @"auth:denied");
        };
        if (@available(iOS 17.0, *))
            [AVAudioApplication requestRecordPermissionWithCompletionHandler:micDone];
        else
            [[AVAudioSession sharedInstance] requestRecordPermission:micDone];
    }];
}

// localeId such as "pl-PL". onDeviceOnly: 1 to refuse the network (needs the language downloaded in
// Settings > General > Keyboard > Dictation languages on some iOS versions); 0 lets Apple's servers help.
extern "C" int CardboardXR_SpeechStart(const char* localeId, int onDeviceOnly)
{
    if (s_Active)
        return 1;

    NSLocale* locale = [NSLocale localeWithLocaleIdentifier:localeId ? [NSString stringWithUTF8String:localeId] : @"pl-PL"];
    s_Recognizer = [[SFSpeechRecognizer alloc] initWithLocale:locale];
    if (s_Recognizer == nil || !s_Recognizer.isAvailable)
    {
        Send("OnNativeError", @"Rozpoznawanie mowy jest niedostępne dla tego języka (sprawdź internet albo język dyktowania).");
        return 0;
    }
    if (onDeviceOnly && !s_Recognizer.supportsOnDeviceRecognition)
    {
        Send("OnNativeError", @"Ten język nie działa offline na tym iPhonie.");
        return 0;
    }

    AVAudioSession* session = [AVAudioSession sharedInstance];
    s_SavedCategory = session.category;
    s_SavedMode = session.mode;
    s_SavedOptions = session.categoryOptions;

    NSError* error = nil;
    [session setCategory:AVAudioSessionCategoryPlayAndRecord
                    mode:AVAudioSessionModeDefault
                 options:AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionAllowBluetooth |
                         AVAudioSessionCategoryOptionAllowBluetoothA2DP | AVAudioSessionCategoryOptionMixWithOthers
                   error:&error];
    if (error == nil)
        [session setActive:YES error:&error];
    if (error != nil)
    {
        Send("OnNativeError", [NSString stringWithFormat:@"Nie można uruchomić mikrofonu: %@", error.localizedDescription]);
        RestoreAudioSession();
        return 0;
    }

    s_Request = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
    s_Request.shouldReportPartialResults = YES;
    if (onDeviceOnly)
        s_Request.requiresOnDeviceRecognition = YES;
    if (@available(iOS 16.0, *))
        s_Request.addsPunctuation = YES;

    s_Engine = [[AVAudioEngine alloc] init];
    AVAudioInputNode* input = s_Engine.inputNode;
    AVAudioFormat* format = [input outputFormatForBus:0];
    if (format.sampleRate <= 0 || format.channelCount == 0)
    {
        Send("OnNativeError", @"Mikrofon nie zwrócił poprawnego formatu dźwięku.");
        s_Active = YES; // so Teardown cleans up
        Teardown(NO);
        return 0;
    }

    SFSpeechAudioBufferRecognitionRequest* request = s_Request;
    [input installTapOnBus:0 bufferSize:1024 format:format block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
        [request appendAudioPCMBuffer:buffer];
    }];

    [s_Engine prepare];
    if (![s_Engine startAndReturnError:&error])
    {
        Send("OnNativeError", [NSString stringWithFormat:@"Nie można uruchomić nagrywania: %@", error.localizedDescription]);
        s_Active = YES;
        Teardown(NO);
        return 0;
    }

    s_Active = YES;
    s_Task = [s_Recognizer recognitionTaskWithRequest:s_Request
                                        resultHandler:^(SFSpeechRecognitionResult* result, NSError* taskError) {
        if (result != nil)
        {
            NSString* text = result.bestTranscription.formattedString;
            if (result.isFinal)
                Send("OnNativeFinal", text);
            else
                Send("OnNativePartial", text);
        }
        if (taskError != nil || (result != nil && result.isFinal))
        {
            if (taskError != nil && result == nil)
            {
                // Code 1110 is "no speech detected": not worth an error.
                if (taskError.code != 1110)
                    Send("OnNativeError", taskError.localizedDescription);
            }
            dispatch_async(dispatch_get_main_queue(), ^{ Teardown(YES); });
        }
    }];

    Send("OnNativeState", @"started");
    return 1;
}

// Stops listening; the recognizer then delivers the final text (OnNativeFinal) and "ended".
extern "C" void CardboardXR_SpeechStop()
{
    if (!s_Active)
        return;
    if (s_Engine)
    {
        [s_Engine stop];
        [s_Engine.inputNode removeTapOnBus:0];
    }
    if (s_Request)
        [s_Request endAudio];
    // The task's result handler finishes the teardown. If it never answers, a guard cleans up.
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(4 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (s_Active)
            Teardown(YES);
    });
}

extern "C" int CardboardXR_SpeechIsActive()
{
    return s_Active ? 1 : 0;
}
