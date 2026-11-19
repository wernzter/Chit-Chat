import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

enum IconError: Error {
    case couldNotRender(size: Int)
    case couldNotEncodePNG(size: Int)
}

func renderIcon(size: Int, tileInset: CGFloat) throws -> Data {
    let colorSpace = CGColorSpace(name: CGColorSpace.sRGB)!
    guard let context = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8,
                                  bytesPerRow: 0, space: colorSpace,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        throw IconError.couldNotRender(size: size)
    }
    let scale = CGFloat(size) / 100
    context.scaleBy(x: scale, y: scale)
    context.clear(CGRect(x: 0, y: 0, width: 100, height: 100))

    let blue = CGColor(red: 0.11, green: 0.30, blue: 0.76, alpha: 1)
    let tile = CGRect(x: 0, y: 0, width: 100, height: 100).insetBy(dx: tileInset, dy: tileInset)
    context.setFillColor(blue)
    context.addPath(CGPath(roundedRect: tile, cornerWidth: 20, cornerHeight: 20, transform: nil))
    context.fillPath()

    let bubble = CGRect(x: 21, y: 36, width: 58, height: 39)
    let tail = CGMutablePath()
    tail.move(to: CGPoint(x: 31, y: 41))
    tail.addLine(to: CGPoint(x: 31, y: 25))
    tail.addLine(to: CGPoint(x: 48, y: 41))
    tail.closeSubpath()

    context.setFillColor(CGColor(gray: 1, alpha: 1))
    context.addPath(CGPath(roundedRect: bubble, cornerWidth: 12, cornerHeight: 12, transform: nil))
    context.fillPath()
    context.addPath(tail)
    context.fillPath()

    context.setFillColor(blue)
    let barWidth: CGFloat = 6
    let bars: [(center: CGFloat, height: CGFloat)] = [(37, 12), (50, 23), (63, 16)]
    for bar in bars {
        let rect = CGRect(x: bar.center - barWidth / 2, y: bubble.midY - bar.height / 2,
                          width: barWidth, height: bar.height)
        context.addPath(CGPath(roundedRect: rect, cornerWidth: barWidth / 2,
                               cornerHeight: barWidth / 2, transform: nil))
        context.fillPath()
    }

    guard let image = context.makeImage() else { throw IconError.couldNotRender(size: size) }
    let data = NSMutableData()
    guard let encoder = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
        throw IconError.couldNotEncodePNG(size: size)
    }
    CGImageDestinationAddImage(encoder, image, nil)
    guard CGImageDestinationFinalize(encoder) else { throw IconError.couldNotEncodePNG(size: size) }
    return data as Data
}

func makeTrayIcon(sizes: [UInt8]) throws -> Data {
    // An ICO has a six-byte header, then one 16-byte directory entry per image.
    var directory = Data([0, 0, 1, 0, UInt8(sizes.count), 0])
    var images = Data()
    var offset = 6 + 16 * sizes.count
    for size in sizes {
        let png = try renderIcon(size: Int(size), tileInset: 1)
        directory.append(contentsOf: [size, size, 0, 0, 1, 0, 32, 0])
        withUnsafeBytes(of: UInt32(png.count).littleEndian) { directory.append(contentsOf: $0) }
        withUnsafeBytes(of: UInt32(offset).littleEndian) { directory.append(contentsOf: $0) }
        images.append(png)
        offset += png.count
    }
    directory.append(images)
    return directory
}

let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let iconset = root.appendingPathComponent("Resources/AppIcon.iconset")
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let suffix = scale == 2 ? "@2x" : ""
        let path = iconset.appendingPathComponent("icon_\(size)x\(size)\(suffix).png")
        try renderIcon(size: size * scale, tileInset: 8).write(to: path, options: .atomic)
    }
}

let tray = root.appendingPathComponent("windows/src/ChitChat.App/Assets/tray.ico")
try makeTrayIcon(sizes: [16, 32]).write(to: tray, options: .atomic)
