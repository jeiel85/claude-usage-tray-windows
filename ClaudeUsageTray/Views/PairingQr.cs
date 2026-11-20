using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ClaudeUsageTray.Views;

/// <summary>
/// 휴대폰 연결 창의 페어링 QR 비트맵(#251).
/// 페어링 링크는 경로 전부와 LAN 인증서 지문을 담아 수백~천 자라 QR 이 77~109칸으로 촘촘하다. 예전에는 칸당 6px PNG 를
/// 216 DIP 칸에 줄여 넣어 칸이 2px 안팎, 그것도 2px·3px 로 들쭉날쭉해져(모니터 위 모아레) 휴대폰이 잘 못 읽었다.
/// 여기서는 칸 하나를 화면 물리 픽셀의 정수배로 그리고, 비트맵 DPI 를 화면에 맞춰 WPF 가 1:1 로 표시하게 한다.
/// </summary>
public static class PairingQr
{
    /// <summary>QR 이 차지할 수 있는 가장 큰 변(DIP). 창 폭과 함께 정한 값이다.</summary>
    public const double MaxSideDip = 360;

    /// <summary>
    /// Input : 여백 포함 칸 수, 최대 변(DIP), 화면 배율(물리 픽셀/DIP)
    /// Output: 칸 하나의 물리 픽셀 수(1 이상의 정수)
    /// </summary>
    public static int PixelsPerModule(int modules, double maxSideDip, double pixelsPerDip) =>
        Math.Max(1, (int)Math.Floor(maxSideDip * pixelsPerDip / modules));

    /// <summary>
    /// Input : QR 에 넣을 문자열, 화면 배율(<see cref="VisualTreeHelper.GetDpi"/> 의 PixelsPerDip)
    /// Output: 얼린 비트맵. DPI 가 96×배율이라 Stretch=None 으로 두면 물리 픽셀 1:1 로 그려진다.
    /// 핵심 로직: 오류 정정은 L(화면 표시용이라 훼손이 없다 — Homebase M-20). 여백 4칸은 QRCoder 행렬에 이미 들어 있다.
    /// </summary>
    public static BitmapSource Render(string text, double pixelsPerDip)
    {
        if (pixelsPerDip <= 0) pixelsPerDip = 1;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
        var matrix = data.ModuleMatrix;
        var modules = matrix.Count;
        var ppm = PixelsPerModule(modules, MaxSideDip, pixelsPerDip);
        var side = modules * ppm;

        var pixels = new byte[side * side];
        for (var row = 0; row < modules; row++)
        {
            var line = matrix[row];
            for (var y = row * ppm; y < (row + 1) * ppm; y++)
            {
                var offset = y * side;
                for (var col = 0; col < modules; col++)
                {
                    var value = line[col] ? (byte)0 : (byte)255;
                    Array.Fill(pixels, value, offset + col * ppm, ppm);
                }
            }
        }

        var dpi = 96 * pixelsPerDip;
        var bitmap = BitmapSource.Create(side, side, dpi, dpi, PixelFormats.Gray8, null, pixels, side);
        bitmap.Freeze();
        return bitmap;
    }
}
