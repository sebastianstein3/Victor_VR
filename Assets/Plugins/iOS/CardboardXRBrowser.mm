// A web browser for the Spatial Workspace: WKWebView (Safari's engine) rendered into a Unity texture,
// so a page can be a window in VR (both eyes) instead of a flat overlay on the phone's screen.
// BrowserView.cs is the C# side. Written for this project (the idea of snapshotting a WKWebView into a
// texture is common; no code was taken from other projects).
//
// How: each web view sits in the app's window BEHIND Unity's view (so WebKit keeps rendering it, but
// it's never seen directly); takeSnapshot copies it into an RGBA buffer that C# uploads to a texture.
//
// Security:
// - Only http(s) and about:blank are loaded; file:, javascript:, data:, tel:, sms:, app links and other
//   schemes are refused, in user-typed URLs and in page navigation alike. (ATS in Info.plist also
//   blocks plain http except on the local network.)
// - Pages can't open windows by themselves; target=_blank links open in the same view.
// - No script message handlers: a page has no way to call into the app.
// - Text and URLs passed into pages go through JSON encoding, never string concatenation.
// - Cookies and logins persist in the app's own WebKit store (like Safari's, but separate from it).

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static const char* const kReceiver = "BrowserNative";

@interface CXRBrowser : NSObject <WKNavigationDelegate, WKUIDelegate>
@property (nonatomic) int identifier;
@property (nonatomic, strong) WKWebView* webView;
@property (nonatomic) int width;
@property (nonatomic) int height;
@property (nonatomic) uint8_t* pixels;  // RGBA, bottom row first (as Unity's LoadRawTextureData expects)
@property (nonatomic) BOOL hasNewFrame;
@property (nonatomic) BOOL snapshotting;
@end

static NSMutableDictionary<NSNumber*, CXRBrowser*>* s_Browsers;
static int s_NextId = 1;

static void SendState(CXRBrowser* browser)
{
    NSDictionary* state = @{
        @"id": @(browser.identifier),
        @"url": browser.webView.URL.absoluteString ?: @"",
        @"title": browser.webView.title ?: @"",
        @"loading": @(browser.webView.isLoading),
        @"canGoBack": @(browser.webView.canGoBack),
        @"canGoForward": @(browser.webView.canGoForward),
    };
    NSData* json = [NSJSONSerialization dataWithJSONObject:state options:0 error:nil];
    NSString* text = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"{}";
    UnitySendMessage(kReceiver, "OnBrowserState", text.UTF8String);
}

static BOOL IsAllowedURL(NSURL* url)
{
    NSString* scheme = url.scheme.lowercaseString;
    if ([scheme isEqualToString:@"http"] || [scheme isEqualToString:@"https"])
        return url.host.length > 0;
    return [url.absoluteString isEqualToString:@"about:blank"];
}

// A JSON string literal for JavaScript: quotes, backslashes, newlines and </script> all escaped.
static NSString* JSString(NSString* value)
{
    NSData* json = [NSJSONSerialization dataWithJSONObject:@[value ?: @""] options:0 error:nil];
    NSString* array = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"[\"\"]";
    // ["..."] -> "..."
    return [array substringWithRange:NSMakeRange(1, array.length - 2)];
}

@implementation CXRBrowser

- (void)dealloc
{
    free(_pixels);
}

- (void)webView:(WKWebView*)webView decidePolicyForNavigationAction:(WKNavigationAction*)action
    decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler
{
    NSURL* url = action.request.URL;
    BOOL allowed = IsAllowedURL(url);
    // Frames inside a page may also use the web's own in-page schemes (iframes from about:srcdoc,
    // blob: and data: for media); still never file:, javascript: or app/phone schemes
    if (!allowed && action.targetFrame != nil && !action.targetFrame.isMainFrame)
    {
        NSString* scheme = url.scheme.lowercaseString;
        allowed = [scheme isEqualToString:@"about"] || [scheme isEqualToString:@"blob"] || [scheme isEqualToString:@"data"];
    }
    decisionHandler(allowed ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}

// Downloads (a response the view can't show) are refused
- (void)webView:(WKWebView*)webView decidePolicyForNavigationResponse:(WKNavigationResponse*)response
    decisionHandler:(void (^)(WKNavigationResponsePolicy))decisionHandler
{
    decisionHandler(response.canShowMIMEType ? WKNavigationResponsePolicyAllow : WKNavigationResponsePolicyCancel);
}

// target=_blank / window.open from a click: load it here instead of a new window
- (WKWebView*)webView:(WKWebView*)webView createWebViewWithConfiguration:(WKWebViewConfiguration*)configuration
    forNavigationAction:(WKNavigationAction*)action windowFeatures:(WKWindowFeatures*)features
{
    if (action.request.URL != nil && IsAllowedURL(action.request.URL))
        [webView loadRequest:action.request];
    return nil;
}

- (void)webView:(WKWebView*)webView didStartProvisionalNavigation:(WKNavigation*)navigation { SendState(self); }
- (void)webView:(WKWebView*)webView didFinishNavigation:(WKNavigation*)navigation { SendState(self); }
- (void)webView:(WKWebView*)webView didFailNavigation:(WKNavigation*)navigation withError:(NSError*)error { SendState(self); }
- (void)webView:(WKWebView*)webView didFailProvisionalNavigation:(WKNavigation*)navigation withError:(NSError*)error { SendState(self); }

// JavaScript alert/confirm/prompt: answered at once (no native dialog over the headset view)
- (void)webView:(WKWebView*)webView runJavaScriptAlertPanelWithMessage:(NSString*)message
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(void))completionHandler { completionHandler(); }
- (void)webView:(WKWebView*)webView runJavaScriptConfirmPanelWithMessage:(NSString*)message
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(BOOL))completionHandler { completionHandler(NO); }
- (void)webView:(WKWebView*)webView runJavaScriptTextInputPanelWithPrompt:(NSString*)prompt defaultText:(NSString*)text
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(NSString*))completionHandler { completionHandler(nil); }

// Copies the view into the pixel buffer (asynchronously; one snapshot at a time)
- (void)snapshot
{
    if (self.snapshotting)
        return;
    self.snapshotting = YES;
    WKSnapshotConfiguration* configuration = [[WKSnapshotConfiguration alloc] init];
    configuration.snapshotWidth = @(self.width / UIScreen.mainScreen.scale); // points; the image comes at screen scale
    __weak CXRBrowser* weakSelf = self;
    [self.webView takeSnapshotWithConfiguration:configuration completionHandler:^(UIImage* image, NSError* error) {
        CXRBrowser* browser = weakSelf;
        if (browser == nil)
            return;
        browser.snapshotting = NO;
        if (image == nil || image.CGImage == nil || browser.pixels == NULL)
            return;
        CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
        CGContextRef context = CGBitmapContextCreate(browser.pixels, browser.width, browser.height, 8, browser.width * 4,
                                                     space, kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
        CGColorSpaceRelease(space);
        if (context == NULL)
            return;
        // CoreGraphics draws with the origin at the bottom-left, which is Unity's row order already
        CGContextDrawImage(context, CGRectMake(0, 0, browser.width, browser.height), image.CGImage);
        CGContextRelease(context);
        browser.hasNewFrame = YES;
    }];
}

@end

static CXRBrowser* Find(int identifier) { return s_Browsers[@(identifier)]; }

static void Evaluate(CXRBrowser* browser, NSString* script)
{
    [browser.webView evaluateJavaScript:script completionHandler:nil];
}

// width/height: the texture size in pixels. Returns the browser's id, or 0.
extern "C" int CardboardXR_BrowserCreate(int width, int height)
{
    if (width < 64 || height < 64 || width > 2048 || height > 2048)
        return 0;
    if (s_Browsers == nil)
        s_Browsers = [NSMutableDictionary dictionary];

    UIWindow* window = UIApplication.sharedApplication.keyWindow;
    if (window == nil)
    {
        for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
        {
            if ([scene isKindOfClass:[UIWindowScene class]] && ((UIWindowScene*)scene).windows.firstObject != nil)
            {
                window = ((UIWindowScene*)scene).windows.firstObject;
                break;
            }
        }
    }
    if (window == nil)
        return 0;

    CXRBrowser* browser = [[CXRBrowser alloc] init];
    browser.identifier = s_NextId++;
    browser.width = width;
    browser.height = height;
    browser.pixels = (uint8_t*)calloc((size_t)width * height * 4, 1);

    WKWebViewConfiguration* configuration = [[WKWebViewConfiguration alloc] init];
    configuration.preferences.javaScriptCanOpenWindowsAutomatically = NO;
    configuration.allowsInlineMediaPlayback = YES;
    configuration.dataDetectorTypes = WKDataDetectorTypeNone; // no tappable phone numbers/addresses
    CGFloat scale = UIScreen.mainScreen.scale;
    CGRect frame = CGRectMake(0, 0, width / scale, height / scale);
    browser.webView = [[WKWebView alloc] initWithFrame:frame configuration:configuration];
    browser.webView.navigationDelegate = browser;
    browser.webView.UIDelegate = browser;
    browser.webView.allowsLinkPreview = NO;
    browser.webView.userInteractionEnabled = NO; // the phone's screen touches go to Unity, not to the page
    // Behind Unity's view: rendered by WebKit, never visible itself
    [window insertSubview:browser.webView atIndex:0];

    s_Browsers[@(browser.identifier)] = browser;
    return browser.identifier;
}

extern "C" void CardboardXR_BrowserDestroy(int identifier)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil)
        return;
    [browser.webView stopLoading];
    browser.webView.navigationDelegate = nil;
    browser.webView.UIDelegate = nil;
    [browser.webView removeFromSuperview];
    [s_Browsers removeObjectForKey:@(identifier)];
}

// Returns 1 if the URL was allowed and is loading.
extern "C" int CardboardXR_BrowserLoad(int identifier, const char* url)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || url == NULL)
        return 0;
    NSURL* target = [NSURL URLWithString:[NSString stringWithUTF8String:url]];
    if (target == nil || !IsAllowedURL(target))
        return 0;
    [browser.webView loadRequest:[NSURLRequest requestWithURL:target]];
    return 1;
}

extern "C" void CardboardXR_BrowserGoBack(int identifier) { [Find(identifier).webView goBack]; }
extern "C" void CardboardXR_BrowserGoForward(int identifier) { [Find(identifier).webView goForward]; }
extern "C" void CardboardXR_BrowserReload(int identifier) { [Find(identifier).webView reload]; }

// A tap at u, v (0..1 from the top-left of the page view): clicks and focuses what is there
extern "C" void CardboardXR_BrowserTap(int identifier, float u, float v)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || !(u >= 0 && u <= 1 && v >= 0 && v <= 1))
        return;
    NSString* script = [NSString stringWithFormat:
        @"(function(x,y){var e=document.elementFromPoint(x*window.innerWidth,y*window.innerHeight);"
         "if(!e)return;if(e.focus)e.focus();e.click&&e.click();})(%f,%f);", u, v];
    Evaluate(browser, script);
}

// Scrolls by a fraction of the view's height (positive = down)
extern "C" void CardboardXR_BrowserScroll(int identifier, float pages)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || !isfinite(pages))
        return;
    Evaluate(browser, [NSString stringWithFormat:@"window.scrollBy(0,%f*window.innerHeight);", fmaxf(-3.f, fminf(3.f, pages))]);
}

// Types text into the focused field of the page
extern "C" void CardboardXR_BrowserInsertText(int identifier, const char* text)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || text == NULL)
        return;
    NSString* value = JSString([NSString stringWithUTF8String:text]);
    Evaluate(browser, [NSString stringWithFormat:
        @"(function(t){var e=document.activeElement;if(!e)return;"
         "if(document.execCommand&&document.execCommand('insertText',false,t))return;"
         "if('value' in e){var s=e.selectionStart||e.value.length;e.value=e.value.slice(0,s)+t+e.value.slice(e.selectionEnd||s);"
         "e.dispatchEvent(new Event('input',{bubbles:true}));}})(%@);", value]);
}

// "backspace" or "enter" in the focused field
extern "C" void CardboardXR_BrowserKey(int identifier, const char* key)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || key == NULL)
        return;
    NSString* name = [NSString stringWithUTF8String:key];
    if ([name isEqualToString:@"backspace"])
    {
        Evaluate(browser, @"(function(){var e=document.activeElement;if(!e)return;"
                           "if(document.execCommand&&document.execCommand('delete'))return;"
                           "if('value' in e){e.value=e.value.slice(0,-1);e.dispatchEvent(new Event('input',{bubbles:true}));}})();");
    }
    else if ([name isEqualToString:@"enter"])
    {
        Evaluate(browser, @"(function(){var e=document.activeElement;if(!e)return;"
                           "['keydown','keypress','keyup'].forEach(function(t){e.dispatchEvent(new KeyboardEvent(t,{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true}));});"
                           "if(e.form&&e.tagName!=='TEXTAREA'){if(e.form.requestSubmit)e.form.requestSubmit();else e.form.submit();}})();");
    }
}

// Asks for a new snapshot and copies the last finished one into dst (RGBA, width*height*4 bytes).
// Returns 1 when dst got a new frame.
extern "C" int CardboardXR_BrowserCopyFrame(int identifier, void* dst, int capacity)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || dst == NULL)
        return 0;
    [browser snapshot];
    size_t size = (size_t)browser.width * browser.height * 4;
    if (!browser.hasNewFrame || capacity < (int)size)
        return 0;
    memcpy(dst, browser.pixels, size);
    browser.hasNewFrame = NO;
    return 1;
}
