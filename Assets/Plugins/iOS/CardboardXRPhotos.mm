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
