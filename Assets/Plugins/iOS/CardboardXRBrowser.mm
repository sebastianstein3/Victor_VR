// A web browser for the Spatial Workspace: WKWebView (Safari's engine) rendered into a Unity texture,
// so a page can be a window in VR (both eyes) instead of a flat overlay on the phone's screen.
// BrowserView.cs is the C# side. Written for this project. The technique of drawing a WKWebView into
// a Metal texture is common (webviewtextureios uses it too, but has no license, so none of its code is
// used here; its pitfalls are avoided: no ownership transfer of Unity's texture, no per-frame leaks).
//
// Rendering: each web view sits in the app's window BEHIND Unity's view, so WebKit keeps rendering it
// but it is never seen directly. Each frame it is drawn (drawViewHierarchyInRect, falling back to the
// asynchronous takeSnapshot) into a reused bitmap, bottom row first as Unity expects, and written
// straight into Unity's Metal texture (replaceRegion); or copied out for C# when that isn't possible.
//
// Like current browsers: loading progress, the real host and whether the page is fully https are
// reported (C# shows them), trackers/ads are blocked with WebKit content rules (as Safari's content
// blockers), pages get dark mode, text zoom, and private windows keep nothing after they close.
//
// Security:
// - The main frame loads only http(s) with a host, or about:blank; file:, javascript:, data:, tel:, sms:,
//   app links and other schemes are refused (typed or from the page). Frames inside a page may also use
//   about:/blob:/data:. ATS in Info.plist blocks plain http except on the local network.
// - No popups by script; target=_blank opens in the same view. No downloads. No JavaScript bridge to
//   the app. alert/confirm/prompt are answered at once; camera/microphone requests are denied (their
//   native prompts would be invisible in the headset). Text passed into pages is JSON-encoded.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>
#import <Metal/Metal.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static const char* const kReceiver = "BrowserNative";

// Common tracking and advertising hosts (a short list in the spirit of Safari's/Brave's blockers),
// blocked by where a request goes, whatever page makes it.
static NSArray<NSString*>* TrackerHosts()
{
    return @[@"doubleclick.net", @"googlesyndication.com", @"googleadservices.com", @"google-analytics.com",
             @"googletagmanager.com", @"adservice.google.com", @"connect.facebook.net", @"scorecardresearch.com",
             @"criteo.com", @"criteo.net", @"taboola.com", @"outbrain.com", @"adnxs.com", @"hotjar.com",
             @"quantserve.com", @"amazon-adsystem.com", @"moatads.com", @"rubiconproject.com", @"pubmatic.com",
             @"openx.net", @"casalemedia.com", @"smartadserver.com", @"gemius.pl", @"adocean.pl"];
}

static WKContentRuleList* s_RuleList;
static BOOL s_RulesCompiling;

@interface CXRBrowser : NSObject <WKNavigationDelegate, WKUIDelegate>
@property (nonatomic) int identifier;
@property (nonatomic, strong) WKWebView* webView;
@property (nonatomic) int width;
@property (nonatomic) int height;
@property (nonatomic) CGContextRef context;   // reused, flipped: memory row 0 = bottom of the page
@property (nonatomic) uint8_t* pixels;
@property (nonatomic, weak) id<MTLTexture> texture; // Unity's; not owned (C# clears it before freeing)
@property (nonatomic) BOOL hasNewFrame;
@property (nonatomic) BOOL snapshotting;
@property (nonatomic) int desktopMode; // 0 auto, 1 always the desktop site, 2 always the mobile site
@end

// Sites that only work as desktop sites (their mobile pages send you to the app instead)
static BOOL NeedsDesktopSite(NSString* host)
{
    host = host.lowercaseString;
    for (NSString* site in @[@"messenger.com", @"web.whatsapp.com", @"facebook.com"])
    {
        if ([host isEqualToString:site] || [host hasSuffix:[@"." stringByAppendingString:site]])
            return YES;
    }
    return NO;
}

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
        @"secure": @([browser.webView.URL.scheme.lowercaseString isEqualToString:@"https"] && browser.webView.hasOnlySecureContent),
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

// A JSON string literal for JavaScript: quotes, backslashes, control characters and "/" escaped.
static NSString* JSString(NSString* value)
{
    NSData* json = [NSJSONSerialization dataWithJSONObject:@[value ?: @""] options:0 error:nil];
    NSString* array = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"[\"\"]";
    return [array substringWithRange:NSMakeRange(1, array.length - 2)]; // ["..."] -> "..."
}

static void CompileRulesOnce()
{
    if (s_RuleList != nil || s_RulesCompiling)
        return;
    s_RulesCompiling = YES;
    NSMutableArray* rules = [NSMutableArray array];
    for (NSString* host in TrackerHosts())
    {
        NSString* escaped = [host stringByReplacingOccurrencesOfString:@"." withString:@"\\."];
        [rules addObject:@{@"trigger": @{@"url-filter": [NSString stringWithFormat:@"^https?://([^/]*\\.)?%@[/:]", escaped]},
                           @"action": @{@"type": @"block"}}];
    }
    NSData* json = [NSJSONSerialization dataWithJSONObject:rules options:0 error:nil];
    NSString* text = [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding];
    [WKContentRuleListStore.defaultStore compileContentRuleListForIdentifier:@"cardboardxr-trackers"
        encodedContentRuleList:text completionHandler:^(WKContentRuleList* list, NSError* error) {
        s_RulesCompiling = NO;
        if (list == nil)
            return;
        s_RuleList = list;
        for (CXRBrowser* browser in s_Browsers.allValues)
            [browser.webView.configuration.userContentController addContentRuleList:list];
    }];
}

@implementation CXRBrowser

- (void)dealloc
{
    if (_context != NULL)
        CGContextRelease(_context);
    free(_pixels);
}

// Like Safari's "Request Desktop Website": the desktop site when asked, for sites that need it, and
// (auto) when the window is wide (900+ points, as wide as a laptop browser window)
- (BOOL)wantsDesktopSiteFor:(NSURL*)url
{
    if (_desktopMode == 1)
        return YES;
    if (_desktopMode == 2)
        return NO;
    return NeedsDesktopSite(url.host ?: @"") || _webView.bounds.size.width >= 900;
}

- (void)webView:(WKWebView*)webView decidePolicyForNavigationAction:(WKNavigationAction*)action
    preferences:(WKWebpagePreferences*)preferences
    decisionHandler:(void (^)(WKNavigationActionPolicy, WKWebpagePreferences*))decisionHandler
{
    NSURL* url = action.request.URL;
    preferences.preferredContentMode = [self wantsDesktopSiteFor:url] ? WKContentModeDesktop : WKContentModeMobile;
    BOOL allowed = IsAllowedURL(url);
    if (!allowed && action.targetFrame != nil && !action.targetFrame.isMainFrame)
    {
        NSString* scheme = url.scheme.lowercaseString;
        allowed = [scheme isEqualToString:@"about"] || [scheme isEqualToString:@"blob"] || [scheme isEqualToString:@"data"];
    }
    decisionHandler(allowed ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel, preferences);
}

- (void)webView:(WKWebView*)webView decidePolicyForNavigationResponse:(WKNavigationResponse*)response
    decisionHandler:(void (^)(WKNavigationResponsePolicy))decisionHandler
{
    decisionHandler(response.canShowMIMEType ? WKNavigationResponsePolicyAllow : WKNavigationResponsePolicyCancel);
}

- (WKWebView*)webView:(WKWebView*)webView createWebViewWithConfiguration:(WKWebViewConfiguration*)configuration
    forNavigationAction:(WKNavigationAction*)action windowFeatures:(WKWindowFeatures*)features
{
    if (action.request.URL != nil && IsAllowedURL(action.request.URL))
        [webView loadRequest:action.request];
    return nil;
}

- (void)webView:(WKWebView*)webView didStartProvisionalNavigation:(WKNavigation*)navigation { SendState(self); }
- (void)webView:(WKWebView*)webView didCommitNavigation:(WKNavigation*)navigation { SendState(self); }
- (void)webView:(WKWebView*)webView didFinishNavigation:(WKNavigation*)navigation { SendState(self); }
- (void)webView:(WKWebView*)webView didFailNavigation:(WKNavigation*)navigation withError:(NSError*)error { SendState(self); }
- (void)webView:(WKWebView*)webView didFailProvisionalNavigation:(WKNavigation*)navigation withError:(NSError*)error { SendState(self); }

- (void)webView:(WKWebView*)webView runJavaScriptAlertPanelWithMessage:(NSString*)message
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(void))completionHandler { completionHandler(); }
- (void)webView:(WKWebView*)webView runJavaScriptConfirmPanelWithMessage:(NSString*)message
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(BOOL))completionHandler { completionHandler(NO); }
- (void)webView:(WKWebView*)webView runJavaScriptTextInputPanelWithPrompt:(NSString*)prompt defaultText:(NSString*)text
    initiatedByFrame:(WKFrameInfo*)frame completionHandler:(void (^)(NSString*))completionHandler { completionHandler(nil); }

// Camera / microphone for a page: denied (the system prompt would be invisible in the headset)
- (void)webView:(WKWebView*)webView requestMediaCapturePermissionForOrigin:(WKSecurityOrigin*)origin
    initiatedByFrame:(WKFrameInfo*)frame type:(WKMediaCaptureType)type
    decisionHandler:(void (^)(WKPermissionDecision))decisionHandler API_AVAILABLE(ios(15.0))
{
    decisionHandler(WKPermissionDecisionDeny);
}

- (BOOL)makeContext
{
    if (_context != NULL)
        return YES;
    _pixels = (uint8_t*)calloc((size_t)_width * _height * 4, 1);
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    _context = CGBitmapContextCreate(_pixels, _width, _height, 8, _width * 4, space,
                                     kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(space);
    if (_context == NULL)
        return NO;
    // A bitmap context's memory starts at the TOP row; Unity textures start at the BOTTOM row. Draw
    // through a vertical flip so memory row 0 holds the bottom of the page.
    CGContextTranslateCTM(_context, 0, _height);
    CGContextScaleCTM(_context, 1, -1);
    return YES;
}

// Draws the page into the bitmap. YES when a new frame is there.
- (BOOL)capture
{
    if (![self makeContext])
        return NO;
    // Fast path: draw the view hierarchy into the (flipped) context through UIKit
    UIGraphicsPushContext(_context);
    CGContextSaveGState(_context);
    CGContextSetRGBFillColor(_context, 1, 1, 1, 1); // pages without a background are white, as in Safari
    CGContextFillRect(_context, CGRectMake(0, 0, _width, _height));
    // UIKit drawing in a flipped context expects y down: undo our flip for UIKit, which flips itself
    CGContextTranslateCTM(_context, 0, _height);
    CGContextScaleCTM(_context, _width / _webView.bounds.size.width, -_height / _webView.bounds.size.height);
    BOOL drawn = [_webView drawViewHierarchyInRect:_webView.bounds afterScreenUpdates:NO];
    CGContextRestoreGState(_context);
    UIGraphicsPopContext();
    if (drawn)
        return YES;

    // Slow path: WebKit's own snapshot (asynchronous); the frame arrives with a later call
    if (_snapshotting)
        return NO;
    _snapshotting = YES;
    WKSnapshotConfiguration* configuration = [[WKSnapshotConfiguration alloc] init];
    configuration.snapshotWidth = @(_webView.bounds.size.width);
    __weak CXRBrowser* weakSelf = self;
    [_webView takeSnapshotWithConfiguration:configuration completionHandler:^(UIImage* image, NSError* error) {
        CXRBrowser* browser = weakSelf;
        if (browser == nil)
            return;
        browser.snapshotting = NO;
        if (image.CGImage == nil || browser.context == NULL)
            return;
        // Through the flip set in makeContext, CGContextDrawImage puts the image's top at memory's end
        CGContextSetRGBFillColor(browser.context, 1, 1, 1, 1);
        CGContextFillRect(browser.context, CGRectMake(0, 0, browser.width, browser.height));
        CGContextDrawImage(browser.context, CGRectMake(0, 0, browser.width, browser.height), image.CGImage);
        browser.hasNewFrame = YES;
    }];
    return NO;
}

@end

static CXRBrowser* Find(int identifier) { return s_Browsers[@(identifier)]; }

static void Evaluate(CXRBrowser* browser, NSString* script)
{
    [browser.webView evaluateJavaScript:script completionHandler:nil];
}

static UIWindow* AppWindow()
{
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:[UIWindowScene class]])
            continue;
        for (UIWindow* window in ((UIWindowScene*)scene).windows)
        {
            if (window.isKeyWindow)
                return window;
        }
        if (((UIWindowScene*)scene).windows.firstObject != nil)
            return ((UIWindowScene*)scene).windows.firstObject;
    }
    return nil;
}

// width/height: texture pixels; privateMode 1: nothing is kept (cookies, cache, history) after it closes.
// Returns the browser's id, or 0.
extern "C" int CardboardXR_BrowserCreate(int width, int height, int privateMode)
{
    if (width < 64 || height < 64 || width > 2048 || height > 2048)
        return 0;
    if (s_Browsers == nil)
        s_Browsers = [NSMutableDictionary dictionary];
    UIWindow* window = AppWindow();
    if (window == nil)
        return 0;
    CompileRulesOnce();

    CXRBrowser* browser = [[CXRBrowser alloc] init];
    browser.identifier = s_NextId++;
    browser.width = width;
    browser.height = height;

    WKWebViewConfiguration* configuration = [[WKWebViewConfiguration alloc] init];
    configuration.preferences.javaScriptCanOpenWindowsAutomatically = NO;
    configuration.allowsInlineMediaPlayback = YES;
    configuration.dataDetectorTypes = WKDataDetectorTypeNone;
    configuration.websiteDataStore = privateMode ? WKWebsiteDataStore.nonPersistentDataStore : WKWebsiteDataStore.defaultDataStore;
    if (s_RuleList != nil)
        [configuration.userContentController addContentRuleList:s_RuleList];

    // Points such that the page is laid out like on a wide phone; the texture has 2 px per point
    CGRect frame = CGRectMake(0, 0, width / 2.0, height / 2.0);
    browser.webView = [[WKWebView alloc] initWithFrame:frame configuration:configuration];
    browser.webView.navigationDelegate = browser;
    browser.webView.UIDelegate = browser;
    browser.webView.allowsLinkPreview = NO;
    browser.webView.userInteractionEnabled = NO; // the phone's screen touches go to Unity, not to the page
    browser.webView.overrideUserInterfaceStyle = UIUserInterfaceStyleDark; // sites with dark mode use it
    [window insertSubview:browser.webView atIndex:0]; // behind Unity's view

    s_Browsers[@(browser.identifier)] = browser;
    return browser.identifier;
}

extern "C" void CardboardXR_BrowserDestroy(int identifier)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil)
        return;
    browser.texture = nil;
    [browser.webView stopLoading];
    browser.webView.navigationDelegate = nil;
    browser.webView.UIDelegate = nil;
    [browser.webView removeFromSuperview];
    [s_Browsers removeObjectForKey:@(identifier)];
}

// A new page size in pixels (the window was stretched): the web view is laid out again at half that in
// points; the bitmap is made again on the next capture. Call SetTexture with a texture of the new size.
extern "C" void CardboardXR_BrowserResize(int identifier, int width, int height)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || width < 64 || height < 64 || width > 2048 || height > 2048)
        return;
    browser.texture = nil;
    browser.hasNewFrame = NO;
    if (browser.context != NULL)
    {
        CGContextRelease(browser.context);
        browser.context = NULL;
    }
    free(browser.pixels);
    browser.pixels = NULL;
    NSURL* current = browser.webView.URL;
    BOOL wasDesktop = current != nil && [browser wantsDesktopSiteFor:current];
    browser.width = width;
    browser.height = height;
    browser.webView.frame = CGRectMake(0, 0, width / 2.0, height / 2.0);
    // Stretched past (or back under) the desktop width: load the other version of the page
    if (current != nil && [browser wantsDesktopSiteFor:current] != wasDesktop)
        [browser.webView reload];
}

// 0 auto, 1 desktop site, 2 mobile site; reloads the page in the chosen version
extern "C" void CardboardXR_BrowserSetDesktopMode(int identifier, int mode)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || mode < 0 || mode > 2)
        return;
    browser.desktopMode = mode;
    if (browser.webView.URL != nil)
        [browser.webView reload];
}

// Unity's texture (Texture2D.GetNativeTexturePtr, Metal) to write frames into; NULL to stop.
extern "C" void CardboardXR_BrowserSetTexture(int identifier, void* texture)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil)
        return;
    // __bridge: Unity keeps ownership (no retain/release transfer)
    browser.texture = texture != NULL ? (__bridge id<MTLTexture>)texture : nil;
}

// Captures the page and writes it into the texture set with SetTexture. Returns 1 when written.
extern "C" int CardboardXR_BrowserUpdateTexture(int identifier)
{
    CXRBrowser* browser = Find(identifier);
    id<MTLTexture> texture = browser.texture;
    if (browser == nil || texture == nil)
        return 0;
    BOOL fresh = [browser capture] || browser.hasNewFrame;
    if (!fresh || (int)texture.width != browser.width || (int)texture.height != browser.height)
        return 0;
    browser.hasNewFrame = NO;
    [texture replaceRegion:MTLRegionMake2D(0, 0, browser.width, browser.height) mipmapLevel:0
                 withBytes:browser.pixels bytesPerRow:browser.width * 4];
    return 1;
}

// For when the texture can't be written directly: copies the frame into dst (RGBA, bottom row first).
extern "C" int CardboardXR_BrowserCopyFrame(int identifier, void* dst, int capacity)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || dst == NULL)
        return 0;
    BOOL fresh = [browser capture] || browser.hasNewFrame;
    size_t size = (size_t)browser.width * browser.height * 4;
    if (!fresh || capacity < (int)size)
        return 0;
    browser.hasNewFrame = NO;
    memcpy(dst, browser.pixels, size);
    return 1;
}

extern "C" int CardboardXR_BrowserLoad(int identifier, const char* url)
{
    CXRBrowser* browser = Find(identifier);
    NSString* text = url != NULL ? [NSString stringWithUTF8String:url] : nil;
    if (browser == nil || text == nil)
        return 0;
    NSURL* target = [NSURL URLWithString:text];
    if (target == nil || !IsAllowedURL(target))
        return 0;
    [browser.webView loadRequest:[NSURLRequest requestWithURL:target]];
    return 1;
}

extern "C" void CardboardXR_BrowserGoBack(int identifier) { [Find(identifier).webView goBack]; }
extern "C" void CardboardXR_BrowserGoForward(int identifier) { [Find(identifier).webView goForward]; }
extern "C" void CardboardXR_BrowserReload(int identifier) { [Find(identifier).webView reload]; }

// Loading progress 0..1, or -1 when not loading
extern "C" float CardboardXR_BrowserProgress(int identifier)
{
    WKWebView* view = Find(identifier).webView;
    return view != nil && view.isLoading ? (float)view.estimatedProgress : -1.f;
}

// Text zoom like Safari's "Aa": 0.5..3
extern "C" void CardboardXR_BrowserSetZoom(int identifier, float zoom)
{
    WKWebView* view = Find(identifier).webView;
    if (view != nil && isfinite(zoom))
        view.pageZoom = fmaxf(0.5f, fminf(3.f, zoom));
}

extern "C" void CardboardXR_BrowserTap(int identifier, float u, float v)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || !(u >= 0 && u <= 1 && v >= 0 && v <= 1))
        return;
    Evaluate(browser, [NSString stringWithFormat:
        @"(function(x,y){var e=document.elementFromPoint(x*window.innerWidth,y*window.innerHeight);"
         "if(!e)return;if(e.focus)e.focus();e.click&&e.click();})(%f,%f);", u, v]);
}

extern "C" void CardboardXR_BrowserScroll(int identifier, float pages)
{
    CXRBrowser* browser = Find(identifier);
    if (browser == nil || !isfinite(pages))
        return;
    Evaluate(browser, [NSString stringWithFormat:@"window.scrollBy({top:%f*window.innerHeight,behavior:'smooth'});",
                       fmaxf(-3.f, fminf(3.f, pages))]);
}

extern "C" void CardboardXR_BrowserInsertText(int identifier, const char* text)
{
    CXRBrowser* browser = Find(identifier);
    NSString* value = text != NULL ? [NSString stringWithUTF8String:text] : nil;
    if (browser == nil || value == nil)
        return;
    Evaluate(browser, [NSString stringWithFormat:
        @"(function(t){var e=document.activeElement;if(!e)return;"
         "if(document.execCommand&&document.execCommand('insertText',false,t))return;"
         "if('value' in e){var s=e.selectionStart||e.value.length;e.value=e.value.slice(0,s)+t+e.value.slice(e.selectionEnd||s);"
         "e.dispatchEvent(new Event('input',{bubbles:true}));}})(%@);", JSString(value)]);
}

extern "C" void CardboardXR_BrowserKey(int identifier, const char* key)
{
    CXRBrowser* browser = Find(identifier);
    NSString* name = key != NULL ? [NSString stringWithUTF8String:key] : nil;
    if (browser == nil || name == nil)
        return;
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
