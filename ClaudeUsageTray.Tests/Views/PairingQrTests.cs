using ClaudeUsageTray.Services.Mobile;
using ClaudeUsageTray.Views;
using QRCoder;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

// 페어링 QR 은 칸이 2px 안팎으로 줄여 그려져 칸 폭이 들쭉날쭉했고 휴대폰이 잘 못 읽었다(#251).
// 칸 하나가 물리 픽셀 정수배인지, 비트맵이 화면 배율 그대로 1:1 로 표시되는지, 행렬과 픽셀이 정확히 맞는지 본다.
public class PairingQrTests
{
    // 실제 크기의 오퍼: LAN 2개(인증서 지문 포함) + Tailscale 이름·IP + Cloudflare — 가장 촘촘한 경우.
    private static string DenseLink() => PairingLink.Build(new PairingOffer(
        MobileProtocol.OfferMarker,
        new string('0', 32),
        "DESKTOP-ABC123",
        new string('A', 22),
        new DateTimeOffset(2026, 11, 15, 12, 34, 56, 789, TimeSpan.Zero),
        [
            new MobileEndpoint(MobileTransport.Lan, "192.168.0.10", 47321, true, new string('a', 64)),
            new MobileEndpoint(MobileTransport.Lan, "10.0.0.20", 47321, true, new string('a', 64)),
            new MobileEndpoint(MobileTransport.Tailscale, "desktop-abc123.tail1a2b3c.ts.net", 47321, false),
            new MobileEndpoint(MobileTransport.Tailscale, "100.101.102.103", 47321, false),
            new MobileEndpoint(MobileTransport.Cloudflare, "quiet-river-lemon-sample.trycloudflare.com", 443, true),
        ]));

    [Theory]
    [InlineData(117, 1.0, 3)]   // 버전 23 + 여백: 100% 에서도 칸당 3px(예전 216 DIP 칸은 1.85px)
    [InlineData(97, 1.0, 3)]
    [InlineData(85, 1.0, 4)]
    [InlineData(117, 1.5, 4)]
    [InlineData(117, 2.0, 6)]
    [InlineData(1000, 1.0, 1)]  // 아무리 커도 0 이 되지 않는다
    public void PixelsPerModule_is_the_largest_integer_that_fits(int modules, double pixelsPerDip, int expected)
    {
        var ppm = PairingQr.PixelsPerModule(modules, PairingQr.MaxSideDip, pixelsPerDip);
        Assert.Equal(expected, ppm);
        if (modules * 2 <= PairingQr.MaxSideDip * pixelsPerDip)
            Assert.True(modules * (ppm + 1) > PairingQr.MaxSideDip * pixelsPerDip);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void Render_draws_each_module_as_a_uniform_integer_block_at_screen_scale(double pixelsPerDip)
    {
        var link = DenseLink();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.L);
        var modules = data.ModuleMatrix.Count;
        Assert.Equal(17 + 4 * data.Version + 8, modules); // 여백 4칸이 행렬에 들어 있다

        var bitmap = PairingQr.Render(link, pixelsPerDip);
        var ppm = PairingQr.PixelsPerModule(modules, PairingQr.MaxSideDip, pixelsPerDip);

        Assert.True(ppm >= 3, $"칸당 {ppm}px — 휴대폰이 읽기에 너무 작다");
        Assert.Equal(modules * ppm, bitmap.PixelWidth);
        Assert.Equal(bitmap.PixelWidth, bitmap.PixelHeight);
        // DPI 가 화면 배율과 같아야 Stretch=None 에서 물리 픽셀 1:1 로 그려진다.
        Assert.Equal(96 * pixelsPerDip, bitmap.DpiX, 6);
        Assert.Equal(96 * pixelsPerDip, bitmap.DpiY, 6);
        Assert.True(bitmap.Width <= PairingQr.MaxSideDip + 1e-9);

        var side = bitmap.PixelWidth;
        var pixels = new byte[side * side];
        bitmap.CopyPixels(pixels, side, 0);
        for (var y = 0; y < side; y++)
        {
            var row = data.ModuleMatrix[y / ppm];
            for (var x = 0; x < side; x++)
            {
                var expected = row[x / ppm] ? 0 : 255;
                Assert.True(pixels[y * side + x] == expected, $"({x},{y}) 픽셀이 칸 ({x / ppm},{y / ppm}) 과 다르다");
            }
        }
    }

    [Fact]
    public void Render_keeps_working_with_a_bogus_scale()
    {
        var bitmap = PairingQr.Render(DenseLink(), 0);
        Assert.Equal(96, bitmap.DpiX, 6);
    }
}
