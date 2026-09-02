using QRCoder;

namespace BusTicketing.Services.Ticketing;

public static class QrCodeGenerator
{
    /// <summary>Renders a QR code as a PNG, ready to drop into a PDF or an <c>&lt;img&gt;</c>.</summary>
    public static byte[] Png(string content, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(pixelsPerModule);
    }
}
