// The iPhone's Photos library for the Workspace.
// Screenshot (WorkspaceLayouts.cs): a JPEG file added to Photos, asking only for "add" access
// (NSPhotoLibraryAddUsageDescription). The result goes to the GameObject "ScreenshotNative" (OnSaved:
// "ok" or the reason).
#import <Foundation/Foundation.h>
#import <Photos/Photos.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static void SendSaved(NSString* result)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage("ScreenshotNative", "OnSaved", result.UTF8String);
    });
}

extern "C" void CardboardXR_SaveToPhotos(const char* path)
{
    if (!path)
        return;
    NSURL* url = [NSURL fileURLWithPath:[NSString stringWithUTF8String:path]];
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status) {
        if (status != PHAuthorizationStatusAuthorized && status != PHAuthorizationStatusLimited)
        {
            SendSaved(@"brak zgody na dodawanie do Zdjęć (Ustawienia > CardboardXR > Zdjęcia)");
            return;
        }
        [PHPhotoLibrary.sharedPhotoLibrary performChanges:^{
            [PHAssetChangeRequest creationRequestForAssetFromImageAtFileURL:url];
        } completionHandler:^(BOOL success, NSError* error) {
            [NSFileManager.defaultManager removeItemAtURL:url error:nil];
            SendSaved(success ? @"ok" : (error.localizedDescription ?: @"błąd"));
        }];
    }];
}

// ---- Gallery (GalleryView.cs) ----
// The library, newest first, filtered to everything / spatial (iOS 18+) / videos. Thumbnails are drawn
// into RGBA buffers (bottom row first, Unity's order). Opening a photo or video writes its original
// file to tmp/Galeria (downloading it from iCloud when needed) and reports the path to the GameObject
// "GalleryNative" (OnExported: {"path","video","spatial"} or {"error"}). Access: NSPhotoLibraryUsageDescription.
#import <UIKit/UIKit.h>

static PHFetchResult<PHAsset*>* s_Assets;
static PHCachingImageManager* s_ImageManager;

static void SendGallery(const char* method, NSDictionary* payload)
{
    NSData* json = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
    NSString* text = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"{\"error\":\"JSON\"}";
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage("GalleryNative", method, text.UTF8String);
    });
}

static BOOL IsSpatial(PHAsset* asset)
{
    if (@available(iOS 18.0, *))
        return (asset.mediaSubtypes & PHAssetMediaSubtypeSpatialMedia) != 0;
    return NO;
}

extern "C" void CardboardXR_PhotosAuthorize(void)
{
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelReadWrite handler:^(PHAuthorizationStatus status) {
        BOOL ok = status == PHAuthorizationStatusAuthorized || status == PHAuthorizationStatusLimited;
        SendGallery("OnAuthorized", @{ @"ok" : @(ok), @"limited" : @(status == PHAuthorizationStatusLimited) });
    }];
}

// filter: 0 photos and videos, 1 spatial only, 2 videos only. Returns how many.
extern "C" int CardboardXR_PhotosFetch(int filter)
{
    PHFetchOptions* options = [PHFetchOptions new];
    options.sortDescriptors = @[ [NSSortDescriptor sortDescriptorWithKey:@"creationDate" ascending:NO] ];
    if (filter == 2)
        options.predicate = [NSPredicate predicateWithFormat:@"mediaType == %d", (int)PHAssetMediaTypeVideo];
    else if (filter == 1)
    {
        if (@available(iOS 18.0, *))
            options.predicate = [NSPredicate predicateWithFormat:@"(mediaSubtypes & %lu) != 0", (unsigned long)PHAssetMediaSubtypeSpatialMedia];
        else
        {
            s_Assets = nil;
            return 0;
        }
    }
    else
        options.predicate = [NSPredicate predicateWithFormat:@"mediaType == %d || mediaType == %d",
                             (int)PHAssetMediaTypeImage, (int)PHAssetMediaTypeVideo];
    s_Assets = [PHAsset fetchAssetsWithOptions:options];
    if (!s_ImageManager)
        s_ImageManager = [PHCachingImageManager new];
    return (int)s_Assets.count;
}

// Bit 1: a video; bit 2: spatial
extern "C" int CardboardXR_PhotosInfo(int index)
{
    if (!s_Assets || index < 0 || index >= (int)s_Assets.count)
        return 0;
    PHAsset* asset = s_Assets[index];
    return (asset.mediaType == PHAssetMediaTypeVideo ? 1 : 0) | (IsSpatial(asset) ? 2 : 0);
}

// A size x size square thumbnail (filled, centred) into dst. 1 = done. Synchronous, local only.
extern "C" int CardboardXR_PhotosThumbnail(int index, int size, void* dst, int capacity)
{
    if (!s_Assets || !dst || index < 0 || index >= (int)s_Assets.count || size <= 0 || capacity < size * size * 4)
        return 0;
    PHImageRequestOptions* options = [PHImageRequestOptions new];
    options.synchronous = YES;
    options.deliveryMode = PHImageRequestOptionsDeliveryModeHighQualityFormat;
    options.resizeMode = PHImageRequestOptionsResizeModeFast;
    options.networkAccessAllowed = NO;
    __block UIImage* result = nil;
    [s_ImageManager requestImageForAsset:s_Assets[index] targetSize:CGSizeMake(size, size) contentMode:PHImageContentModeAspectFill
                                 options:options resultHandler:^(UIImage* image, NSDictionary* info) { result = image; }];
    CGImageRef image = result.CGImage;
    if (!image)
        return 0;
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    CGContextRef context = CGBitmapContextCreate(dst, size, size, 8, size * 4, space,
                                                 kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(space);
    if (!context)
        return 0;
    // Points, not pixels: the image's size times its scale
    double width = result.size.width, height = result.size.height;
    if (width <= 0 || height <= 0)
    {
        CGContextRelease(context);
        return 0;
    }
    double scale = MAX(size / width, size / height);
    double w = width * scale, h = height * scale;
    // Drawn through UIKit, which applies the photo's orientation and assumes y grows downward: in a plain
    // bitmap context that puts the picture's top row last in memory, i.e. bottom row first, Unity's order
    UIGraphicsPushContext(context);
    [result drawInRect:CGRectMake((size - w) / 2, (size - h) / 2, w, h)];
    UIGraphicsPopContext();
    CGContextRelease(context);
    return 1;
}

extern "C" void CardboardXR_PhotosExport(int index)
{
    if (!s_Assets || index < 0 || index >= (int)s_Assets.count)
    {
        SendGallery("OnExported", @{ @"error" : @"brak pliku" });
        return;
    }
    PHAsset* asset = s_Assets[index];
    BOOL video = asset.mediaType == PHAssetMediaTypeVideo;
    BOOL spatial = IsSpatial(asset);
    PHAssetResource* chosen = nil;
    for (PHAssetResource* resource in [PHAssetResource assetResourcesForAsset:asset])
    {
        PHAssetResourceType type = resource.type;
        BOOL full = video ? type == PHAssetResourceTypeFullSizeVideo : type == PHAssetResourceTypeFullSizePhoto;
        BOOL original = video ? type == PHAssetResourceTypeVideo : type == PHAssetResourceTypePhoto;
        if (full || (original && !chosen))
            chosen = resource;
    }
    if (!chosen)
    {
        SendGallery("OnExported", @{ @"error" : @"nie można odczytać pliku" });
        return;
    }
    NSFileManager* files = NSFileManager.defaultManager;
    NSURL* folder = [files.temporaryDirectory URLByAppendingPathComponent:@"Galeria" isDirectory:YES];
    [files createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:nil];
    NSString* ext = chosen.originalFilename.pathExtension.length > 0 ? chosen.originalFilename.pathExtension.lowercaseString
                                                                      : (video ? @"mov" : @"heic");
    NSString* name = [[asset.localIdentifier stringByReplacingOccurrencesOfString:@"/" withString:@"_"] stringByAppendingPathExtension:ext];
    NSURL* file = [folder URLByAppendingPathComponent:name];
    NSDictionary* done = @{ @"path" : file.path, @"video" : @(video), @"spatial" : @(spatial),
                            @"name" : chosen.originalFilename ?: name };
    if ([files fileExistsAtPath:file.path])
    {
        SendGallery("OnExported", done);
        return;
    }
    PHAssetResourceRequestOptions* options = [PHAssetResourceRequestOptions new];
    options.networkAccessAllowed = YES; // from iCloud if the phone keeps only a small copy
    [PHAssetResourceManager.defaultManager writeDataForAssetResource:chosen toFile:file options:options
                                                   completionHandler:^(NSError* error) {
        if (error)
        {
            [files removeItemAtURL:file error:nil];
            SendGallery("OnExported", @{ @"error" : error.localizedDescription ?: @"błąd" });
        }
        else
            SendGallery("OnExported", done);
    }];
}
