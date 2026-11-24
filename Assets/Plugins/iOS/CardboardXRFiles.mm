// Files from outside the app for the Workspace (FileImport.cs, "Więcej > Pliki"):
//  * the system file picker (iCloud Drive, On My iPhone, Google Drive and other providers) copies the
//    chosen files in. It appears on the phone's screen, so the phone comes out of the goggles for it;
//  * one linked folder (e.g. iCloud Drive/VisionPro), chosen once with the same picker, is remembered
//    as a security-scoped bookmark and can then be listed and copied from inside the goggles. Files
//    not yet downloaded from iCloud are fetched by the coordinated read.
// Each file goes to the folder its kind is opened from: PDF, Filmy, Modele, Przestrzenne (spatial
// photos and videos, _sbs pictures, ordinary photos) or Srodowiska (2:1 panoramas); the rest to Inne.
// Results go to the GameObject "FileImportNative" as JSON (OnImported, OnListed, OnLinked).
#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <ImageIO/ImageIO.h>
#import <AVFoundation/AVFoundation.h>
#import <PDFKit/PDFKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);
extern UIViewController* UnityGetGLViewController();

static NSString* const kBookmarkKey = @"CardboardXR.LinkedFolder";

static void Send(const char* method, NSDictionary* payload)
{
    NSData* json = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString* text = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"{\"error\":\"JSON\"}";
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage("FileImportNative", method, text.UTF8String);
    });
}

static NSURL* DocumentsURL()
{
    return [NSFileManager.defaultManager URLsForDirectory:NSDocumentDirectory inDomains:NSUserDomainMask].firstObject;
}

// A plain file name only: no folders, no hidden files, nothing that climbs out of a folder
static BOOL SafeName(NSString* name)
{
    return name.length > 0 && name.length < 256 && ![name hasPrefix:@"."] && [name rangeOfString:@"/"].location == NSNotFound &&
           [name rangeOfString:@":"].location == NSNotFound;
}

static NSString* FolderFor(NSURL* file)
{
    NSString* ext = file.pathExtension.lowercaseString;
    NSString* name = file.lastPathComponent.lowercaseString;
    if ([ext isEqualToString:@"pdf"])
        return @"PDF";
    if ([ext isEqualToString:@"glb"] || [ext isEqualToString:@"gltf"])
        return @"Modele";
    if ([ext isEqualToString:@"heic"] || [ext isEqualToString:@"heif"])
        return @"Przestrzenne";
    if ([ext isEqualToString:@"mov"] || [ext isEqualToString:@"mp4"] || [ext isEqualToString:@"m4v"])
    {
        if (@available(iOS 17.0, *))
        {
            AVURLAsset* asset = [AVURLAsset URLAssetWithURL:file options:nil];
            if ([asset tracksWithMediaCharacteristic:AVMediaCharacteristicContainsStereoMultiviewVideo].count > 0)
                return @"Przestrzenne";
        }
        return [name containsString:@"360"] ? @"Srodowiska" : @"Filmy";
    }
    if ([ext isEqualToString:@"jpg"] || [ext isEqualToString:@"jpeg"] || [ext isEqualToString:@"png"])
    {
        if ([name containsString:@"_sbs"])
            return @"Przestrzenne";
        // A 2:1 picture is an equirectangular panorama: an environment
        CGImageSourceRef source = CGImageSourceCreateWithURL((__bridge CFURLRef)file, NULL);
        if (source)
        {
            NSDictionary* properties = CFBridgingRelease(CGImageSourceCopyPropertiesAtIndex(source, 0, NULL));
            CFRelease(source);
            double width = [properties[(NSString*)kCGImagePropertyPixelWidth] doubleValue];
            double height = [properties[(NSString*)kCGImagePropertyPixelHeight] doubleValue];
            if (height > 0 && fabs(width / height - 2.0) < 0.05)
                return @"Srodowiska";
        }
        return @"Przestrzenne";
    }
    return @"Inne";
}

// "film.mp4", then "film (2).mp4"... so nothing already there is overwritten
static NSURL* UniqueURL(NSURL* folder, NSString* name)
{
    NSURL* url = [folder URLByAppendingPathComponent:name];
    NSString* base = name.stringByDeletingPathExtension;
    NSString* ext = name.pathExtension;
    for (int i = 2; [NSFileManager.defaultManager fileExistsAtPath:url.path] && i < 1000; i++)
    {
        NSString* next = [NSString stringWithFormat:@"%@ (%d)", base, i];
        url = [folder URLByAppendingPathComponent:ext.length > 0 ? [next stringByAppendingPathExtension:ext] : next];
    }
    return url;
}

// Copies one readable file into Documents/<its folder>. Call off the main thread (iCloud downloads).
static NSDictionary* ImportOne(NSURL* source)
{
    NSString* name = source.lastPathComponent;
    if (!SafeName(name))
        return @{ @"name" : name ?: @"", @"error" : @"nieprawidłowa nazwa" };
    NSFileManager* files = NSFileManager.defaultManager;
    NSURL* temp = [files.temporaryDirectory URLByAppendingPathComponent:NSUUID.UUID.UUIDString isDirectory:YES];
    [files createDirectoryAtURL:temp withIntermediateDirectories:YES attributes:nil error:nil];
    NSURL* copy = [temp URLByAppendingPathComponent:name];

    __block BOOL copied = NO;
    __block NSError* copyError = nil;
    NSError* coordinateError = nil;
    BOOL scoped = [source startAccessingSecurityScopedResource];
    NSFileCoordinator* coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
    [coordinator coordinateReadingItemAtURL:source options:NSFileCoordinatorReadingWithoutChanges error:&coordinateError
                                 byAccessor:^(NSURL* url) {
        NSNumber* isFile = nil;
        [url getResourceValue:&isFile forKey:NSURLIsRegularFileKey error:nil];
        if (isFile.boolValue)
            copied = [files copyItemAtURL:url toURL:copy error:&copyError];
    }];
    if (scoped)
        [source stopAccessingSecurityScopedResource];
    if (!copied)
    {
        [files removeItemAtURL:temp error:nil];
        NSError* error = copyError ?: coordinateError;
        return @{ @"name" : name, @"error" : error ? error.localizedDescription : @"to nie jest plik" };
    }

    NSString* folderName = FolderFor(copy);
    NSURL* folder = [DocumentsURL() URLByAppendingPathComponent:folderName isDirectory:YES];
    [files createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:nil];
    NSURL* destination = UniqueURL(folder, name);
    NSError* moveError = nil;
    BOOL moved = [files moveItemAtURL:copy toURL:destination error:&moveError];
    [files removeItemAtURL:temp error:nil];
    if (!moved)
        return @{ @"name" : name, @"error" : moveError.localizedDescription ?: @"zapis" };
    return @{ @"name" : destination.lastPathComponent, @"folder" : folderName };
}

static void ImportAll(NSArray<NSURL*>* urls, BOOL removeSources)
{
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSMutableArray* results = [NSMutableArray array];
        for (NSURL* url in urls)
        {
            [results addObject:ImportOne(url)];
            if (removeSources) // the picker's own copies in tmp/…-Inbox
                [NSFileManager.defaultManager removeItemAtURL:url error:nil];
        }
        Send("OnImported", @{ @"files" : results });
    });
}

// ---- The linked folder ----

static NSURL* LinkedFolder()
{
    NSData* bookmark = [NSUserDefaults.standardUserDefaults dataForKey:kBookmarkKey];
    if (!bookmark)
        return nil;
    BOOL stale = NO;
    NSURL* url = [NSURL URLByResolvingBookmarkData:bookmark options:0 relativeToURL:nil bookmarkDataIsStale:&stale error:nil];
    if (url && stale && [url startAccessingSecurityScopedResource])
    {
        NSData* fresh = [url bookmarkDataWithOptions:NSURLBookmarkCreationMinimalBookmark includingResourceValuesForKeys:nil
                                       relativeToURL:nil error:nil];
        if (fresh)
            [NSUserDefaults.standardUserDefaults setObject:fresh forKey:kBookmarkKey];
        [url stopAccessingSecurityScopedResource];
    }
    return url;
}

static void Link(NSURL* folder)
{
    BOOL scoped = [folder startAccessingSecurityScopedResource];
    NSError* error = nil;
    NSData* bookmark = [folder bookmarkDataWithOptions:NSURLBookmarkCreationMinimalBookmark includingResourceValuesForKeys:nil
                                         relativeToURL:nil error:&error];
    if (scoped)
        [folder stopAccessingSecurityScopedResource];
    if (!bookmark)
    {
        Send("OnLinked", @{ @"error" : error.localizedDescription ?: @"zakładka" });
        return;
    }
    [NSUserDefaults.standardUserDefaults setObject:bookmark forKey:kBookmarkKey];
    Send("OnLinked", @{ @"folder" : folder.lastPathComponent ?: @"" });
}

// ---- The picker ----

@interface CXRFilePickerDelegate : NSObject <UIDocumentPickerDelegate>
@property(nonatomic) BOOL linking;
@end

static CXRFilePickerDelegate* s_Delegate; // the picker holds its delegate weakly

@implementation CXRFilePickerDelegate
- (void)documentPicker:(UIDocumentPickerViewController*)controller didPickDocumentsAtURLs:(NSArray<NSURL*>*)urls
{
    if (self.linking)
    {
        if (urls.firstObject)
            Link(urls.firstObject);
    }
    else
    {
        ImportAll(urls, YES);
    }
    s_Delegate = nil;
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController*)controller
{
    Send(self.linking ? "OnLinked" : "OnImported", @{ @"cancelled" : @YES });
    s_Delegate = nil;
}
@end

static void Present(BOOL linking)
{
    UIViewController* root = UnityGetGLViewController();
    if (!root || root.presentedViewController)
    {
        Send(linking ? "OnLinked" : "OnImported", @{ @"error" : @"okno wyboru jest już otwarte" });
        return;
    }
    UIDocumentPickerViewController* picker = linking
        ? [[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[ UTTypeFolder ]]
        : [[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[ UTTypeData ] asCopy:YES];
    picker.allowsMultipleSelection = !linking;
    s_Delegate = [CXRFilePickerDelegate new];
    s_Delegate.linking = linking;
    picker.delegate = s_Delegate;
    [root presentViewController:picker animated:YES completion:nil];
}

extern "C" {

void CardboardXR_FilesPick(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{ Present(NO); });
}

void CardboardXR_FilesLinkFolder(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{ Present(YES); });
}

// {"folder":"VisionPro","files":[{"name","size","local"}]} or {"error"} / {"none":true}
void CardboardXR_FilesList(void)
{
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSURL* folder = LinkedFolder();
        if (!folder)
        {
            Send("OnListed", @{ @"none" : @YES });
            return;
        }
        BOOL scoped = [folder startAccessingSecurityScopedResource];
        __block NSMutableArray* list = [NSMutableArray array];
        __block NSError* listError = nil;
        NSError* coordinateError = nil;
        NSFileCoordinator* coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
        [coordinator coordinateReadingItemAtURL:folder options:NSFileCoordinatorReadingWithoutChanges error:&coordinateError
                                     byAccessor:^(NSURL* url) {
            NSArray<NSURL*>* items = [NSFileManager.defaultManager contentsOfDirectoryAtURL:url
                includingPropertiesForKeys:@[ NSURLIsDirectoryKey, NSURLFileSizeKey ] options:0 error:&listError];
            for (NSURL* item in items)
            {
                NSString* name = item.lastPathComponent;
                BOOL local = YES;
                // Older iOS lists files still in iCloud as ".name.icloud"
                if ([name hasPrefix:@"."] && [name hasSuffix:@".icloud"] && name.length > 8)
                {
                    name = [name substringWithRange:NSMakeRange(1, name.length - 8)];
                    local = NO;
                }
                NSNumber* isDirectory = nil;
                [item getResourceValue:&isDirectory forKey:NSURLIsDirectoryKey error:nil];
                if (!SafeName(name) || isDirectory.boolValue)
                    continue;
                NSNumber* size = nil;
                [item getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
                [list addObject:@{ @"name" : name, @"size" : size ?: @0, @"local" : @(local) }];
            }
        }];
        if (scoped)
            [folder stopAccessingSecurityScopedResource];
        NSError* error = listError ?: coordinateError;
        if (error)
        {
            Send("OnListed", @{ @"folder" : folder.lastPathComponent ?: @"", @"error" : error.localizedDescription });
            return;
        }
        [list sortUsingComparator:^NSComparisonResult(NSDictionary* a, NSDictionary* b) {
            return [a[@"name"] localizedStandardCompare:b[@"name"]];
        }];
        Send("OnListed", @{ @"folder" : folder.lastPathComponent ?: @"", @"files" : list });
    });
}

// Copies one file of the linked folder in (downloading it from iCloud first when needed)
void CardboardXR_FilesImportLinked(const char* name)
{
    NSString* fileName = name ? [NSString stringWithUTF8String:name] : nil;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSURL* folder = LinkedFolder();
        if (!folder || !SafeName(fileName))
        {
            Send("OnImported", @{ @"error" : @"brak połączonego folderu lub zła nazwa" });
            return;
        }
        BOOL scoped = [folder startAccessingSecurityScopedResource];
        NSDictionary* result = ImportOne([folder URLByAppendingPathComponent:fileName]);
        if (scoped)
            [folder stopAccessingSecurityScopedResource];
        Send("OnImported", @{ @"files" : @[ result ] });
    });
}

void CardboardXR_FilesUnlink(void)
{
    [NSUserDefaults.standardUserDefaults removeObjectForKey:kBookmarkKey];
}

}

// ---- The linked folder for the assistant's tools (V.I.C.T.O.R.) ----
// One call, one answer: the callback gets the request id and a JSON text on the main queue:
// list -> {"folder","files":[{"name","size","local"}]}, read -> {"text"}, write -> {"ok":true}, or {"error"}.
typedef void (*CXRCloudCallback)(int requestId, const char* json);

static void CloudAnswer(CXRCloudCallback callback, int requestId, NSDictionary* payload)
{
    NSData* data = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString* text = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{\"error\":\"json\"}";
    dispatch_async(dispatch_get_main_queue(), ^{ callback(requestId, text.UTF8String); });
}

static const NSUInteger kCloudReadLimit = 4u * 1024u * 1024u;   // bytes of a file we agree to open
static const NSUInteger kCloudWriteLimit = 512u * 1024u;        // bytes we agree to write
static const NSUInteger kCloudTextLimit = 60000;                // characters handed to the model

static NSString* CloudReadText(NSURL* file, NSString** error)
{
    NSString* ext = file.pathExtension.lowercaseString;
    if ([ext isEqualToString:@"pdf"])
    {
        PDFDocument* document = [[PDFDocument alloc] initWithURL:file];
        if (!document || document.isLocked)
        {
            *error = @"nie da się otworzyć PDF-a";
            return nil;
        }
        NSString* text = document.string ?: @"";
        return text.length > kCloudTextLimit ? [text substringToIndex:kCloudTextLimit] : text;
    }
    NSNumber* size = nil;
    [file getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
    if (size.unsignedIntegerValue > kCloudReadLimit)
    {
        *error = @"plik jest za duży (limit 4 MB)";
        return nil;
    }
    NSData* data = [NSData dataWithContentsOfURL:file];
    NSString* text = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
    if (!text)
    {
        *error = @"to nie jest plik tekstowy (UTF-8) ani PDF";
        return nil;
    }
    return text.length > kCloudTextLimit ? [text substringToIndex:kCloudTextLimit] : text;
}

extern "C" void CardboardXR_CloudOp(int requestId, const char* op, const char* name, const char* text, CXRCloudCallback callback)
{
    NSString* operation = op ? [NSString stringWithUTF8String:op] : @"";
    NSString* fileName = name ? [NSString stringWithUTF8String:name] : @"";
    NSString* content = text ? [NSString stringWithUTF8String:text] : @"";
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSURL* folder = LinkedFolder();
        if (!folder)
        {
            CloudAnswer(callback, requestId, @{ @"error" : @"brak połączonego folderu: Więcej > Pliki > Połącz folder" });
            return;
        }
        BOOL isList = [operation isEqualToString:@"list"];
        if (!isList && !SafeName(fileName))
        {
            CloudAnswer(callback, requestId, @{ @"error" : @"zła nazwa pliku (sama nazwa, bez folderów)" });
            return;
        }
        BOOL scoped = [folder startAccessingSecurityScopedResource];
        NSDictionary* answer = nil;
        NSError* coordinateError = nil;
        NSFileCoordinator* coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
        if (isList)
        {
            __block NSMutableArray* list = [NSMutableArray array];
            [coordinator coordinateReadingItemAtURL:folder options:NSFileCoordinatorReadingWithoutChanges error:&coordinateError
                                         byAccessor:^(NSURL* url) {
                NSArray<NSURL*>* items = [NSFileManager.defaultManager contentsOfDirectoryAtURL:url
                    includingPropertiesForKeys:@[ NSURLIsDirectoryKey, NSURLFileSizeKey ] options:0 error:nil];
                for (NSURL* item in items)
                {
                    NSString* itemName = item.lastPathComponent;
                    BOOL local = YES;
                    if ([itemName hasPrefix:@"."] && [itemName hasSuffix:@".icloud"] && itemName.length > 8)
                    {
                        itemName = [itemName substringWithRange:NSMakeRange(1, itemName.length - 8)];
                        local = NO;
                    }
                    NSNumber* isDirectory = nil;
                    [item getResourceValue:&isDirectory forKey:NSURLIsDirectoryKey error:nil];
                    if (!SafeName(itemName) || isDirectory.boolValue)
                        continue;
                    NSNumber* size = nil;
                    [item getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
                    [list addObject:@{ @"name" : itemName, @"size" : size ?: @0, @"local" : @(local) }];
                    if (list.count >= 200)
                        break;
                }
            }];
            answer = coordinateError ? @{ @"error" : coordinateError.localizedDescription }
                                     : @{ @"folder" : folder.lastPathComponent ?: @"", @"files" : list };
        }
        else if ([operation isEqualToString:@"read"])
        {
            NSURL* file = [folder URLByAppendingPathComponent:fileName];
            __block NSString* result = nil;
            __block NSString* failure = nil;
            // Coordinated reading downloads a file that is still only in the cloud
            [coordinator coordinateReadingItemAtURL:file options:0 error:&coordinateError byAccessor:^(NSURL* url) {
                NSString* readError = nil;
                result = CloudReadText(url, &readError);
                failure = readError;
            }];
            if (coordinateError)
                answer = @{ @"error" : coordinateError.localizedDescription };
            else if (result)
                answer = @{ @"text" : result };
            else
                answer = @{ @"error" : failure ?: @"nie udało się odczytać" };
        }
        else if ([operation isEqualToString:@"write"])
        {
            if ([content lengthOfBytesUsingEncoding:NSUTF8StringEncoding] > kCloudWriteLimit)
                answer = @{ @"error" : @"tekst jest za długi (limit 512 KB)" };
            else
            {
                NSURL* file = [folder URLByAppendingPathComponent:fileName];
                __block NSError* writeError = nil;
                [coordinator coordinateWritingItemAtURL:file options:NSFileCoordinatorWritingForReplacing error:&coordinateError
                                             byAccessor:^(NSURL* url) {
                    [content writeToURL:url atomically:YES encoding:NSUTF8StringEncoding error:&writeError];
                }];
                NSError* error = coordinateError ?: writeError;
                answer = error ? @{ @"error" : error.localizedDescription } : @{ @"ok" : @YES };
            }
        }
        else
            answer = @{ @"error" : @"nieznana operacja" };
        if (scoped)
            [folder stopAccessingSecurityScopedResource];
        CloudAnswer(callback, requestId, answer);
    });
}
