// Spatial photos and videos from an iPhone 15 Pro / 16 Pro (or Vision Pro) for the Workspace
// (SpatialMediaView.cs): one picture per eye.
//
// Photos (HEIC): the left/right images are found through the file's stereo-pair group (not always
// indexes 0 and 1), drawn into RGBA buffers bottom row first (Unity's order).
// Videos (MV-HEVC .mov): AVAssetReader asks the decoder for both layers (RequestedMVHEVCVideoLayerIDs),
// and each sample's tagged buffers give the left and right pixel buffers (as Apple's "Reading multiview
// 3D video files" sample). Sound plays through an AVPlayer on the same file, whose clock paces the
// frames. iOS 17+; earlier versions say so.
import Foundation
import AVFoundation
import CoreMedia
import ImageIO
import VideoToolbox
import UIKit

// ---- Photos ----

private final class SpatialPhoto {
    let width: Int
    let height: Int
    let left: CGImage
    let right: CGImage?
    init(left: CGImage, right: CGImage?) {
        self.left = left
        self.right = right
        width = left.width
        height = left.height
    }
}

private var photos: [Int32: SpatialPhoto] = [:]
private var videos: [Int32: AnyObject] = [:]
private var nextId: Int32 = 1

private func stereoIndexes(_ source: CGImageSource) -> (Int, Int)? {
    let count = CGImageSourceGetCount(source)
    // The group may be listed for the whole file or for an image in it
    var all: [[CFString: Any]?] = [CGImageSourceCopyProperties(source, nil) as? [CFString: Any]]
    for index in 0..<count { all.append(CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [CFString: Any]) }
    for properties in all {
        guard let groups = properties?[kCGImagePropertyGroups] as? [[CFString: Any]] else { continue }
        for group in groups {
            if (group[kCGImagePropertyGroupType] as? String) == (kCGImagePropertyGroupTypeStereoPair as String),
               let left = group[kCGImagePropertyGroupImageIndexLeft] as? Int,
               let right = group[kCGImagePropertyGroupImageIndexRight] as? Int, left < count, right < count {
                return (left, right)
            }
        }
    }
    return count >= 2 ? (0, 1) : nil
}

// Opens a photo; width/height of one eye's image. Returns an id (0 = failed). *stereo = 1 for a spatial photo.
@_cdecl("CardboardXR_SpatialPhotoOpen")
public func CardboardXR_SpatialPhotoOpen(_ path: UnsafePointer<CChar>?, _ width: UnsafeMutablePointer<Int32>?,
                                         _ height: UnsafeMutablePointer<Int32>?, _ stereo: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let path = path, let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: String(cString: path)) as CFURL, nil)
    else { return 0 }
    var photo: SpatialPhoto?
    if let (left, right) = stereoIndexes(source),
       let leftImage = CGImageSourceCreateImageAtIndex(source, left, nil),
       let rightImage = CGImageSourceCreateImageAtIndex(source, right, nil) {
        photo = SpatialPhoto(left: leftImage, right: rightImage)
    } else if let image = CGImageSourceCreateImageAtIndex(source, 0, nil) {
        photo = SpatialPhoto(left: image, right: nil)
    }
    guard let found = photo else { return 0 }
    let id = nextId
    nextId += 1
    photos[id] = found
    width?.pointee = Int32(found.width)
    height?.pointee = Int32(found.height)
    stereo?.pointee = found.right != nil ? 1 : 0
    return id
}

// Draws one eye (0 left, 1 right) into dst: width x height RGBA, bottom row first. 1 = done.
@_cdecl("CardboardXR_SpatialPhotoCopy")
public func CardboardXR_SpatialPhotoCopy(_ id: Int32, _ eye: Int32, _ dst: UnsafeMutableRawPointer?,
                                         _ width: Int32, _ height: Int32, _ capacity: Int32) -> Int32 {
    guard let photo = photos[id], let dst = dst, width > 0, height > 0, Int(capacity) >= Int(width) * Int(height) * 4,
          let image = (eye == 1 ? photo.right : nil) ?? photo.left,
          let context = CGContext(data: dst, width: Int(width), height: Int(height), bitsPerComponent: 8,
                                  bytesPerRow: Int(width) * 4, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue)
    else { return 0 }
    // Memory starts at the top row: flip so row 0 is the bottom (Unity's order)
    context.translateBy(x: 0, y: CGFloat(height))
    context.scaleBy(x: 1, y: -1)
    context.draw(image, in: CGRect(x: 0, y: 0, width: Int(width), height: Int(height)))
    return 1
}

@_cdecl("CardboardXR_SpatialPhotoClose")
public func CardboardXR_SpatialPhotoClose(_ id: Int32) { photos[id] = nil }

// ---- Videos ----

@available(iOS 17.0, *)
private final class SpatialVideo {
    let reader: AVAssetReader
    let output: AVAssetReaderTrackOutput
    let player: AVPlayer
    let width: Int
    let height: Int
    var lastTime: Double = -1

    init?(url: URL) {
        let asset = AVURLAsset(url: url)
        guard let track = asset.tracks(withMediaCharacteristic: .containsStereoMultiviewVideo).first,
              let reader = try? AVAssetReader(asset: asset) else { return nil }
        let settings: [String: Any] = [
            AVVideoDecompressionPropertiesKey: [kVTDecompressionPropertyKey_RequestedMVHEVCVideoLayerIDs: [0, 1] as CFArray],
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
        ]
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
        output.alwaysCopiesSampleData = false
        guard reader.canAdd(output) else { return nil }
        reader.add(output)
        guard reader.startReading() else { return nil }
        self.reader = reader
        self.output = output
        let size = track.naturalSize.applying(track.preferredTransform)
        width = Int(abs(size.width))
        height = Int(abs(size.height))
        player = AVPlayer(url: url) // sound, and the clock the frames follow
        player.play()
    }

    // Decodes up to the player's time; copies the newest frame's eyes into the buffers (BGRA, bottom row first)
    func next(_ left: UnsafeMutableRawPointer, _ right: UnsafeMutableRawPointer, _ capacity: Int) -> Bool {
        let now = player.currentTime().seconds
        if !now.isFinite || now <= lastTime { return false }
        var newest: CMSampleBuffer?
        while reader.status == .reading {
            guard let sample = output.copyNextSampleBuffer() else { break }
            newest = sample
            if sample.presentationTimeStamp.seconds >= now { break }
        }
        guard let sample = newest, let tagged = sample.taggedBuffers else { return false }
        lastTime = now
        var copied = 0
        for buffer in tagged {
            guard case let .pixelBuffer(pixels) = buffer.buffer,
                  let view = buffer.tags.first(matchingCategory: .stereoView) else { continue }
            let target = view == .stereoView(.leftEye) ? left : (view == .stereoView(.rightEye) ? right : nil)
            if let target = target, copy(pixels, into: target, capacity) { copied += 1 }
        }
        return copied > 0
    }

    private func copy(_ pixels: CVPixelBuffer, into target: UnsafeMutableRawPointer, _ capacity: Int) -> Bool {
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(pixels) else { return false }
        let w = CVPixelBufferGetWidth(pixels), h = CVPixelBufferGetHeight(pixels)
        let rowBytes = CVPixelBufferGetBytesPerRow(pixels)
        guard w == width, h == height, capacity >= w * h * 4 else { return false }
        for row in 0..<h { // flipped: Unity's row 0 is the bottom
            memcpy(target + (h - 1 - row) * w * 4, base + row * rowBytes, w * 4)
        }
        return true
    }

    func close() {
        player.pause()
        reader.cancelReading()
    }
}

// Opens a spatial (MV-HEVC) video; width/height of one eye. Returns an id (0 = not spatial / failed / iOS < 17).
@_cdecl("CardboardXR_SpatialVideoOpen")
public func CardboardXR_SpatialVideoOpen(_ path: UnsafePointer<CChar>?, _ width: UnsafeMutablePointer<Int32>?,
                                         _ height: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let path = path else { return 0 }
    guard #available(iOS 17.0, *), let video = SpatialVideo(url: URL(fileURLWithPath: String(cString: path))) else { return 0 }
    let id = nextId
    nextId += 1
    videos[id] = video
    width?.pointee = Int32(video.width)
    height?.pointee = Int32(video.height)
    return id
}

// 1 when a new frame was copied (BGRA, bottom row first) into both buffers
@_cdecl("CardboardXR_SpatialVideoNext")
public func CardboardXR_SpatialVideoNext(_ id: Int32, _ left: UnsafeMutableRawPointer?, _ right: UnsafeMutableRawPointer?,
                                         _ capacity: Int32) -> Int32 {
    guard #available(iOS 17.0, *), let video = videos[id] as? SpatialVideo, let left = left, let right = right else { return 0 }
    return video.next(left, right, Int(capacity)) ? 1 : 0
}

@_cdecl("CardboardXR_SpatialVideoClose")
public func CardboardXR_SpatialVideoClose(_ id: Int32) {
    if #available(iOS 17.0, *), let video = videos[id] as? SpatialVideo { video.close() }
    videos[id] = nil
}
