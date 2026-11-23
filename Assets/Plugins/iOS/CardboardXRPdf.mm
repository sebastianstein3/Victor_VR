// PDF pages for the Workspace's reader (PdfView.cs): PDFKit renders a page (with its rotation) into an
// RGBA buffer, bottom row first as Unity textures expect. Files come from the app's own Documents.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <PDFKit/PDFKit.h>

static NSMutableDictionary<NSNumber*, PDFDocument*>* s_Documents;
static int s_NextId = 1;

extern "C" int CardboardXR_PdfOpen(const char* path)
{
    if (path == NULL)
        return 0;
    NSString* file = [NSString stringWithUTF8String:path];
    if (file == nil)
        return 0;
    PDFDocument* document = [[PDFDocument alloc] initWithURL:[NSURL fileURLWithPath:file]];
    if (document == nil || document.isLocked || document.pageCount == 0)
        return 0;
    if (s_Documents == nil)
        s_Documents = [NSMutableDictionary dictionary];
    int identifier = s_NextId++;
    s_Documents[@(identifier)] = document;
    return identifier;
}

extern "C" void CardboardXR_PdfClose(int identifier) { [s_Documents removeObjectForKey:@(identifier)]; }

extern "C" int CardboardXR_PdfPageCount(int identifier) { return (int)s_Documents[@(identifier)].pageCount; }

// Width / height of a page as shown (its rotation applied)
extern "C" float CardboardXR_PdfPageAspect(int identifier, int page)
{
    PDFPage* pdfPage = [s_Documents[@(identifier)] pageAtIndex:page];
    if (pdfPage == nil)
        return 0.7071f;
    CGRect box = [pdfPage boundsForBox:kPDFDisplayBoxCropBox];
    BOOL sideways = (pdfPage.rotation % 180) != 0;
    CGFloat width = sideways ? box.size.height : box.size.width;
    CGFloat height = sideways ? box.size.width : box.size.height;
    return height > 0 ? (float)(width / height) : 0.7071f;
}

// Renders page (0-based) into dst: width x height RGBA, bottom row first, white background. 1 = done.
extern "C" int CardboardXR_PdfRender(int identifier, int page, int width, int height, void* dst, int capacity)
{
    PDFPage* pdfPage = [s_Documents[@(identifier)] pageAtIndex:page];
    if (pdfPage == nil || dst == NULL || width < 16 || height < 16 || width > 4096 || height > 4096 ||
        capacity < width * height * 4)
        return 0;
    UIImage* image = [pdfPage thumbnailOfSize:CGSizeMake(width, height) forBox:kPDFDisplayBoxCropBox];
    if (image.CGImage == nil)
        return 0;
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    CGContextRef context = CGBitmapContextCreate(dst, width, height, 8, width * 4, space,
                                                 kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(space);
    if (context == NULL)
        return 0;
    // Memory starts at the top row; flip so row 0 is the page's bottom (Unity's order)
    CGContextTranslateCTM(context, 0, height);
    CGContextScaleCTM(context, 1, -1);
    CGContextSetRGBFillColor(context, 1, 1, 1, 1);
    CGContextFillRect(context, CGRectMake(0, 0, width, height));
    CGContextDrawImage(context, CGRectMake(0, 0, width, height), image.CGImage);
    CGContextRelease(context);
    return 1;
}
