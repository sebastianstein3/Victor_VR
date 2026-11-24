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
