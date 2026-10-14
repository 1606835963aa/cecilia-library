using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace LightReader.App;
// 首页插画：只在首页可见且系统允许动画时运行，离开首页即停止，不占用阅读时的绘制
public partial class HomeArt : UserControl
{
    private readonly Storyboard motion;
    public HomeArt()
    {
        InitializeComponent();
        motion = (Storyboard)Resources["Motion"];
        IsVisibleChanged += (_, _) => Animate(IsVisible);
        Unloaded += (_, _) => Animate(false);
    }
    private void Animate(bool run)
    {
        if (run && SystemParameters.ClientAreaAnimation) motion.Begin(this, true);
        else motion.Stop(this);
    }
}

// 首页中间横向滚动的格式名称
public sealed class FormatRibbon : Border
{
    private static readonly string[] words = ["docx", "文字", "xlsx", "表格", "pptx", "演示", "wps", "et", "dps", "pdf", "odt", "csv", "tif", "rtf"];
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    private readonly TranslateTransform shift = new();
    public FormatRibbon()
    {
        ClipToBounds = true; BorderThickness = new(0, 1, 0, 1); Padding = new(0, 10, 0, 10);
        SetResourceReference(BorderBrushProperty, "Border");
        strip.RenderTransform = shift;
        // 内容重复两遍，移动半个宽度后无缝接上
        for (var round = 0; round < 2; round++)
            foreach (var word in words)
            {
                var chinese = word[0] > 0x7F;
                var text = new TextBlock { Text = word, Margin = new(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = chinese ? 19 : 24 };
                text.SetResourceReference(TextBlock.FontFamilyProperty, chinese ? "SerifFont" : "LatinFont");
                text.SetResourceReference(TextBlock.ForegroundProperty, chinese ? "Text" : "TextMuted");
                if (!chinese) text.FontStyle = FontStyles.Italic;
                var star = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,-5 C0.5,-1 1,-0.5 5,0 C1,0.5 0.5,1 0,5 C-0.5,1 -1,0.5 -5,0 C-1,-0.5 -0.5,-1 0,-5 Z"), Width = 10, Height = 10, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
                star.SetResourceReference(Shape.FillProperty, "Gold");
                strip.Children.Add(text); strip.Children.Add(star);
            }
        Child = new Canvas { Height = 34, Children = { strip } };
        strip.Loaded += (_, _) => Restart();
        IsVisibleChanged += (_, _) => Restart();
    }
    private void Restart()
    {
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetTop(strip, (34 - strip.DesiredSize.Height) / 2);
        if (!IsVisible || !SystemParameters.ClientAreaAnimation) return;
        var half = strip.DesiredSize.Width / 2;
        if (half > 0) shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -half, TimeSpan.FromSeconds(half / 28)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
