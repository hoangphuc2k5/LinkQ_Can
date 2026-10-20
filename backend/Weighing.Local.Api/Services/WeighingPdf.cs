using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Weighing.Local.Api.Services;

/// <summary>Xuất phiếu cân ra PDF. Font: Arial trên Windows, DejaVu trên Linux (Docker).</summary>
public static class WeighingPdf
{
    private static readonly object Gate = new();
    private static bool _ready;
    private static string _font = "Arial";

    public static byte[] Create(LocalWeighingDto r)
    {
        EnsureFont();
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(28);
                page.DefaultTextStyle(s => s.FontFamily(_font).FontSize(12));
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().AlignCenter().Text("PHIẾU CÂN").FontSize(20).Bold();
                    col.Item().AlignCenter().Text(r.ClientId).FontSize(11);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    Line(col, "Biển số", r.LicensePlate);
                    Line(col, "Lượt cân", r.Direction == Direction.In ? "Xe vào" : "Xe ra");
                    Line(col, "Tình trạng hàng", r.CargoStatus == CargoStatus.Loaded ? "Có hàng" : "Không có hàng");
                    Line(col, "Thời gian cân lần 1", r.WeighedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
                    Line(col, "Cân lần 1 (xe + hàng)", Kg(r.FirstWeightKg));
                    Line(col, "Cân lần 2 (trọng lượng bì)", r.SecondWeightKg is null ? "Chưa cân" : Kg(r.SecondWeightKg.Value));
                    Line(col, "Trọng lượng hàng", r.CargoWeightKg is null ? "Chưa tính" : Kg(r.CargoWeightKg.Value));

                    col.Item().PaddingTop(10).Text("Trọng lượng hàng = Cân lần 1 − Cân lần 2").FontSize(10);
                    col.Item().Text("Mã phiếu: " + r.Id).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    private static void Line(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Text(label);
            row.RelativeItem().AlignRight().Text(value).Bold();
        });
    }

    private static string Kg(decimal n) => n.ToString("#,##0.###", CultureInfo.GetCultureInfo("vi-VN")) + " kg";

    private static void EnsureFont()
    {
        if (_ready) return;
        lock (Gate)
        {
            if (_ready) return;
            if (File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"))
            {
                Register("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
                Register("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
                _font = "DejaVu Sans";
            }
            else
            {
                Register(@"C:\Windows\Fonts\arial.ttf");
                Register(@"C:\Windows\Fonts\arialbd.ttf");
                _font = "Arial";
            }
            _ready = true;
        }
    }

    private static void Register(string path)
    {
        if (!File.Exists(path)) return;
        using var stream = File.OpenRead(path);
        FontManager.RegisterFont(stream);
    }
}
