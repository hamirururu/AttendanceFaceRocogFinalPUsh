using System.Drawing;

namespace AttendanceFaceRocog
{
    public static class AppTheme
    {
        // Light Theme Colors (from your CSS)
        public static class Light
        {
            public static readonly Color Background = Color.FromArgb(245, 245, 247);       // #ffffff
            public static readonly Color Foreground = Color.FromArgb(3, 2, 19);            // #030213
            public static readonly Color Card = Color.FromArgb(255, 255, 255);             // #ffffff
            public static readonly Color CardForeground = Color.FromArgb(3, 2, 19);
            public static readonly Color Primary = Color.FromArgb(3, 2, 19);               // #030213
            public static readonly Color PrimaryForeground = Color.White;
            public static readonly Color Secondary = Color.FromArgb(240, 240, 245);        // oklch approximation
            public static readonly Color SecondaryForeground = Color.FromArgb(3, 2, 19);
            public static readonly Color Muted = Color.FromArgb(236, 236, 240);            // #ececf0
            public static readonly Color MutedForeground = Color.FromArgb(113, 113, 130);  // #717182
            public static readonly Color Accent = Color.FromArgb(233, 235, 239);           // #e9ebef
            public static readonly Color AccentForeground = Color.FromArgb(3, 2, 19);
            public static readonly Color Destructive = Color.FromArgb(212, 24, 61);        // #d4183d
            public static readonly Color DestructiveForeground = Color.White;
            public static readonly Color Border = Color.FromArgb(26, 0, 0, 0);             // rgba(0,0,0,0.1)
            public static readonly Color InputBackground = Color.FromArgb(243, 243, 245);  // #f3f3f5
            public static readonly Color SwitchBackground = Color.FromArgb(203, 206, 212); // #cbced4
        }

        // Dark Theme Colors
        public static class Dark
        {
            public static readonly Color Background = Color.FromArgb(23, 23, 23);
            public static readonly Color Foreground = Color.FromArgb(250, 250, 250);
            public static readonly Color Card = Color.FromArgb(23, 23, 23);
            public static readonly Color CardForeground = Color.FromArgb(250, 250, 250);
            public static readonly Color Primary = Color.FromArgb(250, 250, 250);
            public static readonly Color PrimaryForeground = Color.FromArgb(38, 38, 38);
            public static readonly Color Secondary = Color.FromArgb(55, 55, 55);
            public static readonly Color Muted = Color.FromArgb(55, 55, 55);
            public static readonly Color MutedForeground = Color.FromArgb(163, 163, 163);
            public static readonly Color Accent = Color.FromArgb(55, 55, 55);
            public static readonly Color Border = Color.FromArgb(55, 55, 55);
        }

        // Typography
        public static readonly Font HeadingLarge = new Font("Segoe UI", 24F, FontStyle.Bold);
        public static readonly Font HeadingMedium = new Font("Segoe UI", 18F, FontStyle.Bold);
        public static readonly Font HeadingSmall = new Font("Segoe UI", 14F, FontStyle.Bold);
        public static readonly Font BodyText = new Font("Segoe UI", 12F, FontStyle.Regular);
        public static readonly Font LabelText = new Font("Segoe UI", 10F, FontStyle.Regular);

        // Border Radius (Guna UI uses int for BorderRadius)
        public static readonly int RadiusSm = 6;   // calc(0.625rem - 4px)
        public static readonly int RadiusMd = 8;   // calc(0.625rem - 2px)
        public static readonly int RadiusLg = 10;  // 0.625rem
        public static readonly int RadiusXl = 14;  // calc(0.625rem + 4px)
    }
}
