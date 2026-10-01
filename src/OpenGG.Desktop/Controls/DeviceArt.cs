using System.Windows;
using System.Windows.Media;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Ilustrações vetoriais por tipo de hardware, em tons de cinza, para dispositivos sem foto do modelo. Desenham a
/// categoria (uma placa de vídeo, um SSD), nunca um modelo específico nem o estado do RGB.
/// </summary>
internal static class DeviceArt
{
    public static readonly string[] Kinds =
        ["cpu", "motherboard", "gpu", "ram", "ssd", "hdd", "monitor", "fan", "aio", "keyboard", "mouse", "headset", "mic", "controller", "camera", "speaker", "lighting", "app", "generic"];

    private static readonly Brush Inset = Solid(0x0A), Deep = Solid(0x0E), Body = Solid(0x1A), Face = Solid(0x1E), Panel = Solid(0x24), Raised = Solid(0x2A),
        Detail = Solid(0x3A), Bright = Solid(0x4A), Metal = Solid(0x70), MetalDark = Solid(0x5C), Contact = Solid(0x5C);

    private static readonly Pen Edge = Line(0x2E, 1), Soft = Line(0x26, 1), Hard = Line(0x3A, 1), MetalEdge = Line(0x8A, 1);

    /// <summary>Ilustração do tipo; nulo para tipo desconhecido.</summary>
    public static DrawingImage? For(string kind) => kind switch
    {
        "cpu" => Draw(Cpu),
        "motherboard" => Draw(Motherboard),
        "gpu" => Draw(Gpu),
        "ram" => Draw(Ram),
        "ssd" => Draw(Ssd),
        "hdd" => Draw(Hdd),
        "monitor" => Draw(Monitor),
        "fan" => Draw(dc => Fan(dc, new Point(90, 90), 90, frame: true)),
        "aio" => Draw(Aio),
        "keyboard" => Draw(Keyboard),
        "mouse" => Draw(Mouse),
        "headset" => Draw(Headset),
        "mic" => Draw(Mic),
        "controller" => Draw(Controller),
        "camera" => Draw(Camera),
        "speaker" => Draw(Speakers),
        "lighting" => Draw(LightingController),
        "app" => Draw(App),
        "generic" => Draw(Generic),
        _ => null,
    };

    private static DrawingImage Draw(Action<DrawingContext> paint)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            paint(dc);
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static void Cpu(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Body, Edge, new Rect(10, 10, 180, 180), 8, 8);
        foreach (var (x, y) in new[] { (60, 18), (124, 18), (60, 176), (124, 176), (18, 60), (18, 124), (176, 60), (176, 124) })
        {
            for (var i = 0; i < 3; i++)
            {
                var horizontal = y is 18 or 176;
                dc.DrawRectangle(Detail, null, horizontal ? new Rect(x + 2 + (i * 6), y, 3, 6) : new Rect(x, y + 2 + (i * 6), 6, 3));
            }
        }

        var ihs = Geometry.Parse("M36,29 L64,29 64,43 83,43 83,29 117,29 117,43 136,43 136,29 164,29 164,64 150,64 150,82 171,82 171,117 150,117 150,135 164,135 164,171 136,171 136,157 117,157 117,171 83,171 83,157 64,157 64,171 36,171 36,135 50,135 50,117 29,117 29,82 50,82 50,64 36,64 Z");
        dc.DrawGeometry(Metal, MetalEdge, ihs);
        foreach (var (w, y) in new[] { (58.0, 82.0), (88.0, 96.0), (42.0, 110.0) })
        {
            dc.DrawRoundedRectangle(MetalDark, null, new Rect(100 - (w / 2), y, w, 5), 2.5, 2.5);
        }

        dc.DrawGeometry(MetalDark, null, Geometry.Parse("M16,184 L28,184 16,172 Z"));
    }

    private static void Motherboard(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Solid(0x15), Edge, new Rect(0, 0, 170, 200), 4, 4);
        foreach (var (x, y) in new[] { (9, 9), (86, 9), (161, 9), (9, 118), (161, 118), (9, 191), (86, 191), (161, 191) })
        {
            dc.DrawEllipse(Inset, Hard, new Point(x, y), 3.5, 3.5);
        }

        // Tampa do painel traseiro e dissipador dos VRMs.
        dc.DrawGeometry(Panel, Hard, Geometry.Parse("M4,16 L44,16 44,82 34,94 4,94 Z"));
        for (var y = 26; y < 84; y += 7)
        {
            dc.DrawLine(Soft, new Point(10, y), new Point(38, y));
        }

        dc.DrawRoundedRectangle(Raised, Hard, new Rect(50, 12, 70, 16), 2, 2);
        for (var x = 55; x < 118; x += 5)
        {
            dc.DrawLine(Soft, new Point(x, 14), new Point(x, 26));
        }

        // Soquete, furos do cooler, pentes de memória e conector de 24 pinos.
        dc.DrawRectangle(Deep, Hard, new Rect(58, 36, 52, 52));
        dc.DrawRectangle(Face, Edge, new Rect(64, 42, 40, 40));
        dc.DrawRectangle(Raised, null, new Rect(71, 49, 26, 26));
        foreach (var (x, y) in new[] { (52, 32), (116, 32), (52, 92), (116, 92) })
        {
            dc.DrawEllipse(Inset, Edge, new Point(x, y), 2.5, 2.5);
        }

        for (var i = 0; i < 4; i++)
        {
            dc.DrawRectangle(i % 2 == 0 ? Raised : Face, Edge, new Rect(126 + (i * 7), 24, 4, 82));
            dc.DrawRectangle(Detail, null, new Rect(125.5 + (i * 7), 22, 5, 3));
            dc.DrawRectangle(Detail, null, new Rect(125.5 + (i * 7), 105, 5, 3));
        }

        dc.DrawRectangle(Face, Edge, new Rect(156, 40, 9, 36));

        // Slots PCIe (o primeiro com reforço de metal), dissipadores de M.2 e do chipset.
        dc.DrawRectangle(Raised, Line(0x70, 1), new Rect(16, 112, 112, 7));
        dc.DrawRoundedRectangle(Panel, Hard, new Rect(18, 126, 100, 14), 2, 2);
        dc.DrawRectangle(Face, Edge, new Rect(16, 150, 112, 6));
        dc.DrawRoundedRectangle(Panel, Hard, new Rect(18, 164, 100, 12), 2, 2);
        dc.DrawGeometry(Raised, Hard, Geometry.Parse("M124,128 L164,128 164,190 136,190 124,178 Z"));
        dc.DrawLine(Hard, new Point(132, 140), new Point(156, 164));

        // Áudio e conectores do painel frontal.
        foreach (var y in new[] { 164, 172, 180 })
        {
            dc.DrawEllipse(Raised, Hard, new Point(10, y), 2.5, 2.5);
        }

        dc.DrawRectangle(Face, Edge, new Rect(40, 184, 40, 5));
    }

    private static void Gpu(DrawingContext dc)
    {
        dc.DrawRectangle(Metal, MetalEdge, new Rect(0, 8, 8, 90));
        foreach (var y in new[] { 22, 44, 66 })
        {
            dc.DrawRoundedRectangle(MetalDark, null, new Rect(2.5, y, 3, 14), 1.5, 1.5);
        }

        dc.DrawRectangle(Face, null, new Rect(10, 6, 244, 4));
        dc.DrawRoundedRectangle(Body, Edge, new Rect(10, 10, 244, 84), 10, 10);
        dc.DrawLine(Soft, new Point(20, 14), new Point(244, 14));
        foreach (var x in new[] { 58, 132, 206 })
        {
            Fan(dc, new Point(x, 52), 34, frame: false);
        }

        dc.DrawRectangle(Contact, null, new Rect(72, 94, 112, 4));
    }

    private static void Ram(DrawingContext dc)
    {
        var pcb = Geometry.Parse("M2,44 L238,44 238,62 128,62 128,57 122,57 122,62 2,62 Z");
        dc.DrawGeometry(Body, Edge, pcb);
        for (var x = 6.0; x < 236; x += 3.2)
        {
            if (x is > 119 and < 131)
            {
                continue;
            }

            dc.DrawRectangle(Contact, null, new Rect(x, 55, 1.8, 6));
        }

        dc.DrawGeometry(Raised, Hard, Geometry.Parse("M4,14 L98,14 106,6 236,6 236,48 4,48 Z"));
        dc.DrawRoundedRectangle(Bright, null, new Rect(108, 2, 126, 5), 2.5, 2.5);
        dc.DrawLine(Hard, new Point(150, 6), new Point(128, 48));
        dc.DrawRoundedRectangle(Panel, Edge, new Rect(18, 24, 72, 14), 2, 2);
    }

    private static void Ssd(DrawingContext dc)
    {
        dc.DrawGeometry(Body, Edge, Geometry.Parse("M10,6 L236,6 236,28 232,28 A 4 4 0 0 0 232,36 L236,36 236,58 10,58 Z"));
        dc.DrawRectangle(Face, Edge, new Rect(0, 10, 12, 44));
        for (var y = 12.0; y < 52; y += 3)
        {
            if (y is > 38 and < 44)
            {
                continue;
            }

            dc.DrawRectangle(Contact, null, new Rect(1, y, 9, 1.6));
        }

        dc.DrawRectangle(Deep, Edge, new Rect(38, 17, 30, 30));
        dc.DrawEllipse(Detail, null, new Point(42, 21), 1.3, 1.3);
        dc.DrawRectangle(Deep, Edge, new Rect(80, 12, 58, 40));
        dc.DrawRectangle(Deep, Edge, new Rect(146, 12, 58, 40));
        dc.DrawRectangle(Deep, Edge, new Rect(210, 20, 18, 24));
        foreach (var (x, y) in new[] { (28, 16), (28, 22), (28, 40), (72, 48), (74, 14) })
        {
            dc.DrawRectangle(Detail, null, new Rect(x, y, 4, 2));
        }
    }

    private static void Hdd(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Panel, Hard, new Rect(0, 0, 150, 200), 6, 6);
        dc.DrawRoundedRectangle(Raised, Hard, new Rect(8, 8, 134, 184), 4, 4);
        dc.DrawEllipse(Solid(0x32), Line(0x40, 1), new Point(75, 80), 58, 58);
        dc.DrawEllipse(Raised, Line(0x46, 1), new Point(75, 80), 13, 13);
        for (var i = 0; i < 6; i++)
        {
            var a = i * Math.PI / 3;
            dc.DrawEllipse(Face, null, new Point(75 + (8 * Math.Cos(a)), 80 + (8 * Math.Sin(a))), 1.5, 1.5);
        }

        dc.DrawEllipse(Solid(0x32), Line(0x40, 1), new Point(118, 160), 15, 15);
        dc.DrawGeometry(Solid(0x32), Line(0x40, 1), Geometry.Parse("M108,150 L84,110 90,106 116,146 Z"));
        foreach (var (x, y) in new[] { (15, 15), (135, 15), (15, 185), (135, 185), (15, 100), (135, 100) })
        {
            dc.DrawEllipse(Face, Line(0x46, 1), new Point(x, y), 3, 3);
        }

        dc.DrawRoundedRectangle(Panel, Hard, new Rect(16, 146, 62, 38), 2, 2);
        foreach (var (w, y) in new[] { (44.0, 154.0), (32.0, 162.0), (38.0, 170.0) })
        {
            dc.DrawRoundedRectangle(Detail, null, new Rect(22, y, w, 3), 1.5, 1.5);
        }
    }

    private static void Monitor(DrawingContext dc)
    {
        dc.DrawGeometry(Face, Edge, Geometry.Parse("M118,150 L142,150 148,184 112,184 Z"));
        dc.DrawRoundedRectangle(Face, Edge, new Rect(74, 182, 112, 10), 5, 5);
        dc.DrawRoundedRectangle(Solid(0x16), Edge, new Rect(0, 0, 260, 154), 6, 6);
        dc.DrawRectangle(Solid(0x05), null, new Rect(5, 5, 250, 136));
        dc.DrawGeometry(Solid(0x0B), null, Geometry.Parse("M5,5 L112,5 36,141 5,141 Z"));
        dc.DrawEllipse(Detail, null, new Point(130, 147.5), 1.8, 1.8);
    }

    /// <summary>Ventoinha vista de frente: moldura opcional, nove pás curvas e cubo.</summary>
    private static void Fan(DrawingContext dc, Point c, double half, bool frame)
    {
        var r = frame ? half - 10 : half;
        if (frame)
        {
            dc.DrawRoundedRectangle(Body, Edge, new Rect(c.X - half, c.Y - half, half * 2, half * 2), half * 0.2, half * 0.2);
            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var hole = new Point(c.X + (dx * (half - 14)), c.Y + (dy * (half - 14)));
                dc.DrawEllipse(Inset, Hard, hole, 6, 6);
                dc.DrawEllipse(null, Soft, hole, 9, 9);
            }
        }

        dc.DrawEllipse(Solid(0x0C), Edge, c, r, r);
        var hub = r * 0.3;
        var blade = new Pen(Solid(0x2C), 0.75);
        for (var i = 0; i < 9; i++)
        {
            var a = i * 40.0;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(Polar(c, hub, a), true, true);
                ctx.QuadraticBezierTo(Polar(c, r * 0.7, a - 14), Polar(c, r * 0.96, a + 6), true, true);
                ctx.ArcTo(Polar(c, r * 0.96, a + 30), new Size(r * 0.96, r * 0.96), 0, false, SweepDirection.Clockwise, true, true);
                ctx.QuadraticBezierTo(Polar(c, r * 0.62, a + 18), Polar(c, hub, a + 26), true, true);
            }

            g.Freeze();
            dc.DrawGeometry(Solid(0x1F), blade, g);
        }

        dc.DrawEllipse(Face, Hard, c, hub, hub);
        dc.DrawEllipse(Raised, null, c, hub * 0.62, hub * 0.62);
    }

    private static void Aio(DrawingContext dc)
    {
        var tube = new Pen(Face, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, tube, Geometry.Parse("M14,92 C14,130 40,140 62,142"));
        dc.DrawGeometry(null, tube, Geometry.Parse("M26,92 C26,120 44,128 62,130"));
        dc.DrawRoundedRectangle(Solid(0x16), Edge, new Rect(0, 12, 260, 80), 6, 6);
        dc.DrawRectangle(Face, null, new Rect(1, 13, 16, 78));
        dc.DrawRectangle(Face, null, new Rect(243, 13, 16, 78));
        foreach (var x in new[] { 60, 130, 200 })
        {
            Fan(dc, new Point(x, 52), 36, frame: false);
        }

        dc.DrawEllipse(Body, Hard, new Point(80, 140), 28, 28);
        dc.DrawEllipse(Inset, Edge, new Point(80, 140), 19, 19);
        dc.DrawEllipse(null, Soft, new Point(80, 140), 12, 12);
    }

    private static void Keyboard(DrawingContext dc)
    {
        const double u = 13, gap = 1.6, left = 10, top = 10;
        dc.DrawRoundedRectangle(Body, Edge, new Rect(0, 0, 257, 101), 8, 8);
        dc.DrawRoundedRectangle(Solid(0x11), null, new Rect(5, 5, 247, 91), 5, 5);

        void Key(double col, double row, double width = 1)
        {
            var box = new Rect(left + (col * u), top + (row * u), (width * u) - gap, u - gap);
            dc.DrawRoundedRectangle(Face, null, box, 2, 2);
            box.Inflate(-1.6, -1.6);
            box.Offset(0, -0.6);
            dc.DrawRoundedRectangle(Raised, null, box, 1.5, 1.5);
        }

        Key(0, 0);
        for (var i = 0; i < 12; i++)
        {
            Key(2 + i + (i / 4 * 0.5), 0);
        }

        for (var i = 0; i < 3; i++)
        {
            Key(15.25 + i, 0);
        }

        // Linhas principais: larguras reais de um ANSI TKL.
        double[][] rows =
        [
            [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2],
            [1.5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5],
            [1.75, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.25],
            [2.25, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.75],
            [1.25, 1.25, 1.25, 6.25, 1.25, 1.25, 1.25, 1.25],
        ];
        for (var r = 0; r < rows.Length; r++)
        {
            var col = 0.0;
            foreach (var width in rows[r])
            {
                Key(col, 1.25 + r, width);
                col += width;
            }
        }

        for (var i = 0; i < 3; i++)
        {
            Key(15.25 + i, 1.25);
            Key(15.25 + i, 2.25);
            Key(15.25 + i, 5.25);
        }

        Key(16.25, 4.25);
    }

    private static void Mouse(DrawingContext dc)
    {
        dc.DrawGeometry(Face, Edge, Geometry.Parse("M55,4 C80,4 100,22 102,60 C104,100 100,150 80,170 C70,178 40,178 30,170 C10,150 6,100 8,60 C10,22 30,4 55,4 Z"));
        dc.DrawLine(new Pen(Inset, 1.5), new Point(55, 5), new Point(55, 62));
        dc.DrawGeometry(null, new Pen(Deep, 1.2), Geometry.Parse("M9,62 C30,70 80,70 101,62"));
        dc.DrawRoundedRectangle(Raised, Hard, new Rect(51, 20, 8, 24), 4, 4);
        for (var y = 24; y < 42; y += 4)
        {
            dc.DrawLine(Soft, new Point(52, y), new Point(58, y));
        }

        dc.DrawRoundedRectangle(Panel, Edge, new Rect(6, 76, 5, 16), 2.5, 2.5);
        dc.DrawRoundedRectangle(Panel, Edge, new Rect(6, 96, 5, 16), 2.5, 2.5);
        dc.DrawGeometry(null, Edge, Geometry.Parse("M24,28 C30,17 40,10 50,8"));
    }

    private static void Headset(DrawingContext dc)
    {
        dc.DrawGeometry(null, new Pen(Face, 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M34,104 C34,20 166,20 166,104"));
        dc.DrawGeometry(null, new Pen(Raised, 2), Geometry.Parse("M44,86 C50,36 150,36 156,86"));
        foreach (var x in new[] { 14.0, 142.0 })
        {
            dc.DrawRoundedRectangle(Face, Edge, new Rect(x, 94, 44, 82), 20, 20);
            dc.DrawRoundedRectangle(Panel, null, new Rect(x + 8, 104, 28, 62), 14, 14);
        }

        dc.DrawGeometry(null, new Pen(Face, 4) { EndLineCap = PenLineCap.Round }, Geometry.Parse("M30,168 C30,186 58,190 82,182"));
        dc.DrawEllipse(Raised, Edge, new Point(84, 181), 5, 5);
    }

    private static void Mic(DrawingContext dc)
    {
        dc.DrawGeometry(null, new Pen(Raised, 4), Geometry.Parse("M22,70 L22,118 C22,142 98,142 98,118 L98,70"));
        dc.DrawRoundedRectangle(Face, Edge, new Rect(56, 136, 8, 58), 3, 3);
        dc.DrawEllipse(Face, Edge, new Point(60, 200), 44, 10);
        dc.DrawRoundedRectangle(Face, Edge, new Rect(30, 10, 60, 122), 30, 30);
        for (var y = 22.0; y <= 96; y += 6)
        {
            // Grade do microfone acompanhando a curva do topo.
            var dy = Math.Max(0, 40 - y);
            var inset = 30 - Math.Sqrt(Math.Max(0, (30 * 30) - (dy * dy)));
            dc.DrawLine(Soft, new Point(36 + inset, y), new Point(84 - inset, y));
        }

        dc.DrawRectangle(Raised, Hard, new Rect(30, 100, 60, 8));
        dc.DrawEllipse(Panel, Hard, new Point(22, 110), 5, 5);
        dc.DrawEllipse(Panel, Hard, new Point(98, 110), 5, 5);
    }

    private static void Controller(DrawingContext dc)
    {
        dc.DrawGeometry(Face, Edge, Geometry.Parse("M60,20 C80,14 160,14 180,20 C212,28 232,70 236,110 C240,140 222,156 204,150 C188,145 176,118 160,112 L80,112 C64,118 52,145 36,150 C18,156 0,140 4,110 C8,70 28,28 60,20 Z"));
        foreach (var p in new[] { new Point(70, 56), new Point(150, 86) })
        {
            dc.DrawEllipse(Deep, Edge, p, 16, 16);
            dc.DrawEllipse(Raised, Hard, p, 11, 11);
        }

        dc.DrawRoundedRectangle(Panel, Hard, new Rect(82, 76, 11, 26), 2, 2);
        dc.DrawRoundedRectangle(Panel, Hard, new Rect(75, 83, 25, 11), 2, 2);
        foreach (var p in new[] { new Point(178, 42), new Point(192, 56), new Point(178, 70), new Point(164, 56) })
        {
            dc.DrawEllipse(Panel, Hard, p, 6, 6);
        }

        dc.DrawRoundedRectangle(Raised, null, new Rect(100, 46, 12, 6), 3, 3);
        dc.DrawRoundedRectangle(Raised, null, new Rect(128, 46, 12, 6), 3, 3);
        dc.DrawEllipse(Raised, Hard, new Point(120, 70), 6, 6);
    }

    private static void Camera(DrawingContext dc)
    {
        dc.DrawGeometry(Solid(0x16), Soft, Geometry.Parse("M80,64 L120,64 128,98 72,98 Z"));
        dc.DrawRoundedRectangle(Face, Edge, new Rect(10, 10, 180, 56), 28, 28);
        dc.DrawEllipse(Inset, Hard, new Point(100, 38), 21, 21);
        dc.DrawEllipse(null, new Pen(Panel, 2), new Point(100, 38), 14, 14);
        dc.DrawEllipse(Solid(0x05), null, new Point(100, 38), 8, 8);
        dc.DrawEllipse(Detail, null, new Point(95, 33), 2.5, 2.5);
        dc.DrawEllipse(Detail, null, new Point(142, 38), 2, 2);
        foreach (var x in new[] { 50, 56, 62 })
        {
            dc.DrawEllipse(Deep, null, new Point(x, 38), 1.5, 1.5);
        }
    }

    private static void Speakers(DrawingContext dc)
    {
        foreach (var x in new[] { 10.0, 140.0 })
        {
            dc.DrawRoundedRectangle(Face, Edge, new Rect(x, 10, 90, 150), 8, 8);
            var cx = x + 45;
            dc.DrawEllipse(Deep, Hard, new Point(cx, 46), 12, 12);
            dc.DrawEllipse(Raised, null, new Point(cx, 46), 5, 5);
            dc.DrawEllipse(Deep, Hard, new Point(cx, 108), 32, 32);
            dc.DrawEllipse(null, new Pen(Face, 3), new Point(cx, 108), 26, 26);
            dc.DrawEllipse(Raised, null, new Point(cx, 108), 10, 10);
        }
    }

    private static void LightingController(DrawingContext dc)
    {
        dc.DrawGeometry(null, new Pen(Face, 4) { EndLineCap = PenLineCap.Round }, Geometry.Parse("M10,60 C0,60 -4,80 4,96"));
        dc.DrawRoundedRectangle(Face, Edge, new Rect(10, 20, 180, 80), 10, 10);
        dc.DrawLine(Hard, new Point(20, 24), new Point(180, 24));
        for (var i = 0; i < 6; i++)
        {
            var x = 26 + (i * 26);
            dc.DrawRoundedRectangle(Inset, Hard, new Rect(x, 74, 18, 12), 2, 2);
            for (var p = 0; p < 3; p++)
            {
                dc.DrawEllipse(Detail, null, new Point(x + 4 + (p * 5), 80), 1.2, 1.2);
            }
        }

        dc.DrawEllipse(Bright, null, new Point(170, 42), 3, 3);
    }

    private static void App(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Solid(0x14), Edge, new Rect(0, 0, 220, 150), 10, 10);
        dc.DrawGeometry(Face, null, Geometry.Parse("M10,0.5 L210,0.5 A 9.5 9.5 0 0 1 219.5,10 L219.5,24 L0.5,24 L0.5,10 A 9.5 9.5 0 0 1 10,0.5 Z"));
        dc.DrawLine(Soft, new Point(1, 24), new Point(219, 24));
        foreach (var x in new[] { 14, 28, 42 })
        {
            dc.DrawEllipse(Detail, null, new Point(x, 12), 4, 4);
        }

        double[] levels = [0.55, 0.35, 0.45, 0.7, 0.5, 0.3];
        var points = new List<Point>();
        for (var i = 0; i < levels.Length; i++)
        {
            var x = 40 + (i * 28);
            dc.DrawLine(new Pen(Panel, 2), new Point(x, 44), new Point(x, 132));
            points.Add(new Point(x, 44 + (88 * levels[i])));
        }

        var curve = new StreamGeometry();
        using (var ctx = curve.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        curve.Freeze();
        dc.DrawGeometry(null, new Pen(Solid(0x66), 1.5) { LineJoin = PenLineJoin.Round }, curve);
        foreach (var p in points)
        {
            dc.DrawEllipse(Bright, null, p, 5, 5);
        }
    }

    private static void Generic(DrawingContext dc)
    {
        for (var i = 0; i < 5; i++)
        {
            var o = 52 + (i * 24);
            dc.DrawLine(Hard, new Point(o, 8), new Point(o, 20));
            dc.DrawLine(Hard, new Point(o, 180), new Point(o, 192));
            dc.DrawLine(Hard, new Point(8, o), new Point(20, o));
            dc.DrawLine(Hard, new Point(180, o), new Point(192, o));
        }

        dc.DrawRoundedRectangle(Face, Edge, new Rect(20, 20, 160, 160), 14, 14);
        dc.DrawRoundedRectangle(Raised, Hard, new Rect(62, 62, 76, 76), 8, 8);
    }

    private static Point Polar(Point c, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(c.X + (radius * Math.Cos(a)), c.Y + (radius * Math.Sin(a)));
    }

    private static SolidColorBrush Solid(byte gray)
    {
        var brush = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
        brush.Freeze();
        return brush;
    }

    private static Pen Line(byte gray, double thickness)
    {
        var pen = new Pen(Solid(gray), thickness);
        pen.Freeze();
        return pen;
    }
}
