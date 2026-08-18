using System.Windows.Media;

namespace IndustrialCommDemo
{
    /// <summary>与 App.xaml 语义画笔一致的状态色，供代码后台使用；全部 Frozen，可跨线程安全引用。</summary>
    public static class ThemeBrush
    {
        public static readonly SolidColorBrush Success = Create(0x0F7B0F);
        public static readonly SolidColorBrush Warning = Create(0x8A6100);
        public static readonly SolidColorBrush Danger = Create(0xC42B1C);
        public static readonly SolidColorBrush Info = Create(0x174A73);
        public static readonly SolidColorBrush Muted = Create(0x64748B);

        private static SolidColorBrush Create(int rgb)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            return brush;
        }
    }
}
